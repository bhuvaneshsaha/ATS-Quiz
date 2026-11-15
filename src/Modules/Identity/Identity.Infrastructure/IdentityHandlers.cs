using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.Modules.Identity.Application;
using Shortlist.Modules.Identity.Domain;

namespace Shortlist.Modules.Identity.Infrastructure;

internal sealed class SignInHandler(SignInManager<StaffUser> signInManager, UserManager<StaffUser> users, AccessService access, IConfiguration configuration)
    : ICommandHandler<SignIn, Result<SessionResponse>>
{
    public async Task<Result<SessionResponse>> Handle(SignIn command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is null || user.Status != "Active")
        {
            return Error.Validation("auth.failed", "Email or password is incorrect.");
        }

        var result = await signInManager.PasswordSignInAsync(user, command.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            return new SessionResponse(null, user.Email, user.DisplayName, [], Modules(configuration), true);
        }

        if (!result.Succeeded)
        {
            return Error.Validation("auth.failed", result.IsLockedOut ? "This account is locked." : "Email or password is incorrect.");
        }

        var permissions = await access.ListAsync(user.Id, cancellationToken);
        return new SessionResponse(user.Id, user.Email, user.DisplayName, permissions, Modules(configuration), false);
    }

    internal static IReadOnlyList<ModuleFlag> Modules(IConfiguration configuration) =>
    [
        new("Core", configuration.GetValue("Modules:Core:Enabled", true)),
        new("Assessments", configuration.GetValue("Modules:Assessments:Enabled", true)),
        new("Identity", configuration.GetValue("Modules:Identity:Enabled", true))
    ];
}

internal sealed class SignInMfaHandler(SignInManager<StaffUser> signInManager, UserManager<StaffUser> users, AccessService access, IConfiguration configuration)
    : ICommandHandler<SignInMfa, Result<SessionResponse>>
{
    public async Task<Result<SessionResponse>> Handle(SignInMfa command, CancellationToken cancellationToken)
    {
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(command.Code, isPersistent: true, rememberClient: false);
        if (!result.Succeeded)
        {
            return Error.Validation("auth.mfa", "That authentication code is not valid.");
        }

        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            user = await users.GetUserAsync(signInManager.Context.User);
        }

        if (user is null)
        {
            return Error.Validation("auth.mfa", "The sign-in session expired.");
        }

        var permissions = await access.ListAsync(user.Id, cancellationToken);
        return new SessionResponse(user.Id, user.Email, user.DisplayName, permissions, SignInHandler.Modules(configuration), false);
    }
}

internal sealed class GetSessionHandler(ICurrentUser currentUser, UserManager<StaffUser> users, AccessService access, IConfiguration configuration)
    : IQueryHandler<GetSession, Result<SessionResponse>>
{
    public async Task<Result<SessionResponse>> Handle(GetSession query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var permissions = await access.ListAsync(user.Id, cancellationToken);
        return new SessionResponse(user.Id, user.Email, user.DisplayName, permissions, SignInHandler.Modules(configuration), false);
    }
}

internal sealed class ForgotPasswordHandler(UserManager<StaffUser> users, IEmailSender email) : ICommandHandler<ForgotPassword, Result>
{
    public async Task<Result> Handle(ForgotPassword command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await email.SendAsync(new EmailMessage(user.Email!, "Reset your Shortlist password", $"Reset token: {token}"), cancellationToken);
        }

        return Result.Ok();
    }
}

