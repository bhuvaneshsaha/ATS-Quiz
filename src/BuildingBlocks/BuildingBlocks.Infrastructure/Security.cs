using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.BuildingBlocks.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ReadGuid(ClaimTypes.NameIdentifier) ?? ReadGuid("sub");

    public string? Email => User?.FindFirstValue(ClaimTypes.Email);

    public bool IsCandidate => User?.FindFirstValue("auth_kind") == "candidate";

    public Guid? InvitationId => ReadGuid("invitation_id");

    private Guid? ReadGuid(string claim)
    {
        var value = User?.FindFirstValue(claim);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public sealed class HttpTenantContext(IHttpContextAccessor accessor, IOptions<WorkspaceOptions> options) : ITenantContext
{
    public Guid WorkspaceId
    {
        get
        {
            var claim = accessor.HttpContext?.User.FindFirstValue("workspace_id");
            return Guid.TryParse(claim, out var id) ? id : options.Value.Id;
        }
    }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null)
        {
            return existing;
        }

        if (!policyName.Contains('.', StringComparison.Ordinal))
        {
            return null;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}

public sealed class PermissionAuthorizationHandler(IPermissionChecker checker, ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return;
        }

        if (await checker.HasAsync(userId, requirement.Permission, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}

public static class SecurityRegistration
{
    public static IServiceCollection AddShortlistSecurity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddAuthorization();
        return services;
    }
}
