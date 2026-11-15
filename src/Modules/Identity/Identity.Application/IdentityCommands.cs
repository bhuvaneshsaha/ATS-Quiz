using FluentValidation;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.Modules.Identity.Contracts;

namespace Shortlist.Modules.Identity.Application;

public sealed class IdentityPermissionSource : IPermissionSource
{
    public IEnumerable<PermissionDefinition> GetPermissions() =>
    [
        new(IdentityPermissions.UsersManage, "Identity", "Create and edit staff accounts"),
        new(IdentityPermissions.RolesManage, "Identity", "Create roles and assign them"),
        new(IdentityPermissions.AuditView, "Identity", "View identity audit entries")
    ];
}

public sealed record SessionResponse(
    Guid? Id,
    string? Email,
    string? DisplayName,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ModuleFlag> Modules,
    bool MfaRequired);

public sealed record ModuleFlag(string Name, bool Enabled);

public sealed record UserResponse(Guid Id, string Email, string DisplayName, string Status, IReadOnlyList<AssignmentResponse> Assignments);
public sealed record AssignmentResponse(Guid Id, Guid RoleId, string RoleName, string Scope, IReadOnlyList<Guid> HiringUnitIds, IReadOnlyList<Guid> FunctionIds);
public sealed record RoleResponse(Guid Id, string Name, string? Description, bool IsTemplate, IReadOnlyList<string> Permissions);
public sealed record PermissionResponse(string Key, string Module, string Description);

public sealed record SignIn(string Email, string Password) : ICommand<Result<SessionResponse>>;
public sealed record SignInMfa(string Code) : ICommand<Result<SessionResponse>>;
public sealed record ForgotPassword(string Email) : ICommand<Result>;
public sealed record ResetPassword(string Email, string Token, string Password) : ICommand<Result>;
public sealed record GetSession : IQuery<Result<SessionResponse>>;
public sealed record CreateUser(string Email, string DisplayName, string Password) : ICommand<Result<UserResponse>>;
public sealed record ListUsers : IQuery<Result<IReadOnlyList<UserResponse>>>;
public sealed record SetUserStatus(Guid UserId, string Status) : ICommand<Result<UserResponse>>;
public sealed record CreateRole(string Name, string? Description, IReadOnlyList<string> Permissions) : ICommand<Result<RoleResponse>>;
public sealed record UpdateRole(Guid RoleId, string Name, string? Description, IReadOnlyList<string> Permissions) : ICommand<Result<RoleResponse>>;
public sealed record ListRoles : IQuery<Result<IReadOnlyList<RoleResponse>>>;
public sealed record ListPermissions : IQuery<Result<IReadOnlyList<PermissionResponse>>>;
public sealed record AssignRole(Guid RoleId, Guid UserId, string Scope, IReadOnlyList<Guid> HiringUnitIds, IReadOnlyList<Guid> FunctionIds) : ICommand<Result<AssignmentResponse>>;
public sealed record DeleteAssignment(Guid AssignmentId) : ICommand<Result>;
public sealed record StartMfa : IQuery<Result<MfaSetupResponse>>;
public sealed record EnableMfa(string Code) : ICommand<Result>;
public sealed record MfaSetupResponse(string SharedKey, string OtpauthUri);

public sealed class SignInValidator : AbstractValidator<SignIn>
{
    public SignInValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.Password).NotEmpty();
    }
}

public sealed class CreateUserValidator : AbstractValidator<CreateUser>
{
    public CreateUserValidator()
    {
        RuleFor(command => command.Email).NotEmpty().EmailAddress();
        RuleFor(command => command.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Password).MinimumLength(12);
    }
}

public sealed class CreateRoleValidator : AbstractValidator<CreateRole>
{
    public CreateRoleValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(100);
        RuleFor(command => command.Permissions).NotEmpty();
    }
}
