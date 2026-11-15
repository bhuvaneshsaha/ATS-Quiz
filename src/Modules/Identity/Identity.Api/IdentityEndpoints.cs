using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Shortlist.Adapters.Email.Smtp;
using Shortlist.BuildingBlocks.Application;
using Shortlist.BuildingBlocks.Infrastructure;
using Shortlist.Modules.Identity.Application;
using Shortlist.Modules.Identity.Contracts;

namespace Shortlist.Modules.Identity.Api;

public static class IdentityEndpoints
{
    public static void Map(IEndpointRouteBuilder api, bool exposeDevMailbox)
    {
        var auth = api.MapGroup("/v1/auth").WithTags("Auth");
        auth.MapGet("/xsrf", (HttpContext http, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(http);
            return Results.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous().DisableAntiforgery();
        auth.MapPost("/sign-in", async (SignIn command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToHttpResult()).AllowAnonymous().DisableAntiforgery();
        auth.MapPost("/sign-in/mfa", async (SignInMfa command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToHttpResult()).AllowAnonymous().DisableAntiforgery();
        auth.MapPost("/sign-out", async (HttpContext http) =>
        {
            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(http);
            return Results.NoContent();
        }).RequireAuthorization();
        auth.MapPost("/forgot-password", async (ForgotPassword command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToHttpResult()).AllowAnonymous().DisableAntiforgery();
        auth.MapPost("/reset-password", async (ResetPassword command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToHttpResult()).AllowAnonymous().DisableAntiforgery();
        auth.MapGet("/me", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new GetSession(), ct)).ToHttpResult()).RequireAuthorization();
        auth.MapGet("/mfa", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new StartMfa(), ct)).ToHttpResult()).RequireAuthorization();
        auth.MapPost("/mfa", async (EnableMfa command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToHttpResult()).RequireAuthorization();

        var users = api.MapGroup("/v1/users").WithTags("Users").RequireAuthorization();
        users.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListUsers(), ct)).ToHttpResult()).RequirePermission(IdentityPermissions.UsersManage);
        users.MapPost("/", async (CreateUser command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(user => $"/api/identity/v1/users/{user.Id}")).RequirePermission(IdentityPermissions.UsersManage).WithIdempotency();
        users.MapPost("/{id:guid}/status", async (Guid id, StatusBody body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new SetUserStatus(id, body.Status), ct)).ToHttpResult()).RequirePermission(IdentityPermissions.UsersManage);

        var roles = api.MapGroup("/v1/roles").WithTags("Roles").RequireAuthorization();
        roles.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListRoles(), ct)).ToHttpResult()).RequirePermission(IdentityPermissions.RolesManage);
        roles.MapGet("/permissions", async (IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Query(new ListPermissions(), ct)).ToHttpResult()).RequirePermission(IdentityPermissions.RolesManage);
        roles.MapPost("/", async (CreateRole command, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(command, ct)).ToCreatedResult(role => $"/api/identity/v1/roles/{role.Id}")).RequirePermission(IdentityPermissions.RolesManage).WithIdempotency();
        roles.MapPut("/{id:guid}", async (Guid id, UpdateRoleRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new UpdateRole(id, body.Name, body.Description, body.Permissions), ct)).ToHttpResult()).RequirePermission(IdentityPermissions.RolesManage);
        roles.MapPost("/{id:guid}/assignments", async (Guid id, AssignRoleRequest body, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new AssignRole(id, body.UserId, body.Scope, body.HiringUnitIds, body.FunctionIds), ct)).ToHttpResult()).RequirePermission(IdentityPermissions.RolesManage);
        roles.MapDelete("/assignments/{assignmentId:guid}", async (Guid assignmentId, IDispatcher dispatcher, CancellationToken ct) =>
            (await dispatcher.Send(new DeleteAssignment(assignmentId), ct)).ToHttpResult()).RequirePermission(IdentityPermissions.RolesManage);

        if (exposeDevMailbox)
        {
            api.MapGet("/v1/dev/emails", (LoggingEmailSender sender) => sender.Messages).RequireAuthorization().RequirePermission(IdentityPermissions.UsersManage);
        }
    }
}

public sealed record StatusBody(string Status);
public sealed record UpdateRoleRequest(string Name, string? Description, string[] Permissions);
public sealed record AssignRoleRequest(Guid UserId, string Scope, Guid[] HiringUnitIds, Guid[] FunctionIds);
