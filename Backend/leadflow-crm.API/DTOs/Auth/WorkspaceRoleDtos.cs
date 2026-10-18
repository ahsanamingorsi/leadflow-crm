using System.ComponentModel.DataAnnotations;

namespace leadflow_crm.API.DTOs.Auth;

public sealed class CreateWorkspaceRoleRequest
{
    [Required, StringLength(80, MinimumLength = 2)]
    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<string> Permissions { get; init; } = [];
}

public sealed class UpdateWorkspaceRolePermissionsRequest
{
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

public sealed record WorkspaceRoleResponse(
    Guid Id,
    string DisplayName,
    bool IsSystemRole,
    IReadOnlyList<string> Permissions);
