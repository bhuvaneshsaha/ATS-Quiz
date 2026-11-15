using Microsoft.EntityFrameworkCore;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Assessments.Contracts;
using Shortlist.Modules.Core.Application;
using Shortlist.Modules.Core.Domain;

namespace Shortlist.Modules.Core.Infrastructure;

internal sealed class CreateCandidateHandler(CoreDbContext db, IClock clock) : ICommandHandler<CreateCandidate, Result<CandidateDetails>>
{
    public async Task<Result<CandidateDetails>> Handle(CreateCandidate command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var existing = await db.Candidates.IgnoreQueryFilters().AnyAsync(candidate => candidate.Email == email && !candidate.IsErased, cancellationToken);
        if (existing)
        {
            return Error.ConflictError("candidate.email", "A candidate with that email already exists.");
        }

        var candidate = Candidate.Create(command.Name, email, command.Phone, command.Location, command.Source, command.ExternalId, command.Consent, command.ConsentText, clock.UtcNow);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync(cancellationToken);
        return Result<CandidateDetails>.Or(await CandidateReader.LoadAsync(db, candidate.Id, cancellationToken), Error.NotFoundError("candidate.not_found", "Candidate was not found."));
    }
}

internal sealed class UpdateCandidateHandler(CoreDbContext db) : ICommandHandler<UpdateCandidate, Result<CandidateDetails>>
{
    public async Task<Result<CandidateDetails>> Handle(UpdateCandidate command, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (candidate is null)
        {
            return Error.NotFoundError("candidate.not_found", "Candidate was not found.");
        }

        candidate.Update(command.Name, command.Phone, command.Location, command.Source);
        await db.SaveChangesAsync(cancellationToken);
        return Result<CandidateDetails>.Or(await CandidateReader.LoadAsync(db, candidate.Id, cancellationToken), Error.NotFoundError("candidate.not_found", "Candidate was not found."));
    }
}

internal sealed class ListCandidatesHandler(CoreDbContext db) : IQueryHandler<ListCandidates, Result<CursorPage<CandidateSummary>>>
{
    public async Task<Result<CursorPage<CandidateSummary>>> Handle(ListCandidates query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 100);
        var candidates = db.Candidates.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            candidates = candidates.Where(candidate => candidate.Name.ToLower().Contains(term) || candidate.Email.ToLower().Contains(term));
        }

        if (CursorCodec.TryDecode(query.After, out var createdAt, out var id))
        {
            candidates = candidates.Where(candidate => candidate.CreatedAt < createdAt || (candidate.CreatedAt == createdAt && candidate.Id.CompareTo(id) < 0));
        }

        var page = await candidates.OrderByDescending(candidate => candidate.CreatedAt).ThenByDescending(candidate => candidate.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var slice = page.Take(limit).ToList();
        var items = slice.Select(candidate => new CandidateSummary(candidate.Id, candidate.Name, candidate.Email, candidate.Phone, candidate.Location, candidate.Source, candidate.CreatedAt)).ToList();
        var next = page.Count > limit ? CursorCodec.Encode(slice[^1].CreatedAt, slice[^1].Id) : null;
        return new CursorPage<CandidateSummary>(items, next);
    }
}

internal sealed class GetCandidateHandler(CoreDbContext db) : IQueryHandler<GetCandidate, Result<CandidateDetails>>
{
    public async Task<Result<CandidateDetails>> Handle(GetCandidate query, CancellationToken cancellationToken) =>
        await CandidateReader.LoadAsync(db, query.Id, cancellationToken) is { } details
            ? details
            : Error.NotFoundError("candidate.not_found", "Candidate was not found.");
}

