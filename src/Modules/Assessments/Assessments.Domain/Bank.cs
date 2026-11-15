using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Assessments.Domain;

public sealed class Skill : Entity, IAudited
{
    private Skill()
    {
    }

    public Guid FunctionId { get; private set; }
    public string Name { get; private set; } = "";

    public static Skill Create(Guid functionId, string name) =>
        new() { FunctionId = functionId, Name = name.Trim() };
}

public sealed class Question : Entity, IAudited
{
    private readonly List<QuestionVersion> _versions = [];

    private Question()
    {
    }

    public Guid FunctionId { get; private set; }
    public QuestionStatus Status { get; private set; }
    public Guid CurrentVersionId { get; private set; }
    public IReadOnlyList<QuestionVersion> Versions => _versions;

    public static Question Create(Guid functionId, QuestionContent content, decimal points, int? timeLimitSeconds, IReadOnlyList<Guid> skillIds)
    {
        var failure = QuestionRules.Validate(content, points);
        if (failure is not null)
        {
            throw new DomainRuleException(failure.Value.Code, failure.Value.Message);
        }

        var question = new Question
        {
            FunctionId = functionId,
            Status = QuestionStatus.Draft
        };
        var version = QuestionVersion.Create(question.Id, 1, content, points, timeLimitSeconds, skillIds);
        question._versions.Add(version);
        question.CurrentVersionId = version.Id;
        return question;
    }

    public RuleFailure? SubmitForReview()
    {
        if (Status != QuestionStatus.Draft)
        {
            return new RuleFailure("question.status", "Only a draft can be submitted for review.");
        }

        Status = QuestionStatus.InReview;
        return null;
    }

    public RuleFailure? Publish(DateTimeOffset now)
    {
        if (Status != QuestionStatus.InReview)
        {
            return new RuleFailure("question.status", "Only a question in review can be approved.");
        }

        var current = Current();
        current.Publish(now);
        Status = QuestionStatus.Published;
        return null;
    }

    public RuleFailure? Revise(QuestionContent content, decimal points, int? timeLimitSeconds, IReadOnlyList<Guid> skillIds)
    {
        if (Status != QuestionStatus.Published)
        {
            return new RuleFailure("question.status", "Revise a published question to create the next version.");
        }

        var failure = QuestionRules.Validate(content, points);
        if (failure is not null)
        {
            return failure;
        }

        var version = QuestionVersion.Create(Id, Current().Number + 1, content, points, timeLimitSeconds, skillIds);
        _versions.Add(version);
        CurrentVersionId = version.Id;
        Status = QuestionStatus.Draft;
        return null;
    }

    public void Archive() => Status = QuestionStatus.Archived;

    public QuestionVersion Current() => _versions.Single(version => version.Id == CurrentVersionId);
}

public sealed class QuestionVersion : Entity
{
    private QuestionVersion()
    {
    }

    public Guid QuestionId { get; private set; }
    public int Number { get; private set; }
    public string Type { get; private set; } = "";
    public QuestionContent Content { get; private set; } = null!;
    public decimal Points { get; private set; }
    public int? TimeLimitSeconds { get; private set; }
    public List<Guid> SkillIds { get; private set; } = [];
    public bool IsPublished { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static QuestionVersion Create(Guid questionId, int number, QuestionContent content, decimal points, int? timeLimitSeconds, IReadOnlyList<Guid> skillIds) =>
        new()
        {
            QuestionId = questionId,
            Number = number,
            Type = QuestionRules.TypeName(content),
            Content = content,
            Points = points,
            TimeLimitSeconds = timeLimitSeconds,
            SkillIds = skillIds.ToList()
        };

    public void ReplaceDraft(QuestionContent content, decimal points, int? timeLimitSeconds, IReadOnlyList<Guid> skillIds)
    {
        if (IsPublished)
        {
            throw new DomainRuleException("question.version_published", "A published question version cannot be edited.");
        }

        var failure = QuestionRules.Validate(content, points);
        if (failure is not null)
        {
            throw new DomainRuleException(failure.Value.Code, failure.Value.Message);
        }

        Content = content;
        Points = points;
        TimeLimitSeconds = timeLimitSeconds;
        Type = QuestionRules.TypeName(content);
        SkillIds = skillIds.ToList();
    }

    public void Publish(DateTimeOffset now)
    {
        IsPublished = true;
        PublishedAt = now;
    }
}
