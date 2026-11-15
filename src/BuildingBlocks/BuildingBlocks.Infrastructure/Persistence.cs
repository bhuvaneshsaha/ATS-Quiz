using System.Data;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.BuildingBlocks.Infrastructure;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? Error { get; set; }
    public int Attempts { get; set; }
}

public sealed class InboxMessage
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Type { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Key { get; set; } = "";
    public Guid? UserId { get; set; }
    public string RequestHash { get; set; } = "";
    public int StatusCode { get; set; }
    public string ContentType { get; set; } = "application/json";
    public string Body { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public static class IntegrationJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";

    public static Type Resolve(string name)
    {
        var type = Type.GetType(name, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        var fullName = name.Split(',')[0].Trim();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        throw new InvalidOperationException($"Unknown integration event '{name}'.");
    }

    public static async Task PublishAsync(IEventBus bus, IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var method = typeof(IEventBus).GetMethod(nameof(IEventBus.PublishAsync))!;
        var closed = method.MakeGenericMethod(integrationEvent.GetType());
        var task = (Task)closed.Invoke(bus, [integrationEvent, cancellationToken])!;
        await task;
    }
}

public sealed class DbContextTransaction(DbContext db) : ITransactionalStore
{
    private IDbContextTransaction? _transaction;

    public async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (_transaction is not null)
        {
            return;
        }

        _transaction = await db.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
        db.ChangeTracker.Clear();
    }
}

public static class WorkspaceFilters
{
    public static void Apply(ModelBuilder modelBuilder, ITenantContext tenant)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(Entity).IsAssignableFrom(entityType.ClrType) || entityType.ClrType.IsAbstract)
            {
                continue;
            }

            var method = typeof(WorkspaceFilters)
                .GetMethod(nameof(Set), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            method.Invoke(null, [modelBuilder, tenant]);
        }
    }

    private static void Set<TEntity>(ModelBuilder modelBuilder, ITenantContext tenant)
        where TEntity : Entity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.WorkspaceId == tenant.WorkspaceId);
    }
}

public sealed class ModuleSaveChangesInterceptor(
    ITenantContext tenant,
    ICurrentUser currentUser,
    IClock clock,
    IEnumerable<IIntegrationEventMapper> mappers) : SaveChangesInterceptor
{
    private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp", "TokenHash", "CodeHash"
    };

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null)
        {
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        var now = clock.UtcNow;
        foreach (var entry in context.ChangeTracker.Entries<Entity>().ToList())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.SetWorkspace(tenant.WorkspaceId);
                entry.Entity.Touch(now);
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.Touch(now);
            }
        }

        WriteAudit(context, now);
        WriteOutbox(context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteAudit(DbContext context, DateTimeOffset now)
    {
        var entries = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is IAudited and Entity && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var entity = (Entity)entry.Entity;
            context.Set<AuditEntry>().Add(new AuditEntry
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = entity.WorkspaceId == Guid.Empty ? tenant.WorkspaceId : entity.WorkspaceId,
                ActorId = currentUser.UserId,
                Action = entry.State.ToString(),
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entity.Id,
                Before = entry.State == EntityState.Added ? null : Snapshot(entry.OriginalValues),
                After = entry.State == EntityState.Deleted ? null : Snapshot(entry.CurrentValues),
                At = now
            });
        }
    }

    private void WriteOutbox(DbContext context)
    {
        var entities = context.ChangeTracker.Entries<Entity>()
            .Select(entry => entry.Entity)
            .Where(entity => entity.DomainEvents.Count > 0)
            .ToList();

        foreach (var entity in entities)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                foreach (var mapper in mappers)
                {
                    var integration = mapper.Map(domainEvent, entity.WorkspaceId == Guid.Empty ? tenant.WorkspaceId : entity.WorkspaceId);
                    if (integration is null)
                    {
                        continue;
                    }

                    context.Set<OutboxMessage>().Add(new OutboxMessage
                    {
                        Id = integration.EventId,
                        WorkspaceId = entity.WorkspaceId == Guid.Empty ? tenant.WorkspaceId : entity.WorkspaceId,
                        Type = IntegrationJson.TypeName(integration.GetType()),
                        Payload = JsonSerializer.Serialize(integration, integration.GetType(), IntegrationJson.Options),
                        OccurredAt = integration.OccurredAt
                    });
                }
            }

            entity.ClearDomainEvents();
        }
    }

    private static string Snapshot(PropertyValues values)
    {
        var bag = new Dictionary<string, string?>();
        foreach (var property in values.Properties)
        {
            if (property.IsShadowProperty() || property.IsIndexerProperty() || Sensitive.Contains(property.Name))
            {
                continue;
            }

            if (property.ClrType != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.ClrType) && property.ClrType != typeof(string))
            {
                continue;
            }

            var value = values[property];
            bag[property.Name] = value switch
            {
                null => null,
                string text => text,
                Guid or DateTimeOffset or DateTime or DateOnly or decimal or int or long or bool or double or float => value.ToString(),
                Enum enumValue => enumValue.ToString(),
                _ => JsonSerializer.Serialize(value, IntegrationJson.Options)
            };
        }

        return JsonSerializer.Serialize(bag, IntegrationJson.Options);
    }
}

