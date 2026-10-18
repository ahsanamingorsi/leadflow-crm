using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models;
using leadflow_crm.API.Models.Crm;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Services;

public sealed class CrmException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class CrmService(LeadflowDbContext db)
{
    public LeadflowDbContext Db => db;
    public ApplicationUser Actor { get; private set; } = null!;
    private HashSet<string> permissions = [];
    public bool Can(string permission) => permissions.Contains(permission);
    public void Require(string permission)
    {
        if (!Can(permission)) throw new CrmException(403, "You do not have permission for this action.");
    }
    public async Task InitializeAsync(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new CrmException(401, "Please log in.");
        Actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.IsActive && x.Workspace.IsActive)
            ?? throw new CrmException(401, "Your session or workspace is no longer active.");
        permissions = (await (from ur in db.UserRoles
            join p in db.RolePermissions on new { ur.RoleId, ur.WorkspaceId } equals new { p.RoleId, p.WorkspaceId }
            where ur.UserId == id && ur.WorkspaceId == Actor.WorkspaceId
            select p.PermissionKey).ToListAsync()).ToHashSet(StringComparer.Ordinal);
    }

    public IQueryable<T> Records<T>() where T : CrmRecord =>
        db.Set<T>().Where(x => x.WorkspaceId == Actor.WorkspaceId && !x.IsDeleted);
    public async Task<T> FindAsync<T>(Guid id) where T : CrmRecord =>
        await Records<T>().SingleOrDefaultAsync(x => x.Id == id)
        ?? throw new CrmException(404, "The requested record was not found in your workspace.");
    public async Task ValidateOwnerAsync(Guid? id)
    {
        if (id.HasValue && !await db.Users.AnyAsync(x => x.Id == id && x.WorkspaceId == Actor.WorkspaceId && x.IsActive))
            throw new CrmException(400, "Choose an active employee from your workspace.");
    }
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    public static string Snapshot(object value) => JsonSerializer.Serialize(value);
    public static Dictionary<Guid, JsonElement> Values(string json) => JsonSerializer.Deserialize<Dictionary<Guid, JsonElement>>(json)!;
    public static void CheckVersion(CrmRecord record, byte[]? version)
    {
        if (version is null || version.Length != 8) throw new CrmException(400, "Supply the record's rowVersion from its latest response.");
        if (!record.RowVersion.SequenceEqual(version)) throw new CrmException(409, "This record changed. Reload it before saving again.");
    }
    public static void CheckHeaderVersion(CrmRecord record, string? version)
    {
        byte[]? bytes = null;
        try { if (version is not null) bytes = Convert.FromBase64String(version.Trim('"')); }
        catch (FormatException) { throw new CrmException(400, "If-Match must contain the base64 rowVersion."); }
        CheckVersion(record, bytes);
    }
    public AuditLogEntry Track(CrmRecord record, string eventType, string? before = null, CrmEntityType? type = null, string? summary = null)
    {
        record.UpdatedAtUtc = DateTime.UtcNow;
        var audit = new AuditLogEntry
        {
            WorkspaceId = Actor.WorkspaceId, ActorUserId = Actor.Id, ActorWorkspaceId = Actor.WorkspaceId,
            EventType = eventType, TargetType = record.GetType().Name, TargetId = record.Id.ToString(),
            BeforeJson = before, AfterJson = Snapshot(record)
        };
        db.AuditLogs.Add(audit);
        if (type.HasValue)
        {
            var description = summary ?? eventType;
            if (before is not null)
            {
                using var old = JsonDocument.Parse(before);
                using var current = JsonDocument.Parse(audit.AfterJson);
                var changed = current.RootElement.EnumerateObject().Where(p =>
                    p.Name is not ("UpdatedAtUtc" or "RowVersion") && old.RootElement.TryGetProperty(p.Name, out var value) &&
                    value.GetRawText() != p.Value.GetRawText()).Select(p => p.Name == "CustomFieldsJson" ? "custom fields" : p.Name).ToArray();
                if (changed.Length > 0) description += $" Changed: {string.Join(", ", changed)}.";
            }
            AddActivity(type.Value, record.Id, eventType, description);
        }
        return audit;
    }
    public CrmActivity AddActivity(CrmEntityType type, Guid id, string eventType, string summary)
    {
        var activity = new CrmActivity { WorkspaceId = Actor.WorkspaceId, ActorUserId = Actor.Id,
            EntityType = type, RecordId = id, EventType = eventType, Summary = summary };
        db.Set<CrmActivity>().Add(activity);
        return activity;
    }
    public Task SaveAsync() => db.SaveChangesAsync();

    public async Task<string> ValidateFieldsAsync(CrmEntityType type, Guid? pipelineId,
        Dictionary<Guid, JsonElement> values, Guid? stageId = null)
    {
        if (values.Count > 100) throw new CrmException(400, "A record may contain at most 100 custom fields.");
        var definitions = await Records<CustomFieldDefinition>().Where(x => x.EntityType == type &&
            (x.PipelineId == null || x.PipelineId == pipelineId)).ToListAsync();
        var byId = definitions.ToDictionary(x => x.Id);
        foreach (var (id, value) in values)
        {
            if (!byId.TryGetValue(id, out var field)) throw new CrmException(400, $"Custom field {id} does not belong to this record or pipeline.");
            if (value.ValueKind == JsonValueKind.Null) continue;
            var valid = field.FieldType switch
            {
                CustomFieldType.Text => value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 2000,
                CustomFieldType.Number => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
                CustomFieldType.Date => value.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(
                    value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                CustomFieldType.Dropdown => value.ValueKind == JsonValueKind.String &&
                    JsonSerializer.Deserialize<List<string>>(field.OptionsJson)!.Contains(value.GetString()!, StringComparer.Ordinal),
                CustomFieldType.Checkbox => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => false
            };
            if (!valid) throw new CrmException(400, $"Field '{field.Name}' must be a valid {field.FieldType} value.");
        }
        if (stageId.HasValue)
        {
            var required = await db.Set<StageRequiredField>().Where(x => x.WorkspaceId == Actor.WorkspaceId && x.StageId == stageId)
                .Select(x => x.FieldId).ToListAsync();
            var missing = required.Where(id => !values.TryGetValue(id, out var value) || value.ValueKind == JsonValueKind.Null ||
                (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
                .Select(id => byId.TryGetValue(id, out var field) ? field.Name : id.ToString()).ToArray();
            if (missing.Length > 0) throw new CrmException(400, $"Complete the required fields for this stage: {string.Join(", ", missing)}.");
        }
        return JsonSerializer.Serialize(values.Where(x => x.Value.ValueKind != JsonValueKind.Null).ToDictionary(x => x.Key, x => x.Value));
    }

    public CompanyResponse View(Company x) => new(x.Id, x.Name, x.Email, x.Phone, x.Website, x.Address,
        Values(x.CustomFieldsJson), x.CreatedAtUtc, x.UpdatedAtUtc, x.RowVersion);
    public ContactResponse View(Contact x) => new(x.Id, x.DisplayName, x.Email, x.Phone, x.CompanyId, x.OwnerUserId,
        x.OptedOut, Values(x.CustomFieldsJson), x.CreatedAtUtc, x.UpdatedAtUtc, x.RowVersion);
    public OpportunityResponse View(Opportunity x) => new(x.Id, x.Title, x.ContactId, x.PipelineId, x.StageId, x.OwnerUserId,
        Can(PermissionCatalog.OpportunityValueView) ? x.Value : null, !Can(PermissionCatalog.OpportunityValueView), x.CurrencyCode,
        x.ExpectedCloseDate, x.Description, Values(x.CustomFieldsJson), x.CreatedAtUtc, x.UpdatedAtUtc, x.RowVersion);
    public static CustomFieldResponse View(CustomFieldDefinition x) => new(x.Id, x.Name, x.EntityType, x.PipelineId,
        x.FieldType, JsonSerializer.Deserialize<List<string>>(x.OptionsJson)!, x.RowVersion);
    public async Task<PipelineResponse> ViewPipelineAsync(Pipeline x)
    {
        var stages = await Records<PipelineStage>().Where(s => s.PipelineId == x.Id).OrderBy(s => s.Position).ThenBy(s => s.Id).ToListAsync();
        var stageIds = stages.Select(s => s.Id).ToArray();
        var rules = await db.Set<StageRequiredField>().Where(r => r.WorkspaceId == Actor.WorkspaceId && stageIds.Contains(r.StageId)).ToListAsync();
        return new(x.Id, x.Name, x.IsDefault, stages.Select(s => new StageResponse(s.Id, s.PipelineId, s.Name, s.Position, s.Kind,
            rules.Where(r => r.StageId == s.Id).Select(r => r.FieldId).ToArray(), s.RowVersion)).ToArray(), x.RowVersion);
    }

    public static void AddDefaultPipeline(LeadflowDbContext db, Guid workspaceId)
    {
        var pipeline = new Pipeline { WorkspaceId = workspaceId, Name = "Sales", IsDefault = true };
        db.Set<Pipeline>().Add(pipeline);
        var stages = new[] { ("New", StageKind.Open), ("Qualified", StageKind.Open), ("Proposal", StageKind.Open),
            ("Won", StageKind.Won), ("Lost", StageKind.Lost) };
        for (var i = 0; i < stages.Length; i++) db.Set<PipelineStage>().Add(new PipelineStage
        { WorkspaceId = workspaceId, PipelineId = pipeline.Id, Name = stages[i].Item1, Kind = stages[i].Item2, Position = i });
    }
}
