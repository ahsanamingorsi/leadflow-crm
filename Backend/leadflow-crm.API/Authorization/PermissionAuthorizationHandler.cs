using System.Security.Claims;
using leadflow_crm.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Authorization;

public sealed class PermissionAuthorizationHandler(LeadflowDbContext dbContext)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .Where(candidate =>
                candidate.Id == userId &&
                candidate.IsActive &&
                candidate.Workspace.IsActive)
            .Select(candidate => new { candidate.Id, candidate.WorkspaceId })
            .SingleOrDefaultAsync();

        if (user is null)
        {
            return;
        }

        var hasPermission = await dbContext.RolePermissions
            .AsNoTracking()
            .AnyAsync(permission =>
                permission.WorkspaceId == user.WorkspaceId &&
                permission.PermissionKey == requirement.PermissionKey &&
                dbContext.UserRoles.Any(userRole =>
                    userRole.UserId == user.Id &&
                    userRole.WorkspaceId == user.WorkspaceId &&
                    userRole.RoleId == permission.RoleId));

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
