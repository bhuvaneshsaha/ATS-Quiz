using System.Text.Json;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Assessments.Domain;

public sealed record PaperSection(Guid SectionId, string Name, int Minutes, decimal Weight, IReadOnlyList<Guid> QuestionVersionIds);

public sealed record Paper(IReadOnlyList<PaperSection> Sections)
{
    public bool Contains(Guid questionVersionId) =>
        Sections.Any(section => section.QuestionVersionIds.Contains(questionVersionId));

    public PaperSection? SectionFor(Guid questionVersionId) =>
        Sections.FirstOrDefault(section => section.QuestionVersionIds.Contains(questionVersionId));
}

public sealed class Invitation : Entity, IAudited
{
    private Invitation()
    {
    }

    public Guid ApplicationId { get; private set; }
    public Guid StageBindingId { get; private set; }
    public Guid AssessmentVersionId { get; private set; }
    public string TokenHash { get; private set; } = "";
    public DateTimeOffset ExpiresAt { get; private set; }
    public int ExtraTimePercent { get; private set; }
    public InvitationStatus Status { get; private set; }
    public Guid SourceEventId { get; private set; }
    public string? CodeHash { get; private set; }
    public DateTimeOffset? CodeExpiresAt { get; private set; }
    public int CodeAttempts { get; private set; }
    public string CandidateEmail { get; private set; } = "";

    public static Invitation Create(
        Guid applicationId,
        Guid stageBindingId,
        Guid assessmentVersionId,
        string tokenHash,
        DateTimeOffset expiresAt,
        int extraTimePercent,
        Guid sourceEventId,
        string candidateEmail) =>
        new()
        {
            ApplicationId = applicationId,
            StageBindingId = stageBindingId,
            AssessmentVersionId = assessmentVersionId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            ExtraTimePercent = extraTimePercent,
            Status = InvitationStatus.Pending,
            SourceEventId = sourceEventId,
            CandidateEmail = candidateEmail
        };

    public void ReplaceToken(string tokenHash) => TokenHash = tokenHash;

    public void MarkSent(DateTimeOffset now)
    {
        Status = InvitationStatus.Sent;
        Raise(new InvitationDispatched(Guid.CreateVersion7(), now, Id, ApplicationId, ExpiresAt));
    }

    public void IssueCode(string codeHash, DateTimeOffset expiresAt)
    {
        CodeHash = codeHash;
        CodeExpiresAt = expiresAt;
        CodeAttempts = 0;
    }

    public RuleFailure? VerifyCode(string codeHash, DateTimeOffset now)
    {
        if (Status is InvitationStatus.Expired or InvitationStatus.Cancelled)
        {
            return new RuleFailure("invitation.closed", "This invitation is no longer valid.");
        }

        if (now > ExpiresAt)
        {
            Status = InvitationStatus.Expired;
            return new RuleFailure("invitation.expired", "This invitation has expired.");
        }

        if (CodeHash is null || CodeExpiresAt is null || now > CodeExpiresAt)
        {
            return new RuleFailure("invitation.code", "Request a new code.");
        }

        CodeAttempts++;
        if (CodeAttempts > 5)
        {
            return new RuleFailure("invitation.code", "Too many attempts. Request a new code.");
        }

        if (!string.Equals(CodeHash, codeHash, StringComparison.Ordinal))
        {
            return new RuleFailure("invitation.code", "That code is not correct.");
        }

        return null;
    }

    public void MarkStarted()
    {
        if (Status is InvitationStatus.Pending or InvitationStatus.Sent)
        {
            Status = InvitationStatus.Started;
        }
    }

    public void MarkSubmitted() => Status = InvitationStatus.Submitted;

    public void Expire() => Status = InvitationStatus.Expired;
}

public sealed class Attempt : Entity, IAudited
{
    private Attempt()
    {
    }

