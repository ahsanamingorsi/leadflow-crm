using System.Security.Claims;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Auth;
using leadflow_crm.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[ApiController]
[Route("api/workspace/roles")]
[Authorize(Policy = PermissionCatalog.WorkspaceRolesManage)]
public sealed class WorkspaceRolesController(
    LeadflowDbContext dbContext,
    RoleManager<ApplicationRole> roleManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkspaceRoleResponse>>> GetRoles()
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var roles = await dbContext.Roles
            .AsNoTracking()
            .Where(role => role.WorkspaceId == currentUser.WorkspaceId)
            .OrderBy(role => role.DisplayName)
            .ToListAsync();

        var roleIds = roles.Select(role => role.Id).ToArray();
        var permissions = await dbContext.RolePermissions
            .AsNoTracking()
            .Where(permission => permission.WorkspaceId == currentUser.WorkspaceId &&
                                 roleIds.Contains(permission.RoleId))
            .ToListAsync();

        var permissionsByRole = permissions
            .GroupBy(permission => permission.RoleId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(permission => permission.PermissionKey)
                    .OrderBy(permissionKey => permissionKey)
                    .ToArray());

        return Ok(roles.Select(role => new WorkspaceRoleResponse(
            role.Id,
            role.DisplayName,
            role.IsSystemRole,
            permissionsByRole.GetValueOrDefault(role.Id, [])))
            .ToArray());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<WorkspaceRoleResponse>> CreateRole(CreateWorkspaceRoleRequest request)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var displayName = request.DisplayName.Trim();
        if (IsBaseRoleName(displayName))
        {
            return BadRequest(new { error = "Owner, Admin and User are reserved role names." });
        }

        var permissionError = ValidatePermissions(request.Permissions);
        if (permissionError is not null)
        {
            return BadRequest(new { error = permissionError });
        }

        var duplicateName = await dbContext.Roles.AnyAsync(role =>
            role.WorkspaceId == currentUser.WorkspaceId && role.DisplayName == displayName);
        if (duplicateName)
        {
            return Conflict(new { error = "A role with this name already exists." });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var role = new ApplicationRole
        {
            WorkspaceId = currentUser.WorkspaceId,
            Name = $"custom-{Guid.NewGuid():N}",
            DisplayName = displayName,
            IsSystemRole = false
        };

        var createResult = await roleManager.CreateAsync(role);

        if (!createResult.Succeeded)
        {
            return Problem(
                title: "Role creation failed.",
                detail: string.Join(" ", createResult.Errors.Select(error => error.Description)),
                statusCode: StatusCodes.Status500InternalServerError);
        }

        var permissionRows = request.Permissions
            .Distinct(StringComparer.Ordinal)
            .Select(permissionKey => new RolePermission
            {
                WorkspaceId = currentUser.WorkspaceId,
                RoleId = role.Id,
                PermissionKey = permissionKey
            })
            .ToArray();
        dbContext.RolePermissions.AddRange(permissionRows);
        dbContext.AuditLogs.Add(CreateAuditEntry(
            currentUser,
            "workspace.role.created",
            role.Id,
            after: new { role.DisplayName, Permissions = permissionRows.Select(row => row.PermissionKey) }));

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var response = new WorkspaceRoleResponse(
            role.Id,
            role.DisplayName,
            role.IsSystemRole,
            permissionRows.Select(row => row.PermissionKey).OrderBy(key => key).ToArray());
        return Created($"/api/workspace/roles/{role.Id}", response);
    }

    [HttpPut("{roleId:guid}/permissions")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<WorkspaceRoleResponse>> UpdatePermissions(
        Guid roleId,
        UpdateWorkspaceRolePermissionsRequest request)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var permissionError = ValidatePermissions(request.Permissions);
        if (permissionError is not null)
        {
            return BadRequest(new { error = permissionError });
        }

        var role = await dbContext.Roles.SingleOrDefaultAsync(candidate =>
            candidate.Id == roleId && candidate.WorkspaceId == currentUser.WorkspaceId);
        if (role is null)
        {
            return NotFound();
        }

        if (string.Equals(role.DisplayName, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "The Owner role permissions cannot be changed." });
        }

        var existingPermissions = await dbContext.RolePermissions
            .Where(permission => permission.WorkspaceId == currentUser.WorkspaceId &&
                                 permission.RoleId == role.Id)
            .ToListAsync();
        var before = existingPermissions.Select(permission => permission.PermissionKey).OrderBy(key => key).ToArray();
        var after = request.Permissions.Distinct(StringComparer.Ordinal).OrderBy(key => key).ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        dbContext.RolePermissions.RemoveRange(existingPermissions);
        dbContext.RolePermissions.AddRange(after.Select(permissionKey => new RolePermission
        {
            WorkspaceId = currentUser.WorkspaceId,
            RoleId = role.Id,
            PermissionKey = permissionKey
        }));
        dbContext.AuditLogs.Add(CreateAuditEntry(
            currentUser,
            "workspace.role.permissions.updated",
            role.Id,
            before,
            after));

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new WorkspaceRoleResponse(role.Id, role.DisplayName, role.IsSystemRole, after));
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return null;
        }

        return await dbContext.Users.SingleOrDefaultAsync(user =>
            user.Id == userId && user.IsActive && user.Workspace.IsActive);
    }

    private static bool IsBaseRoleName(string name) =>
        string.Equals(name, "Owner", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "User", StringComparison.OrdinalIgnoreCase);

    private static string? ValidatePermissions(IReadOnlyList<string> permissions)
    {
        var supportedPermissions = PermissionCatalog.All.ToHashSet(StringComparer.Ordinal);
        var unsupported = permissions
            .Where(permission => !supportedPermissions.Contains(permission))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return unsupported.Length == 0
            ? null
            : $"Unsupported permission key(s): {string.Join(", ", unsupported)}";
    }

    private static AuditLogEntry CreateAuditEntry(
        ApplicationUser actor,
        string eventType,
        Guid roleId,
        object? before = null,
        object? after = null) => new()
    {
        WorkspaceId = actor.WorkspaceId,
        ActorUserId = actor.Id,
        ActorWorkspaceId = actor.WorkspaceId,
        ActorType = "User",
        EventType = eventType,
        TargetType = "WorkspaceRole",
        TargetId = roleId.ToString(),
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
        AfterJson = after is null ? null : JsonSerializer.Serialize(after)
    };
}
