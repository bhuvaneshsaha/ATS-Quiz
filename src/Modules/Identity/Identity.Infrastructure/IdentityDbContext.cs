using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Identity.Domain;

namespace Shortlist.Modules.Identity.Infrastructure;

public sealed class StaffUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public Guid WorkspaceId { get; set; }
    public string Status { get; set; } = "Active";
}

public sealed class StaffRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
    public bool IsTemplate { get; set; }
    public Guid WorkspaceId { get; set; }
}

public sealed class RolePermission
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = "";
}

public sealed class PermissionCatalogueEntry
{
    public string Key { get; set; } = "";
    public Guid WorkspaceId { get; set; }
    public string Module { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenant)
    : IdentityDbContext<StaffUser, StaffRole, Guid>(options)
{
    public const string Schema = "identity";

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<PermissionCatalogueEntry> PermissionCatalogue => Set<PermissionCatalogueEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<RolePermission>().HasKey(permission => new { permission.RoleId, permission.PermissionKey });
        modelBuilder.Entity<PermissionCatalogueEntry>().HasKey(entry => new { entry.WorkspaceId, entry.Key });
        modelBuilder.Entity<IdempotencyRecord>().HasIndex(record => new { record.WorkspaceId, record.Key, record.UserId }).IsUnique();
        modelBuilder.Entity<OutboxMessage>().HasKey(message => message.Id);
        modelBuilder.Entity<InboxMessage>().HasKey(message => message.Id);
        modelBuilder.Entity<AuditEntry>().HasKey(entry => entry.Id);
        WorkspaceFilters.Apply(modelBuilder, tenant);
        IgnoreEvents(modelBuilder);
    }

    private static void IgnoreEvents(ModelBuilder modelBuilder)
    {
        var types = modelBuilder.Model.GetEntityTypes().Select(type => type.ClrType).Where(type => typeof(Entity).IsAssignableFrom(type)).ToList();
        foreach (var type in types)
        {
            modelBuilder.Entity(type).Ignore(nameof(Entity.DomainEvents));
        }
    }
}

public sealed class IdentityOutboxStore(IdentityDbContext db, IClock clock) : OutboxStore<IdentityDbContext>(db, clock)
{
    public override string Module => "Identity";
    protected override string Schema => IdentityDbContext.Schema;
}

public sealed class IdentityDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>();
        Postgres.UseShortlist(options, Environment.GetEnvironmentVariable("SHORTLIST_CONNECTION")
            ?? "Host=127.0.0.1;Port=5432;Database=shortlist;Username=shortlist;Password=shortlist");
        return new IdentityDbContext(options.Options, new FixedTenant());
    }

    private sealed class FixedTenant : ITenantContext
    {
        public Guid WorkspaceId => Guid.Parse("019a0000-0000-7000-8000-000000000001");
    }
}
