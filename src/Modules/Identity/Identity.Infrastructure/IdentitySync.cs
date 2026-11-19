using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.Modules.Identity.Domain;

namespace Shortlist.Modules.Identity.Infrastructure;

public sealed class SeedOptions
{
    public const string Section = "Seed";
    public string AdminEmail { get; set; } = "";
    public string AdminPassword { get; set; } = "";
    public string AdminDisplayName { get; set; } = "Administrator";
}

public sealed class IdentityStartupSync(
    IdentityDbContext db,
    ITenantContext tenant,
    IEnumerable<IPermissionSource> sources,
    RoleManager<StaffRole> roles,
    UserManager<StaffUser> users,
    IOptions<SeedOptions> seed,
    IHostEnvironment environment,
    ILogger<IdentityStartupSync> logger) : IStartupSync
{
    private static readonly Dictionary<string, string[]> Templates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Recruiter"] =
        [
            "Core.HiringUnits.View", "Core.Functions.View", "Core.Openings.View", "Core.Openings.Manage",
            "Core.Candidates.View", "Core.Candidates.Manage", "Core.Applications.View", "Core.Applications.MoveStage",
            "Core.Notes.Write", "Assessments.Questions.Read", "Assessments.Assessments.Write", "Assessments.Assessments.Publish",
            "Assessments.Bindings.Manage", "Assessments.Invitations.View", "Assessments.Results.View"
        ],
        ["Hiring manager"] =
        [
            "Core.Openings.View", "Core.Candidates.View", "Core.Applications.View", "Assessments.Results.View"
        ],
        ["Grader"] = ["Assessments.Grading.Grade"],
        ["Question author"] = ["Core.Functions.View", "Assessments.Questions.Read", "Assessments.Questions.Write"],
        ["Question reviewer"] = ["Assessments.Questions.Read", "Assessments.Questions.Write", "Assessments.Questions.Approve"]
    };

    public int Order => 1;

    public async Task SyncAsync(CancellationToken cancellationToken)
    {
        var definitions = sources.SelectMany(source => source.GetPermissions()).ToList();
        var existing = await db.PermissionCatalogue.Where(entry => entry.WorkspaceId == tenant.WorkspaceId).ToListAsync(cancellationToken);
        foreach (var definition in definitions)
        {
            var row = existing.SingleOrDefault(entry => entry.Key == definition.Key);
            if (row is null)
            {
                db.PermissionCatalogue.Add(new PermissionCatalogueEntry
                {
                    Key = definition.Key,
                    WorkspaceId = tenant.WorkspaceId,
                    Module = definition.Module,
                    Description = definition.Description
                });
            }
            else
            {
                row.Description = definition.Description;
                row.Module = definition.Module;
            }
        }

        var known = definitions.Select(definition => definition.Key).ToHashSet();
        db.PermissionCatalogue.RemoveRange(existing.Where(entry => !known.Contains(entry.Key)));
        await db.SaveChangesAsync(cancellationToken);

        var allKeys = definitions.Select(definition => definition.Key).ToArray();
        await EnsureRole("Super admin", "Every permission in the workspace.", allKeys, isTemplate: true, cancellationToken);
        foreach (var (name, permissions) in Templates)
        {
            await EnsureRole(name, $"{name} template.", permissions.Where(known.Contains).ToArray(), true, cancellationToken);
        }

        await EnsureAdmin(cancellationToken);
    }

    private async Task EnsureRole(string name, string description, string[] permissions, bool isTemplate, CancellationToken cancellationToken)
    {
        var role = await roles.FindByNameAsync(name);
        if (role is null)
        {
            role = new StaffRole
            {
                Id = Guid.CreateVersion7(),
                Name = name,
                Description = description,
                IsTemplate = isTemplate,
                WorkspaceId = tenant.WorkspaceId
            };
            var created = await roles.CreateAsync(role);
            if (!created.Succeeded)
            {
                logger.LogWarning("Could not create role {Role}: {Errors}", name, string.Join(", ", created.Errors.Select(error => error.Description)));
                return;
            }
        }

        var current = await db.RolePermissions.Where(permission => permission.RoleId == role.Id).Select(permission => permission.PermissionKey).ToListAsync(cancellationToken);
        foreach (var permission in permissions.Except(current))
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = permission });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureAdmin(CancellationToken cancellationToken)
    {
        var options = seed.Value;
        if (string.IsNullOrWhiteSpace(options.AdminEmail) || string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            return;
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Test") && options.AdminPassword == "Change-Me-Admin-1")
        {
            logger.LogError("Refusing to seed the development admin password outside Development.");
            return;
        }

        var email = options.AdminEmail.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new StaffUser
            {
                Id = Guid.CreateVersion7(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = options.AdminDisplayName,
                WorkspaceId = tenant.WorkspaceId,
                Status = "Active"
            };
            var created = await users.CreateAsync(user, options.AdminPassword);
            if (!created.Succeeded)
            {
                logger.LogError("Could not seed admin: {Errors}", string.Join(", ", created.Errors.Select(error => error.Description)));
                return;
            }
        }

        var role = await roles.FindByNameAsync("Super admin");
        if (role is null)
        {
            return;
        }

        var assigned = await db.RoleAssignments.AnyAsync(assignment => assignment.UserId == user.Id && assignment.RoleId == role.Id, cancellationToken);
        if (!assigned)
        {
            db.RoleAssignments.Add(RoleAssignment.Create(user.Id, role.Id, Scope.All, [], []));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
