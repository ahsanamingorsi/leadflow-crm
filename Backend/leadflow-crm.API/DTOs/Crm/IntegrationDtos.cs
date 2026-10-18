using System.ComponentModel.DataAnnotations;
using leadflow_crm.API.Models.Crm;

namespace leadflow_crm.API.DTOs.Crm;

public sealed class EmailIntegrationRequest
{
    [Required, StringLength(255)] public string Host { get; init; } = string.Empty;
    [Range(1, 65535)] public int Port { get; init; } = 587;
    [Required, EmailAddress, StringLength(256)] public string FromAddress { get; init; } = string.Empty;
    public bool EnableSsl { get; init; } = true;
    [StringLength(256)] public string? Username { get; init; }
    [StringLength(512)] public string? Password { get; init; }
    [StringLength(256)] public string? InboundWebhookSecret { get; init; }
}

public sealed record EmailIntegrationResponse(
    IntegrationStatus Status, string? Host, int? Port, string? FromAddress,
    bool EnableSsl, bool HasCredentials, bool HasInboundWebhook, DateTime? ConnectedAtUtc,
    string? LastError);

public sealed record IntegrationStatusResponse(
    string Provider, IntegrationStatus Status, DateTime? ConnectedAtUtc,
    DateTime? LastCheckedAtUtc, string? LastError);

public sealed class EmailReplyRequest
{
    [Required, StringLength(10000, MinimumLength = 1)] public string BodyText { get; init; } = string.Empty;
}

public sealed record EmailMessageResponse(
    Guid Id, EmailMessageDirection Direction, EmailDeliveryStatus Status, string FromAddress,
    string ToAddress, string Subject, string BodyText, string? MessageId, string? InReplyTo,
    DateTime CreatedAtUtc, DateTime? DeliveredAtUtc, string? LastError);

public sealed record EmailConversationResponse(
    Guid Id, Guid? ContactId, Guid? LeadId, string ParticipantEmail, string Subject,
    DateTime LastMessageAtUtc, IReadOnlyList<EmailMessageResponse> Messages);

public sealed class InboundEmailWebhookRequest
{
    [Required, StringLength(998)] public string MessageId { get; init; } = string.Empty;
    [Required, StringLength(255)] public string ThreadId { get; init; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string FromAddress { get; init; } = string.Empty;
    [Required, EmailAddress, StringLength(256)] public string ToAddress { get; init; } = string.Empty;
    [Required, StringLength(500)] public string Subject { get; init; } = string.Empty;
    [Required, StringLength(40000)] public string TextBody { get; init; } = string.Empty;
    [StringLength(998)] public string? InReplyTo { get; init; }
    [StringLength(2000)] public string? References { get; init; }
    public DateTimeOffset? ReceivedAt { get; init; }
}

public sealed record PaymentResponse(
    Guid Id, Guid OpportunityId, decimal Amount, string CurrencyCode, PaymentStatus Status,
    string? CheckoutUrl, DateTime CreatedAtUtc, DateTime? PaidAtUtc, DateTime? ExpiresAtUtc,
    DateTime? RefundedAtUtc, string? LastError);

public sealed class CreateCheckoutRequest
{
    [Required] public Guid OpportunityId { get; init; }
    [Required, StringLength(255, MinimumLength = 8)] public string IdempotencyKey { get; init; } = string.Empty;
}

public sealed record CalendarConnectionResponse(bool Connected, string? EmailAddress, string? CalendarId,
    DateTime? ConnectedAtUtc, DateTime? LastCheckedAtUtc, string? LastError);

public sealed class CalendarAppointmentRequest
{
    [Required, StringLength(200)] public string Title { get; init; } = string.Empty;
    [StringLength(4000)] public string? Description { get; init; }
    public Guid? ContactId { get; init; }
    public Guid? OpportunityId { get; init; }
    [EmailAddress, StringLength(256)] public string? AttendeeEmail { get; init; }
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
}

public sealed record CalendarAppointmentResponse(
    Guid Id, Guid? ContactId, Guid? OpportunityId, string Title, string? Description,
    string? AttendeeEmail, DateTime StartsAtUtc, DateTime EndsAtUtc, string TimeZoneId,
    string? HtmlLink, AppointmentStatus Status, string? LastError);
