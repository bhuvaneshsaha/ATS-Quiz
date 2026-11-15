using System.Text.Json;
using FluentValidation;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;

namespace Shortlist.Modules.Assessments.Application;

public static class AssessmentPermissions
{
    public const string QuestionsRead = "Assessments.Questions.Read";
    public const string QuestionsWrite = "Assessments.Questions.Write";
    public const string QuestionsApprove = "Assessments.Questions.Approve";
    public const string AssessmentsWrite = "Assessments.Assessments.Write";
    public const string AssessmentsPublish = "Assessments.Assessments.Publish";
    public const string BindingsManage = "Assessments.Bindings.Manage";
    public const string InvitationsView = "Assessments.Invitations.View";
    public const string GradingGrade = "Assessments.Grading.Grade";
    public const string ResultsView = "Assessments.Results.View";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(QuestionsRead, "Assessments", "Read questions while building a test"),
        new(QuestionsWrite, "Assessments", "Create and edit questions"),
        new(QuestionsApprove, "Assessments", "Approve questions for use"),
        new(AssessmentsWrite, "Assessments", "Build assessments"),
        new(AssessmentsPublish, "Assessments", "Publish an assessment version"),
        new(BindingsManage, "Assessments", "Attach a test to an opening stage"),
        new(InvitationsView, "Assessments", "View invitations"),
        new(GradingGrade, "Assessments", "Grade written answers"),
        new(ResultsView, "Assessments", "View results and reports")
    ];
}

public sealed class AssessmentsPermissionSource : IPermissionSource
{
    public IEnumerable<PermissionDefinition> GetPermissions() => AssessmentPermissions.All;
}

public sealed record QuestionResponse(Guid Id, Guid FunctionId, string Status, Guid CurrentVersionId, int VersionNumber, string Type, decimal Points, bool VersionPublished, JsonElement Content);
public sealed record AssessmentResponse(Guid Id, Guid FunctionId, string Name, string Status, AssessmentVersionResponse? Current);
public sealed record AssessmentVersionResponse(Guid Id, int Number, decimal PassMark, bool DesktopOnly, bool IsPublished, IReadOnlyList<SectionResponse> Sections);
public sealed record SectionResponse(Guid Id, int Order, string Name, int Minutes, decimal Weight, string Source, IReadOnlyList<Guid> QuestionVersionIds, int? DrawCount);
public sealed record BindingResponse(Guid Id, Guid OpeningStageId, Guid AssessmentVersionId, decimal? PassMarkOverride, int ValidDays, int ExtraTimePercent, string AssessmentName);
public sealed record InvitationResponse(Guid Id, Guid ApplicationId, string Status, DateTimeOffset ExpiresAt, int ExtraTimePercent);
public sealed record PaperResponse(Guid AttemptId, DateTimeOffset ServerTime, DateTimeOffset Deadline, bool DesktopOnly, IReadOnlyList<PaperSectionResponse> Sections, IReadOnlyList<SavedAnswerResponse> Answers);
public sealed record PaperSectionResponse(Guid SectionId, string Name, int Minutes, decimal Weight, DateTimeOffset Deadline, IReadOnlyList<JsonElement> Questions);
public sealed record SavedAnswerResponse(Guid QuestionVersionId, JsonElement Response);
public sealed record CandidateSessionResponse(Guid InvitationId, string AssessmentName, bool DesktopOnly, int TotalMinutes, bool AttemptStarted);
public sealed record SaveAnswerResponse(bool Accepted, DateTimeOffset ServerTime, DateTimeOffset Deadline);
public sealed record GradeQueueItem(Guid AnswerId, Guid AttemptId, string Prompt, string Rubric, decimal Points, string ResponseJson);
public sealed record ResultResponse(Guid AttemptId, Guid ApplicationId, decimal Total, decimal Max, bool Passed, string Status, string SectionScoresJson);
public sealed record ResultListItem(Guid AttemptId, Guid ApplicationId, string CandidateName, decimal Total, decimal Max, bool Passed, string Status);

public sealed record SectionInput(string Name, int Minutes, decimal Weight, string Source, Guid[] QuestionVersionIds, int? DrawCount, string[]? DrawTypes, Guid[]? SkillIds);