    public Guid InvitationId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset Deadline { get; private set; }
    public AttemptStatus Status { get; private set; }
    public string PaperJson { get; private set; } = "{}";
    public DateTimeOffset? SubmittedAt { get; private set; }

    public static Attempt Start(Guid invitationId, Guid applicationId, DateTimeOffset startedAt, DateTimeOffset deadline, Paper paper) =>
        new()
        {
            InvitationId = invitationId,
            ApplicationId = applicationId,
            StartedAt = startedAt,
            Deadline = deadline,
            Status = AttemptStatus.InProgress,
            PaperJson = JsonSerializer.Serialize(paper, QuestionJson.Options)
        };

    public Paper ReadPaper() =>
        JsonSerializer.Deserialize<Paper>(PaperJson, QuestionJson.Options)
        ?? new Paper([]);

    public bool Contains(Guid questionVersionId) => ReadPaper().Contains(questionVersionId);

    public DateTimeOffset SectionDeadline(Guid sectionId) => Deadline;

    public RuleFailure? EnsureOpen(DateTimeOffset now, Guid? sectionId = null)
    {
        if (Status != AttemptStatus.InProgress || now > Deadline || (sectionId is Guid id && now > SectionDeadline(id)))
        {
            return new RuleFailure("attempt.closed", "This attempt is closed.");
        }

        return null;
    }

    public void Close(AttemptStatus status, DateTimeOffset now)
    {
        if (Status != AttemptStatus.InProgress)
        {
            return;
        }

        Status = status;
        SubmittedAt = now;
        Raise(new AttemptClosed(Guid.CreateVersion7(), now, Id, ApplicationId, status.ToString()));
    }
}

public sealed record InvitationDispatched(Guid EventId, DateTimeOffset OccurredAt, Guid InvitationId, Guid ApplicationId, DateTimeOffset ExpiresAt) : IDomainEvent;

public sealed record AttemptClosed(Guid EventId, DateTimeOffset OccurredAt, Guid AttemptId, Guid ApplicationId, string Status) : IDomainEvent;

public sealed record ResultPublished(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid AttemptId,
    Guid ApplicationId,
    decimal Total,
    decimal Max,
    bool Passed,
    string Status) : IDomainEvent;

public sealed class Answer : Entity
{
    private Answer()
    {
    }

    public Guid AttemptId { get; private set; }
    public Guid QuestionVersionId { get; private set; }
    public string ResponseJson { get; private set; } = "{}";
    public int Sequence { get; private set; }
    public DateTimeOffset SavedAt { get; private set; }

    public static Answer Create(Guid attemptId, Guid questionVersionId, string responseJson, int sequence, DateTimeOffset savedAt) =>
        new()
        {
            AttemptId = attemptId,
            QuestionVersionId = questionVersionId,
            ResponseJson = responseJson,
            Sequence = sequence,
            SavedAt = savedAt
        };
}

public sealed class Grade : Entity, IAudited
{
    private Grade()
    {
    }

    public Guid AnswerId { get; private set; }
    public Guid? GraderId { get; private set; }
    public bool IsAutomatic { get; private set; }
    public decimal Score { get; private set; }
    public string? RubricScoresJson { get; private set; }
    public string? Comment { get; private set; }

    public static Grade Auto(Guid answerId, decimal score) =>
        new()
        {
            AnswerId = answerId,
            IsAutomatic = true,
            Score = score
        };

    public static Grade Manual(Guid answerId, Guid graderId, decimal score, string? comment, string? rubricScoresJson) =>
        new()
        {
            AnswerId = answerId,
            GraderId = graderId,
            IsAutomatic = false,
            Score = score,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            RubricScoresJson = rubricScoresJson
        };
}

public sealed class AssessmentResult : Entity, IAudited
{
    private AssessmentResult()
    {
    }

    public Guid AttemptId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public string SectionScoresJson { get; private set; } = "[]";
    public decimal Total { get; private set; }
    public decimal Max { get; private set; }
    public bool Passed { get; private set; }
    public ResultStatus Status { get; private set; }

