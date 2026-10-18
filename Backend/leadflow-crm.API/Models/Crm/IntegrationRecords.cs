using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace leadflow_crm.API.Models.Crm;

[JsonConverter(typeof(JsonStringEnumConverter<IntegrationStatus>))]
public enum IntegrationStatus { NotConfigured, Configured, Connected, Error, Disabled }
[JsonConverter(typeof(JsonStringEnumConverter<EmailMessageDirection>))]
public enum EmailMessageDirection { Inbound, Outbound }
[JsonConverter(typeof(JsonStringEnumConverter<EmailDeliveryStatus>))]
public enum EmailDeliveryStatus { Received, Pending, Sent, Failed, Unknown }
[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus { Pending, Paid, Failed, Expired, Refunded }
[JsonConverter(typeof(JsonStringEnumConverter<AppointmentStatus>))]
public enum AppointmentStatus { Scheduled, Cancelled, Failed }

public sealed class WorkspaceIntegration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    [MaxLength(32)] public string Provider { get; set; } = string.Empty;
    [MaxLength(24)] public string Status { get; set; } = nameof(IntegrationStatus.NotConfigured);
    public string EncryptedSettingsJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConnectedAtUtc { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
}

public sealed class IntegrationSyncEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    [MaxLength(32)] public string Provider { get; set; } = string.Empty;
    [MaxLength(80)] public string EventType { get; set; } = string.Empty;
    [MaxLength(255)] public string? ExternalEventId { get; set; }
    [MaxLength(16)] public string Status { get; set; } = "Succeeded";
    [MaxLength(1000)] public string Summary { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EmailConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? LeadId { get; set; }
    [MaxLength(255)] public string ExternalThreadId { get; set; } = string.Empty;
    [MaxLength(256)] public string ParticipantEmail { get; set; } = string.Empty;
    [MaxLength(500)] public string Subject { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<EmailMessage> Messages { get; set; } = new List<EmailMessage>();
}

public sealed class EmailMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ConversationId { get; set; }
    public EmailMessageDirection Direction { get; set; }
    public EmailDeliveryStatus Status { get; set; }
    [MaxLength(256)] public string FromAddress { get; set; } = string.Empty;
    [MaxLength(256)] public string ToAddress { get; set; } = string.Empty;
    [MaxLength(500)] public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    [MaxLength(998)] public string? MessageId { get; set; }
    [MaxLength(998)] public string? InReplyTo { get; set; }
    [MaxLength(2000)] public string? References { get; set; }
    [MaxLength(1000)] public string? ProviderMessageId { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAtUtc { get; set; }
}

public sealed class PaymentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid OpportunityId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public decimal Amount { get; set; }
    public long AmountMinor { get; set; }
    [MaxLength(3)] public string CurrencyCode { get; set; } = "PKR";
    [MaxLength(20)] public string Status { get; set; } = nameof(PaymentStatus.Pending);
    [MaxLength(255)] public string? StripeCheckoutSessionId { get; set; }
    [MaxLength(255)] public string? StripePaymentIntentId { get; set; }
    [MaxLength(255)] public string IdempotencyKey { get; set; } = string.Empty;
    [MaxLength(2048)] public string? CheckoutUrl { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? RefundedAtUtc { get; set; }
}

public sealed class CalendarConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(256)] public string EmailAddress { get; set; } = string.Empty;
    [MaxLength(255)] public string CalendarId { get; set; } = "primary";
    public string EncryptedRefreshToken { get; set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedAtUtc { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
}

public sealed class CalendarOAuthState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(64)] public string StateHash { get; set; } = string.Empty;
    [MaxLength(512)] public string RedirectUri { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}

public sealed class CalendarAppointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? OpportunityId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string? Description { get; set; }
    [MaxLength(256)] public string? AttendeeEmail { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    [MaxLength(80)] public string TimeZoneId { get; set; } = "Asia/Karachi";
    [MaxLength(255)] public string? GoogleEventId { get; set; }
    [MaxLength(2048)] public string? HtmlLink { get; set; }
    [MaxLength(20)] public string Status { get; set; } = nameof(AppointmentStatus.Scheduled);
    [MaxLength(1000)] public string? LastError { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
