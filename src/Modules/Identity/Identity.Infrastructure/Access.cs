using Microsoft.EntityFrameworkCore;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Infrastructure;

namespace Shortlist.Modules.Identity.Infrastructure;

public sealed class AccessService(IdentityDbContext db, ICacheStore cache) : IPermissionChecker, IRoleAssignments, IUserLookup
{
    public async Task<bool> HasAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await LoadAsync(userId, cancellationToken)).Any(grant => grant.Permission == permission);

    public async Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        (await LoadAsync(userId, cancellationToken)).Select(grant => grant.Permission).Distinct().OrderBy(key => key).ToList();

    public Task InvalidateAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.RemoveAsync(Key(userId), cancellationToken);

    public async Task<IReadOnlyList<AccessGrant>> FindAsync(Guid userId, string permission, CancellationToken cancellationToken) =>
        (await LoadAsync(userId, cancellationToken)).Where(grant => grant.Permission == permission).ToList();

    public async Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        return user is null ? null : new UserSummary(user.Id, user.Email ?? "", user.DisplayName, user.Status);
    }

    public async Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        var users = await db.Users.Where(user => userIds.Contains(user.Id)).ToListAsync(cancellationToken);
        return users.Select(user => new UserSummary(user.Id, user.Email ?? "", user.DisplayName, user.Status)).ToList();
    }

    private async Task<List<AccessGrant>> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync<List<AccessGrant>>(Key(userId), cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var assignments = await db.RoleAssignments.Where(assignment => assignment.UserId == userId).ToListAsync(cancellationToken);
        var roleIds = assignments.Select(assignment => assignment.RoleId).Distinct().ToList();
        var permissions = await db.RolePermissions.Where(permission => roleIds.Contains(permission.RoleId)).ToListAsync(cancellationToken);
        var grants = new List<AccessGrant>();
        foreach (var assignment in assignments)
        {
            foreach (var permission in permissions.Where(item => item.RoleId == assignment.RoleId))
            {
                grants.Add(new AccessGrant(permission.PermissionKey, assignment.Scope, assignment.HiringUnitIds, assignment.FunctionIds));
            }
        }

        await cache.SetAsync(Key(userId), grants, TimeSpan.FromMinutes(5), cancellationToken);
        return grants;
    }

    private static string Key(Guid userId) => $"authz:{userId}";
}

public sealed class IdempotencyStore(IdentityDbContext db, ITenantContext tenant, IClock clock) : IIdempotencyStore
{
    public async Task<IdempotencyHit> LookupAsync(string key, Guid? userId, string requestHash, CancellationToken cancellationToken)
    {
        var record = await db.IdempotencyRecords.SingleOrDefaultAsync(
            item => item.WorkspaceId == tenant.WorkspaceId && item.Key == key && item.UserId == userId,
            cancellationToken);
        if (record is null)
        {
            return new IdempotencyHit(IdempotencyStatus.Missing, null, null, null);
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new IdempotencyHit(IdempotencyStatus.Conflict, null, null, null);
        }

        return new IdempotencyHit(IdempotencyStatus.Replay, record.StatusCode, record.ContentType, record.Body);
    }

    public async Task SaveAsync(string key, Guid? userId, string requestHash, int statusCode, string contentType, string body, CancellationToken cancellationToken)
    {
        var existing = await db.IdempotencyRecords.SingleOrDefaultAsync(
            item => item.WorkspaceId == tenant.WorkspaceId && item.Key == key && item.UserId == userId,
            cancellationToken);
        if (existing is not null)
        {
            return;
        }

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = tenant.WorkspaceId,
            Key = key,
            UserId = userId,
            RequestHash = requestHash,
            StatusCode = statusCode,
            ContentType = contentType,
            Body = body,
            CreatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
