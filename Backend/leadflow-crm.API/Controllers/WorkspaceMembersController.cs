using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Auth;
using leadflow_crm.API.Models;
using leadflow_crm.API.Models.Crm;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[ApiController]
[Route("api/workspace")]
public sealed class WorkspaceMembersController(
    LeadflowDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    [HttpGet("members")]
    [Authorize(Policy = PermissionCatalog.WorkspaceMembersManage)]
    public async Task<ActionResult<IReadOnlyList<WorkspaceMemberResponse>>> GetMembers()
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var members = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.WorkspaceId == currentUser.WorkspaceId)
            .OrderBy(user => user.DisplayName)
            .ToListAsync();

        var userIds = members.Select(user => user.Id).ToArray();
        var assignments = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.WorkspaceId == currentUser.WorkspaceId && userIds.Contains(userRole.UserId)
            select new { userRole.UserId, role.DisplayName })
            .ToListAsync();
        var rolesByUser = assignments
            .GroupBy(assignment => assignment.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.DisplayName).OrderBy(name => name).ToArray());

        return Ok(members.Select(user => new WorkspaceMemberResponse(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.IsActive,
            rolesByUser.GetValueOrDefault(user.Id, [])))
            .ToArray());
    }

    [HttpPost("invitations")]
    [Authorize(Policy = PermissionCatalog.WorkspaceMembersManage)]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<InvitationCreatedResponse>> CreateInvitation(CreateInvitationRequest request)
    {
        var currentUser = await GetCurrentUserAsync();
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        var role = await dbContext.Roles.SingleOrDefaultAsync(candidate =>
            candidate.Id == request.RoleId && candidate.WorkspaceId == currentUser.WorkspaceId);
        if (role is null)
        {
            return NotFound(new { error = "Role was not found in this workspace." });
        }

        var actorIsOwner = await IsOwnerAsync(currentUser);
        var grantsRoleManagement = await dbContext.RolePermissions.AnyAsync(permission =>
            permission.WorkspaceId == currentUser.WorkspaceId &&
            permission.RoleId == role.Id &&
            permission.PermissionKey == PermissionCatalog.WorkspaceRolesManage);
        if (string.Equals(role.DisplayName, "Owner", StringComparison.OrdinalIgnoreCase) ||
            (!actorIsOwner && grantsRoleManagement))
        {
            return Forbid();
        }

        var now = DateTime.UtcNow;
        var existingInvitation = await dbContext.WorkspaceInvitations.AnyAsync(invitation =>
            invitation.WorkspaceId == currentUser.WorkspaceId &&
            invitation.Email == email &&
            invitation.AcceptedAtUtc == null &&
            invitation.RevokedAtUtc == null &&
            invitation.ExpiresAtUtc > now);
        if (existingInvitation)
        {
            return Conflict(new { error = "An active invitation already exists for this email." });
        }

        var token = CreateToken();
        var invitation = new WorkspaceInvitation
        {
            WorkspaceId = currentUser.WorkspaceId,
            RoleId = role.Id,
            InvitedByUserId = currentUser.Id,
            InvitedByWorkspaceId = currentUser.WorkspaceId,
            Email = email,
            TokenHash = HashToken(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7)
        };
        dbContext.WorkspaceInvitations.Add(invitation);
        dbContext.AuditLogs.Add(CreateAuditEntry(
            currentUser,
            "workspace.member.invited",
            "WorkspaceInvitation",
            invitation.Id,
            after: new { invitation.Email, Role = role.DisplayName, invitation.ExpiresAtUtc }));
        await dbContext.SaveChangesAsync();

        return Created($"/api/workspace/invitations/{invitation.Id}", new InvitationCreatedResponse(
            invitation.Id,
            invitation.Email,
            role.DisplayName,
            invitation.ExpiresAtUtc,
            token));
    }

    [HttpPost("invitations/accept")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CurrentUserResponse>> AcceptInvitation(AcceptInvitationRequest request)
    {
        var now = DateTime.UtcNow;
        var tokenHash = HashToken(request.Token);
        var invitation = await dbContext.WorkspaceInvitations
            .SingleOrDefaultAsync(candidate =>
                candidate.TokenHash == tokenHash &&
                candidate.AcceptedAtUtc == null &&
                candidate.RevokedAtUtc == null &&
                candidate.ExpiresAtUtc > now &&
                candidate.Workspace.IsActive);

        if (invitation is null)
        {
            return BadRequest(new { error = "This invitation is invalid, expired, or already used." });
        }

        if (await userManager.FindByEmailAsync(invitation.Email) is not null)
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var user = new ApplicationUser
        {
            WorkspaceId = invitation.WorkspaceId,
            DisplayName = request.DisplayName.Trim(),
            Email = invitation.Email,
            UserName = invitation.Email,
            EmailConfirmed = false
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
            return BadRequest(new
            {
                errors = result.Errors.Select(error => new { error.Code, error.Description })
            });
        }

        dbContext.UserRoles.Add(new ApplicationUserRole
        {
            UserId = user.Id,
            RoleId = invitation.RoleId,
            WorkspaceId = invitation.WorkspaceId
        });
        dbContext.EmployeeAvailabilities.Add(new EmployeeAvailability { WorkspaceId = user.WorkspaceId, UserId = user.Id });
        invitation.AcceptedAtUtc = now;
        dbContext.AuditLogs.Add(CreateAuditEntry(
            user,
            "workspace.invitation.accepted",
            "WorkspaceInvitation",
            invitation.Id,
            after: new { UserId = user.Id, RoleId = invitation.RoleId }));

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await signInManager.SignInAsync(user, isPersistent: false);

        return Ok(await CreateCurrentUserResponseAsync(user));
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

    private Task<bool> IsOwnerAsync(ApplicationUser user) => (
        from userRole in dbContext.UserRoles.AsNoTracking()
        join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
        where userRole.UserId == user.Id &&
              userRole.WorkspaceId == user.WorkspaceId &&
              role.DisplayName == "Owner"
        select role.Id).AnyAsync();

    private async Task<CurrentUserResponse> CreateCurrentUserResponseAsync(ApplicationUser user)
    {
        var workspace = await dbContext.Workspaces
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.WorkspaceId);
        var roles = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == user.Id && userRole.WorkspaceId == user.WorkspaceId
            select role.DisplayName)
            .OrderBy(name => name)
            .ToListAsync();
        var permissions = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join permission in dbContext.RolePermissions.AsNoTracking()
                on new { userRole.RoleId, userRole.WorkspaceId }
                equals new { permission.RoleId, permission.WorkspaceId }
            where userRole.UserId == user.Id && userRole.WorkspaceId == user.WorkspaceId
            select permission.PermissionKey)
            .Distinct()
            .OrderBy(permissionKey => permissionKey)
            .ToListAsync();

        return new CurrentUserResponse(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            workspace.Id,
            workspace.Name,
            workspace.CurrencyCode,
            workspace.TimeZoneId,
            roles,
            permissions);
    }

    private static string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string HashToken(string token) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static AuditLogEntry CreateAuditEntry(
        ApplicationUser actor,
        string eventType,
        string targetType,
        Guid targetId,
        object? before = null,
        object? after = null) => new()
    {
        WorkspaceId = actor.WorkspaceId,
        ActorUserId = actor.Id,
        ActorWorkspaceId = actor.WorkspaceId,
        ActorType = "User",
        EventType = eventType,
        TargetType = targetType,
        TargetId = targetId.ToString(),
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
        AfterJson = after is null ? null : JsonSerializer.Serialize(after)
    };
}
