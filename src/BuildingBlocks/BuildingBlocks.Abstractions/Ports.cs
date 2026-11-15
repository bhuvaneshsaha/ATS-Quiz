using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shortlist.BuildingBlocks.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ITenantContext
{
    Guid WorkspaceId { get; }
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    bool IsCandidate { get; }
    Guid? InvitationId { get; }
}

public enum Scope
{
    All,
    Own,
    Assigned,
    Sent
}

public sealed record PermissionDefinition(string Key, string Module, string Description);

public sealed record AccessGrant(
    string Permission,
    Scope Scope,
    IReadOnlyList<Guid> HiringUnitIds,
    IReadOnlyList<Guid> FunctionIds);

public interface IPermissionChecker
{
    Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken);
}

public interface IRoleAssignments
{
    Task<IReadOnlyList<AccessGrant>> FindAsync(Guid userId, string permission, CancellationToken cancellationToken);
}

public interface IUserLookup
{
    Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public sealed record UserSummary(Guid Id, string Email, string DisplayName, string Status);

public sealed record StoredFile(string Key, string ContentType, long Size);

public interface IFileStore
{
    Task<StoredFile> SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);
    Task<Uri> GetReadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}

public interface ICacheStore
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken);
    Task SetAsync<T>(string key, T value, TimeSpan lifetime, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}

public interface IIntegrationEventMapper
{
    IIntegrationEvent? Map(IDomainEvent domainEvent, Guid workspaceId);
}

public interface IPdfRenderer
{
    string ContentType { get; }
    string FileExtension { get; }
    Task<byte[]> RenderHtmlAsync(string html, CancellationToken cancellationToken);
}

public interface IModule
{
    string Name { get; }
    void Register(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}

public interface IPermissionSource
{
    IEnumerable<PermissionDefinition> GetPermissions();
}

public interface IStartupSync
{
    int Order { get; }
    Task SyncAsync(CancellationToken cancellationToken);
}

public enum IdempotencyStatus
{
    Missing,
    Replay,
    Conflict
}

public sealed record IdempotencyHit(IdempotencyStatus Status, int? ResponseStatus, string? ContentType, string? Body);

public interface IIdempotencyStore
{
    Task<IdempotencyHit> LookupAsync(string key, Guid? userId, string requestHash, CancellationToken cancellationToken);
    Task SaveAsync(string key, Guid? userId, string requestHash, int statusCode, string contentType, string body, CancellationToken cancellationToken);
}

public interface IMaintenanceJob
{
    string Name { get; }
    Task RunAsync(CancellationToken cancellationToken);
}

public interface IOutboxStore
{
    string Module { get; }
    Task<int> DispatchBatchAsync(IEventBus bus, CancellationToken cancellationToken);
}

public interface ITransactionalStore
{
    Task BeginAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}
