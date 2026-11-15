using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Core.Domain;

public enum HiringUnitType
{
    Department,
    Client
}

public enum OpeningStatus
{
    Draft,
    Open,
    OnHold,
    Filled,
    ClosedUnfilled
}

public enum StageType
{
    Applied,
    Assessment,
    Interview,
    Offer,
    SentToClient,
    Hired,
    Rejected,
    Withdrawn
}

public enum ApplicationStatus
{
    Active,
    Hired,
    Rejected,
    Withdrawn
}

public sealed record StageSeed(string Key, string Name, StageType Type, string? AutomatedBy, int Order);

public static class StageCatalogue
{
    public static IReadOnlyList<StageSeed> All { get; } =
    [
        new("applied", "Applied", StageType.Applied, null, 1),
        new("assessment", "Assessment", StageType.Assessment, "Assessments", 2),
        new("interview", "Interview", StageType.Interview, null, 3),
        new("offer", "Offer", StageType.Offer, null, 4),
        new("sent-to-client", "Sent to client", StageType.SentToClient, null, 5),
        new("hired", "Hired", StageType.Hired, null, 6),
        new("rejected", "Rejected", StageType.Rejected, null, 7),
        new("withdrawn", "Withdrawn", StageType.Withdrawn, null, 8)
    ];
}

public sealed class HiringUnit : Entity, IAudited
{
    private HiringUnit()
    {
    }

    public HiringUnitType Type { get; private set; }
    public string Name { get; private set; } = "";
    public Guid? OwnerUserId { get; private set; }
    public string? ExternalId { get; private set; }
    public bool IsArchived { get; private set; }

    public static HiringUnit Create(HiringUnitType type, string name, Guid? ownerUserId, string? externalId) =>
        new()
        {
            Type = type,
            Name = name.Trim(),
            OwnerUserId = ownerUserId,
            ExternalId = string.IsNullOrWhiteSpace(externalId) ? null : externalId.Trim()
        };

    public void Rename(string name, Guid? ownerUserId)
    {
        Name = name.Trim();
        OwnerUserId = ownerUserId;
    }

    public void Archive() => IsArchived = true;
}

public sealed class ClientContact : Entity, IAudited
{
    private ClientContact()
    {
    }

    public Guid HiringUnitId { get; private set; }
    public string Name { get; private set; } = "";
    public string Email { get; private set; } = "";
    public bool PortalAccess { get; private set; }

    public static ClientContact Create(Guid hiringUnitId, string name, string email) =>
        new()
        {
            HiringUnitId = hiringUnitId,
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PortalAccess = false
        };
}

public sealed class Function : Entity, IAudited
{
    private Function()
    {
    }

    public string Name { get; private set; } = "";
    public Guid? OwnerUserId { get; private set; }
    public bool IsArchived { get; private set; }

    public static Function Create(string name, Guid? ownerUserId) =>
        new()
        {
            Name = name.Trim(),
            OwnerUserId = ownerUserId
        };

    public void Rename(string name, Guid? ownerUserId)
    {
        Name = name.Trim();
        OwnerUserId = ownerUserId;
    }

    public void Archive() => IsArchived = true;
}

public sealed class StageDefinition : Entity
{
    private StageDefinition()
    {
    }

    public string Key { get; private set; } = "";
    public string Name { get; private set; } = "";
    public StageType Type { get; private set; }
    public string? AutomatedBy { get; private set; }
    public int Order { get; private set; }

    public static StageDefinition Create(StageSeed seed) =>
        new()
        {
            Key = seed.Key,
            Name = seed.Name,
            Type = seed.Type,
            AutomatedBy = seed.AutomatedBy,
            Order = seed.Order
        };
}

public sealed class OpeningNumber
{
    public Guid WorkspaceId { get; set; }
    public int NextNumber { get; set; } = 1;
}
