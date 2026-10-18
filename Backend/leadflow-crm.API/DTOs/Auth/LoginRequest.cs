using System.ComponentModel.DataAnnotations;

namespace leadflow_crm.API.DTOs.Auth;

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, StringLength(128)]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}
