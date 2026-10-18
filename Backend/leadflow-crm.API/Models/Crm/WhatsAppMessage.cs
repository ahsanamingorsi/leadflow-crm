using System.ComponentModel.DataAnnotations;

namespace leadflow_crm.API.Models.Crm;

public sealed class WhatsAppMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? LeadId { get; set; }
    public Guid? IntakeId { get; set; }
    public bool IsOptOut { get; set; }
    public Guid? ActorUserId { get; set; }
    [MaxLength(32)] public string Phone { get; set; } = "";
    [MaxLength(12)] public string Direction { get; set; } = "Inbound";
    [MaxLength(24)] public string Status { get; set; } = "received";
    [MaxLength(4096)] public string Body { get; set; } = "";
    [MaxLength(34)] public string? ContentSid { get; set; }
    [MaxLength(34)] public string? ProviderMessageId { get; set; }
    [MaxLength(100)] public string ClientKey { get; set; } = "";
    [MaxLength(500)] public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
