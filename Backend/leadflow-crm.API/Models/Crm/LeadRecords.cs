using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace leadflow_crm.API.Models.Crm;

[JsonConverter(typeof(JsonStringEnumConverter<EmployeeAvailabilityStatus>))]
public enum EmployeeAvailabilityStatus { Available, Busy, OnLeave, Offline }
[JsonConverter(typeof(JsonStringEnumConverter<LeadIntakeStatus>))]
public enum LeadIntakeStatus { WaitingForOwner, Assigned, NeedsContactReview }
[JsonConverter(typeof(JsonStringEnumConverter<LeadNotificationStatus>))]
public enum LeadNotificationStatus { Pending, Sent, Failed }

public sealed class EmployeeAvailability
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public EmployeeAvailabilityStatus Status { get; set; } = EmployeeAvailabilityStatus.Available;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class LeadAssignmentConfiguration
{
    public Guid WorkspaceId { get; set; }
    public int MaxOpenLeadsPerEmployee { get; set; } = 10;
    public int AcknowledgementWindowSeconds { get; set; } = 300;
    public int IdleWindowSeconds { get; set; } = 86400;
    public int RetryIntervalSeconds { get; set; } = 30;
    public string EscalationUserIdsJson { get; set; } = "[]";
    public string EscalationIntervalsSecondsJson { get; set; } = "[300]";
    public Guid? RoundRobinLastUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class LeadIntake
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    [MaxLength(64)] public string IdempotencyKeyHash { get; set; } = string.Empty;
    [MaxLength(20)] public string Source { get; set; } = string.Empty;
    [MaxLength(120)] public string? SourceDetail { get; set; }
    [MaxLength(160)] public string ContactName { get; set; } = string.Empty;
    [MaxLength(256)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(160)] public string? CompanyName { get; set; }
    [MaxLength(4000)] public string? Message { get; set; }
    public LeadIntakeStatus Status { get; set; } = LeadIntakeStatus.WaitingForOwner;
    public Guid? MatchedContactId { get; set; }
    public Guid? LeadId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    [MaxLength(500)] public string? LastError { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class LeadUserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid LeadId { get; set; }
    public Guid RecipientUserId { get; set; }
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    [MaxLength(1000)] public string Message { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class Lead : CrmRecord
{
    public Guid ContactId { get; set; }
    public Contact Contact { get; set; } = null!;
    public Guid OwnerUserId { get; set; }
    public Guid IntakeId { get; set; }
    [MaxLength(20)] public string Source { get; set; } = string.Empty;
    [MaxLength(120)] public string? SourceDetail { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime AcknowledgementDeadlineUtc { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public DateTime LastActionAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? NextEscalationAtUtc { get; set; }
    public int EscalationCount { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    [MaxLength(500)] public string? AttentionReason { get; set; }
}

public sealed class LeadAssignmentHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid LeadId { get; set; }
    public Guid? PreviousOwnerUserId { get; set; }
    public Guid NewOwnerUserId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorWorkspaceId { get; set; }
    [MaxLength(20)] public string ActorType { get; set; } = "System";
    [MaxLength(80)] public string Reason { get; set; } = string.Empty;
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime AcknowledgementDeadlineUtc { get; set; }
}

public sealed class LeadNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid LeadId { get; set; }
    public Guid RecipientUserId { get; set; }
    [MaxLength(256)] public string RecipientEmail { get; set; } = string.Empty;
    [MaxLength(500)] public string Subject { get; set; } = string.Empty;
    [MaxLength(4000)] public string BodyText { get; set; } = string.Empty;
    public LeadNotificationStatus Status { get; set; } = LeadNotificationStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
    [MaxLength(1000)] public string? LastError { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