public sealed record CreateQuestion(Guid FunctionId, decimal Points, int? TimeLimitSeconds, Guid[] SkillIds, JsonElement Content) : ICommand<Result<QuestionResponse>>;
public sealed record UpdateQuestionDraft(Guid QuestionId, decimal Points, int? TimeLimitSeconds, Guid[] SkillIds, JsonElement Content) : ICommand<Result<QuestionResponse>>;
public sealed record SubmitQuestion(Guid QuestionId) : ICommand<Result<QuestionResponse>>;
public sealed record PublishQuestion(Guid QuestionId) : ICommand<Result<QuestionResponse>>;
public sealed record ReviseQuestion(Guid QuestionId, decimal Points, int? TimeLimitSeconds, Guid[] SkillIds, JsonElement Content) : ICommand<Result<QuestionResponse>>;
public sealed record ListQuestions(Guid? FunctionId, string? Status) : IQuery<Result<IReadOnlyList<QuestionResponse>>>;
public sealed record GetQuestion(Guid QuestionId) : IQuery<Result<QuestionResponse>>;
public sealed record CreateAssessment(Guid FunctionId, string Name, decimal PassMark, bool DesktopOnly, SectionInput[] Sections) : ICommand<Result<AssessmentResponse>>;
public sealed record PublishAssessment(Guid AssessmentId) : ICommand<Result<AssessmentResponse>>;
public sealed record ListAssessments(Guid? FunctionId) : IQuery<Result<IReadOnlyList<AssessmentResponse>>>;
public sealed record CreateBinding(Guid OpeningStageId, Guid AssessmentVersionId, decimal? PassMarkOverride, int ValidDays, int ExtraTimePercent) : ICommand<Result<BindingResponse>>;
public sealed record ListBindings(Guid? OpeningStageId) : IQuery<Result<IReadOnlyList<BindingResponse>>>;
public sealed record ListInvitations(Guid? ApplicationId) : IQuery<Result<IReadOnlyList<InvitationResponse>>>;
public sealed record RequestCandidateCode(string Token) : ICommand<Result>;
public sealed record VerifyCandidateCode(string Token, string Code) : ICommand<Result<VerifiedCandidate>>;
public sealed record VerifiedCandidate(Guid InvitationId, Guid WorkspaceId, string Email, string AssessmentName, bool DesktopOnly, int TotalMinutes, bool AttemptStarted);
public sealed record GetCandidateSession : IQuery<Result<CandidateSessionResponse>>;
public sealed record StartAttempt : ICommand<Result<PaperResponse>>;
public sealed record GetPaper : IQuery<Result<PaperResponse>>;
public sealed record SaveAnswer(Guid AttemptId, Guid QuestionVersionId, Guid SectionId, int Sequence, string ResponseJson) : ICommand<Result<SaveAnswerResponse>>;
public sealed record SubmitAttempt(Guid AttemptId) : ICommand<Result<ResultResponse>>;
public sealed record RecordIntegrity(Guid AttemptId, string Type, string? Detail) : ICommand<Result>;
public sealed record ListGradeQueue : IQuery<Result<IReadOnlyList<GradeQueueItem>>>;
public sealed record GradeAnswer(Guid AnswerId, decimal Score, string? Comment) : ICommand<Result<ResultResponse>>;
public sealed record GetResult(Guid AttemptId) : IQuery<Result<ResultResponse>>;
public sealed record ListResults : IQuery<Result<IReadOnlyList<ResultListItem>>>;

public sealed class CreateQuestionValidator : AbstractValidator<CreateQuestion>
{
    public CreateQuestionValidator()
    {
        RuleFor(command => command.FunctionId).NotEmpty();
        RuleFor(command => command.Points).GreaterThan(0);
    }
}

public sealed class CreateAssessmentValidator : AbstractValidator<CreateAssessment>
{
    public CreateAssessmentValidator()
    {
        RuleFor(command => command.Name).NotEmpty();
        RuleFor(command => command.Sections).NotEmpty();
        RuleFor(command => command.PassMark).InclusiveBetween(0, 100);
    }
}
