using System.ComponentModel.DataAnnotations;
using leadflow_crm.API.Models.Crm;

namespace leadflow_crm.API.DTOs.Crm;

public class LeadIntakeRequest : IValidatableObject
{
    [Required, StringLength(160, MinimumLength = 2)] public string ContactName { get; init; } = string.Empty;
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
    [StringLength(30)] public string? Phone { get; init; }
    [StringLength(160)] public string? CompanyName { get; init; }
    [StringLength(120)] public string? SourceDetail { get; init; }
    [StringLength(4000)] public string? Message { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Email) && string.IsNullOrWhiteSpace(Phone))
            yield return new ValidationResult("Provide an email address or a phone number.", [nameof(Email), nameof(Phone)]);
    }
}

public sealed class CreateManualLeadRequest : LeadIntakeRequest
{
    public Guid? ContactId { get; init; }
    [StringLength(20)] public string Source { get; init; } = "Manual";
}
public sealed class LeadListQuery : PageQuery
{
    public Guid? OwnerUserId { get; init; }
    public bool? Acknowledged { get; init; }
    public bool? Closed { get; init; }
    [StringLength(20)] public string? Source { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
}
public sealed class AssignmentSettingsRequest
{
    [Range(1, 1000)] public int MaxOpenLeadsPerEmployee { get; init; }
    [Range(1, 86400)] public int AcknowledgementWindowSeconds { get; init; }
    [Range(1, 31536000)] public int IdleWindowSeconds { get; init; }
    [Range(1, 86400)] public int RetryIntervalSeconds { get; init; }
    [MaxLength(20)] public List<Guid> EscalationUserIds { get; init; } = [];
    [MinLength(1), MaxLength(20)] public List<int> EscalationIntervalsSeconds { get; init; } = [];
    [Required] public byte[]? RowVersion { get; init; }
}
public sealed class AvailabilityRequest
{
    [EnumDataType(typeof(EmployeeAvailabilityStatus))] public EmployeeAvailabilityStatus Status { get; init; }
    public byte[]? RowVersion { get; init; }
}
public sealed class LeadVersionRequest { [Required] public byte[]? RowVersion { get; init; } }
public sealed class ReassignLeadRequest
{
    public Guid OwnerUserId { get; init; }
    [Required, StringLength(400)] public string Reason { get; init; } = string.Empty;
    [Required] public byte[]? RowVersion { get; init; }
}
public sealed class RetryIntakeRequest
{
    public Guid? ContactId { get; init; }
    [Required] public byte[]? RowVersion { get; init; }
}
public sealed class NotificationEmailRequest
{
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
}
public sealed record LeadResponse(Guid Id, Guid ContactId, string ContactName, string? Email, string? Phone,
    Guid OwnerUserId, string OwnerName, string Source, string? SourceDetail, DateTime AssignedAtUtc,
    DateTime AcknowledgementDeadlineUtc, DateTime? AcknowledgedAtUtc, DateTime LastActionAtUtc,
    int EscalationCount, bool IsClosed, string? AttentionReason, byte[] RowVersion);
public sealed record LeadAssignmentResponse(Guid Id, Guid? PreviousOwnerUserId, string? PreviousOwnerName,
    Guid NewOwnerUserId, string NewOwnerName, Guid? ActorUserId, string ActorType, string Reason,
    DateTime AssignedAtUtc, DateTime AcknowledgementDeadlineUtc);
public sealed record AssignmentSettingsResponse(int MaxOpenLeadsPerEmployee, int AcknowledgementWindowSeconds,
    int IdleWindowSeconds, int RetryIntervalSeconds, IReadOnlyList<Guid> EscalationUserIds,
    IReadOnlyList<int> EscalationIntervalsSeconds, byte[] RowVersion);
public sealed record EmployeeAvailabilityResponse(Guid UserId, string DisplayName, string Email,
    EmployeeAvailabilityStatus Status, int OpenLeadCount, int AcknowledgementWindowSeconds,
    DateTime ChangedAtUtc, byte[] RowVersion);
public sealed record PendingIntakeResponse(Guid Id, string Source, string? SourceDetail, string ContactName,
    string? Email, string? Phone, string? CompanyName, string? Message, LeadIntakeStatus Status,
    int AttemptCount, DateTime ReceivedAtUtc, DateTime? LastAttemptAtUtc, string? LastError, byte[] RowVersion);
public sealed record LeadNotificationResponse(Guid Id, Guid LeadId, Guid RecipientUserId, string RecipientEmail,
    string Subject, LeadNotificationStatus Status, int AttemptCount, DateTime CreatedAtUtc,
    DateTime? SentAtUtc, DateTime? NextAttemptAtUtc, string? LastError, byte[] RowVersion);
public sealed record UserLeadNotificationResponse(Guid Id, Guid LeadId, string Title, string Message,
    DateTime CreatedAtUtc, DateTime? ReadAtUtc, byte[] RowVersion);
public sealed record RecoveryLeadResponse(LeadResponse Lead, IReadOnlyList<string> Reasons);
public sealed record IntakeQueuedResponse(Guid IntakeId, string Status, Guid? LeadId, DateTime ReceivedAtUtc,
    string? Message, bool IsDuplicate = false);
