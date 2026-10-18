namespace leadflow_crm.API.Models;

public sealed class AuditLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? ActorWorkspaceId { get; set; }
    public string ActorType { get; set; } = "User";
    public string EventType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }

    public Workspace Workspace { get; set; } = null!;
    public ApplicationUser? ActorUser { get; set; }
}
