using Microsoft.AspNetCore.Authorization;

namespace leadflow_crm.API.Authorization;

public sealed class PermissionRequirement(string permissionKey) : IAuthorizationRequirement
{
    public string PermissionKey { get; } = permissionKey;
}