internal sealed class EraseCandidateHandler(CoreDbContext db, IClock clock, IFileStore files) : ICommandHandler<EraseCandidate, Result>
{
    public async Task<Result> Handle(EraseCandidate command, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(item => item.Id == command.Id, cancellationToken);
        if (candidate is null)
        {
            return Error.NotFoundError("candidate.not_found", "Candidate was not found.");
        }

        candidate.Erase(clock.UtcNow);
        var documents = await db.Documents.Where(document => document.CandidateId == candidate.Id).ToListAsync(cancellationToken);
        foreach (var document in documents)
        {
            await files.DeleteAsync(document.FileKey, cancellationToken);
            db.Documents.Remove(document);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

internal sealed class AddApplicationHandler(CoreDbContext db) : ICommandHandler<AddApplication, Result<ApplicationResponse>>
{
    public async Task<Result<ApplicationResponse>> Handle(AddApplication command, CancellationToken cancellationToken)
    {
        var opening = await db.Openings.Include(item => item.Stages).SingleOrDefaultAsync(item => item.Id == command.OpeningId, cancellationToken);
        var candidate = await db.Candidates.SingleOrDefaultAsync(item => item.Id == command.CandidateId, cancellationToken);
        if (opening is null || candidate is null)
        {
            return Error.NotFoundError("application.missing", "The opening or candidate was not found.");
        }

        if (opening.Status is OpeningStatus.Filled or OpeningStatus.ClosedUnfilled)
        {
            return Error.ConflictError("opening.closed", "This opening is not accepting applications.");
        }

        var duplicate = await db.Applications.AnyAsync(item => item.OpeningId == opening.Id && item.CandidateId == candidate.Id, cancellationToken);
        if (duplicate)
        {
            return Error.ConflictError("application.duplicate", "This candidate is already in the opening.");
        }

        var applied = opening.Stages.OrderBy(stage => stage.Order).First(stage => stage.Type == StageType.Applied);
        var application = Domain.Application.Start(candidate.Id, opening.Id, applied);
        db.Applications.Add(application);
        db.StageTransitions.Add(StageTransition.Record(application.Id, null, applied.Id, null, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        return new ApplicationResponse(application.Id, candidate.Id, candidate.Name, candidate.Email, applied.Id, applied.DisplayName, application.Status.ToString(), null, null, null, null, null);
    }
}

internal sealed class MoveApplicationStageHandler(CoreDbContext db, IClock clock, ICurrentUser currentUser) : ICommandHandler<MoveApplicationStage, Result<ApplicationResponse>>
{
    public async Task<Result<ApplicationResponse>> Handle(MoveApplicationStage command, CancellationToken cancellationToken)
    {
        var application = await db.Applications.SingleOrDefaultAsync(item => item.Id == command.ApplicationId && item.OpeningId == command.OpeningId, cancellationToken);
        var stage = await db.OpeningStages.SingleOrDefaultAsync(item => item.Id == command.ToStageId, cancellationToken);
        if (application is null || stage is null)
        {
            return Error.NotFoundError("application.not_found", "Application was not found.");
        }

        var from = application.CurrentStageId;
        var failure = application.MoveTo(stage, currentUser.UserId, null, clock.UtcNow);
        if (failure is not null)
        {
            return Error.ConflictError(failure.Value.Code, failure.Value.Message);
        }

        db.StageTransitions.Add(StageTransition.Record(application.Id, from, stage.Id, currentUser.UserId, null, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
        var candidate = await db.Candidates.SingleAsync(item => item.Id == application.CandidateId, cancellationToken);
        return new ApplicationResponse(application.Id, candidate.Id, candidate.Name, candidate.Email, stage.Id, stage.DisplayName, application.Status.ToString(), null, null, null, null, null);
    }
}

internal sealed class AddNoteHandler(CoreDbContext db, ICurrentUser currentUser) : ICommandHandler<AddNote, Result<NoteResponse>>
{
    public async Task<Result<NoteResponse>> Handle(AddNote command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid author)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var note = Note.Write(command.ApplicationId, command.CandidateId, author, command.Body);
        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        return new NoteResponse(note.Id, note.Body, note.AuthorUserId, note.CreatedAt);
    }
}

internal sealed class ListAuditHandler(CoreDbContext db, ITenantContext tenant) : IQueryHandler<ListAudit, Result<CursorPage<AuditResponse>>>
{
    public async Task<Result<CursorPage<AuditResponse>>> Handle(ListAudit query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 100);
        var rows = db.AuditEntries.Where(entry => entry.WorkspaceId == tenant.WorkspaceId);
        if (CursorCodec.TryDecode(query.After, out var createdAt, out var id))
        {
            rows = rows.Where(entry => entry.At < createdAt || (entry.At == createdAt && entry.Id.CompareTo(id) < 0));
        }

        var page = await rows.OrderByDescending(entry => entry.At).ThenByDescending(entry => entry.Id).Take(limit + 1).ToListAsync(cancellationToken);
        var slice = page.Take(limit).ToList();
        var items = slice.Select(entry => new AuditResponse(entry.Id, entry.ActorId, entry.Action, entry.EntityType, entry.EntityId, entry.Before, entry.After, entry.At)).ToList();
        var next = page.Count > limit ? CursorCodec.Encode(slice[^1].At, slice[^1].Id) : null;
        return new CursorPage<AuditResponse>(items, next);
    }
}

internal sealed class RecordAssessmentResultHandler(CoreDbContext db, IClock clock) : IIntegrationEventHandler<AssessmentResultReady>
{
    public async Task HandleAsync(AssessmentResultReady @event, CancellationToken cancellationToken)
    {
        if (await Inbox.ExistsAsync(db, @event.EventId, cancellationToken))
        {
            return;
        }

        var existing = await db.AssessmentResults.SingleOrDefaultAsync(result => result.AttemptId == @event.AttemptId, cancellationToken);
        if (existing is null)
        {
            db.AssessmentResults.Add(ApplicationAssessmentResult.Record(@event.ApplicationId, @event.AttemptId, @event.Total, @event.Max, @event.Passed, @event.Status, clock.UtcNow));
        }
        else
        {
            existing.Update(@event.Total, @event.Max, @event.Passed, @event.Status, clock.UtcNow);
        }

        if (@event.Passed && @event.Status == "Final")
        {
            var application = await db.Applications.SingleOrDefaultAsync(item => item.Id == @event.ApplicationId, cancellationToken);
            var current = application is null ? null : await db.OpeningStages.SingleOrDefaultAsync(stage => stage.Id == application.CurrentStageId, cancellationToken);
            if (application is not null && current is { AutoAdvance: true })
            {
                var next = await db.OpeningStages
                    .Where(stage => stage.OpeningId == application.OpeningId && stage.Order > current.Order && stage.Type != StageType.Rejected && stage.Type != StageType.Withdrawn)
                    .OrderBy(stage => stage.Order)
                    .FirstOrDefaultAsync(cancellationToken);
                if (next is not null)
                {
                    var from = application.CurrentStageId;
                    var failure = application.MoveTo(next, null, "Assessments", clock.UtcNow);
                    if (failure is null)
                    {
                        db.StageTransitions.Add(StageTransition.Record(application.Id, from, next.Id, null, "Assessments", clock.UtcNow));
                    }
                }
            }
        }

        Inbox.Record(db, @event, @event.WorkspaceId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class RecordInvitationHandler(CoreDbContext db, IClock clock) : IIntegrationEventHandler<AssessmentInvited>
{
    public async Task HandleAsync(AssessmentInvited @event, CancellationToken cancellationToken)
    {
        if (await Inbox.ExistsAsync(db, @event.EventId, cancellationToken))
        {
            return;
        }

        db.Notes.Add(Note.Write(@event.ApplicationId, null, Guid.Empty, $"Assessment invite sent. It expires {_eventExpires(@event)}."));
        Inbox.Record(db, @event, @event.WorkspaceId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string _eventExpires(AssessmentInvited @event) => @event.ExpiresAt.ToString("u");
}

internal static class CandidateReader
{
    public static async Task<CandidateDetails?> LoadAsync(CoreDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        var applications = await (
            from application in db.Applications
            join stage in db.OpeningStages on application.CurrentStageId equals stage.Id
            where application.CandidateId == id
            select new ApplicationResponse(application.Id, candidate.Id, candidate.Name, candidate.Email, stage.Id, stage.DisplayName, application.Status.ToString(), null, null, null, null, null))
            .ToListAsync(cancellationToken);
        var notes = await db.Notes.Where(note => note.CandidateId == id).OrderByDescending(note => note.CreatedAt)
            .Select(note => new NoteResponse(note.Id, note.Body, note.AuthorUserId, note.CreatedAt))
            .ToListAsync(cancellationToken);
        return new CandidateDetails(candidate.Id, candidate.Name, candidate.Email, candidate.Phone, candidate.Location, candidate.Source, candidate.ExternalId, candidate.ConsentAt, applications, notes);
    }
}

public sealed class StageCatalogueSync(CoreDbContext db, ITenantContext tenant) : IStartupSync
{
    public int Order => 3;

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var existing = await db.StageDefinitions.ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        foreach (var seed in StageCatalogue.All)
        {
            var stage = StageDefinition.Create(seed);
            stage.SetWorkspace(tenant.WorkspaceId);
            db.StageDefinitions.Add(stage);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
