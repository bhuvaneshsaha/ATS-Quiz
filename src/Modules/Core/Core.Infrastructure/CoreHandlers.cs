using Microsoft.EntityFrameworkCore;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.Modules.Core.Application;
using Shortlist.Modules.Core.Domain;
using App = Shortlist.Modules.Core.Domain.Application;

namespace Shortlist.Modules.Core.Infrastructure;

internal sealed class CreateHiringUnitHandler(CoreDbContext db) : ICommandHandler<CreateHiringUnit, Result<HiringUnitResponse>>
{
    public async Task<Result<HiringUnitResponse>> Handle(CreateHiringUnit command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<HiringUnitType>(command.Type, out var type))
        {
            return Error.Validation("hiring_unit.type", "Type must be Department or Client.");
        }

        var unit = HiringUnit.Create(type, command.Name, command.OwnerUserId, command.ExternalId);
        db.HiringUnits.Add(unit);
        await db.SaveChangesAsync(cancellationToken);
        return Map(unit);
    }

    internal static HiringUnitResponse Map(HiringUnit unit) =>
        new(unit.Id, unit.Type.ToString(), unit.Name, unit.OwnerUserId, unit.ExternalId, unit.IsArchived);
}

internal sealed class ListHiringUnitsHandler(CoreDbContext db) : IQueryHandler<ListHiringUnits, Result<IReadOnlyList<HiringUnitResponse>>>
{
    public async Task<Result<IReadOnlyList<HiringUnitResponse>>> Handle(ListHiringUnits query, CancellationToken cancellationToken)
    {
        var units = await db.HiringUnits
            .Where(unit => query.IncludeArchived || !unit.IsArchived)
            .OrderBy(unit => unit.Name)
            .ToListAsync(cancellationToken);
        return units.Select(CreateHiringUnitHandler.Map).ToList();
    }
}

internal sealed class CreateFunctionHandler(CoreDbContext db) : ICommandHandler<CreateFunction, Result<FunctionResponse>>
{
    public async Task<Result<FunctionResponse>> Handle(CreateFunction command, CancellationToken cancellationToken)
    {
        var function = Function.Create(command.Name, command.OwnerUserId);
        db.Functions.Add(function);
        await db.SaveChangesAsync(cancellationToken);
        return new FunctionResponse(function.Id, function.Name, function.OwnerUserId, function.IsArchived);
    }
}

internal sealed class ListFunctionsHandler(CoreDbContext db) : IQueryHandler<ListFunctions, Result<IReadOnlyList<FunctionResponse>>>
{
    public async Task<Result<IReadOnlyList<FunctionResponse>>> Handle(ListFunctions query, CancellationToken cancellationToken)
    {
        var functions = await db.Functions
            .Where(function => query.IncludeArchived || !function.IsArchived)
            .OrderBy(function => function.Name)
            .ToListAsync(cancellationToken);
        return functions.Select(function => new FunctionResponse(function.Id, function.Name, function.OwnerUserId, function.IsArchived)).ToList();
    }
}

internal sealed class CreateOpeningHandler(CoreDbContext db, ITenantContext tenant, IRoleAssignments roles, ICurrentUser currentUser)
    : ICommandHandler<CreateOpening, Result<OpeningDetails>>
{
    public async Task<Result<OpeningDetails>> Handle(CreateOpening command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var grants = await roles.FindAsync(userId, CorePermissions.OpeningsManage, cancellationToken);
        if (!OpeningAccess.CanManageUnit(grants, command.HiringUnitId))
        {
            return Error.ForbiddenError("opening.forbidden", "You cannot open a role for that hiring unit.");
        }

        var catalogue = await db.StageDefinitions.OrderBy(stage => stage.Order).ToListAsync(cancellationToken);
        if (catalogue.Count == 0)
        {
            return Error.ConflictError("stages.missing", "The stage catalogue has not been synced.");
        }

        var sequence = await db.OpeningNumbers.SingleOrDefaultAsync(item => item.WorkspaceId == tenant.WorkspaceId, cancellationToken);
        if (sequence is null)
        {
            sequence = new OpeningNumber { WorkspaceId = tenant.WorkspaceId, NextNumber = 1 };
            db.OpeningNumbers.Add(sequence);
        }

        var code = $"OPN-{sequence.NextNumber:0000}";
        sequence.NextNumber++;
        var opening = Opening.Create(code, command.Title, command.HiringUnitId, command.FunctionId, command.Positions, command.Location, command.Seniority, command.Reason, command.TargetDate, command.HiringManagerId, command.RecruiterId, command.ExternalId, catalogue);
        db.Openings.Add(opening);
        await db.SaveChangesAsync(cancellationToken);
        return Result<OpeningDetails>.Or(await OpeningReader.LoadAsync(db, opening.Id, cancellationToken), Error.NotFoundError("opening.not_found", "Opening was not found."));
    }
}

