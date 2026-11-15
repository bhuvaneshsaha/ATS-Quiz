using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Core.Domain;

public sealed class Opening : Entity, IAudited
{
    private readonly List<OpeningStage> _stages = [];
    private readonly List<OpeningPanelist> _panel = [];

    private Opening()
    {
    }

    public string Code { get; private set; } = "";
    public string Title { get; private set; } = "";
    public Guid HiringUnitId { get; private set; }
    public Guid FunctionId { get; private set; }
    public int Positions { get; private set; }
    public OpeningStatus Status { get; private set; }
    public string? Location { get; private set; }
    public string? Seniority { get; private set; }
    public string? Reason { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public Guid? HiringManagerId { get; private set; }
    public Guid? RecruiterId { get; private set; }
    public string? ExternalId { get; private set; }
    public uint RowVersion { get; private set; } = 1;
    public IReadOnlyList<OpeningStage> Stages => _stages;
    public IReadOnlyList<OpeningPanelist> Panel => _panel;

    public static Opening Create(
        string code,
        string title,
        Guid hiringUnitId,
        Guid functionId,
        int positions,
        string? location,
        string? seniority,
        string? reason,
        DateOnly? targetDate,
        Guid? hiringManagerId,
        Guid? recruiterId,
        string? externalId,
        IReadOnlyList<StageDefinition> catalogue)
    {
        var opening = new Opening
        {
            Code = code,
            Title = title.Trim(),
            HiringUnitId = hiringUnitId,
            FunctionId = functionId,
            Positions = positions,
            Status = OpeningStatus.Draft,
            Location = Clean(location),
            Seniority = Clean(seniority),
            Reason = Clean(reason),
            TargetDate = targetDate,
            HiringManagerId = hiringManagerId,
            RecruiterId = recruiterId,
            ExternalId = Clean(externalId)
        };

        foreach (var stage in catalogue.OrderBy(item => item.Order))
        {
            opening._stages.Add(OpeningStage.FromCatalogue(opening.Id, stage));
        }

        if (recruiterId is Guid recruiter)
        {
            opening._panel.Add(OpeningPanelist.Create(opening.Id, recruiter));
        }

        if (hiringManagerId is Guid manager && manager != recruiterId)
        {
            opening._panel.Add(OpeningPanelist.Create(opening.Id, manager));
        }

        return opening;
    }

    public RuleFailure? Update(
        string title,
        int positions,
        string? location,
        string? seniority,
        string? reason,
        DateOnly? targetDate,
        Guid? hiringManagerId,
        Guid? recruiterId,
        uint? expectedVersion)
    {
        if (expectedVersion is not null && expectedVersion.Value != RowVersion)
        {
            return new RuleFailure("opening.stale", "The opening was updated by someone else.");
        }

        if (Status is OpeningStatus.Filled or OpeningStatus.ClosedUnfilled)
        {
            return new RuleFailure("opening.closed", "A closed opening cannot be edited.");
        }

        Title = title.Trim();
        Positions = positions;
        Location = Clean(location);
        Seniority = Clean(seniority);
        Reason = Clean(reason);
        TargetDate = targetDate;
        HiringManagerId = hiringManagerId;
        RecruiterId = recruiterId;
        RowVersion++;
        return null;
    }

    public RuleFailure? ChangeStatus(OpeningStatus next)
    {
        var allowed = (Status, next) switch
        {
            (OpeningStatus.Draft, OpeningStatus.Open) => true,
            (OpeningStatus.Draft, OpeningStatus.ClosedUnfilled) => true,
            (OpeningStatus.Open, OpeningStatus.OnHold) => true,
            (OpeningStatus.Open, OpeningStatus.Filled) => true,
            (OpeningStatus.Open, OpeningStatus.ClosedUnfilled) => true,
            (OpeningStatus.OnHold, OpeningStatus.Open) => true,
            (OpeningStatus.OnHold, OpeningStatus.ClosedUnfilled) => true,
            _ => false
        };

        if (!allowed)
        {
            return new RuleFailure("opening.status", $"Cannot move an opening from {Status} to {next}.");
        }

        Status = next;
        RowVersion++;
        return null;
    }

    public void SetAutoAdvance(Guid openingStageId, bool autoAdvance)
    {
        var stage = _stages.Single(item => item.Id == openingStageId);
        stage.SetAutoAdvance(autoAdvance);
        RowVersion++;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class OpeningStage : Entity
{
    private OpeningStage()
    {
    }

    public Guid OpeningId { get; private set; }
    public Guid StageDefinitionId { get; private set; }
    public int Order { get; private set; }
    public string DisplayName { get; private set; } = "";
    public StageType Type { get; private set; }
    public string? AutomatedBy { get; private set; }
    public bool AutoAdvance { get; private set; }

    public static OpeningStage FromCatalogue(Guid openingId, StageDefinition definition) =>
        new()
        {
            OpeningId = openingId,
            StageDefinitionId = definition.Id,
            Order = definition.Order,
            DisplayName = definition.Name,
            Type = definition.Type,
            AutomatedBy = definition.AutomatedBy
        };

    public void SetAutoAdvance(bool autoAdvance) => AutoAdvance = autoAdvance;
}

public sealed class OpeningPanelist : Entity
{
    private OpeningPanelist()
    {
    }

    public Guid OpeningId { get; private set; }
    public Guid UserId { get; private set; }

    public static OpeningPanelist Create(Guid openingId, Guid userId) =>
        new() { OpeningId = openingId, UserId = userId };
}
