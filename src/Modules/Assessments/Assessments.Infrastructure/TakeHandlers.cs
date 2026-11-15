using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Assessments.Application;
using Shortlist.Modules.Assessments.Domain;
using Shortlist.Modules.Core.Contracts;

namespace Shortlist.Modules.Assessments.Infrastructure;

internal static class Secrets
{
    public static string NewToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return raw.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
}

internal sealed class CreateBindingHandler(
    AssessmentsDbContext db,
    IApplicationReader applications,
    IEmailSender email,
    IClock clock,
    IConfiguration configuration) : ICommandHandler<CreateBinding, Result<BindingResponse>>
{
    public async Task<Result<BindingResponse>> Handle(CreateBinding command, CancellationToken cancellationToken)
    {
        var version = await db.AssessmentVersions.SingleOrDefaultAsync(item => item.Id == command.AssessmentVersionId, cancellationToken);
        if (version is null || !version.IsPublished)
        {
            return Error.ConflictError("binding.version", "Bind a published assessment version.");
        }

        var validDays = command.ValidDays <= 0 ? 7 : command.ValidDays;
        var extra = Math.Max(0, command.ExtraTimePercent);
        var existing = await db.StageBindings.Where(item => item.OpeningStageId == command.OpeningStageId).OrderBy(item => item.CreatedAt).ToListAsync(cancellationToken);
        StageBinding binding;
        if (existing.Count == 0)
        {
            binding = StageBinding.Create(command.OpeningStageId, command.AssessmentVersionId, command.PassMarkOverride, validDays, extra);
            db.StageBindings.Add(binding);
        }
        else
        {
            binding = existing[0];
            binding.Retarget(command.AssessmentVersionId, command.PassMarkOverride, validDays, extra);
            if (existing.Count > 1)
            {
                db.StageBindings.RemoveRange(existing.Skip(1));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await InvitationMailer.InviteSittingAsync(db, applications, email, clock, configuration, binding, cancellationToken);
        var name = await AssessmentName(db, binding.AssessmentVersionId, cancellationToken);
        return Map(binding, name);
    }

    internal static async Task<string> AssessmentName(AssessmentsDbContext db, Guid versionId, CancellationToken cancellationToken)
    {
        var version = await db.AssessmentVersions.SingleAsync(item => item.Id == versionId, cancellationToken);
        var assessment = await db.Assessments.SingleAsync(item => item.Id == version.AssessmentId, cancellationToken);
        return assessment.Name;
    }

    internal static BindingResponse Map(StageBinding binding, string assessmentName) =>
        new(binding.Id, binding.OpeningStageId, binding.AssessmentVersionId, binding.PassMarkOverride, binding.ValidDays, binding.ExtraTimePercent, assessmentName);
}

internal static class InvitationMailer
{
    public static async Task SendAsync(AssessmentsDbContext db, IEmailSender email, IClock clock, IConfiguration configuration, Invitation invitation, string candidateEmail, CancellationToken cancellationToken)
    {
        var others = await db.Invitations.Where(item => item.ApplicationId == invitation.ApplicationId && item.Id != invitation.Id && (item.Status == InvitationStatus.Pending || item.Status == InvitationStatus.Sent)).ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            other.Cancel();
        }

        var token = Secrets.NewToken();
        invitation.ReplaceToken(Secrets.Hash(token));
        await db.SaveChangesAsync(cancellationToken);
        var link = $"{configuration["App:PublicBaseUrl"]?.TrimEnd('/')}/take/{token}";
        await email.SendAsync(new EmailMessage(candidateEmail, "You are invited to a Shortlist assessment", $"Open your assessment: {link}\nThis link expires {invitation.ExpiresAt:u}."), cancellationToken);
        invitation.MarkSent(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task InviteSittingAsync(
        AssessmentsDbContext db,
        IApplicationReader applications,
        IEmailSender email,
        IClock clock,
        IConfiguration configuration,
        StageBinding binding,
        CancellationToken cancellationToken)
    {
        var sitting = await applications.ListOnStageAsync(binding.OpeningStageId, cancellationToken);
        foreach (var snapshot in sitting)
        {
            var invitation = await db.Invitations
                .Where(item => item.ApplicationId == snapshot.Id)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            var attempt = invitation is null
                ? null
                : await db.Attempts.SingleOrDefaultAsync(item => item.InvitationId == invitation.Id, cancellationToken);
            if (attempt is not null || invitation?.Status is InvitationStatus.Started)
            {
                continue;
            }

            if (invitation is null || invitation.Status is InvitationStatus.Submitted or InvitationStatus.Expired or InvitationStatus.Cancelled)
            {
                invitation = Invitation.Create(snapshot.Id, binding.Id, binding.AssessmentVersionId, Secrets.Hash(Secrets.NewToken()), clock.UtcNow.AddDays(binding.ValidDays), binding.ExtraTimePercent, Guid.CreateVersion7(), snapshot.CandidateEmail);
                db.Invitations.Add(invitation);
                await db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                invitation.PointAt(binding.Id, binding.AssessmentVersionId, clock.UtcNow.AddDays(binding.ValidDays), binding.ExtraTimePercent);
            }

            await SendAsync(db, email, clock, configuration, invitation, snapshot.CandidateEmail, cancellationToken);
        }
    }
}

internal sealed class ListBindingsHandler(AssessmentsDbContext db) : IQueryHandler<ListBindings, Result<IReadOnlyList<BindingResponse>>>
{
    public async Task<Result<IReadOnlyList<BindingResponse>>> Handle(ListBindings query, CancellationToken cancellationToken)
    {
        var bindings = db.StageBindings.AsQueryable();
        if (query.OpeningStageId is Guid stageId)
        {
            bindings = bindings.Where(item => item.OpeningStageId == stageId);
        }

        var rows = await bindings.OrderByDescending(item => item.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string>();
        var responses = new List<BindingResponse>();
        foreach (var binding in rows)
        {
            if (!names.TryGetValue(binding.AssessmentVersionId, out var name))
            {
                name = await CreateBindingHandler.AssessmentName(db, binding.AssessmentVersionId, cancellationToken);
                names[binding.AssessmentVersionId] = name;
            }

            responses.Add(CreateBindingHandler.Map(binding, name));
        }

        return responses;
    }
}

internal sealed class ResendInvitationHandler(AssessmentsDbContext db, IEmailSender email, IClock clock, IConfiguration configuration) : ICommandHandler<ResendInvitation, Result>
{
    public async Task<Result> Handle(ResendInvitation command, CancellationToken cancellationToken)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(item => item.Id == command.InvitationId, cancellationToken);
        if (invitation is null || invitation.Status is InvitationStatus.Submitted or InvitationStatus.Cancelled or InvitationStatus.Expired)
        {
            return Error.ConflictError("invitation.closed", "This invitation can no longer be resent.");
        }

        var attempt = await db.Attempts.SingleOrDefaultAsync(item => item.InvitationId == invitation.Id, cancellationToken);
        if (attempt is not null)
        {
            return Error.ConflictError("invitation.started", "This candidate has already started.");
        }

        await InvitationMailer.SendAsync(db, email, clock, configuration, invitation, invitation.CandidateEmail, cancellationToken);
        return Result.Ok();
    }
}

internal sealed class ListInvitationsHandler(AssessmentsDbContext db) : IQueryHandler<ListInvitations, Result<IReadOnlyList<InvitationResponse>>>
{
    public async Task<Result<IReadOnlyList<InvitationResponse>>> Handle(ListInvitations query, CancellationToken cancellationToken)
    {
        var invitations = db.Invitations.AsQueryable();
        if (query.ApplicationId is Guid applicationId)
        {
            invitations = invitations.Where(invitation => invitation.ApplicationId == applicationId);
        }

        var rows = await invitations.OrderByDescending(invitation => invitation.CreatedAt).Take(100).ToListAsync(cancellationToken);
        return rows.Select(invitation => new InvitationResponse(invitation.Id, invitation.ApplicationId, invitation.Status.ToString(), invitation.ExpiresAt, invitation.ExtraTimePercent)).ToList();
    }
}

internal sealed class InviteOnStageChangedHandler(
    AssessmentsDbContext db,
    IApplicationReader applications,
    IEmailSender email,
    IClock clock,
    IConfiguration configuration) : IIntegrationEventHandler<ApplicationStageChanged>
{
    public async Task HandleAsync(ApplicationStageChanged @event, CancellationToken cancellationToken)
    {
        if (await Inbox.ExistsAsync(db, @event.EventId, cancellationToken))
        {
            return;
        }

        var binding = await db.StageBindings.SingleOrDefaultAsync(item => item.OpeningStageId == @event.OpeningStageId, cancellationToken);
        if (binding is null)
        {
            Inbox.Record(db, @event, @event.WorkspaceId, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var snapshot = await applications.FindAsync(@event.ApplicationId, cancellationToken);
        if (snapshot is null)
        {
            throw new InvalidOperationException($"Application {@event.ApplicationId} was not found for an assessment invite.");
        }

        var invitation = await db.Invitations.SingleOrDefaultAsync(item => item.SourceEventId == @event.EventId, cancellationToken);
        var keepId = invitation?.Id ?? Guid.Empty;
        var open = await db.Invitations.Where(item => item.ApplicationId == snapshot.Id && item.Id != keepId && (item.Status == InvitationStatus.Pending || item.Status == InvitationStatus.Sent)).ToListAsync(cancellationToken);
        foreach (var item in open)
        {
            item.Cancel();
        }

        var token = Secrets.NewToken();
        if (invitation is null)
        {
            invitation = Invitation.Create(snapshot.Id, binding.Id, binding.AssessmentVersionId, Secrets.Hash(token), clock.UtcNow.AddDays(binding.ValidDays), binding.ExtraTimePercent, @event.EventId, snapshot.CandidateEmail);
            db.Invitations.Add(invitation);
        }
        else if (invitation.Status == InvitationStatus.Sent)
        {
            Inbox.Record(db, @event, @event.WorkspaceId, clock.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        else
        {
            invitation.ReplaceToken(Secrets.Hash(token));
        }

        await db.SaveChangesAsync(cancellationToken);
        var link = $"{configuration["App:PublicBaseUrl"]?.TrimEnd('/')}/take/{token}";
        await email.SendAsync(new EmailMessage(snapshot.CandidateEmail, "You are invited to a Shortlist assessment", $"Open your assessment: {link}\nThis link expires {invitation.ExpiresAt:u}."), cancellationToken);
        invitation.MarkSent(clock.UtcNow);
        Inbox.Record(db, @event, @event.WorkspaceId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class RequestCandidateCodeHandler(AssessmentsDbContext db, IEmailSender email, IClock clock) : ICommandHandler<RequestCandidateCode, Result>
{
    public async Task<Result> Handle(RequestCandidateCode command, CancellationToken cancellationToken)
    {
        var invitation = await Find(db, command.Token, cancellationToken);
        if (invitation is null || invitation.Status is InvitationStatus.Cancelled)
        {
            return Error.NotFoundError("invitation.not_found", "This invitation link is not valid.");
        }

        if (invitation.Status is InvitationStatus.Expired || clock.UtcNow > invitation.ExpiresAt)
        {
            if (invitation.Status is not InvitationStatus.Expired)
            {
                invitation.Expire();
                await db.SaveChangesAsync(cancellationToken);
            }

            return Error.Validation("invitation.expired", "This link has expired. Ask the recruiter for a new one.");
        }

        var code = Secrets.NewCode();
        invitation.IssueCode(Secrets.Hash(code), clock.UtcNow.AddMinutes(10));
        await db.SaveChangesAsync(cancellationToken);
        await email.SendAsync(new EmailMessage(invitation.CandidateEmail, "Your Shortlist code", $"Your code is {code}. It expires in 10 minutes."), cancellationToken);
        return Result.Ok();
    }

    internal static Task<Invitation?> Find(AssessmentsDbContext db, string token, CancellationToken cancellationToken)
    {
        var hash = Secrets.Hash(token);
        return db.Invitations.SingleOrDefaultAsync(invitation => invitation.TokenHash == hash, cancellationToken);
    }
}

internal sealed class VerifyCandidateCodeHandler(AssessmentsDbContext db, IClock clock) : ICommandHandler<VerifyCandidateCode, Result<VerifiedCandidate>>
{
    public async Task<Result<VerifiedCandidate>> Handle(VerifyCandidateCode command, CancellationToken cancellationToken)
    {
        var invitation = await RequestCandidateCodeHandler.Find(db, command.Token, cancellationToken);
        if (invitation is null)
        {
            return Error.NotFoundError("invitation.not_found", "This invitation link is not valid.");
        }

        var failure = invitation.VerifyCode(Secrets.Hash(command.Code.Trim()), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        if (failure is not null)
        {
            return Error.Validation(failure.Value.Code, failure.Value.Message);
        }

        return await CandidateSessions.DescribeAsync(db, invitation, cancellationToken);
    }
}

internal static class CandidateSessions
{
    public static async Task<VerifiedCandidate> DescribeAsync(AssessmentsDbContext db, Invitation invitation, CancellationToken cancellationToken)
    {
        var version = await db.AssessmentVersions.Include(item => item.Sections).SingleAsync(item => item.Id == invitation.AssessmentVersionId, cancellationToken);
        var assessment = await db.Assessments.SingleAsync(item => item.Id == version.AssessmentId, cancellationToken);
        var attempt = await db.Attempts.SingleOrDefaultAsync(item => item.InvitationId == invitation.Id, cancellationToken);
        var enforced = (int)Math.Ceiling(version.TotalMinutes * (100d + invitation.ExtraTimePercent) / 100d);
        return new VerifiedCandidate(invitation.Id, invitation.WorkspaceId, invitation.CandidateEmail, assessment.Name, version.DesktopOnly, version.TotalMinutes, invitation.ExtraTimePercent, enforced, attempt is not null, attempt is not null && attempt.Status != AttemptStatus.InProgress);
    }
}

internal sealed class GetCandidateSessionHandler(AssessmentsDbContext db, ICurrentUser currentUser) : IQueryHandler<GetCandidateSession, Result<CandidateSessionResponse>>
{
    public async Task<Result<CandidateSessionResponse>> Handle(GetCandidateSession query, CancellationToken cancellationToken)
    {
        var invitation = await StartAttemptHandler.CurrentInvitation(db, currentUser, cancellationToken);
        if (invitation is null)
        {
            return Error.ForbiddenError("candidate.session", "Open your invitation again.");
        }

        var described = await CandidateSessions.DescribeAsync(db, invitation, cancellationToken);
        return new CandidateSessionResponse(described.InvitationId, described.AssessmentName, described.DesktopOnly, described.TotalMinutes, described.ExtraTimePercent, described.EnforcedMinutes, described.AttemptStarted, described.AttemptFinished);
    }
}

internal sealed class StartAttemptHandler(AssessmentsDbContext db, ICurrentUser currentUser, IClock clock) : ICommandHandler<StartAttempt, Result<PaperResponse>>
{
    public async Task<Result<PaperResponse>> Handle(StartAttempt command, CancellationToken cancellationToken)
    {
        var invitation = await CurrentInvitation(db, currentUser, cancellationToken);
        if (invitation is null)
        {
            return Error.ForbiddenError("candidate.session", "Open your invitation again.");
        }

        var existing = await db.Attempts.SingleOrDefaultAsync(attempt => attempt.InvitationId == invitation.Id, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != AttemptStatus.InProgress)
            {
                return Error.ConflictError("attempt.closed", "This attempt is already finished.");
            }

            return await PaperBuilder.BuildAsync(db, existing, cancellationToken);
        }

        var version = await db.AssessmentVersions.Include(item => item.Sections).SingleAsync(item => item.Id == invitation.AssessmentVersionId, cancellationToken);
        var assessment = await db.Assessments.SingleAsync(item => item.Id == version.AssessmentId, cancellationToken);
        var sections = new List<PaperSection>();
        foreach (var section in version.Sections.OrderBy(item => item.Order))
        {
            IReadOnlyList<Guid> questions = section.Source == SectionSource.Fixed
                ? section.QuestionVersionIds
                : await DrawAsync(db, assessment.FunctionId, section.Draw!, cancellationToken);
            if (questions.Count == 0)
            {
                return Error.ConflictError("attempt.draw", $"Section {section.Name} has no questions to draw.");
            }

            sections.Add(new PaperSection(section.Id, section.Name, section.Minutes, section.Weight, questions));
        }

        var paper = new Paper(sections);
        var deadline = Deadlines.Compute(clock.UtcNow, version.TotalMinutes, invitation.ExtraTimePercent);
        var attempt = Attempt.Start(invitation.Id, invitation.ApplicationId, clock.UtcNow, deadline, paper);
        invitation.MarkStarted();
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        return await PaperBuilder.BuildAsync(db, attempt, cancellationToken);
    }

    private static async Task<IReadOnlyList<Guid>> DrawAsync(AssessmentsDbContext db, Guid functionId, RandomDraw draw, CancellationToken cancellationToken)
    {
        var pool = await (
            from version in db.QuestionVersions
            join question in db.Questions on version.QuestionId equals question.Id
            where question.FunctionId == functionId && version.IsPublished && draw.Types.Contains(version.Type)
            select version).ToListAsync(cancellationToken);
        if (draw.SkillIds.Count > 0)
        {
            pool = pool.Where(version => version.SkillIds.Any(draw.SkillIds.Contains)).ToList();
        }

        if (pool.Count < draw.Count)
        {
            return [];
        }

        return pool.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(draw.Count).Select(version => version.Id).ToList();
    }

    internal static async Task<Invitation?> CurrentInvitation(AssessmentsDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.InvitationId is not Guid id)
        {
            return null;
        }

        return await db.Invitations.SingleOrDefaultAsync(invitation => invitation.Id == id, cancellationToken);
    }
}

internal sealed class GetPaperHandler(AssessmentsDbContext db, ICurrentUser currentUser) : IQueryHandler<GetPaper, Result<PaperResponse>>
{
    public async Task<Result<PaperResponse>> Handle(GetPaper query, CancellationToken cancellationToken)
    {
        var invitation = await StartAttemptHandler.CurrentInvitation(db, currentUser, cancellationToken);
        if (invitation is null)
        {
            return Error.ForbiddenError("candidate.session", "Open your invitation again.");
        }

        var attempt = await db.Attempts.SingleOrDefaultAsync(item => item.InvitationId == invitation.Id, cancellationToken);
        return attempt is null
            ? Error.NotFoundError("attempt.not_found", "The attempt has not started.")
            : await PaperBuilder.BuildAsync(db, attempt, cancellationToken);
    }
}

internal static class PaperBuilder
{
    public static async Task<PaperResponse> BuildAsync(AssessmentsDbContext db, Attempt attempt, CancellationToken cancellationToken)
    {
        var paper = attempt.ReadPaper();
        var ids = paper.Sections.SelectMany(section => section.QuestionVersionIds).ToList();
        var versions = await db.QuestionVersions.Where(version => ids.Contains(version.Id)).ToDictionaryAsync(version => version.Id, cancellationToken);
        var assessmentVersionId = await db.Invitations.Where(invitation => invitation.Id == attempt.InvitationId).Select(invitation => invitation.AssessmentVersionId).SingleAsync(cancellationToken);
        var version = await db.AssessmentVersions.SingleAsync(item => item.Id == assessmentVersionId, cancellationToken);
        var sections = paper.Sections.Select(section => new PaperSectionResponse(
            section.SectionId,
            section.Name,
            section.Minutes,
            section.Weight,
            attempt.SectionDeadline(section.SectionId),
            section.QuestionVersionIds.Where(versions.ContainsKey).Select(id =>
            {
                var presented = QuestionPresenter.Present(versions[id], attempt.Id);
                return JsonSerializer.SerializeToElement(presented, QuestionJson.Options);
            }).ToList())).ToList();
        var answers = await db.Answers.Where(answer => answer.AttemptId == attempt.Id).ToListAsync(cancellationToken);
        var saved = answers.Select(answer => new SavedAnswerResponse(
            answer.QuestionVersionId,
            JsonSerializer.Deserialize<JsonElement>(answer.ResponseJson, QuestionJson.Options))).ToList();
        return new PaperResponse(attempt.Id, DateTimeOffset.UtcNow, attempt.Deadline, version.DesktopOnly, sections, saved);
    }
}

internal sealed class SaveAnswerHandler(AssessmentsDbContext db, ICurrentUser currentUser, IClock clock) : ICommandHandler<SaveAnswer, Result<SaveAnswerResponse>>
{
    public async Task<Result<SaveAnswerResponse>> Handle(SaveAnswer command, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(item => item.Id == command.AttemptId, cancellationToken);
        if (attempt is null)
        {
            return Error.NotFoundError("attempt.not_found", "Attempt was not found.");
        }

        if (currentUser.InvitationId != attempt.InvitationId)
        {
            return Error.ForbiddenError("candidate.session", "Open your invitation again.");
        }

        var now = clock.UtcNow;
        var closed = attempt.EnsureOpen(now, command.SectionId);
        if (closed is not null)
        {
            return Error.ConflictError(closed.Value.Code, closed.Value.Message);
        }

        if (!attempt.Contains(command.QuestionVersionId))
        {
            return Error.NotFoundError("attempt.question_not_in_paper", "That question is not on this paper.");
        }

        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO assessments.answers (id, workspace_id, attempt_id, question_version_id, response, sequence, saved_at, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {attempt.WorkspaceId}, {command.AttemptId}, {command.QuestionVersionId},
                    {command.ResponseJson}::jsonb, {command.Sequence}, {now}, {now}, {now})
            ON CONFLICT (attempt_id, question_version_id) DO UPDATE
               SET response = EXCLUDED.response, sequence = EXCLUDED.sequence, saved_at = EXCLUDED.saved_at, updated_at = EXCLUDED.updated_at
             WHERE assessments.answers.sequence < EXCLUDED.sequence
            """, cancellationToken);

        return new SaveAnswerResponse(rows == 1, now, attempt.SectionDeadline(command.SectionId));
    }
}

internal sealed class SubmitAttemptHandler(AssessmentsDbContext db, ICurrentUser currentUser, AttemptFinalizer finalizer) : ICommandHandler<SubmitAttempt, Result<ResultResponse>>
{
    public async Task<Result<ResultResponse>> Handle(SubmitAttempt command, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(item => item.Id == command.AttemptId, cancellationToken);
        if (attempt is null)
        {
            return Error.NotFoundError("attempt.not_found", "Attempt was not found.");
        }

        if (currentUser.InvitationId != attempt.InvitationId)
        {
            return Error.ForbiddenError("candidate.session", "Open your invitation again.");
        }

        return await finalizer.CloseAndScoreAsync(command.AttemptId, AttemptStatus.Submitted, cancellationToken);
    }
}

internal sealed class RecordIntegrityHandler(AssessmentsDbContext db, IClock clock) : ICommandHandler<RecordIntegrity, Result>
{
    public async Task<Result> Handle(RecordIntegrity command, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(item => item.Id == command.AttemptId, cancellationToken);
        if (attempt is null)
        {
            return Error.NotFoundError("attempt.not_found", "Attempt was not found.");
        }

        db.IntegrityEvents.Add(IntegrityEvent.Record(attempt.Id, command.Type, clock.UtcNow, command.Detail));
        await db.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}

internal sealed class ListGradeQueueHandler(AssessmentsDbContext db) : IQueryHandler<ListGradeQueue, Result<IReadOnlyList<GradeQueueItem>>>
{
    public async Task<Result<IReadOnlyList<GradeQueueItem>>> Handle(ListGradeQueue query, CancellationToken cancellationToken)
    {
        var rows = await (
            from answer in db.Answers
            join version in db.QuestionVersions on answer.QuestionVersionId equals version.Id
            where version.Type == "long_answer" && !db.Grades.Any(grade => grade.AnswerId == answer.Id)
            orderby answer.SavedAt
            select new { answer, version }).ToListAsync(cancellationToken);

        var attemptIds = rows.Select(row => row.answer.AttemptId).Distinct().ToList();
        var attempts = await db.Attempts.Where(item => attemptIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var invitationIds = attempts.Values.Select(item => item.InvitationId).Distinct().ToList();
        var invitations = await db.Invitations.Where(item => invitationIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var versionIds = invitations.Values.Select(item => item.AssessmentVersionId).Distinct().ToList();
        var versions = await db.AssessmentVersions.Where(item => versionIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.AssessmentId, cancellationToken);
        var assessmentIds = versions.Values.Distinct().ToList();
        var names = await db.Assessments.Where(item => assessmentIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        return rows.Select(row =>
        {
            var rubric = row.version.Content is LongAnswerContent longAnswer ? longAnswer.Rubric : "";
            var assessment = "";
            if (attempts.TryGetValue(row.answer.AttemptId, out var attempt)
                && invitations.TryGetValue(attempt.InvitationId, out var invitation)
                && versions.TryGetValue(invitation.AssessmentVersionId, out var assessmentId)
                && names.TryGetValue(assessmentId, out var found))
            {
                assessment = found;
            }

            return new GradeQueueItem(row.answer.Id, row.answer.AttemptId, assessment, row.version.Content.Prompt, rubric, row.version.Points, row.answer.ResponseJson);
        }).ToList();
    }
}

internal sealed class GradeAnswerHandler(AssessmentsDbContext db, ICurrentUser currentUser, AttemptFinalizer finalizer) : ICommandHandler<GradeAnswer, Result<ResultResponse>>
{
    public async Task<Result<ResultResponse>> Handle(GradeAnswer command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid graderId)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var answer = await db.Answers.SingleOrDefaultAsync(item => item.Id == command.AnswerId, cancellationToken);
        var version = answer is null ? null : await db.QuestionVersions.SingleOrDefaultAsync(item => item.Id == answer.QuestionVersionId, cancellationToken);
        if (answer is null || version is null)
        {
            return Error.NotFoundError("answer.not_found", "Answer was not found.");
        }

        if (command.Score < 0 || command.Score > version.Points)
        {
            return Error.Validation("grade.score", "The score must be between zero and the question points.");
        }

        if (await db.Grades.AnyAsync(grade => grade.AnswerId == answer.Id, cancellationToken))
        {
            return Error.ConflictError("grade.exists", "This answer already has a grade.");
        }

        db.Grades.Add(Grade.Manual(answer.Id, graderId, command.Score, command.Comment, null));
        await db.SaveChangesAsync(cancellationToken);
        return await finalizer.CloseAndScoreAsync(answer.AttemptId, null, cancellationToken);
    }
}

internal sealed class GetResultHandler(AssessmentsDbContext db) : IQueryHandler<GetResult, Result<ResultResponse>>
{
    public async Task<Result<ResultResponse>> Handle(GetResult query, CancellationToken cancellationToken)
    {
        var result = await db.Results.SingleOrDefaultAsync(item => item.AttemptId == query.AttemptId, cancellationToken);
        return result is null
            ? Error.NotFoundError("result.not_found", "Result was not found.")
            : new ResultResponse(result.AttemptId, result.ApplicationId, result.Total, result.Max, result.Passed, result.Status.ToString(), result.SectionScoresJson);
    }
}

public sealed class AttemptFinalizer(AssessmentsDbContext db, IClock clock)
{
    public async Task<Result<ResultResponse>> CloseAndScoreAsync(Guid attemptId, AttemptStatus? closeAs, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(item => item.Id == attemptId, cancellationToken);
        if (attempt is null)
        {
            return Error.NotFoundError("attempt.not_found", "Attempt was not found.");
        }

        if (closeAs is not null && attempt.Status != AttemptStatus.InProgress)
        {
            var already = await db.Results.SingleOrDefaultAsync(item => item.AttemptId == attempt.Id, cancellationToken);
            if (already is not null)
            {
                return Map(already);
            }
        }

        if (closeAs is AttemptStatus status)
        {
            status = Attempt.StatusForClose(status, clock.UtcNow, attempt.Deadline);
            attempt.Close(status, clock.UtcNow);
            var invitation = await db.Invitations.SingleAsync(item => item.Id == attempt.InvitationId, cancellationToken);
            if (status == AttemptStatus.Submitted)
            {
                invitation.MarkSubmitted();
            }
        }

        var paper = attempt.ReadPaper();
        var questionIds = paper.Sections.SelectMany(section => section.QuestionVersionIds).Distinct().ToList();
        var versions = await db.QuestionVersions.Where(version => questionIds.Contains(version.Id)).ToDictionaryAsync(version => version.Id, cancellationToken);
        var answers = await db.Answers.Where(answer => answer.AttemptId == attempt.Id).ToListAsync(cancellationToken);
        var answerIds = answers.Select(answer => answer.Id).ToList();
        var grades = await db.Grades.Where(grade => answerIds.Contains(grade.AnswerId)).ToListAsync(cancellationToken);
        var outstandingManual = false;

        foreach (var questionId in questionIds)
        {
            if (!versions.TryGetValue(questionId, out var version))
            {
                continue;
            }

            var answer = answers.FirstOrDefault(item => item.QuestionVersionId == questionId);
            if (!QuestionRules.IsAutoGraded(version.Content))
            {
                if (answer is null || grades.All(grade => grade.AnswerId != answer.Id))
                {
                    outstandingManual = true;
                }

                continue;
            }

            if (answer is not null && grades.Any(grade => grade.AnswerId == answer.Id))
            {
                continue;
            }

            var outcome = ScoreCalculator.Score(version.Content, version.Points, answer?.ResponseJson ?? "{}");
            if (answer is null)
            {
                answer = Answer.Create(attempt.Id, questionId, "{}", 0, clock.UtcNow);
                db.Answers.Add(answer);
                answers.Add(answer);
            }

            var grade = Grade.Auto(answer.Id, outcome.Score);
            db.Grades.Add(grade);
            grades.Add(grade);
        }

        var sectionScores = new List<object>();
        decimal total = 0;
        decimal max = 0;
        decimal weighted = 0;
        decimal weight = 0;
        foreach (var section in paper.Sections)
        {
            decimal sectionScore = 0;
            decimal sectionMax = 0;
            foreach (var questionId in section.QuestionVersionIds)
            {
                if (!versions.TryGetValue(questionId, out var version))
                {
                    continue;
                }

                sectionMax += version.Points;
                var answer = answers.FirstOrDefault(item => item.QuestionVersionId == questionId);
                var grade = answer is null ? null : grades.FirstOrDefault(item => item.AnswerId == answer.Id);
                sectionScore += grade?.Score ?? 0;
            }

            total += sectionScore;
            max += sectionMax;
            var ratio = sectionMax == 0 ? 0 : sectionScore / sectionMax;
            weighted += ratio * section.Weight;
            weight += section.Weight;
            sectionScores.Add(new { section.SectionId, section.Name, score = sectionScore, max = sectionMax, section.Weight });
        }

        var percent = weight == 0 ? 0 : weighted / weight * 100m;
        var invitationRow = await db.Invitations.SingleAsync(item => item.Id == attempt.InvitationId, cancellationToken);
        var binding = await db.StageBindings.SingleAsync(item => item.Id == invitationRow.StageBindingId, cancellationToken);
        var versionRow = await db.AssessmentVersions.SingleAsync(item => item.Id == invitationRow.AssessmentVersionId, cancellationToken);
        var passMark = binding.PassMarkOverride ?? versionRow.PassMark;
        var passed = percent >= passMark;
        var resultStatus = outstandingManual ? ResultStatus.Provisional : ResultStatus.Final;
        var json = JsonSerializer.Serialize(sectionScores, QuestionJson.Options);
        var existing = await db.Results.SingleOrDefaultAsync(item => item.AttemptId == attempt.Id, cancellationToken);
        if (existing is null)
        {
            existing = AssessmentResult.Create(attempt.Id, attempt.ApplicationId, json, total, max, passed, resultStatus, clock.UtcNow);
            db.Results.Add(existing);
        }
        else
        {
            existing.Update(json, total, max, passed, resultStatus, clock.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Map(existing);
    }

    private static ResultResponse Map(AssessmentResult result) =>
        new(result.AttemptId, result.ApplicationId, result.Total, result.Max, result.Passed, result.Status.ToString(), result.SectionScoresJson);
}

internal sealed class ListResultsHandler(AssessmentsDbContext db, IApplicationReader applications, ICatalogueReader catalogue, IPdfRenderer pdf) : IQueryHandler<ListResults, Result<IReadOnlyList<ResultListItem>>>
{
    public async Task<Result<IReadOnlyList<ResultListItem>>> Handle(ListResults query, CancellationToken cancellationToken)
    {
        var rows = await db.Results.OrderByDescending(item => item.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var snapshots = await applications.FindManyAsync(rows.Select(item => item.ApplicationId).Distinct().ToList(), cancellationToken);
        var names = snapshots.ToDictionary(item => item.Id, item => item);
        var titles = await catalogue.OpeningTitlesAsync(snapshots.Select(item => item.OpeningId).Distinct().ToList(), cancellationToken);
        var attemptIds = rows.Select(item => item.AttemptId).ToList();
        var attempts = await db.Attempts.Where(item => attemptIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var invitationIds = attempts.Values.Select(item => item.InvitationId).Distinct().ToList();
        var invitations = await db.Invitations.Where(item => invitationIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var versionIds = invitations.Values.Select(item => item.AssessmentVersionId).Distinct().ToList();
        var versions = await db.AssessmentVersions.Where(item => versionIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var assessmentIds = versions.Values.Select(item => item.AssessmentId).Distinct().ToList();
        var assessmentNames = await db.Assessments.Where(item => assessmentIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        return rows.Select(item =>
        {
            names.TryGetValue(item.ApplicationId, out var snapshot);
            var openingId = snapshot?.OpeningId;
            var opening = openingId is Guid id && titles.TryGetValue(id, out var title) ? title : "";
            var assessment = "";
            if (attempts.TryGetValue(item.AttemptId, out var attempt)
                && invitations.TryGetValue(attempt.InvitationId, out var invitation)
                && versions.TryGetValue(invitation.AssessmentVersionId, out var version)
                && assessmentNames.TryGetValue(version.AssessmentId, out var found))
            {
                assessment = found;
            }

            return new ResultListItem(
                item.AttemptId,
                item.ApplicationId,
                snapshot?.CandidateName ?? "Candidate",
                openingId,
                opening,
                assessment,
                item.Total,
                item.Max,
                item.Passed,
                item.Status.ToString(),
                item.SectionScoresJson,
                pdf.ContentType == "application/pdf" ? "pdf" : "html");
        }).ToList();
    }
}

public sealed class CloseOverdueAttemptsJob(AssessmentsDbContext db, IClock clock, AttemptFinalizer finalizer) : IMaintenanceJob
{
    public string Name => "close-overdue-attempts";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Attempts.IgnoreQueryFilters()
            .Where(attempt => attempt.Status == AttemptStatus.InProgress && attempt.Deadline < clock.UtcNow)
            .Select(attempt => attempt.Id)
            .Take(50)
            .ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await finalizer.CloseAndScoreAsync(id, AttemptStatus.Expired, cancellationToken);
        }
    }
}

public sealed class ExpireInvitationsJob(AssessmentsDbContext db, IClock clock) : IMaintenanceJob
{
    public string Name => "expire-invitations";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var invitations = await db.Invitations.IgnoreQueryFilters()
            .Where(invitation => invitation.ExpiresAt < clock.UtcNow && (invitation.Status == InvitationStatus.Pending || invitation.Status == InvitationStatus.Sent))
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var invitation in invitations)
        {
            invitation.Expire();
        }

        if (invitations.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
