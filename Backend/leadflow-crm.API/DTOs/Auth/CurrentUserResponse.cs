namespace leadflow_crm.API.DTOs.Auth;

public sealed record CurrentUserResponse(
    Guid UserId,
    string DisplayName,
    string Email,
    Guid WorkspaceId,
    string WorkspaceName,
    string CurrencyCode,
    string TimeZoneId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions);
