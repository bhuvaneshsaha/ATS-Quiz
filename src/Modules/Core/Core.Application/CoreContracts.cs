using FluentValidation;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.Modules.Core.Domain;

namespace Shortlist.Modules.Core.Application;

public static class CorePermissions
{
    public const string HiringUnitsView = "Core.HiringUnits.View";
    public const string HiringUnitsManage = "Core.HiringUnits.Manage";
    public const string FunctionsView = "Core.Functions.View";
    public const string FunctionsManage = "Core.Functions.Manage";
    public const string OpeningsView = "Core.Openings.View";
    public const string OpeningsManage = "Core.Openings.Manage";
    public const string CandidatesView = "Core.Candidates.View";
    public const string CandidatesManage = "Core.Candidates.Manage";
    public const string ApplicationsView = "Core.Applications.View";
    public const string ApplicationsMove = "Core.Applications.MoveStage";
    public const string NotesWrite = "Core.Notes.Write";
    public const string AuditView = "Core.Audit.View";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(HiringUnitsView, "Core", "View hiring units"),
        new(HiringUnitsManage, "Core", "Create and edit hiring units"),
        new(FunctionsView, "Core", "View functions"),
        new(FunctionsManage, "Core", "Create and edit functions"),
        new(OpeningsView, "Core", "View openings"),
        new(OpeningsManage, "Core", "Create and edit openings"),
        new(CandidatesView, "Core", "View candidates"),
        new(CandidatesManage, "Core", "Create and edit candidates"),
        new(ApplicationsView, "Core", "View applications"),
        new(ApplicationsMove, "Core", "Move an application between stages"),
        new(NotesWrite, "Core", "Write notes"),
        new(AuditView, "Core", "View the hiring audit log")
    ];
}

public sealed class CorePermissionSource : IPermissionSource
{
    public IEnumerable<PermissionDefinition> GetPermissions() => CorePermissions.All;
}

public sealed record HiringUnitResponse(Guid Id, string Type, string Name, Guid? OwnerUserId, string? ExternalId, bool IsArchived);
public sealed record FunctionResponse(Guid Id, string Name, Guid? OwnerUserId, bool IsArchived);
public sealed record StageResponse(Guid Id, string Key, string Name, string Type, string? AutomatedBy, int Order, bool AutoAdvance);
public sealed record OpeningSummary(Guid Id, string Code, string Title, string Status, Guid HiringUnitId, string HiringUnitName, Guid FunctionId, string FunctionName, int Positions, int ApplicationCount, DateTimeOffset CreatedAt, uint RowVersion);
public sealed record OpeningDetails(Guid Id, string Code, string Title, string Status, Guid HiringUnitId, Guid FunctionId, int Positions, string? Location, string? Seniority, string? Reason, DateOnly? TargetDate, Guid? HiringManagerId, Guid? RecruiterId, string? ExternalId, uint RowVersion, IReadOnlyList<StageResponse> Stages, IReadOnlyList<ApplicationResponse> Applications);
public sealed record ApplicationResponse(Guid Id, Guid CandidateId, string CandidateName, string CandidateEmail, Guid CurrentStageId, string StageName, string Status, decimal? Score, decimal? MaxScore, bool? Passed, string? ResultStatus);
public sealed record CandidateSummary(Guid Id, string Name, string Email, string? Phone, string? Location, string? Source, DateTimeOffset CreatedAt);
public sealed record CandidateDetails(Guid Id, string Name, string Email, string? Phone, string? Location, string? Source, string? ExternalId, DateTimeOffset? ConsentAt, IReadOnlyList<ApplicationResponse> Applications, IReadOnlyList<NoteResponse> Notes);
public sealed record NoteResponse(Guid Id, string Body, Guid AuthorUserId, DateTimeOffset CreatedAt);
public sealed record AuditResponse(Guid Id, Guid? ActorId, string Action, string EntityType, Guid EntityId, string? Before, string? After, DateTimeOffset At);
public sealed record RankingRow(Guid ApplicationId, Guid CandidateId, string CandidateName, string StageName, decimal? Score, bool? Passed, string Status);