internal sealed class ResetPasswordHandler(UserManager<StaffUser> users) : ICommandHandler<ResetPassword, Result>
{
    public async Task<Result> Handle(ResetPassword command, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(command.Email.Trim());
        if (user is null)
        {
            return Error.Validation("auth.reset", "The reset request is not valid.");
        }

        var result = await users.ResetPasswordAsync(user, command.Token, command.Password);
        return result.Succeeded
            ? Result.Ok()
            : Error.Validation("auth.reset", string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}

internal sealed class CreateUserHandler(UserManager<StaffUser> users, ITenantContext tenant, IdentityDbContext db) : ICommandHandler<CreateUser, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> Handle(CreateUser command, CancellationToken cancellationToken)
    {
        var user = new StaffUser
        {
            Id = Guid.CreateVersion7(),
            UserName = command.Email.Trim().ToLowerInvariant(),
            Email = command.Email.Trim().ToLowerInvariant(),
            DisplayName = command.DisplayName.Trim(),
            WorkspaceId = tenant.WorkspaceId,
            EmailConfirmed = true,
            Status = "Active"
        };
        var result = await users.CreateAsync(user, command.Password);
        if (!result.Succeeded)
        {
            return Error.Validation("user.invalid", string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        return Result<UserResponse>.Or(await UserReader.LoadAsync(db, user.Id, cancellationToken), Error.NotFoundError("user.not_found", "User was not found."));
    }
}

internal sealed class ListUsersHandler(IdentityDbContext db, ITenantContext tenant) : IQueryHandler<ListUsers, Result<IReadOnlyList<UserResponse>>>
{
    public async Task<Result<IReadOnlyList<UserResponse>>> Handle(ListUsers query, CancellationToken cancellationToken)
    {
        var users = await db.Users.Where(user => user.WorkspaceId == tenant.WorkspaceId).OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var responses = new List<UserResponse>();
        foreach (var user in users)
        {
            var loaded = await UserReader.LoadAsync(db, user.Id, cancellationToken);
            if (loaded is not null)
            {
                responses.Add(loaded);
            }
        }

        return responses;
    }
}

internal sealed class SetUserStatusHandler(UserManager<StaffUser> users, IdentityDbContext db, AccessService access) : ICommandHandler<SetUserStatus, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> Handle(SetUserStatus command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            return Error.NotFoundError("user.not_found", "User was not found.");
        }

        user.Status = command.Status == "Disabled" ? "Disabled" : "Active";
        if (user.Status == "Disabled")
        {
            await users.UpdateSecurityStampAsync(user);
        }

        await users.UpdateAsync(user);
        await access.InvalidateAsync(user.Id, cancellationToken);
        return Result<UserResponse>.Or(await UserReader.LoadAsync(db, user.Id, cancellationToken), Error.NotFoundError("user.not_found", "User was not found."));
    }
}

internal sealed class CreateRoleHandler(RoleManager<StaffRole> roles, IdentityDbContext db, ITenantContext tenant) : ICommandHandler<CreateRole, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(CreateRole command, CancellationToken cancellationToken)
    {
        var role = new StaffRole
        {
            Id = Guid.CreateVersion7(),
            Name = command.Name.Trim(),
            Description = command.Description,
            WorkspaceId = tenant.WorkspaceId,
            IsTemplate = false
        };
        var result = await roles.CreateAsync(role);
        if (!result.Succeeded)
        {
            return Error.Validation("role.invalid", string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        await ReplacePermissions(db, role.Id, command.Permissions, cancellationToken);
        return Result<RoleResponse>.Or(await RoleReader.LoadAsync(db, role.Id, cancellationToken), Error.NotFoundError("role.not_found", "Role was not found."));
    }

    internal static async Task ReplacePermissions(IdentityDbContext db, Guid roleId, IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        var existing = await db.RolePermissions.Where(permission => permission.RoleId == roleId).ToListAsync(cancellationToken);
        db.RolePermissions.RemoveRange(existing);
        foreach (var permission in permissions.Distinct())
        {
            db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionKey = permission });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal sealed class UpdateRoleHandler(RoleManager<StaffRole> roles, IdentityDbContext db, AccessService access) : ICommandHandler<UpdateRole, Result<RoleResponse>>
{
    public async Task<Result<RoleResponse>> Handle(UpdateRole command, CancellationToken cancellationToken)
    {
        var role = await roles.FindByIdAsync(command.RoleId.ToString());
        if (role is null)
        {
            return Error.NotFoundError("role.not_found", "Role was not found.");
        }

        role.Name = command.Name.Trim();
        role.Description = command.Description;
        await roles.UpdateAsync(role);
        await CreateRoleHandler.ReplacePermissions(db, role.Id, command.Permissions, cancellationToken);
        var userIds = await db.RoleAssignments.Where(assignment => assignment.RoleId == role.Id).Select(assignment => assignment.UserId).ToListAsync(cancellationToken);
        foreach (var userId in userIds)
        {
            await access.InvalidateAsync(userId, cancellationToken);
        }

        return Result<RoleResponse>.Or(await RoleReader.LoadAsync(db, role.Id, cancellationToken), Error.NotFoundError("role.not_found", "Role was not found."));
    }
}

internal sealed class ListRolesHandler(IdentityDbContext db, ITenantContext tenant) : IQueryHandler<ListRoles, Result<IReadOnlyList<RoleResponse>>>
{
    public async Task<Result<IReadOnlyList<RoleResponse>>> Handle(ListRoles query, CancellationToken cancellationToken)
    {
        var roles = await db.Roles.Where(role => role.WorkspaceId == tenant.WorkspaceId).OrderBy(role => role.Name).ToListAsync(cancellationToken);
        var responses = new List<RoleResponse>();
        foreach (var role in roles)
        {
            var loaded = await RoleReader.LoadAsync(db, role.Id, cancellationToken);
            if (loaded is not null)
            {
                responses.Add(loaded);
            }
        }

        return responses;
    }
}

internal sealed class ListPermissionsHandler(IdentityDbContext db, ITenantContext tenant) : IQueryHandler<ListPermissions, Result<IReadOnlyList<PermissionResponse>>>
{
    public async Task<Result<IReadOnlyList<PermissionResponse>>> Handle(ListPermissions query, CancellationToken cancellationToken)
    {
        var rows = await db.PermissionCatalogue.Where(entry => entry.WorkspaceId == tenant.WorkspaceId).OrderBy(entry => entry.Key).ToListAsync(cancellationToken);
        return rows.Select(entry => new PermissionResponse(entry.Key, entry.Module, entry.Description)).ToList();
    }
}

internal sealed class AssignRoleHandler(IdentityDbContext db, AccessService access) : ICommandHandler<AssignRole, Result<AssignmentResponse>>
{
    public async Task<Result<AssignmentResponse>> Handle(AssignRole command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Scope>(command.Scope, out var scope))
        {
            return Error.Validation("assignment.scope", "Scope must be All, Own, Assigned or Sent.");
        }

        var role = await db.Roles.SingleOrDefaultAsync(item => item.Id == command.RoleId, cancellationToken);
        if (role is null)
        {
            return Error.NotFoundError("role.not_found", "Role was not found.");
        }

        var assignment = RoleAssignment.Create(command.UserId, command.RoleId, scope, command.HiringUnitIds, command.FunctionIds);
        db.RoleAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken);
        await access.InvalidateAsync(command.UserId, cancellationToken);
        return new AssignmentResponse(assignment.Id, role.Id, role.Name ?? "", scope.ToString(), assignment.HiringUnitIds, assignment.FunctionIds);
    }
}

internal sealed class DeleteAssignmentHandler(IdentityDbContext db, AccessService access) : ICommandHandler<DeleteAssignment, Result>
{
    public async Task<Result> Handle(DeleteAssignment command, CancellationToken cancellationToken)
    {
        var assignment = await db.RoleAssignments.SingleOrDefaultAsync(item => item.Id == command.AssignmentId, cancellationToken);
        if (assignment is null)
        {
            return Error.NotFoundError("assignment.not_found", "Assignment was not found.");
        }

        db.RoleAssignments.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        await access.InvalidateAsync(assignment.UserId, cancellationToken);
        return Result.Ok();
    }
}

internal sealed class StartMfaHandler(UserManager<StaffUser> users, ICurrentUser currentUser, UrlEncoder urlEncoder) : IQueryHandler<StartMfa, Result<MfaSetupResponse>>
{
    public async Task<Result<MfaSetupResponse>> Handle(StartMfa query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.NotFoundError("user.not_found", "User was not found.");
        }

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        var email = urlEncoder.Encode(user.Email ?? "staff");
        var uri = $"otpauth://totp/Shortlist:{email}?secret={key}&issuer=Shortlist&digits=6";
        return new MfaSetupResponse(FormatKey(key!), uri);
    }

    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString();
    }
}

