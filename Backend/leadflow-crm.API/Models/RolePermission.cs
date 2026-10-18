namespace leadflow_crm.API.Models;

public sealed class RolePermission
{
    public Guid WorkspaceId { get; set; }
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = string.Empty;
    public DateTime GrantedAtUtc { get; set; } = DateTime.UtcNow;

    public ApplicationRole Role { get; set; } = null!;
}