public sealed record CreateHiringUnit(string Type, string Name, Guid? OwnerUserId, string? ExternalId) : ICommand<Result<HiringUnitResponse>>;
public sealed record ListHiringUnits(bool IncludeArchived) : IQuery<Result<IReadOnlyList<HiringUnitResponse>>>;
public sealed record CreateFunction(string Name, Guid? OwnerUserId) : ICommand<Result<FunctionResponse>>;
public sealed record ListFunctions(bool IncludeArchived) : IQuery<Result<IReadOnlyList<FunctionResponse>>>;
public sealed record CreateOpening(string Title, Guid HiringUnitId, Guid FunctionId, int Positions, string? Location, string? Seniority, string? Reason, DateOnly? TargetDate, Guid? HiringManagerId, Guid? RecruiterId, string? ExternalId) : ICommand<Result<OpeningDetails>>;
public sealed record UpdateOpening(Guid Id, string Title, int Positions, string? Location, string? Seniority, string? Reason, DateOnly? TargetDate, Guid? HiringManagerId, Guid? RecruiterId, uint RowVersion) : ICommand<Result<OpeningDetails>>;
public sealed record ChangeOpeningStatus(Guid Id, string Status) : ICommand<Result<OpeningDetails>>;
public sealed record SetStageAutoAdvance(Guid OpeningId, Guid StageId, bool AutoAdvance) : ICommand<Result<OpeningDetails>>;
public sealed record ListOpenings(string? Status, Guid? HiringUnitId, string? Search, string? After, int Limit = 50) : IQuery<Result<CursorPage<OpeningSummary>>>;
public sealed record GetOpening(Guid Id) : IQuery<Result<OpeningDetails>>;
public sealed record GetRanking(Guid OpeningId) : IQuery<Result<IReadOnlyList<RankingRow>>>;
public sealed record CreateCandidate(string Name, string Email, string? Phone, string? Location, string? Source, string? ExternalId, bool Consent, string? ConsentText) : ICommand<Result<CandidateDetails>>;
public sealed record UpdateCandidate(Guid Id, string Name, string? Phone, string? Location, string? Source) : ICommand<Result<CandidateDetails>>;
public sealed record ListCandidates(string? Search, string? After, int Limit = 50) : IQuery<Result<CursorPage<CandidateSummary>>>;
public sealed record GetCandidate(Guid Id) : IQuery<Result<CandidateDetails>>;
public sealed record EraseCandidate(Guid Id) : ICommand<Result>;
public sealed record AddApplication(Guid OpeningId, Guid CandidateId) : ICommand<Result<ApplicationResponse>>;
public sealed record MoveApplicationStage(Guid OpeningId, Guid ApplicationId, Guid ToStageId) : ICommand<Result<ApplicationResponse>>;
public sealed record AddNote(Guid? ApplicationId, Guid? CandidateId, string Body) : ICommand<Result<NoteResponse>>;
public sealed record ListAudit(string? After, int Limit = 50) : IQuery<Result<CursorPage<AuditResponse>>>;

public sealed class CreateHiringUnitValidator : AbstractValidator<CreateHiringUnit>
{
    public CreateHiringUnitValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Type).Must(type => type is "Department" or "Client");
    }
}

public sealed class CreateOpeningValidator : AbstractValidator<CreateOpening>
{
    public CreateOpeningValidator()
    {
        RuleFor(command => command.Title).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Positions).GreaterThan(0);
        RuleFor(command => command.HiringUnitId).NotEmpty();
        RuleFor(command => command.FunctionId).NotEmpty();
    }
}

public sealed class CreateCandidateValidator : AbstractValidator<CreateCandidate>
{
    public CreateCandidateValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
    }
}

public sealed class MoveApplicationStageValidator : AbstractValidator<MoveApplicationStage>
{
    public MoveApplicationStageValidator()
    {
        RuleFor(command => command.ToStageId).NotEmpty();
    }
}

public sealed class AddNoteValidator : AbstractValidator<AddNote>
{
    public AddNoteValidator()
    {
        RuleFor(command => command.Body).NotEmpty().MaximumLength(4000);
        RuleFor(command => command).Must(command => command.ApplicationId is not null || command.CandidateId is not null)
            .WithMessage("A note belongs to a candidate or an application.");
    }
}
