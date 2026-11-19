using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Assessments.Domain;

public sealed record RandomDraw(int Count, IReadOnlyList<string> Types, IReadOnlyList<Guid> SkillIds);

public sealed class Assessment : Entity, IAudited
{
    private readonly List<AssessmentVersion> _versions = [];

    private Assessment()
    {
    }

    public Guid FunctionId { get; private set; }
    public string Name { get; private set; } = "";
    public AssessmentStatus Status { get; private set; }
    public Guid? CurrentVersionId { get; private set; }
    public IReadOnlyList<AssessmentVersion> Versions => _versions;

    public static Assessment Create(Guid functionId, string name) =>
        new()
        {
            FunctionId = functionId,
            Name = name.Trim(),
            Status = AssessmentStatus.Draft
        };

    public AssessmentVersion AddDraft(decimal passMark, bool desktopOnly, IReadOnlyList<SectionDraft> sections)
    {
        var number = _versions.Count == 0 ? 1 : _versions.Max(version => version.Number) + 1;
        var version = AssessmentVersion.Create(Id, number, passMark, desktopOnly, sections);
        _versions.Add(version);
        CurrentVersionId = version.Id;
        Status = AssessmentStatus.Draft;
        return version;
    }

    public RuleFailure? Publish(DateTimeOffset now)
    {
        if (CurrentVersionId is null)
        {
            return new RuleFailure("assessment.empty", "Add a version before publishing.");
        }

        var current = _versions.Single(version => version.Id == CurrentVersionId);
        var failure = current.Publish(now);
        if (failure is not null)
        {
            return failure;
        }

        Status = AssessmentStatus.Published;
        return null;
    }
}

public sealed record SectionDraft(
    string Name,
    int Minutes,
    decimal Weight,
    SectionSource Source,
    IReadOnlyList<Guid> QuestionVersionIds,
    RandomDraw? Draw);

public sealed class AssessmentVersion : Entity
{
    private readonly List<Section> _sections = [];

    private AssessmentVersion()
    {
    }

    public Guid AssessmentId { get; private set; }
    public int Number { get; private set; }
    public decimal PassMark { get; private set; }
    public bool DesktopOnly { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public bool IsPublished { get; private set; }
    public IReadOnlyList<Section> Sections => _sections;

    public static AssessmentVersion Create(Guid assessmentId, int number, decimal passMark, bool desktopOnly, IReadOnlyList<SectionDraft> sections)
    {
        var version = new AssessmentVersion
        {
            AssessmentId = assessmentId,
            Number = number,
            PassMark = passMark,
            DesktopOnly = desktopOnly
        };

        var order = 1;
        foreach (var section in sections)
        {
            version._sections.Add(Section.Create(version.Id, order++, section));
        }

        return version;
    }

    public RuleFailure? Publish(DateTimeOffset now)
    {
        if (IsPublished)
        {
            return new RuleFailure("assessment.published", "This version is already published.");
        }

        if (_sections.Count == 0)
        {
            return new RuleFailure("assessment.sections", "A test needs at least one section.");
        }

        if (_sections.Any(section => section.Minutes <= 0))
        {
            return new RuleFailure("assessment.minutes", "Every section needs a time limit.");
        }

        if (_sections.Any(section => section.Source == SectionSource.Fixed && section.QuestionVersionIds.Count == 0))
        {
            return new RuleFailure("assessment.questions", "A fixed section needs questions.");
        }

        if (_sections.Any(section => section.Source == SectionSource.RandomDraw && (section.Draw is null || section.Draw.Count <= 0)))
        {
            return new RuleFailure("assessment.draw", "A random section needs a draw size.");
        }

        IsPublished = true;
        PublishedAt = now;
        return null;
    }

    public int TotalMinutes => _sections.Sum(section => section.Minutes);
}

public sealed class Section : Entity
{
    private Section()
    {
    }

    public Guid AssessmentVersionId { get; private set; }
    public int Order { get; private set; }
    public string Name { get; private set; } = "";
    public int Minutes { get; private set; }
    public decimal Weight { get; private set; }
    public SectionSource Source { get; private set; }
    public List<Guid> QuestionVersionIds { get; private set; } = [];
    public RandomDraw? Draw { get; private set; }

    public static Section Create(Guid assessmentVersionId, int order, SectionDraft draft) =>
        new()
        {
            AssessmentVersionId = assessmentVersionId,
            Order = order,
            Name = draft.Name.Trim(),
            Minutes = draft.Minutes,
            Weight = draft.Weight <= 0 ? 1 : draft.Weight,
            Source = draft.Source,
            QuestionVersionIds = draft.QuestionVersionIds.ToList(),
            Draw = draft.Draw
        };
}

public sealed class StageBinding : Entity, IAudited
{
    private StageBinding()
    {
    }

    public Guid OpeningStageId { get; private set; }
    public Guid AssessmentVersionId { get; private set; }
    public decimal? PassMarkOverride { get; private set; }
    public int ValidDays { get; private set; }
    public int ExtraTimePercent { get; private set; }

    public static StageBinding Create(Guid openingStageId, Guid assessmentVersionId, decimal? passMarkOverride, int validDays, int extraTimePercent) =>
        new()
        {
            OpeningStageId = openingStageId,
            AssessmentVersionId = assessmentVersionId,
            PassMarkOverride = passMarkOverride,
            ValidDays = validDays,
            ExtraTimePercent = extraTimePercent
        };

    public void Retarget(Guid assessmentVersionId, decimal? passMarkOverride, int validDays, int extraTimePercent)
    {
        AssessmentVersionId = assessmentVersionId;
        PassMarkOverride = passMarkOverride;
        ValidDays = validDays;
        ExtraTimePercent = extraTimePercent;
    }
}
