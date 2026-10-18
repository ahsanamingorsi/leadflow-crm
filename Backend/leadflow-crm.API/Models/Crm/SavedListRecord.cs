using System.ComponentModel.DataAnnotations;

namespace leadflow_crm.API.Models.Crm;

public sealed class SavedListRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid OwnerUserId { get; set; }
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    [MaxLength(24)] public string EntityType { get; set; } = string.Empty;
    public string FiltersJson { get; set; } = "{}";
    public bool IsShared { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
