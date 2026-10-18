using System.ComponentModel.DataAnnotations;
namespace leadflow_crm.API.DTOs.Crm;
public sealed class EmailComposeRequest
{
    public Guid ContactId { get; init; }
    [Required, StringLength(100, MinimumLength = 8)] public string ClientKey { get; init; } = "";
    [Required, StringLength(490)] public string Subject { get; init; } = "";
    [Required, StringLength(10000)] public string BodyText { get; init; } = "";
}
