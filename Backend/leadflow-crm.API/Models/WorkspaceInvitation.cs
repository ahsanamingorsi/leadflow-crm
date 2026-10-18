namespace leadflow_crm.API.Models;

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid RoleId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public Guid InvitedByWorkspaceId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public Workspace Workspace { get; set; } = null!;
    public ApplicationRole Role { get; set; } = null!;
    public ApplicationUser InvitedByUser { get; set; } = null!;
}