public abstract class OutboxStore<TContext>(TContext db, IClock clock) : IOutboxStore
    where TContext : DbContext
{
    public abstract string Module { get; }
    protected abstract string Schema { get; }

    public async Task<int> DispatchBatchAsync(IEventBus bus, CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var rows = await ClaimAsync(transaction, cancellationToken);
            foreach (var row in rows)
            {
                try
                {
                    var type = IntegrationJson.Resolve(row.Type);
                    var integration = (IIntegrationEvent)JsonSerializer.Deserialize(row.Payload, type, IntegrationJson.Options)!;
                    await IntegrationJson.PublishAsync(bus, integration, cancellationToken);
                    await MarkProcessedAsync(row.Id, cancellationToken);
                }
                catch (Exception exception)
                {
                    await MarkFailedAsync(row.Id, exception.Message, cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return rows.Count;
        });
    }

    private async Task<List<OutboxRow>> ClaimAsync(IDbContextTransaction transaction, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = $"""
            SELECT id, type, payload
            FROM {Schema}.outbox_messages
            WHERE processed_at IS NULL AND attempts < 10
            ORDER BY occurred_at
            LIMIT 20
            FOR UPDATE SKIP LOCKED
            """;

        var rows = new List<OutboxRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new OutboxRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    private Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken)
    {
#pragma warning disable EF1002
        return db.Database.ExecuteSqlRawAsync(
            $"UPDATE {Schema}.outbox_messages SET processed_at = {{0}}, error = NULL WHERE id = {{1}}",
            [clock.UtcNow, id],
            cancellationToken);
#pragma warning restore EF1002
    }

    private Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken)
    {
        var message = error.Length > 2000 ? error[..2000] : error;
#pragma warning disable EF1002
        return db.Database.ExecuteSqlRawAsync(
            $"UPDATE {Schema}.outbox_messages SET attempts = attempts + 1, error = {{0}} WHERE id = {{1}}",
            [message, id],
            cancellationToken);
#pragma warning restore EF1002
    }

    private sealed record OutboxRow(Guid Id, string Type, string Payload);
}

public static class Inbox
{
    public static async Task<bool> ExistsAsync(DbContext db, Guid eventId, CancellationToken cancellationToken) =>
        await db.Set<InboxMessage>().AnyAsync(message => message.Id == eventId, cancellationToken);

    public static void Record(DbContext db, IIntegrationEvent integrationEvent, Guid workspaceId, DateTimeOffset now)
    {
        db.Set<InboxMessage>().Add(new InboxMessage
        {
            Id = integrationEvent.EventId,
            WorkspaceId = workspaceId,
            Type = integrationEvent.GetType().Name,
            ProcessedAt = now
        });
    }
}

public static class Postgres
{
    public static void UseShortlist(DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class ConfiguredTenantContext(Microsoft.Extensions.Options.IOptions<WorkspaceOptions> options) : ITenantContext
{
    public Guid WorkspaceId => options.Value.Id;
}

public sealed class WorkspaceOptions
{
    public const string Section = "Workspace";
    public Guid Id { get; set; }
    public string Name { get; set; } = "Shortlist";
}

public static class InfrastructureRegistration
{
    public static IServiceCollection AddModuleInterceptors(this IServiceCollection services)
    {
        services.AddScoped<ModuleSaveChangesInterceptor>();
        return services;
    }
}
