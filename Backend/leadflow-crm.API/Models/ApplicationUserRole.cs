using Microsoft.AspNetCore.Identity;

namespace leadflow_crm.API.Models;

public sealed class ApplicationUserRole : IdentityUserRole<Guid>
{
    public Guid WorkspaceId { get; set; }

}
