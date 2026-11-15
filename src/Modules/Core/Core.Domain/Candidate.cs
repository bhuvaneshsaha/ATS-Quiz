using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Core.Domain;

public sealed record ApplicationMoved(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid ApplicationId,
    Guid OpeningId,
    Guid OpeningStageId,
    Guid CandidateId,
    string StageType,
    string? AutomatedBy) : IDomainEvent;

public sealed class Candidate : Entity, IAudited
{
    private Candidate()
    {
    }

    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string? Phone { get; private set; }
    public string? Location { get; private set; }
    public string? Source { get; private set; }
    public string? ExternalId { get; private set; }
    public DateTimeOffset? ConsentAt { get; private set; }
    public string? ConsentText { get; private set; }
    public bool IsErased { get; private set; }
    public DateTimeOffset? ErasedAt { get; private set; }

    public static Candidate Create(
        string name,
        string email,
        string? phone,
        string? location,
        string? source,
        string? externalId,
        bool consent,
        string? consentText,
        DateTimeOffset now)
    {
        return new Candidate
        {
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            Phone = Clean(phone),
            Location = Clean(location),
            Source = Clean(source),
            ExternalId = Clean(externalId),
            ConsentAt = consent ? now : null,
            ConsentText = consent ? consentText?.Trim() : null
        };
    }

    public void Update(string name, string? phone, string? location, string? source)
    {
        if (IsErased)
        {
            throw new DomainRuleException("candidate.erased", "An erased candidate cannot be edited.");
        }

        Name = name.Trim();
        Phone = Clean(phone);
        Location = Clean(location);
        Source = Clean(source);
    }

    public IReadOnlyList<string> Erase(DateTimeOffset now)
    {
        IsErased = true;
        ErasedAt = now;
        Name = "Erased candidate";
        var previousEmail = Email;
        Email = $"erased+{Id:N}@shortlist.invalid";
        Phone = null;
        Location = null;
        Source = null;
        ExternalId = null;
        ConsentText = null;
        return [previousEmail];
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class Application : Entity, IAudited
{
    private Application()
    {
    }

    public Guid CandidateId { get; private set; }
    public Guid OpeningId { get; private set; }
    public Guid CurrentStageId { get; private set; }
    public ApplicationStatus Status { get; private set; }

    public static Application Start(Guid candidateId, Guid openingId, OpeningStage appliedStage) =>
        new()
        {
            CandidateId = candidateId,
            OpeningId = openingId,
            CurrentStageId = appliedStage.Id,
            Status = ApplicationStatus.Active
        };

    public RuleFailure? MoveTo(OpeningStage stage, Guid? byUserId, string? byModule, DateTimeOffset now)
    {
        if (Status is ApplicationStatus.Hired or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
        {
            return new RuleFailure("application.terminal", "This application is already finished.");
        }

        if (stage.OpeningId != OpeningId)
        {
            return new RuleFailure("application.stage", "That stage belongs to a different opening.");
        }

        if (stage.Id == CurrentStageId)
        {
            return new RuleFailure("application.stage", "The candidate is already in that stage.");
        }

        var from = CurrentStageId;
        CurrentStageId = stage.Id;
        Status = stage.Type switch
        {
            StageType.Hired => ApplicationStatus.Hired,
            StageType.Rejected => ApplicationStatus.Rejected,
            StageType.Withdrawn => ApplicationStatus.Withdrawn,
            _ => ApplicationStatus.Active
        };

        Raise(new ApplicationMoved(
            Guid.CreateVersion7(),
            now,
            Id,
            OpeningId,
            stage.Id,
            CandidateId,
            stage.Type.ToString(),
            stage.AutomatedBy));

        _ = (from, byUserId, byModule);
        return null;
    }
}

public sealed class StageTransition : Entity
{
    private StageTransition()
    {
    }

    public Guid ApplicationId { get; private set; }
    public Guid? FromStageId { get; private set; }
    public Guid ToStageId { get; private set; }
    public Guid? ByUserId { get; private set; }
    public string? ByModule { get; private set; }
    public DateTimeOffset At { get; private set; }

    public static StageTransition Record(Guid applicationId, Guid? fromStageId, Guid toStageId, Guid? byUserId, string? byModule, DateTimeOffset at) =>
        new()
        {
            ApplicationId = applicationId,
            FromStageId = fromStageId,
            ToStageId = toStageId,
            ByUserId = byUserId,
            ByModule = byModule,
            At = at
        };
}

public sealed class Note : Entity, IAudited
{
    private Note()
    {
    }

    public Guid? ApplicationId { get; private set; }
    public Guid? CandidateId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = "";

    public static Note Write(Guid? applicationId, Guid? candidateId, Guid authorUserId, string body) =>
        new()
        {
            ApplicationId = applicationId,
            CandidateId = candidateId,
            AuthorUserId = authorUserId,
            Body = body.Trim()
        };
}

public sealed class Document : Entity, IAudited
{
    private Document()
    {
    }

    public Guid? ApplicationId { get; private set; }
    public Guid? CandidateId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string FileKey { get; private set; } = "";
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";

    public static Document Store(Guid? applicationId, Guid? candidateId, Guid authorUserId, string fileKey, string fileName, string contentType) =>
        new()
        {
            ApplicationId = applicationId,
            CandidateId = candidateId,
            AuthorUserId = authorUserId,
            FileKey = fileKey,
            FileName = fileName,
            ContentType = contentType
        };
}

public sealed class ApplicationAssessmentResult : Entity
{
    private ApplicationAssessmentResult()
    {
    }

    public Guid ApplicationId { get; private set; }
    public Guid AttemptId { get; private set; }
    public decimal Total { get; private set; }
    public decimal Max { get; private set; }
    public bool Passed { get; private set; }
    public string Status { get; private set; } = "";
    public DateTimeOffset RecordedAt { get; private set; }

    public static ApplicationAssessmentResult Record(
        Guid applicationId,
        Guid attemptId,
        decimal total,
        decimal max,
        bool passed,
        string status,
        DateTimeOffset recordedAt) =>
        new()
        {
            ApplicationId = applicationId,
            AttemptId = attemptId,
            Total = total,
            Max = max,
            Passed = passed,
            Status = status,
            RecordedAt = recordedAt
        };

    public void Update(decimal total, decimal max, bool passed, string status, DateTimeOffset recordedAt)
    {
        Total = total;
        Max = max;
        Passed = passed;
        Status = status;
        RecordedAt = recordedAt;
    }
}
