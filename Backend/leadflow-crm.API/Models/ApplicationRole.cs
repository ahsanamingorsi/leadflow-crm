using Microsoft.AspNetCore.Identity;

namespace leadflow_crm.API.Models;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public Guid WorkspaceId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
}