internal sealed class EnableMfaHandler(UserManager<StaffUser> users, ICurrentUser currentUser) : ICommandHandler<EnableMfa, Result>
{
    public async Task<Result> Handle(EnableMfa command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Error.ForbiddenError("auth.required", "Sign in first.");
        }

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Error.NotFoundError("user.not_found", "User was not found.");
        }

        var valid = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, command.Code.Replace(" ", "", StringComparison.Ordinal));
        if (!valid)
        {
            return Error.Validation("auth.mfa", "That authentication code is not valid.");
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        return Result.Ok();
    }
}

internal static class UserReader
{
    public static async Task<UserResponse?> LoadAsync(IdentityDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var assignments = await (
            from assignment in db.RoleAssignments
            join role in db.Roles on assignment.RoleId equals role.Id
            where assignment.UserId == id
            select new AssignmentResponse(assignment.Id, role.Id, role.Name ?? "", assignment.Scope.ToString(), assignment.HiringUnitIds, assignment.FunctionIds))
            .ToListAsync(cancellationToken);
        return new UserResponse(user.Id, user.Email ?? "", user.DisplayName, user.Status, assignments);
    }
}

internal static class RoleReader
{
    public static async Task<RoleResponse?> LoadAsync(IdentityDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (role is null)
        {
            return null;
        }

        var permissions = await db.RolePermissions.Where(permission => permission.RoleId == id).Select(permission => permission.PermissionKey).OrderBy(key => key).ToListAsync(cancellationToken);
        return new RoleResponse(role.Id, role.Name ?? "", role.Description, role.IsTemplate, permissions);
    }
}