    public static AssessmentResult Create(
        Guid attemptId,
        Guid applicationId,
        string sectionScoresJson,
        decimal total,
        decimal max,
        bool passed,
        ResultStatus status,
        DateTimeOffset now)
    {
        var result = new AssessmentResult
        {
            AttemptId = attemptId,
            ApplicationId = applicationId,
            SectionScoresJson = sectionScoresJson,
            Total = total,
            Max = max,
            Passed = passed,
            Status = status
        };

        if (status == ResultStatus.Final)
        {
            result.Raise(new ResultPublished(Guid.CreateVersion7(), now, attemptId, applicationId, total, max, passed, status.ToString()));
        }

        return result;
    }

    public void Update(string sectionScoresJson, decimal total, decimal max, bool passed, ResultStatus status, DateTimeOffset now)
    {
        SectionScoresJson = sectionScoresJson;
        Total = total;
        Max = max;
        Passed = passed;
        Status = status;
        if (status == ResultStatus.Final)
        {
            Raise(new ResultPublished(Guid.CreateVersion7(), now, AttemptId, ApplicationId, Total, Max, Passed, Status.ToString()));
        }
    }
}

public sealed class IntegrityEvent : Entity
{
    private IntegrityEvent()
    {
    }

    public Guid AttemptId { get; private set; }
    public string Type { get; private set; } = "";
    public DateTimeOffset At { get; private set; }
    public string? Detail { get; private set; }

    public static IntegrityEvent Record(Guid attemptId, string type, DateTimeOffset at, string? detail) =>
        new()
        {
            AttemptId = attemptId,
            Type = type,
            At = at,
            Detail = detail
        };
}

public static class QuestionJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record PresentedOption(Guid Id, string Text);
public sealed record PresentedItem(Guid Id, string Text);
public sealed record PresentedMatchSide(Guid Id, string Text);

public sealed record PresentedQuestion(
    Guid QuestionVersionId,
    string Type,
    string Prompt,
    decimal Points,
    int? TimeLimitSeconds,
    IReadOnlyList<PresentedOption> Options,
    IReadOnlyList<PresentedItem> Items,
    IReadOnlyList<PresentedMatchSide> Left,
    IReadOnlyList<PresentedMatchSide> Right);

public static class QuestionPresenter
{
    public static PresentedQuestion Present(QuestionVersion version, Guid shuffleSeed)
    {
        var content = version.Content;
        return content switch
        {
            SingleChoiceContent single => Choice(version, StableShuffle.Shuffle(single.Options, shuffleSeed).Select(option => new PresentedOption(option.Id, option.Text)).ToList(), [], [], []),
            MultipleChoiceContent multiple => Choice(version, StableShuffle.Shuffle(multiple.Options, shuffleSeed).Select(option => new PresentedOption(option.Id, option.Text)).ToList(), [], [], []),
            OrderingContent ordering => Choice(version, [], StableShuffle.Shuffle(ordering.Items, shuffleSeed).Select(item => new PresentedItem(item.Id, item.Text)).ToList(), [], []),
            MatchingContent matching => Choice(
                version,
                [],
                [],
                matching.Pairs.Select(pair => new PresentedMatchSide(pair.Id, pair.Left)).ToList(),
                StableShuffle.Shuffle(matching.Pairs, shuffleSeed).Select(pair => new PresentedMatchSide(pair.Id, pair.Right)).ToList()),
            _ => Choice(version, [], [], [], [])
        };
    }

    private static PresentedQuestion Choice(
        QuestionVersion version,
        IReadOnlyList<PresentedOption> options,
        IReadOnlyList<PresentedItem> items,
        IReadOnlyList<PresentedMatchSide> left,
        IReadOnlyList<PresentedMatchSide> right) =>
        new(version.Id, version.Type, version.Content.Prompt, version.Points, version.TimeLimitSeconds, options, items, left, right);
}
