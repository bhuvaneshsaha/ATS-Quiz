using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Core.Application;

namespace Shortlist.Modules.Core.Api;

public static class CoreEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var units = api.MapGroup("/v1/hiring-units").WithTags("Hiring units").RequireAuthorization();
        units.MapGet("/", async (bool? includeArchived, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListHiringUnits(includeArchived == true), ct)).ToHttpResult()).RequirePermission(CorePermissions.HiringUnitsView);
        units.MapPost("/", async (CreateHiringUnit command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(unit => $"/api/core/v1/hiring-units/{unit.Id}")).RequirePermission(CorePermissions.HiringUnitsManage).WithIdempotency();

        var functions = api.MapGroup("/v1/functions").WithTags("Functions").RequireAuthorization();
        functions.MapGet("/", async (bool? includeArchived, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListFunctions(includeArchived == true), ct)).ToHttpResult()).RequirePermission(CorePermissions.FunctionsView);
        functions.MapPost("/", async (CreateFunction command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(function => $"/api/core/v1/functions/{function.Id}")).RequirePermission(CorePermissions.FunctionsManage).WithIdempotency();

        var openings = api.MapGroup("/v1/openings").WithTags("Openings").RequireAuthorization();
        openings.MapGet("/", async ([AsParameters] ListOpenings query, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(query, ct)).ToHttpResult()).RequirePermission(CorePermissions.OpeningsView);
        openings.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new GetOpening(id), ct)).ToHttpResult()).RequirePermission(CorePermissions.OpeningsView);
        openings.MapGet("/{id:guid}/ranking", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new GetRanking(id), ct)).ToHttpResult()).RequirePermission(CorePermissions.OpeningsView);
        openings.MapPost("/", async (CreateOpening command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(opening => $"/api/core/v1/openings/{opening.Id}")).RequirePermission(CorePermissions.OpeningsManage).WithIdempotency();
        openings.MapPut("/{id:guid}", async (Guid id, UpdateOpeningRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new UpdateOpening(id, body.Title, body.Positions, body.Location, body.Seniority, body.Reason, body.TargetDate, body.HiringManagerId, body.RecruiterId, body.RowVersion), ct)).ToHttpResult()).RequirePermission(CorePermissions.OpeningsManage);
        openings.MapPost("/{id:guid}/status", async (Guid id, StatusRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ChangeOpeningStatus(id, body.Status), ct)).ToHttpResult()).RequirePermission(CorePermissions.OpeningsManage);
        openings.MapPost("/{id:guid}/stages/{stageId:guid}/auto-advance", async (Guid id, Guid stageId, AutoAdvanceRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SetStageAutoAdvance(id, stageId, body.AutoAdvance), ct)).ToHttpResult()).RequirePermission(CorePermissions.OpeningsManage);
        openings.MapPost("/{id:guid}/applications", async (Guid id, AddApplicationRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new AddApplication(id, body.CandidateId), ct)).ToCreatedResult(_ => $"/api/core/v1/openings/{id}")).RequirePermission(CorePermissions.ApplicationsMove).WithIdempotency();
        openings.MapPost("/{id:guid}/applications/{applicationId:guid}/stage", async (Guid id, Guid applicationId, MoveStageRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new MoveApplicationStage(id, applicationId, body.ToStageId), ct)).ToHttpResult()).RequirePermission(CorePermissions.ApplicationsMove);

        var candidates = api.MapGroup("/v1/candidates").WithTags("Candidates").RequireAuthorization();
        candidates.MapGet("/", async ([AsParameters] ListCandidates query, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(query, ct)).ToHttpResult()).RequirePermission(CorePermissions.CandidatesView);
        candidates.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new GetCandidate(id), ct)).ToHttpResult()).RequirePermission(CorePermissions.CandidatesView);
        candidates.MapPost("/", async (CreateCandidate command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(candidate => $"/api/core/v1/candidates/{candidate.Id}")).RequirePermission(CorePermissions.CandidatesManage).WithIdempotency();
        candidates.MapPut("/{id:guid}", async (Guid id, UpdateCandidateRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new UpdateCandidate(id, body.Name, body.Phone, body.Location, body.Source), ct)).ToHttpResult()).RequirePermission(CorePermissions.CandidatesManage);
        candidates.MapPost("/{id:guid}/erase", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new EraseCandidate(id), ct)).ToHttpResult()).RequirePermission(CorePermissions.CandidatesManage);
        candidates.MapPost("/{id:guid}/notes", async (Guid id, NoteRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new AddNote(null, id, body.Body), ct)).ToHttpResult()).RequirePermission(CorePermissions.NotesWrite);

        api.MapGet("/v1/audit", async ([AsParameters] ListAudit query, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(query, ct)).ToHttpResult()).RequireAuthorization().RequirePermission(CorePermissions.AuditView);

        api.MapGet("/v1/files/{**key}", async (string key, IFileStore files, CancellationToken ct) =>
        {
            var stream = await files.OpenReadAsync(key, ct);
            return Results.File(stream, "application/octet-stream");
        }).RequireAuthorization();
    }
}

public sealed record UpdateOpeningRequest(string Title, int Positions, string? Location, string? Seniority, string? Reason, DateOnly? TargetDate, Guid? HiringManagerId, Guid? RecruiterId, uint RowVersion);
public sealed record StatusRequest(string Status);
public sealed record AutoAdvanceRequest(bool AutoAdvance);
public sealed record AddApplicationRequest(Guid CandidateId);
public sealed record MoveStageRequest(Guid ToStageId);
public sealed record UpdateCandidateRequest(string Name, string? Phone, string? Location, string? Source);
public sealed record NoteRequest(string Body);
