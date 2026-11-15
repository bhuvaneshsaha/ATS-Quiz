using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Identity.Domain;

public sealed class RoleAssignment : Entity, IAudited
{
    private RoleAssignment()
    {
    }

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Scope Scope { get; private set; }
    public List<Guid> HiringUnitIds { get; private set; } = [];
    public List<Guid> FunctionIds { get; private set; } = [];

    public static RoleAssignment Create(Guid userId, Guid roleId, Scope scope, IReadOnlyList<Guid> hiringUnitIds, IReadOnlyList<Guid> functionIds) =>
        new()
        {
            UserId = userId,
            RoleId = roleId,
            Scope = scope,
            HiringUnitIds = hiringUnitIds.Distinct().ToList(),
            FunctionIds = functionIds.Distinct().ToList()
        };
}
