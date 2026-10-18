using System.ComponentModel.DataAnnotations;

namespace leadflow_crm.API.DTOs.Auth;

public sealed record ChangeMemberRoleRequest(Guid RoleId);

public sealed class CreateInvitationRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public Guid RoleId { get; init; }
}

public sealed class AcceptInvitationRequest
{
    [Required, StringLength(128, MinimumLength = 32)]
    public string Token { get; init; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}

public sealed record InvitationCreatedResponse(
    Guid InvitationId,
    string Email,
    string RoleName,
    DateTime ExpiresAtUtc,
    string InvitationToken);

public sealed record WorkspaceMemberResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    bool IsActive,
    IReadOnlyList<string> Roles);
