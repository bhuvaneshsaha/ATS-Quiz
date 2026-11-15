using Microsoft.EntityFrameworkCore;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Assessments.Contracts;
using Shortlist.Modules.Assessments.Domain;

namespace Shortlist.Modules.Assessments.Infrastructure;

public sealed class AssessmentsDbContext(DbContextOptions<AssessmentsDbContext> options, ITenantContext tenant) : DbContext(options)
{
    public const string Schema = "assessments";

    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionVersion> QuestionVersions => Set<QuestionVersion>();
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentVersion> AssessmentVersions => Set<AssessmentVersion>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<StageBinding> StageBindings => Set<StageBinding>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<Answer> Answers => Set<Answer>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<AssessmentResult> Results => Set<AssessmentResult>();
    public DbSet<IntegrityEvent> IntegrityEvents => Set<IntegrityEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Question>().HasMany(question => question.Versions).WithOne().HasForeignKey(version => version.QuestionId);
        modelBuilder.Entity<Question>().Navigation(question => question.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<QuestionVersion>().Property(version => version.Content).HasColumnType("jsonb").HasConversion(
            content => System.Text.Json.JsonSerializer.Serialize(content, QuestionJson.Options),
            json => System.Text.Json.JsonSerializer.Deserialize<QuestionContent>(json, QuestionJson.Options)!);
        modelBuilder.Entity<Assessment>().HasMany(assessment => assessment.Versions).WithOne().HasForeignKey(version => version.AssessmentId);
        modelBuilder.Entity<Assessment>().Navigation(assessment => assessment.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<AssessmentVersion>().HasMany(version => version.Sections).WithOne().HasForeignKey(section => section.AssessmentVersionId);
        modelBuilder.Entity<AssessmentVersion>().Navigation(version => version.Sections).HasField("_sections").UsePropertyAccessMode(PropertyAccessMode.Field);
        modelBuilder.Entity<Section>().Property(section => section.Draw).HasColumnType("jsonb").HasConversion(
            draw => draw == null ? null : System.Text.Json.JsonSerializer.Serialize(draw, QuestionJson.Options),
            json => json == null ? null : System.Text.Json.JsonSerializer.Deserialize<RandomDraw>(json, QuestionJson.Options));
        modelBuilder.Entity<Answer>().Property(answer => answer.ResponseJson).HasColumnName("response").HasColumnType("jsonb");
        modelBuilder.Entity<Answer>().HasIndex(answer => new { answer.AttemptId, answer.QuestionVersionId }).IsUnique();
        modelBuilder.Entity<Attempt>().Property(attempt => attempt.PaperJson).HasColumnType("jsonb");
        modelBuilder.Entity<Invitation>().HasIndex(invitation => invitation.TokenHash);
        modelBuilder.Entity<Invitation>().HasIndex(invitation => invitation.SourceEventId).IsUnique();
        modelBuilder.Entity<OutboxMessage>().HasKey(message => message.Id);
        modelBuilder.Entity<InboxMessage>().HasKey(message => message.Id);
        modelBuilder.Entity<AuditEntry>().HasKey(entry => entry.Id);
        WorkspaceFilters.Apply(modelBuilder, tenant);
        var types = modelBuilder.Model.GetEntityTypes().Select(type => type.ClrType).Where(type => typeof(Entity).IsAssignableFrom(type)).ToList();
        foreach (var type in types)
        {
            modelBuilder.Entity(type).Ignore(nameof(Entity.DomainEvents));
        }
    }
}

public sealed class AssessmentsOutboxStore(AssessmentsDbContext db, IClock clock) : OutboxStore<AssessmentsDbContext>(db, clock)
{
    public override string Module => "Assessments";
    protected override string Schema => AssessmentsDbContext.Schema;
}

public sealed class AssessmentsEventMapper : IIntegrationEventMapper
{
    public IIntegrationEvent? Map(IDomainEvent domainEvent, Guid workspaceId) => domainEvent switch
    {
        InvitationDispatched invited => new AssessmentInvited(invited.EventId, invited.OccurredAt, workspaceId, invited.ApplicationId, invited.InvitationId, invited.ExpiresAt),
        AttemptClosed closed => new AssessmentSubmitted(closed.EventId, closed.OccurredAt, workspaceId, closed.ApplicationId, closed.AttemptId),
        ResultPublished published => new AssessmentResultReady(published.EventId, published.OccurredAt, workspaceId, published.ApplicationId, published.AttemptId, published.Total, published.Max, published.Passed, published.Status),
        _ => null
    };
}

public sealed class AssessmentsDesignTimeFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AssessmentsDbContext>
{
    public AssessmentsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AssessmentsDbContext>();
        Postgres.UseShortlist(options, Environment.GetEnvironmentVariable("SHORTLIST_CONNECTION")
            ?? "Host=127.0.0.1;Port=5432;Database=shortlist;Username=shortlist;Password=shortlist");
        return new AssessmentsDbContext(options.Options, new FixedTenant());
    }

    private sealed class FixedTenant : ITenantContext
    {
        public Guid WorkspaceId => Guid.Parse("019a0000-0000-7000-8000-000000000001");
    }
}
