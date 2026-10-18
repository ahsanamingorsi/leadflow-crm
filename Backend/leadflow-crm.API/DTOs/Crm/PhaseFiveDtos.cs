using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace leadflow_crm.API.DTOs.Crm;

public sealed record DashboardStageMetric(Guid StageId, string StageName, int OpportunityCount, decimal? OpenValue);
public sealed record DashboardAvailabilityMetric(string Status, int EmployeeCount);
public sealed record DashboardAppointmentMetric(Guid Id, string Title, string? ContactName, DateTime StartsAtUtc,
    string TimeZoneId, string OwnerName);
public sealed record DashboardOverviewResponse(
    DateTime GeneratedAtUtc,
    bool CompanyWide,
    int NewLeadsToday,
    int OpenLeads,
    double? AverageFirstResponseSeconds,
    int RecoveryQueueCount,
    int EscalationsLast24Hours,
    int? AvailableEmployees,
    int? BusyEmployees,
    int? OnLeaveEmployees,
    int? OfflineEmployees,
    string? MyAvailability,
    decimal? OpenPipelineValue,
    bool PipelineValueHidden,
    decimal? CollectedThisMonth,
    int? PaidPaymentsThisMonth,
    bool RevenueHidden,
    IReadOnlyList<DashboardStageMetric> PipelineByStage,
    IReadOnlyList<DashboardAvailabilityMetric> Availability,
    IReadOnlyList<DashboardAppointmentMetric> UpcomingAppointments);

public sealed class SavedListFilterRequest
{
    [StringLength(200)] public string? Search { get; init; }
    [StringLength(120)] public string? Source { get; init; }
    public Guid? OwnerUserId { get; init; }
    public Guid? PipelineId { get; init; }
    public Guid? StageId { get; init; }
    public DateTime? CreatedFromUtc { get; init; }
    public DateTime? CreatedToUtc { get; init; }
    public Guid? CustomFieldId { get; init; }
    [StringLength(2000)] public string? CustomFieldValue { get; init; }
}

public sealed class SavedListRequest
{
    [Required, StringLength(120)] public string Name { get; init; } = string.Empty;
    [Required, RegularExpression("^(Contact|Opportunity|Lead)$")] public string EntityType { get; init; } = string.Empty;
    public SavedListFilterRequest Filters { get; init; } = new();
    public bool IsShared { get; init; }
    public byte[]? RowVersion { get; init; }
}

public sealed record SavedListResponse(Guid Id, string Name, string EntityType, SavedListFilterRequest Filters,
    bool IsShared, Guid OwnerUserId, string OwnerName, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);
public sealed record SavedListResultResponse(Guid Id, string Label, string? SecondaryLabel, string? EntityStatus,
    Guid? OwnerUserId, string? OwnerName, DateTime CreatedAtUtc, decimal? Value, bool ValueHidden, string? CurrencyCode);
public sealed record AuditTrailResponse(Guid Id, string ActorType, Guid? ActorUserId, string? ActorName,
    string EventType, string TargetType, string TargetId, DateTime OccurredAtUtc, JsonElement? Before, JsonElement? After);
