using System.ComponentModel.DataAnnotations;

namespace leadflow_crm.API.DTOs.Auth;

public sealed class RegisterRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string WorkspaceName { get; init; } = string.Empty;

    [Required, StringLength(120, MinimumLength = 2)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = string.Empty;
}
