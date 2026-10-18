using System.Globalization;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/saved-lists")]
public sealed class SavedListsController(CrmService crm) : CrmControllerBase(crm)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SavedListResponse>>> List([FromQuery] string? entityType,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(entityType)) RequireEntityView(entityType);
        var query = Crm.Db.SavedLists.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId &&
            (x.OwnerUserId == Crm.Actor.Id || x.IsShared));
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(x => x.EntityType == entityType);
        else
        {
            var allowed = new List<string>();
            if (Crm.Can(PermissionCatalog.ContactsView)) allowed.Add("Contact");
            if (Crm.Can(PermissionCatalog.OpportunitiesView)) allowed.Add("Opportunity");
            if (Crm.Can(PermissionCatalog.LeadsView)) allowed.Add("Lead");
            query = query.Where(x => allowed.Contains(x.EntityType));
        }
        var rows = await query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Name).Take(200).ToListAsync(cancellationToken);
        var ownerIds = rows.Select(x => x.OwnerUserId).Distinct().ToArray();
        var owners = await Crm.Db.Users.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && ownerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return Ok(rows.Select(x => ToResponse(x, owners.GetValueOrDefault(x.OwnerUserId, "Inactive user"))).ToArray());
    }

    [HttpPost]
    public async Task<ActionResult<SavedListResponse>> Create(SavedListRequest request, CancellationToken cancellationToken)
    {
        RequireEntityView(request.EntityType);
        await ValidateFilterAsync(request.EntityType, request.Filters, cancellationToken);
        EnsureSharePermission(request.IsShared);
        var now = DateTime.UtcNow;
        var row = new SavedListRecord
        {
            WorkspaceId = Crm.Actor.WorkspaceId,
            OwnerUserId = Crm.Actor.Id,
            Name = request.Name.Trim(),
            EntityType = request.EntityType,
            FiltersJson = JsonSerializer.Serialize(request.Filters, JsonOptions),
            IsShared = request.IsShared,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        Crm.Db.SavedLists.Add(row);
        Crm.Db.AuditLogs.Add(LeadWorkflowService.CreateAudit(Crm.Actor.WorkspaceId, Crm.Actor.Id,
            "crm.saved-list.created", "SavedList", row.Id, new { row.Name, row.EntityType, row.IsShared }));
        await Crm.Db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(List), new { entityType = row.EntityType }, ToResponse(row, Crm.Actor.DisplayName));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SavedListResponse>> Update(Guid id, SavedListRequest request, CancellationToken cancellationToken)
    {
        var row = await OwnedListAsync(id, cancellationToken);
        if (row.EntityType != request.EntityType) RequireEntityView(request.EntityType);
        RequireEntityView(request.EntityType);
        await ValidateFilterAsync(request.EntityType, request.Filters, cancellationToken);
        EnsureSharePermission(request.IsShared);
        if (request.RowVersion is null || request.RowVersion.Length != 8)
            throw new CrmException(400, "Supply the saved list rowVersion from its latest response.");
        if (!row.RowVersion.SequenceEqual(request.RowVersion))
            throw new CrmException(409, "This saved list changed. Reload it before saving again.");
        var before = new { row.Name, row.EntityType, row.IsShared };
        row.Name = request.Name.Trim();
        row.EntityType = request.EntityType;
        row.FiltersJson = JsonSerializer.Serialize(request.Filters, JsonOptions);
        row.IsShared = request.IsShared;
        row.UpdatedAtUtc = DateTime.UtcNow;
        Crm.Db.AuditLogs.Add(LeadWorkflowService.CreateAudit(Crm.Actor.WorkspaceId, Crm.Actor.Id,
            "crm.saved-list.updated", "SavedList", row.Id, new { Before = before, After = new { row.Name, row.EntityType, row.IsShared } }));
        await Crm.Db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(row, Crm.Actor.DisplayName));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var row = await OwnedListAsync(id, cancellationToken);
        Crm.Db.SavedLists.Remove(row);
        Crm.Db.AuditLogs.Add(LeadWorkflowService.CreateAudit(Crm.Actor.WorkspaceId, Crm.Actor.Id,
            "crm.saved-list.deleted", "SavedList", id, new { row.Name, row.EntityType, row.IsShared }));
        await Crm.Db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/results")]
    public async Task<ActionResult<PageResult<SavedListResultResponse>>> Results(Guid id, [FromQuery] PageQuery request,
        CancellationToken cancellationToken)
    {
        var saved = await VisibleListAsync(id, cancellationToken);
        RequireEntityView(saved.EntityType);
        var filter = JsonSerializer.Deserialize<SavedListFilterRequest>(saved.FiltersJson, JsonOptions) ?? new();
        await ValidateFilterAsync(saved.EntityType, filter, cancellationToken);
        return saved.EntityType switch
        {
            "Contact" => Ok(await ContactResultsAsync(filter, request, cancellationToken)),
            "Opportunity" => Ok(await OpportunityResultsAsync(filter, request, cancellationToken)),
            "Lead" => Ok(await LeadResultsAsync(filter, request, cancellationToken)),
            _ => throw new CrmException(400, "This saved list has an unsupported record type.")
        };
    }

    private async Task<PageResult<SavedListResultResponse>> ContactResultsAsync(SavedListFilterRequest filter,
        PageQuery page, CancellationToken cancellationToken)
    {
        var query = Crm.Db.Contacts.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x => x.DisplayName.Contains(term) || (x.Email != null && x.Email.Contains(term)) ||
                (x.Phone != null && x.Phone.Contains(term)));
        }
        if (filter.OwnerUserId.HasValue) query = query.Where(x => x.OwnerUserId == filter.OwnerUserId);
        query = ApplyCreatedRange(query, filter);
        var needle = await CustomFieldNeedleAsync("Contact", null, filter, cancellationToken);
        if (needle is not null) query = query.Where(x => x.CustomFieldsJson.Contains(needle));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Id).Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize).ToListAsync(cancellationToken);
        var ownerIds = rows.Select(x => x.OwnerUserId).OfType<Guid>().Distinct().ToArray();
        var owners = await Crm.Db.Users.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && ownerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return new(rows.Select(x => new SavedListResultResponse(x.Id, x.DisplayName, x.Email, null, x.OwnerUserId,
            x.OwnerUserId.HasValue ? owners.GetValueOrDefault(x.OwnerUserId.Value) : null, x.CreatedAtUtc, null, true, null)).ToArray(),
            total, page.Page, page.PageSize);
    }

    private async Task<PageResult<SavedListResultResponse>> OpportunityResultsAsync(SavedListFilterRequest filter,
        PageQuery page, CancellationToken cancellationToken)
    {
        var query = Crm.Db.Set<Opportunity>().AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(filter.Search)) query = query.Where(x => x.Title.Contains(filter.Search.Trim()));
        if (filter.OwnerUserId.HasValue) query = query.Where(x => x.OwnerUserId == filter.OwnerUserId);
        if (filter.PipelineId.HasValue) query = query.Where(x => x.PipelineId == filter.PipelineId);
        if (filter.StageId.HasValue) query = query.Where(x => x.StageId == filter.StageId);
        query = ApplyCreatedRange(query, filter);
        var needle = await CustomFieldNeedleAsync("Opportunity", filter.PipelineId, filter, cancellationToken);
        if (needle is not null) query = query.Where(x => x.CustomFieldsJson.Contains(needle));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize).ToListAsync(cancellationToken);
        var contactIds = rows.Select(x => x.ContactId).Distinct().ToArray();
        var stageIds = rows.Select(x => x.StageId).Distinct().ToArray();
        var ownerIds = rows.Select(x => x.OwnerUserId).Distinct().ToArray();
        var contacts = await Crm.Db.Contacts.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var stages = await Crm.Db.Set<PipelineStage>().AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && stageIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var owners = await Crm.Db.Users.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && ownerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        var canSeeValues = Crm.Can(PermissionCatalog.OpportunityValueView);
        return new(rows.Select(x => new SavedListResultResponse(x.Id, x.Title, contacts.GetValueOrDefault(x.ContactId),
            stages.GetValueOrDefault(x.StageId), x.OwnerUserId, owners.GetValueOrDefault(x.OwnerUserId), x.CreatedAtUtc,
            canSeeValues ? x.Value : null, !canSeeValues, x.CurrencyCode)).ToArray(), total, page.Page, page.PageSize);
    }

    private async Task<PageResult<SavedListResultResponse>> LeadResultsAsync(SavedListFilterRequest filter,
        PageQuery page, CancellationToken cancellationToken)
    {
        var query = Crm.Db.Leads.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && !x.IsDeleted);
        if (!Crm.Can(PermissionCatalog.LeadsViewAll)) query = query.Where(x => x.OwnerUserId == Crm.Actor.Id);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(x => x.Contact.DisplayName.Contains(term) || (x.Contact.Email != null && x.Contact.Email.Contains(term)) ||
                (x.Contact.Phone != null && x.Contact.Phone.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(filter.Source)) query = query.Where(x => x.Source == filter.Source.Trim());
        if (filter.OwnerUserId.HasValue) query = query.Where(x => x.OwnerUserId == filter.OwnerUserId);
        query = ApplyCreatedRange(query, filter);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page.Page - 1) * page.PageSize)
            .Take(page.PageSize).ToListAsync(cancellationToken);
        var ownerIds = rows.Select(x => x.OwnerUserId).Distinct().ToArray();
        var owners = await Crm.Db.Users.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && ownerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, cancellationToken);
        return new(rows.Select(x => new SavedListResultResponse(x.Id, x.Contact.DisplayName,
            x.Contact.Email ?? x.Contact.Phone, x.AcknowledgedAtUtc.HasValue ? "Acknowledged" : "Awaiting response",
            x.OwnerUserId, owners.GetValueOrDefault(x.OwnerUserId), x.CreatedAtUtc, null, true, null)).ToArray(),
            total, page.Page, page.PageSize);
    }

    private async Task<string?> CustomFieldNeedleAsync(string entityType, Guid? pipelineId,
        SavedListFilterRequest filter, CancellationToken cancellationToken)
    {
        if (!filter.CustomFieldId.HasValue) return null;
        var field = await Crm.Db.Set<CustomFieldDefinition>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.WorkspaceId == Crm.Actor.WorkspaceId && !x.IsDeleted && x.Id == filter.CustomFieldId &&
            x.EntityType.ToString() == entityType && (x.PipelineId == null || x.PipelineId == pipelineId), cancellationToken)
            ?? throw new CrmException(400, "Choose a custom field for this record type and pipeline.");
        var value = filter.CustomFieldValue ?? string.Empty;
        string jsonValue;
        switch (field.FieldType)
        {
            case CustomFieldType.Text:
            case CustomFieldType.Dropdown:
                if (field.FieldType == CustomFieldType.Dropdown &&
                    !JsonSerializer.Deserialize<List<string>>(field.OptionsJson)!.Contains(value, StringComparer.Ordinal))
                    throw new CrmException(400, "Choose a valid dropdown option.");
                jsonValue = JsonSerializer.Serialize(value);
                break;
            case CustomFieldType.Number:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    throw new CrmException(400, "Enter a valid number for the custom-field filter.");
                jsonValue = JsonSerializer.Serialize(number);
                break;
            case CustomFieldType.Date:
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    throw new CrmException(400, "Enter a date in yyyy-MM-dd format for the custom-field filter.");
                jsonValue = JsonSerializer.Serialize(value);
                break;
            case CustomFieldType.Checkbox:
                if (!bool.TryParse(value, out var boolean)) throw new CrmException(400, "Choose true or false for the custom-field filter.");
                jsonValue = JsonSerializer.Serialize(boolean);
                break;
            default: throw new CrmException(400, "This custom-field type cannot be used in a saved list.");
        }
        return $"\"{field.Id:D}\":{jsonValue}";
    }

    private async Task ValidateFilterAsync(string entityType, SavedListFilterRequest filter, CancellationToken cancellationToken)
    {
        RequireEntityView(entityType);
        if (filter.CreatedFromUtc > filter.CreatedToUtc) throw new CrmException(400, "The start date must be before the end date.");
        if (filter.CustomFieldId.HasValue != (filter.CustomFieldValue is not null))
            throw new CrmException(400, "Select a custom field and enter its value together.");
        if (filter.OwnerUserId.HasValue) await Crm.ValidateOwnerAsync(filter.OwnerUserId);
        if (entityType != "Opportunity" && (filter.PipelineId.HasValue || filter.StageId.HasValue))
            throw new CrmException(400, "Pipeline and stage filters apply only to opportunities.");
        if (entityType != "Lead" && !string.IsNullOrWhiteSpace(filter.Source))
            throw new CrmException(400, "Source filters apply only to leads.");
        if (entityType == "Lead" && filter.CustomFieldId.HasValue)
            throw new CrmException(400, "Custom-field filters apply to contacts and opportunities.");
        if (filter.PipelineId.HasValue && !await Crm.Db.Set<Pipeline>().AnyAsync(x => x.Id == filter.PipelineId &&
                x.WorkspaceId == Crm.Actor.WorkspaceId && !x.IsDeleted, cancellationToken))
            throw new CrmException(400, "Choose a pipeline from this workspace.");
        if (filter.StageId.HasValue && !await Crm.Db.Set<PipelineStage>().AnyAsync(x => x.Id == filter.StageId &&
                x.WorkspaceId == Crm.Actor.WorkspaceId && !x.IsDeleted && (!filter.PipelineId.HasValue || x.PipelineId == filter.PipelineId), cancellationToken))
            throw new CrmException(400, "Choose a stage from the selected workspace pipeline.");
        if (filter.CustomFieldId.HasValue)
            _ = await CustomFieldNeedleAsync(entityType, filter.PipelineId, filter, cancellationToken);
    }

    private IQueryable<T> ApplyCreatedRange<T>(IQueryable<T> query, SavedListFilterRequest filter) where T : CrmRecord
    {
        if (filter.CreatedFromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= filter.CreatedFromUtc.Value);
        if (filter.CreatedToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < filter.CreatedToUtc.Value);
        return query;
    }

    private async Task<SavedListRecord> OwnedListAsync(Guid id, CancellationToken cancellationToken) =>
        await Crm.Db.SavedLists.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == Crm.Actor.WorkspaceId &&
            x.OwnerUserId == Crm.Actor.Id, cancellationToken)
        ?? throw new CrmException(404, "The saved list was not found.");

    private async Task<SavedListRecord> VisibleListAsync(Guid id, CancellationToken cancellationToken) =>
        await Crm.Db.SavedLists.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == Crm.Actor.WorkspaceId &&
            (x.OwnerUserId == Crm.Actor.Id || x.IsShared), cancellationToken)
        ?? throw new CrmException(404, "The saved list was not found.");

    private void EnsureSharePermission(bool isShared)
    {
        if (isShared && !Crm.Can(PermissionCatalog.WorkspaceMembersManage) && !Crm.Can(PermissionCatalog.WorkspaceRolesManage))
            throw new CrmException(403, "Only workspace managers can share saved lists.");
    }

    private void RequireEntityView(string entityType)
    {
        var permission = entityType switch
        {
            "Contact" => PermissionCatalog.ContactsView,
            "Opportunity" => PermissionCatalog.OpportunitiesView,
            "Lead" => PermissionCatalog.LeadsView,
            _ => throw new CrmException(400, "EntityType must be Contact, Opportunity, or Lead.")
        };
        Crm.Require(permission);
    }

    private static SavedListResponse ToResponse(SavedListRecord row, string ownerName)
    {
        var filters = JsonSerializer.Deserialize<SavedListFilterRequest>(row.FiltersJson, JsonOptions) ?? new();
        return new(row.Id, row.Name, row.EntityType, filters, row.IsShared, row.OwnerUserId, ownerName,
            DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc), DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc), row.RowVersion);
    }
}
