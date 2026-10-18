using Microsoft.AspNetCore.Identity;

namespace leadflow_crm.API.Models;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public Guid WorkspaceId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Workspace Workspace { get; set; } = null!;
}
