using System.Security.Claims;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Auth;
using leadflow_crm.API.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    LeadflowDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult GetAntiforgeryToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }

    [HttpPost("register")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CurrentUserResponse>> Register(RegisterRequest request)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Conflict(new { error = "An account with this email already exists." });
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var workspace = new Workspace
        {
            Name = request.WorkspaceName.Trim(),
            CurrencyCode = "PKR",
            TimeZoneId = "Asia/Karachi"
        };
        dbContext.Workspaces.Add(workspace);
        await dbContext.SaveChangesAsync();

        var roleDefinitions = new[]
        {
            new RoleDefinition("Owner", PermissionCatalog.All, true),
            new RoleDefinition(
                "Admin",
                PermissionCatalog.All.Except([PermissionCatalog.WorkspaceRolesManage]).ToArray(),
                true),
            new RoleDefinition("User", PermissionCatalog.DefaultUser, true)
        };

        var createdRoles = new Dictionary<string, ApplicationRole>(StringComparer.Ordinal);
        foreach (var definition in roleDefinitions)
        {
            var role = new ApplicationRole
            {
                WorkspaceId = workspace.Id,
                Name = $"{definition.DisplayName.ToLowerInvariant()}-{workspace.Id:N}",
                DisplayName = definition.DisplayName,
                IsSystemRole = definition.IsSystemRole
            };

            var roleResult = await roleManager.CreateAsync(role);
            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return Problem(
                    title: "Workspace setup failed.",
                    detail: string.Join(" ", roleResult.Errors.Select(error => error.Description)),
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            createdRoles.Add(definition.DisplayName, role);
            dbContext.RolePermissions.AddRange(definition.PermissionKeys.Select(permissionKey =>
                new RolePermission
                {
                    WorkspaceId = workspace.Id,
                    RoleId = role.Id,
                    PermissionKey = permissionKey
                }));
        }

        var user = new ApplicationUser
        {
            WorkspaceId = workspace.Id,
            DisplayName = request.DisplayName.Trim(),
            Email = email,
            UserName = email,
            EmailConfirmed = false
        };

        var userResult = await userManager.CreateAsync(user, request.Password);
        if (!userResult.Succeeded)
        {
            await transaction.RollbackAsync();
            return BadRequest(new
            {
                errors = userResult.Errors.Select(error => new
                {
                    error.Code,
                    error.Description
                })
            });
        }

        dbContext.UserRoles.Add(new ApplicationUserRole
        {
            UserId = user.Id,
            RoleId = createdRoles["Owner"].Id,
            WorkspaceId = workspace.Id
        });
        dbContext.AuditLogs.Add(new AuditLogEntry
        {
            WorkspaceId = workspace.Id,
            ActorUserId = user.Id,
            ActorWorkspaceId = workspace.Id,
            ActorType = "User",
            EventType = "workspace.created",
            TargetType = "Workspace",
            TargetId = workspace.Id.ToString(),
            AfterJson = JsonSerializer.Serialize(new
            {
                workspace.Name,
                workspace.CurrencyCode,
                workspace.TimeZoneId,
                OwnerUserId = user.Id
            })
        });

        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        await signInManager.SignInAsync(user, isPersistent: false);

        var response = await CreateCurrentUserResponseAsync(user);
        return Created("/api/auth/me", response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CurrentUserResponse>> Login(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive ||
            !await dbContext.Workspaces.AnyAsync(workspace =>
                workspace.Id == user.WorkspaceId && workspace.IsActive))
        {
            return Unauthorized(new { error = "Email or password is incorrect." });
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            request.RememberMe,
            lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked,
                new { error = "Too many failed attempts. Try again later." });
        }

        if (!result.Succeeded)
        {
            return Unauthorized(new { error = "Email or password is incorrect." });
        }

        return Ok(await CreateCurrentUserResponseAsync(user));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentUserResponse>> GetCurrentUser()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var parsedUserId))
        {
            return Unauthorized();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate =>
                candidate.Id == parsedUserId &&
                candidate.IsActive &&
                candidate.Workspace.IsActive);

        return user is null ? Unauthorized() : Ok(await CreateCurrentUserResponseAsync(user));
    }

    [HttpPost("logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    private async Task<CurrentUserResponse> CreateCurrentUserResponseAsync(ApplicationUser user)
    {
        var workspace = await dbContext.Workspaces
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == user.WorkspaceId);

        var roles = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == user.Id && userRole.WorkspaceId == user.WorkspaceId
            orderby role.DisplayName
            select role.DisplayName)
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

    private sealed record RoleDefinition(
        string DisplayName,
        IReadOnlyList<string> PermissionKeys,
        bool IsSystemRole);
}
