using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/custom-fields")]
public sealed class CustomFieldsController(CrmService crm) : CrmControllerBase(crm)
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomFieldResponse>>> List(
        [FromQuery, EnumDataType(typeof(CrmEntityType))] CrmEntityType entityType, [FromQuery] Guid? pipelineId)
    {
        var prefix = entityType switch { CrmEntityType.Company => "crm.companies", CrmEntityType.Contact => "crm.contacts", _ => "crm.opportunities" };
        if (!Crm.Can(PermissionCatalog.CustomFieldsManage) && !Crm.Can(prefix + ".view") && !Crm.Can(prefix + ".create") && !Crm.Can(prefix + ".edit"))
            throw new CrmException(403, "You do not have access to these custom fields.");
        if (pipelineId.HasValue) await Crm.FindAsync<Pipeline>(pipelineId.Value);
        var rows = await Crm.Records<CustomFieldDefinition>().AsNoTracking().Where(x => x.EntityType == entityType &&
            (x.PipelineId == null || x.PipelineId == pipelineId)).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync();
        return Ok(rows.Select(CrmService.View).ToArray());
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.CustomFieldsManage)]
    public async Task<ActionResult<CustomFieldResponse>> Create(CustomFieldRequest request)
    {
        await ValidateAsync(request);
        if (await Crm.Records<CustomFieldDefinition>().CountAsync(x => x.EntityType == request.EntityType &&
            (x.PipelineId == null || x.PipelineId == request.PipelineId)) >= 100)
            throw new CrmException(400, "This record scope already has 100 custom fields.");
        var row = new CustomFieldDefinition { WorkspaceId = Crm.Actor.WorkspaceId, Name = request.Name.Trim(),
            EntityType = request.EntityType, PipelineId = request.PipelineId, FieldType = request.FieldType,
            OptionsJson = JsonSerializer.Serialize(request.Options.Select(x => x.Trim())) };
        Crm.Db.Add(row); Crm.Track(row, "crm.custom-field.created");
        await Crm.SaveAsync();
        return Created($"/api/custom-fields?entityType={row.EntityType}&pipelineId={row.PipelineId}", CrmService.View(row));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = PermissionCatalog.CustomFieldsManage)]
    public async Task<ActionResult<CustomFieldResponse>> Update(Guid id, CustomFieldRequest request)
    {
        var row = await Crm.FindAsync<CustomFieldDefinition>(id);
        CrmService.CheckVersion(row, request.RowVersion);
        await ValidateAsync(request);
        if (row.FieldType != request.FieldType || row.EntityType != request.EntityType || row.PipelineId != request.PipelineId)
            throw new CrmException(409, "A field's type and scope cannot be changed. Create a new field instead.");
        var options = request.Options.Select(x => x.Trim()).ToList();
        if (row.FieldType == CustomFieldType.Dropdown)
        {
            foreach (var value in await StoredValuesAsync(row))
                if (value.ValueKind == JsonValueKind.String && !options.Contains(value.GetString()!, StringComparer.Ordinal))
                    throw new CrmException(409, "A dropdown option is still used by a record and cannot be removed.");
        }
        var before = CrmService.Snapshot(row);
        row.Name = request.Name.Trim(); row.OptionsJson = JsonSerializer.Serialize(options);
        Crm.Track(row, "crm.custom-field.updated", before);
        await Crm.SaveAsync();
        return Ok(CrmService.View(row));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = PermissionCatalog.CustomFieldsManage)]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "If-Match")] string? rowVersion)
    {
        var row = await Crm.FindAsync<CustomFieldDefinition>(id);
        CrmService.CheckHeaderVersion(row, rowVersion);
        if (await Crm.Db.Set<StageRequiredField>().AnyAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.FieldId == id) ||
            (await StoredValuesAsync(row)).Count > 0)
            throw new CrmException(409, "This field is used by records or stage requirements. Remove those references first.");
        var before = CrmService.Snapshot(row); row.IsDeleted = true;
        Crm.Track(row, "crm.custom-field.deleted", before);
        await Crm.SaveAsync();
        return NoContent();
    }

    private async Task ValidateAsync(CustomFieldRequest request)
    {
        if (request.PipelineId.HasValue)
        {
            if (request.EntityType != CrmEntityType.Opportunity) throw new CrmException(400, "Only opportunity fields can have a pipeline scope.");
            await Crm.FindAsync<Pipeline>(request.PipelineId.Value);
        }
        if (request.FieldType == CustomFieldType.Dropdown)
        {
            if (request.Options.Count == 0 || request.Options.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 120) ||
                request.Options.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Options.Count)
                throw new CrmException(400, "Dropdown fields need unique, non-empty options of at most 120 characters.");
        }
        else if (request.Options.Count > 0) throw new CrmException(400, "Options are supported only for dropdown fields.");
    }

    private async Task<List<JsonElement>> StoredValuesAsync(CustomFieldDefinition field)
    {
        // Include deleted records so retained historical values remain interpretable.
        var json = field.EntityType switch
        {
            CrmEntityType.Company => await Crm.Db.Set<Company>().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId).Select(x => x.CustomFieldsJson).ToListAsync(),
            CrmEntityType.Contact => await Crm.Db.Set<Contact>().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId).Select(x => x.CustomFieldsJson).ToListAsync(),
            _ => await Crm.Db.Set<Opportunity>().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId).Select(x => x.CustomFieldsJson).ToListAsync()
        };
        return json.Select(CrmService.Values).Where(x => x.ContainsKey(field.Id)).Select(x => x[field.Id]).ToList();
    }
}
