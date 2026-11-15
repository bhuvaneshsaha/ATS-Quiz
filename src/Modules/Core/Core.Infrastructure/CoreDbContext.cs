using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Core.Contracts;
using Shortlist.Modules.Core.Domain;

namespace Shortlist.Modules.Core.Infrastructure;

public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public const string Schema = "core";

    public DbSet<HiringUnit> HiringUnits => Set<HiringUnit>();
    public DbSet<ClientContact> ClientContacts => Set<ClientContact>();
    public DbSet<Function> Functions => Set<Function>();
    public DbSet<StageDefinition> StageDefinitions => Set<StageDefinition>();
    public DbSet<OpeningNumber> OpeningNumbers => Set<OpeningNumber>();
    public DbSet<Opening> Openings => Set<Opening>();
    public DbSet<OpeningStage> OpeningStages => Set<OpeningStage>();
    public DbSet<OpeningPanelist> OpeningPanelists => Set<OpeningPanelist>();
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Domain.Application> Applications => Set<Domain.Application>();
    public DbSet<StageTransition> StageTransitions => Set<StageTransition>();
    public DbSet<Note> Notes => Set<Note>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ApplicationAssessmentResult> AssessmentResults => Set<ApplicationAssessmentResult>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<OpeningNumber>().HasKey(number => number.WorkspaceId);
        modelBuilder.Entity<Opening>().HasIndex(opening => new { opening.WorkspaceId, opening.Code }).IsUnique();
        modelBuilder.Entity<Opening>().HasMany(opening => opening.Stages).WithOne().HasForeignKey(stage => stage.OpeningId);
        modelBuilder.Entity<Opening>().HasMany(opening => opening.Panel).WithOne().HasForeignKey(panelist => panelist.OpeningId);
        modelBuilder.Entity<Opening>().Navigation(opening => opening.Stages).HasField("_stages").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Opening>().Navigation(opening => opening.Panel).HasField("_panel").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Domain.Application>().HasIndex(application => new { application.WorkspaceId, application.CandidateId, application.OpeningId }).IsUnique();
        modelBuilder.Entity<Candidate>().HasIndex(candidate => new { candidate.WorkspaceId, candidate.Email });
        modelBuilder.Entity<OutboxMessage>().HasIndex(message => message.ProcessedAt);
        modelBuilder.Entity<InboxMessage>().HasKey(message => message.Id);
        modelBuilder.Entity<AuditEntry>().HasKey(entry => entry.Id);
        modelBuilder.Entity<OutboxMessage>().HasKey(message => message.Id);
        WorkspaceFilters.Apply(modelBuilder, tenant);
        modelBuilder.Entity<Candidate>().HasQueryFilter(candidate => candidate.WorkspaceId == tenant.WorkspaceId && !candidate.IsErased);
        IgnoreEvents(modelBuilder);
    }

    private static void IgnoreEvents(ModelBuilder modelBuilder)
    {
        var types = modelBuilder.Model.GetEntityTypes()
            .Select(type => type.ClrType)
            .Where(type => typeof(Entity).IsAssignableFrom(type))
            .ToList();
        foreach (var type in types)
        {
            modelBuilder.Entity(type).Ignore(nameof(Entity.DomainEvents));
        }
    }
}

public sealed class CoreOutboxStore(CoreDbContext db, IClock clock) : OutboxStore<CoreDbContext>(db, clock)
{
    public override string Module => "Core";
    protected override string Schema => CoreDbContext.Schema;
}

public sealed class CoreEventMapper : IIntegrationEventMapper
{
    public IIntegrationEvent? Map(IDomainEvent domainEvent, Guid workspaceId) => domainEvent switch
    {
        ApplicationMoved moved => new ApplicationStageChanged(
            moved.EventId,
            moved.OccurredAt,
            workspaceId,
            moved.ApplicationId,
            moved.OpeningId,
            moved.OpeningStageId,
            moved.CandidateId,
            moved.StageType,
            moved.AutomatedBy),
        _ => null
    };
}

public sealed class CoreApplicationReader(CoreDbContext db) : IApplicationReader
{
    public async Task<ApplicationSnapshot?> FindAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var row = await (
            from application in db.Applications
            join candidate in db.Candidates.IgnoreQueryFilters() on application.CandidateId equals candidate.Id
            where application.Id == applicationId
            select new ApplicationSnapshot(application.Id, application.OpeningId, application.CandidateId, application.CurrentStageId, candidate.Email, candidate.Name))
            .SingleOrDefaultAsync(cancellationToken);
        return row;
    }

    public async Task<OpeningStageSnapshot?> FindStageAsync(Guid openingStageId, CancellationToken cancellationToken)
    {
        var stage = await db.OpeningStages.SingleOrDefaultAsync(item => item.Id == openingStageId, cancellationToken);
        return stage is null
            ? null
            : new OpeningStageSnapshot(stage.Id, stage.OpeningId, stage.DisplayName, stage.Type.ToString(), stage.AutomatedBy, stage.AutoAdvance);
    }
}

public sealed class CoreDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>();
        Postgres.UseShortlist(options, Environment.GetEnvironmentVariable("SHORTLIST_CONNECTION")
            ?? "Host=127.0.0.1;Port=5432;Database=shortlist;Username=shortlist;Password=shortlist");
        return new CoreDbContext(options.Options, new FixedTenant());
    }

    private sealed class FixedTenant : ITenantContext
    {
        public Guid WorkspaceId => Guid.Parse("019a0000-0000-7000-8000-000000000001");
    }
}
