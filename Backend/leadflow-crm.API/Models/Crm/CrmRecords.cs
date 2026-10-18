using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace leadflow_crm.API.Models.Crm;

[JsonConverter(typeof(JsonStringEnumConverter<StageKind>))]
public enum StageKind { Open, Won, Lost }
[JsonConverter(typeof(JsonStringEnumConverter<CrmEntityType>))]
public enum CrmEntityType { Company, Contact, Opportunity }
[JsonConverter(typeof(JsonStringEnumConverter<CustomFieldType>))]
public enum CustomFieldType { Text, Number, Date, Dropdown, Checkbox }

// Each concrete record maps to its own table; this base only shares properties.
public abstract class CrmRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}

public sealed class Company : CrmRecord
{
    [MaxLength(160)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(500)] public string? Website { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public string CustomFieldsJson { get; set; } = "{}";
}

public sealed class Contact : CrmRecord
{
    [MaxLength(160)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(256)] public string? Email { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid? OwnerUserId { get; set; }
    public bool OptedOut { get; set; }
    public string CustomFieldsJson { get; set; } = "{}";
}

public sealed class Pipeline : CrmRecord
{
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class PipelineStage : CrmRecord
{
    public Guid PipelineId { get; set; }
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
    public StageKind Kind { get; set; }
}

public sealed class Opportunity : CrmRecord
{
    [MaxLength(200)] public string Title { get; set; } = string.Empty;
    public Guid ContactId { get; set; }
    public Guid PipelineId { get; set; }
    public Guid StageId { get; set; }
    public Guid OwnerUserId { get; set; }
    public decimal Value { get; set; }
    [MaxLength(3)] public string CurrencyCode { get; set; } = "PKR";
    public DateOnly? ExpectedCloseDate { get; set; }
    [MaxLength(4000)] public string? Description { get; set; }
    public string CustomFieldsJson { get; set; } = "{}";
}

public sealed class CustomFieldDefinition : CrmRecord
{
    [MaxLength(120)] public string Name { get; set; } = string.Empty;
    public CrmEntityType EntityType { get; set; }
    public Guid? PipelineId { get; set; }
    public CustomFieldType FieldType { get; set; }
    public string OptionsJson { get; set; } = "[]";
}

public sealed class StageRequiredField
{
    public Guid WorkspaceId { get; set; }
    public Guid StageId { get; set; }
    public Guid FieldId { get; set; }
}

public sealed class CrmActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public CrmEntityType EntityType { get; set; }
    public Guid RecordId { get; set; }
    public Guid? ActorUserId { get; set; }
    [MaxLength(80)] public string EventType { get; set; } = string.Empty;
    [MaxLength(4000)] public string Summary { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
