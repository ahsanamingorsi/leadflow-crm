using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using leadflow_crm.API.Models.Crm;

namespace leadflow_crm.API.DTOs.Crm;

public class PageQuery
{
    [Range(1, 100000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 25;
    [StringLength(200)] public string? Search { get; init; }
}
public sealed class RecordQuery : PageQuery
{
    public Guid? CompanyId { get; init; }
    public Guid? ContactId { get; init; }
    public Guid? OwnerUserId { get; init; }
    public Guid? PipelineId { get; init; }
    public Guid? StageId { get; init; }
    public DateTime? CreatedFromUtc { get; init; }
    public DateTime? CreatedToUtc { get; init; }
}
public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
public abstract class VersionedRequest { public byte[]? RowVersion { get; init; } }
public sealed class CompanyRequest : VersionedRequest
{
    [Required, StringLength(160)] public string Name { get; init; } = string.Empty;
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
    [StringLength(30)] public string? Phone { get; init; }
    [Url, StringLength(500)] public string? Website { get; init; }
    [StringLength(500)] public string? Address { get; init; }
    [Required] public Dictionary<Guid, JsonElement> CustomFields { get; init; } = [];
}
public sealed class ContactRequest : VersionedRequest
{
    [Required, StringLength(160)] public string DisplayName { get; init; } = string.Empty;
    [EmailAddress, StringLength(256)] public string? Email { get; init; }
    [StringLength(30)] public string? Phone { get; init; }
    public Guid? CompanyId { get; init; }
    public Guid? OwnerUserId { get; init; }
    public bool OptedOut { get; init; }
    [Required] public Dictionary<Guid, JsonElement> CustomFields { get; init; } = [];
}
public sealed class OpportunityRequest : VersionedRequest
{
    [Required, StringLength(200)] public string Title { get; init; } = string.Empty;
    public Guid ContactId { get; init; }
    public Guid PipelineId { get; init; }
    public Guid StageId { get; init; }
    public Guid OwnerUserId { get; init; }
    [Range(typeof(decimal), "0", "9999999999999999.99")] public decimal? Value { get; init; }
    public DateOnly? ExpectedCloseDate { get; init; }
    [StringLength(4000)] public string? Description { get; init; }
    [Required] public Dictionary<Guid, JsonElement> CustomFields { get; init; } = [];
}
public sealed class MoveOpportunityRequest : VersionedRequest
{
    public Guid StageId { get; init; }
    // Optional full replacement of custom values, allowing required fields and the move in one operation.
    public Dictionary<Guid, JsonElement>? CustomFields { get; init; }
}
public sealed class PipelineRequest : VersionedRequest
{
    [Required, StringLength(120)] public string Name { get; init; } = string.Empty;
}
public sealed class StageRequest : VersionedRequest
{
    [Required, StringLength(120)] public string Name { get; init; } = string.Empty;
    [EnumDataType(typeof(StageKind))] public StageKind Kind { get; init; }
}
public sealed class StageOrderItem : VersionedRequest { public Guid Id { get; init; } }
public sealed class ReorderStagesRequest
{
    [Required, MinLength(1), MaxLength(100)] public List<StageOrderItem> Stages { get; init; } = [];
}
public sealed class RequiredFieldsRequest : VersionedRequest
{
    [Required, MaxLength(100)] public List<Guid> FieldIds { get; init; } = [];
}
public sealed class CustomFieldRequest : VersionedRequest
{
    [Required, StringLength(120)] public string Name { get; init; } = string.Empty;
    [EnumDataType(typeof(CrmEntityType))] public CrmEntityType EntityType { get; init; }
    public Guid? PipelineId { get; init; }
    [EnumDataType(typeof(CustomFieldType))] public CustomFieldType FieldType { get; init; }
    [Required, MaxLength(100)] public List<string> Options { get; init; } = [];
}
public sealed class NoteRequest
{
    [Required, StringLength(4000)] public string Text { get; init; } = string.Empty;
}
public sealed record CompanyResponse(Guid Id, string Name, string? Email, string? Phone, string? Website,
    string? Address, Dictionary<Guid, JsonElement> CustomFields, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);
public sealed record ContactResponse(Guid Id, string DisplayName, string? Email, string? Phone, Guid? CompanyId,
    Guid? OwnerUserId, bool OptedOut, Dictionary<Guid, JsonElement> CustomFields,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);
public sealed record OpportunityResponse(Guid Id, string Title, Guid ContactId, Guid PipelineId, Guid StageId,
    Guid OwnerUserId, decimal? Value, bool ValueHidden, string CurrencyCode, DateOnly? ExpectedCloseDate,
    string? Description, Dictionary<Guid, JsonElement> CustomFields, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, byte[] RowVersion);
public sealed record StageResponse(Guid Id, Guid PipelineId, string Name, int Position, StageKind Kind,
    IReadOnlyList<Guid> RequiredFieldIds, byte[] RowVersion);
public sealed record PipelineResponse(Guid Id, string Name, bool IsDefault, IReadOnlyList<StageResponse> Stages, byte[] RowVersion);
public sealed record CustomFieldResponse(Guid Id, string Name, CrmEntityType EntityType, Guid? PipelineId,
    CustomFieldType FieldType, IReadOnlyList<string> Options, byte[] RowVersion);
public sealed record ActivityResponse(Guid Id, CrmEntityType EntityType, Guid RecordId, Guid ActorUserId,
    string ActorName, string EventType, string Summary, DateTime OccurredAtUtc);