internal sealed class UpdateOpeningHandler(CoreDbContext db, IRoleAssignments roles, ICurrentUser currentUser) : ICommandHandler<UpdateOpening, Result<OpeningDetails>>
{
    public async Task<Result<OpeningDetails>> Handle(UpdateOpening command, CancellationToken cancellationToken)
    {
        var opening = await OpeningReader.TrackedAsync(db, command.Id, cancellationToken);
        if (opening is null || currentUser.UserId is not Guid userId)
        {
            return Error.NotFoundError("opening.not_found", "Opening was not found.");
        }

        var grants = await roles.FindAsync(userId, CorePermissions.OpeningsManage, cancellationToken);
        if (!OpeningAccess.CanSee(grants, opening, userId))
        {
            return Error.NotFoundError("opening.not_found", "Opening was not found.");
        }

        var failure = opening.Update(command.Title, command.Positions, command.Location, command.Seniority, command.Reason, command.TargetDate, command.HiringManagerId, command.RecruiterId, command.RowVersion);
        if (failure is not null)
        {
            return failure.Value.Code == "opening.stale"
                ? Error.Precondition(failure.Value.Code, failure.Value.Message)
                : Error.ConflictError(failure.Value.Code, failure.Value.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<OpeningDetails>.Or(await OpeningReader.LoadAsync(db, opening.Id, cancellationToken), Error.NotFoundError("opening.not_found", "Opening was not found."));
    }
}

internal sealed class ChangeOpeningStatusHandler(CoreDbContext db, IRoleAssignments roles, ICurrentUser currentUser) : ICommandHandler<ChangeOpeningStatus, Result<OpeningDetails>>
{
    public async Task<Result<OpeningDetails>> Handle(ChangeOpeningStatus command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OpeningStatus>(command.Status, out var status))
        {
            return Error.Validation("opening.status", "Unknown opening status.");
        }

        var opening = await OpeningReader.TrackedAsync(db, command.Id, cancellationToken);
        if (opening is null || currentUser.UserId is not Guid userId || !OpeningAccess.CanSee(await roles.FindAsync(userId, CorePermissions.OpeningsManage, cancellationToken), opening, userId))
        {
            return Error.NotFoundError("opening.not_found", "Opening was not found.");
        }

        var failure = opening.ChangeStatus(status);
        if (failure is not null)
        {
            return Error.ConflictError(failure.Value.Code, failure.Value.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<OpeningDetails>.Or(await OpeningReader.LoadAsync(db, opening.Id, cancellationToken), Error.NotFoundError("opening.not_found", "Opening was not found."));
    }
}

internal sealed class SetStageAutoAdvanceHandler(CoreDbContext db) : ICommandHandler<SetStageAutoAdvance, Result<OpeningDetails>>
{
    public async Task<Result<OpeningDetails>> Handle(SetStageAutoAdvance command, CancellationToken cancellationToken)
    {
        var opening = await OpeningReader.TrackedAsync(db, command.OpeningId, cancellationToken);
        if (opening is null || opening.Stages.All(stage => stage.Id != command.StageId))
        {
            return Error.NotFoundError("opening.not_found", "Opening was not found.");
        }

        opening.SetAutoAdvance(command.StageId, command.AutoAdvance);
        await db.SaveChangesAsync(cancellationToken);
        return Result<OpeningDetails>.Or(await OpeningReader.LoadAsync(db, opening.Id, cancellationToken), Error.NotFoundError("opening.not_found", "Opening was not found."));
    }
}

internal sealed class ListOpeningsHandler(CoreDbContext db, IRoleAssignments roles, ICurrentUser currentUser) : IQueryHandler<ListOpenings, Result<CursorPage<OpeningSummary>>>
{
    public async Task<Result<CursorPage<OpeningSummary>>> Handle(ListOpenings query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var grants = await roles.FindAsync(userId, CorePermissions.OpeningsView, cancellationToken);
        var limit = Math.Clamp(query.Limit, 1, 100);
        var openings = OpeningAccess.Filter(db.Openings.AsQueryable(), grants, userId);
        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<OpeningStatus>(query.Status, true, out var status))
        {
            openings = openings.Where(opening => opening.Status == status);
        }

        if (query.HiringUnitId is Guid unitId)
        {
            openings = openings.Where(opening => opening.HiringUnitId == unitId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            openings = openings.Where(opening => opening.Title.ToLower().Contains(term) || opening.Code.ToLower().Contains(term));
        }

        if (CursorCodec.TryDecode(query.After, out var createdAt, out var id))
        {
            openings = openings.Where(opening => opening.CreatedAt < createdAt || (opening.CreatedAt == createdAt && opening.Id.CompareTo(id) < 0));
        }

        var page = await openings.OrderByDescending(opening => opening.CreatedAt).ThenByDescending(opening => opening.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var slice = page.Take(limit).ToList();
        var unitIds = slice.Select(opening => opening.HiringUnitId).Distinct().ToList();
        var functionIds = slice.Select(opening => opening.FunctionId).Distinct().ToList();
        var openingIds = slice.Select(opening => opening.Id).ToList();
        var units = await db.HiringUnits.Where(unit => unitIds.Contains(unit.Id)).ToDictionaryAsync(unit => unit.Id, cancellationToken);
        var functions = await db.Functions.Where(function => functionIds.Contains(function.Id)).ToDictionaryAsync(function => function.Id, cancellationToken);
        var counts = await db.Applications.Where(application => openingIds.Contains(application.OpeningId))
            .GroupBy(application => application.OpeningId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);

        var items = slice.Select(opening => new OpeningSummary(
            opening.Id,
            opening.Code,
            opening.Title,
            opening.Status.ToString(),
            opening.HiringUnitId,
            units.GetValueOrDefault(opening.HiringUnitId)?.Name ?? "",
            opening.FunctionId,
            functions.GetValueOrDefault(opening.FunctionId)?.Name ?? "",
            opening.Positions,
            counts.GetValueOrDefault(opening.Id),
            opening.CreatedAt,
            opening.RowVersion)).ToList();

        var next = page.Count > limit ? CursorCodec.Encode(slice[^1].CreatedAt, slice[^1].Id) : null;
        return new CursorPage<OpeningSummary>(items, next);
    }
}

internal sealed class GetOpeningHandler(CoreDbContext db, IRoleAssignments roles, ICurrentUser currentUser) : IQueryHandler<GetOpening, Result<OpeningDetails>>
{
    public async Task<Result<OpeningDetails>> Handle(GetOpening query, CancellationToken cancellationToken)
    {
        var opening = await OpeningReader.TrackedAsync(db, query.Id, cancellationToken);
        if (opening is null || currentUser.UserId is not Guid userId || !OpeningAccess.CanSee(await roles.FindAsync(userId, CorePermissions.OpeningsView, cancellationToken), opening, userId))
        {
            return Error.NotFoundError("opening.not_found", "Opening was not found.");
        }

        return Result<OpeningDetails>.Or(await OpeningReader.LoadAsync(db, opening.Id, cancellationToken), Error.NotFoundError("opening.not_found", "Opening was not found."));
    }
}

internal sealed class GetRankingHandler(CoreDbContext db, IRoleAssignments roles, ICurrentUser currentUser) : IQueryHandler<GetRanking, Result<IReadOnlyList<RankingRow>>>
{
    public async Task<Result<IReadOnlyList<RankingRow>>> Handle(GetRanking query, CancellationToken cancellationToken)
    {
        var opening = await OpeningReader.TrackedAsync(db, query.OpeningId, cancellationToken);
        if (opening is null || currentUser.UserId is not Guid userId || !OpeningAccess.CanSee(await roles.FindAsync(userId, CorePermissions.OpeningsView, cancellationToken), opening, userId))
        {
            return Error.NotFoundError("opening.not_found", "Opening was not found.");
        }

        var rows = await (
            from application in db.Applications
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            join stage in db.OpeningStages on application.CurrentStageId equals stage.Id
            where application.OpeningId == opening.Id
            join result in db.AssessmentResults on application.Id equals result.ApplicationId into results
            from result in results.OrderByDescending(item => item.RecordedAt).Take(1).DefaultIfEmpty()
            orderby result.Total descending
            select new RankingRow(application.Id, candidate.Id, candidate.Name, stage.DisplayName, result == null ? null : result.Total, result == null ? null : result.Passed, application.Status.ToString()))
            .ToListAsync(cancellationToken);
        return rows;
    }
}

internal static class OpeningAccess
{
    public static bool CanManageUnit(IReadOnlyList<AccessGrant> grants, Guid hiringUnitId) =>
        grants.Any(grant => grant.Scope == Scope.All)
        || grants.Any(grant => grant.Scope == Scope.Own && grant.HiringUnitIds.Contains(hiringUnitId))
        || grants.Any(grant => grant.Scope == Scope.Assigned);

    public static bool CanSee(IReadOnlyList<AccessGrant> grants, Opening opening, Guid userId) =>
        grants.Any(grant => grant.Scope == Scope.All)
        || grants.Any(grant => grant.Scope == Scope.Own && grant.HiringUnitIds.Contains(opening.HiringUnitId))
        || (grants.Any(grant => grant.Scope == Scope.Assigned) && (opening.RecruiterId == userId || opening.HiringManagerId == userId || opening.Panel.Any(panelist => panelist.UserId == userId)));

    public static IQueryable<Opening> Filter(IQueryable<Opening> openings, IReadOnlyList<AccessGrant> grants, Guid userId)
    {
        if (grants.Count == 0)
        {
            return openings.Where(_ => false);
        }

        if (grants.Any(grant => grant.Scope == Scope.All))
        {
            return openings;
        }

        var units = grants.Where(grant => grant.Scope == Scope.Own).SelectMany(grant => grant.HiringUnitIds).Distinct().ToList();
        var assigned = grants.Any(grant => grant.Scope == Scope.Assigned);
        return openings.Where(opening =>
            units.Contains(opening.HiringUnitId)
            || (assigned && (opening.RecruiterId == userId || opening.HiringManagerId == userId || opening.Panel.Any(panelist => panelist.UserId == userId))));
    }
}

internal static class OpeningReader
{
    public static Task<Opening?> TrackedAsync(CoreDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.Openings.Include(opening => opening.Stages).Include(opening => opening.Panel).SingleOrDefaultAsync(opening => opening.Id == id, cancellationToken);

    public static async Task<OpeningDetails?> LoadAsync(CoreDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var opening = await TrackedAsync(db, id, cancellationToken);
        if (opening is null)
        {
            return null;
        }

        var applications = await (
            from application in db.Applications
            join candidate in db.Candidates on application.CandidateId equals candidate.Id
            join stage in db.OpeningStages on application.CurrentStageId equals stage.Id
            where application.OpeningId == id
            join result in db.AssessmentResults on application.Id equals result.ApplicationId into results
            from result in results.OrderByDescending(item => item.RecordedAt).Take(1).DefaultIfEmpty()
            orderby candidate.Name
            select new ApplicationResponse(
                application.Id,
                candidate.Id,
                candidate.Name,
                candidate.Email,
                application.CurrentStageId,
                stage.DisplayName,
                application.Status.ToString(),
                result == null ? null : result.Total,
                result == null ? null : result.Max,
                result == null ? null : result.Passed,
                result == null ? null : result.Status))
            .ToListAsync(cancellationToken);

        var stages = opening.Stages.OrderBy(stage => stage.Order)
            .Select(stage => new StageResponse(stage.Id, stage.Type.ToString().ToLowerInvariant(), stage.DisplayName, stage.Type.ToString(), stage.AutomatedBy, stage.Order, stage.AutoAdvance))
            .ToList();

        return new OpeningDetails(opening.Id, opening.Code, opening.Title, opening.Status.ToString(), opening.HiringUnitId, opening.FunctionId, opening.Positions, opening.Location, opening.Seniority, opening.Reason, opening.TargetDate, opening.HiringManagerId, opening.RecruiterId, opening.ExternalId, opening.RowVersion, stages, applications);
    }
}
