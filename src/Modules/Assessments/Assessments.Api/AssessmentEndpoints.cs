using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Assessments.Application;

namespace Shortlist.Modules.Assessments.Api;

public static class AssessmentEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var questions = api.MapGroup("/v1/questions").WithTags("Questions").RequireAuthorization();
        questions.MapGet("/", async (Guid? functionId, string? status, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListQuestions(functionId, status), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.QuestionsWrite);
        questions.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new GetQuestion(id), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.QuestionsWrite);
        questions.MapPost("/", async (CreateQuestion command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(question => $"/api/assessments/v1/questions/{question.Id}")).RequirePermission(AssessmentPermissions.QuestionsWrite).WithIdempotency();
        questions.MapPut("/{id:guid}", async (Guid id, UpdateQuestionRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new UpdateQuestionDraft(id, body.Points, body.TimeLimitSeconds, body.SkillIds, body.Content), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.QuestionsWrite);
        questions.MapPost("/{id:guid}/submit", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SubmitQuestion(id), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.QuestionsWrite);
        questions.MapPost("/{id:guid}/publish", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new PublishQuestion(id), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.QuestionsApprove);
        questions.MapPost("/{id:guid}/revise", async (Guid id, UpdateQuestionRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new ReviseQuestion(id, body.Points, body.TimeLimitSeconds, body.SkillIds, body.Content), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.QuestionsWrite);

        var assessments = api.MapGroup("/v1/assessments").WithTags("Assessments").RequireAuthorization();
        assessments.MapGet("/", async (Guid? functionId, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListAssessments(functionId), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.AssessmentsWrite);
        assessments.MapPost("/", async (CreateAssessment command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(assessment => $"/api/assessments/v1/assessments/{assessment.Id}")).RequirePermission(AssessmentPermissions.AssessmentsWrite).WithIdempotency();
        assessments.MapPost("/{id:guid}/publish", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new PublishAssessment(id), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.AssessmentsPublish);

        api.MapPost("/v1/bindings", async (CreateBinding command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(binding => $"/api/assessments/v1/bindings/{binding.Id}"))
            .RequireAuthorization().RequirePermission(AssessmentPermissions.BindingsManage).WithIdempotency();
        api.MapGet("/v1/invitations", async (Guid? applicationId, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListInvitations(applicationId), ct)).ToHttpResult())
            .RequireAuthorization().RequirePermission(AssessmentPermissions.InvitationsView);

        var grading = api.MapGroup("/v1/grading").WithTags("Grading").RequireAuthorization();
        grading.MapGet("/queue", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListGradeQueue(), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.GradingGrade);
        grading.MapPost("/answers/{answerId:guid}", async (Guid answerId, GradeRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new GradeAnswer(answerId, body.Score, body.Comment), ct)).ToHttpResult()).RequirePermission(AssessmentPermissions.GradingGrade);

        api.MapGet("/v1/results/{attemptId:guid}", async (Guid attemptId, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new GetResult(attemptId), ct)).ToHttpResult()).RequireAuthorization().RequirePermission(AssessmentPermissions.ResultsView);
        api.MapGet("/v1/results/{attemptId:guid}/report", async (Guid attemptId, IDispatcher dispatcher, IPdfRenderer pdf, CancellationToken ct) =>
        {
            var result = await dispatcher.Query(new GetResult(attemptId), ct);
            if (!result.IsSuccess || result.Value is null)
            {
                return result.ToHttpResult();
            }

            var html = $"<html><body><h1>Assessment result</h1><p>Score {result.Value.Total} / {result.Value.Max}</p><p>{(result.Value.Passed ? "Passed" : "Not passed")} ({result.Value.Status})</p></body></html>";
            var bytes = await pdf.RenderHtmlAsync(html, ct);
            return Results.File(bytes, pdf.ContentType, $"result-{attemptId}.{pdf.FileExtension}");
        }).RequireAuthorization().RequirePermission(AssessmentPermissions.ResultsView);

        var take = api.MapGroup("/v1/take").WithTags("Take");
        take.MapPost("/{token}/code", async (string token, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new RequestCandidateCode(token), ct)).ToHttpResult()).AllowAnonymous().DisableAntiforgery().RequireRateLimiting("candidate-auth");
        take.MapPost("/{token}/verify", async (string token, CodeRequest body, IDispatcher dispatcher, HttpContext http, CancellationToken ct) =>
        {
            var result = await dispatcher.Send(new VerifyCandidateCode(token, body.Code), ct);
            if (!result.IsSuccess || result.Value is null)
            {
                return result.ToHttpResult();
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, result.Value.InvitationId.ToString()),
                new Claim("invitation_id", result.Value.InvitationId.ToString()),
                new Claim("workspace_id", result.Value.WorkspaceId.ToString()),
                new Claim("auth_kind", "candidate"),
                new Claim(ClaimTypes.Email, result.Value.Email)
            };
            await http.SignInAsync("Candidate", new ClaimsPrincipal(new ClaimsIdentity(claims, "Candidate")));
            return Results.Ok(new { invitationId = result.Value.InvitationId });
        }).AllowAnonymous().DisableAntiforgery().RequireRateLimiting("candidate-auth");
        take.MapPost("/start", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new StartAttempt(), ct)).ToHttpResult()).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Candidate" });
        take.MapGet("/paper", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new GetPaper(), ct)).ToHttpResult()).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Candidate" });
        take.MapPost("/answers", async (SaveAnswerRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SaveAnswer(body.AttemptId, body.QuestionVersionId, body.SectionId, body.Sequence, body.Response.GetRawText()), ct)).ToHttpResult())
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Candidate" });
        take.MapPost("/submit", async (SubmitRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SubmitAttempt(body.AttemptId), ct)).ToHttpResult()).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Candidate" });
        take.MapPost("/integrity", async (IntegrityRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new RecordIntegrity(body.AttemptId, body.Type, body.Detail), ct)).ToHttpResult()).RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = "Candidate" });
    }
}

public sealed record UpdateQuestionRequest(decimal Points, int? TimeLimitSeconds, Guid[] SkillIds, JsonElement Content);
public sealed record GradeRequest(decimal Score, string? Comment);
public sealed record CodeRequest(string Code);
public sealed record SaveAnswerRequest(Guid AttemptId, Guid QuestionVersionId, Guid SectionId, int Sequence, JsonElement Response);
public sealed record SubmitRequest(Guid AttemptId);
public sealed record IntegrityRequest(Guid AttemptId, string Type, string? Detail);
