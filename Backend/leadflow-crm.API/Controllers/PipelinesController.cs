using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace leadflow_crm.API.Controllers;

[Route("api/pipelines")]
public sealed class PipelinesController(CrmService crm) : CrmControllerBase(crm)
{
    private void RequireRead()
    {
        if (!Crm.Can(PermissionCatalog.PipelinesManage) && !Crm.Can(PermissionCatalog.OpportunitiesView) &&
            !Crm.Can(PermissionCatalog.OpportunitiesCreate) && !Crm.Can(PermissionCatalog.OpportunitiesEdit))
            throw new CrmException(403, "You do not have access to pipelines.");
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<PipelineResponse>>> List([FromQuery] PageQuery request)
    {
        RequireRead();
        var query = Crm.Records<Pipeline>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Name.Contains(request.Search.Trim()));
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        var views = new List<PipelineResponse>();
        foreach (var row in rows) views.Add(await Crm.ViewPipelineAsync(row));
        return Ok(new PageResult<PipelineResponse>(views, count, request.Page, request.PageSize));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PipelineResponse>> Get(Guid id)
    {
        RequireRead();
        return Ok(await Crm.ViewPipelineAsync(await Crm.FindAsync<Pipeline>(id)));
    }

    [HttpPost, Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<ActionResult<PipelineResponse>> Create(PipelineRequest request)
    {
        var row = new Pipeline { WorkspaceId = Crm.Actor.WorkspaceId, Name = request.Name.Trim() };
        Crm.Db.Set<Pipeline>().Add(row);
        Crm.Track(row, "crm.pipeline.created");
        await Crm.SaveAsync();
        return CreatedAtAction(nameof(Get), new { id = row.Id }, await Crm.ViewPipelineAsync(row));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<ActionResult<PipelineResponse>> Update(Guid id, PipelineRequest request)
    {
        var row = await Crm.FindAsync<Pipeline>(id);
        CrmService.CheckVersion(row, request.RowVersion);
        var before = CrmService.Snapshot(row);
        row.Name = request.Name.Trim();
        Crm.Track(row, "crm.pipeline.updated", before);
        await Crm.SaveAsync();
        return Ok(await Crm.ViewPipelineAsync(row));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "If-Match")] string? rowVersion)
    {
        var row = await Crm.FindAsync<Pipeline>(id);
        CrmService.CheckHeaderVersion(row, rowVersion);
        if (row.IsDefault) throw new CrmException(409, "The default pipeline cannot be deleted.");
        if (await Crm.Records<PipelineStage>().AnyAsync(x => x.PipelineId == id) ||
            await Crm.Records<CustomFieldDefinition>().AnyAsync(x => x.PipelineId == id) ||
            await Crm.Records<Opportunity>().AnyAsync(x => x.PipelineId == id))
            throw new CrmException(409, "Remove this pipeline's opportunities, stages and custom fields before deleting it.");
        var before = CrmService.Snapshot(row); row.IsDeleted = true;
        Crm.Track(row, "crm.pipeline.deleted", before);
        await Crm.SaveAsync();
        return NoContent();
    }

    [HttpPost("{pipelineId:guid}/stages"), Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<ActionResult<PipelineResponse>> CreateStage(Guid pipelineId, StageRequest request)
    {
        var pipeline = await Crm.FindAsync<Pipeline>(pipelineId);
        var stages = await Crm.Records<PipelineStage>().Where(x => x.PipelineId == pipelineId).ToListAsync();
        if (stages.Count >= 100) throw new CrmException(400, "A pipeline may contain at most 100 stages.");
        var row = new PipelineStage { WorkspaceId = Crm.Actor.WorkspaceId, PipelineId = pipelineId, Name = request.Name.Trim(),
            Kind = request.Kind, Position = stages.Count == 0 ? 0 : stages.Max(x => x.Position) + 1 };
        Crm.Db.Set<PipelineStage>().Add(row);
        Crm.Track(row, "crm.stage.created");
        await Crm.SaveAsync();
        return Created($"/api/pipelines/{pipelineId}", await Crm.ViewPipelineAsync(pipeline));
    }

    [HttpPut("{pipelineId:guid}/stages/{id:guid}"), Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<ActionResult<PipelineResponse>> UpdateStage(Guid pipelineId, Guid id, StageRequest request)
    {
        var pipeline = await Crm.FindAsync<Pipeline>(pipelineId);
        var row = await FindStageAsync(pipelineId, id);
        CrmService.CheckVersion(row, request.RowVersion);
        if (row.Kind != request.Kind && await Crm.Records<Opportunity>().AnyAsync(x => x.StageId == id))
            throw new CrmException(409, "Move the opportunities out before changing this stage's Open/Won/Lost classification.");
        var before = CrmService.Snapshot(row);
        row.Name = request.Name.Trim(); row.Kind = request.Kind;
        Crm.Track(row, "crm.stage.updated", before);
        await Crm.SaveAsync();
        return Ok(await Crm.ViewPipelineAsync(pipeline));
    }

    [HttpDelete("{pipelineId:guid}/stages/{id:guid}"), Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<IActionResult> DeleteStage(Guid pipelineId, Guid id, [FromHeader(Name = "If-Match")] string? rowVersion)
    {
        var pipeline = await Crm.FindAsync<Pipeline>(pipelineId);
        var row = await FindStageAsync(pipelineId, id);
        CrmService.CheckHeaderVersion(row, rowVersion);
        if (await Crm.Records<Opportunity>().AnyAsync(x => x.StageId == id))
            throw new CrmException(409, "Move the opportunities to another stage before deleting this stage.");
        if (pipeline.IsDefault && await Crm.Records<PipelineStage>().CountAsync(x => x.PipelineId == pipelineId) == 1)
            throw new CrmException(409, "The default pipeline must keep at least one stage.");
        var before = CrmService.Snapshot(row); row.IsDeleted = true;
        var rules = await Crm.Db.Set<StageRequiredField>().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.StageId == id).ToListAsync();
        Crm.Db.RemoveRange(rules);
        Crm.Track(row, "crm.stage.deleted", before);
        await Crm.SaveAsync();
        return NoContent();
    }

    [HttpPut("{pipelineId:guid}/stage-order"), Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<ActionResult<PipelineResponse>> Reorder(Guid pipelineId, ReorderStagesRequest request)
    {
        var pipeline = await Crm.FindAsync<Pipeline>(pipelineId);
        var stages = await Crm.Records<PipelineStage>().Where(x => x.PipelineId == pipelineId).ToListAsync();
        if (request.Stages.Any(x => x is null) || request.Stages.Count != stages.Count ||
            request.Stages.Select(x => x.Id).Distinct().Count() != stages.Count ||
            request.Stages.Any(x => stages.All(s => s.Id != x.Id)))
            throw new CrmException(400, "Supply every stage exactly once in the desired order.");
        for (var i = 0; i < request.Stages.Count; i++)
        {
            var row = stages.Single(x => x.Id == request.Stages[i].Id);
            CrmService.CheckVersion(row, request.Stages[i].RowVersion);
            var before = CrmService.Snapshot(row); row.Position = i;
            Crm.Track(row, "crm.stage.reordered", before);
        }
        await Crm.SaveAsync();
        return Ok(await Crm.ViewPipelineAsync(pipeline));
    }

    [HttpPut("{pipelineId:guid}/stages/{id:guid}/required-fields"), Authorize(Policy = PermissionCatalog.PipelinesManage)]
    public async Task<ActionResult<PipelineResponse>> SetRequiredFields(Guid pipelineId, Guid id, RequiredFieldsRequest request)
    {
        var pipeline = await Crm.FindAsync<Pipeline>(pipelineId);
        var stage = await FindStageAsync(pipelineId, id);
        CrmService.CheckVersion(stage, request.RowVersion);
        var ids = request.FieldIds.Distinct().ToArray();
        var fields = await Crm.Records<CustomFieldDefinition>().Where(x => ids.Contains(x.Id) && x.EntityType == CrmEntityType.Opportunity &&
            (x.PipelineId == null || x.PipelineId == pipelineId)).ToListAsync();
        if (fields.Count != ids.Length) throw new CrmException(400, "Required fields must belong to this pipeline's opportunities.");
        var existingValues = await Crm.Records<Opportunity>().Where(x => x.StageId == id).Select(x => x.CustomFieldsJson).ToListAsync();
        foreach (var json in existingValues)
        {
            var values = CrmService.Values(json);
            if (ids.Any(fieldId => !values.TryGetValue(fieldId, out var value) || value.ValueKind == JsonValueKind.Null ||
                (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))))
                throw new CrmException(409, "Fill these fields on existing opportunities in this stage before making them required.");
        }
        var rules = await Crm.Db.Set<StageRequiredField>().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.StageId == id).ToListAsync();
        var previousIds = rules.Select(x => x.FieldId).ToArray();
        Crm.Db.RemoveRange(rules.Where(x => !ids.Contains(x.FieldId)));
        foreach (var fieldId in ids.Except(previousIds)) Crm.Db.Add(new StageRequiredField
        { WorkspaceId = Crm.Actor.WorkspaceId, StageId = id, FieldId = fieldId });
        var audit = Crm.Track(stage, "crm.stage.required-fields.updated", CrmService.Snapshot(new { Stage = stage, RequiredFieldIds = previousIds }));
        // Store the new configuration in the audit record as well as the stage metadata.
        audit.AfterJson = CrmService.Snapshot(new { Stage = stage, RequiredFieldIds = ids });
        await Crm.SaveAsync();
        return Ok(await Crm.ViewPipelineAsync(pipeline));
    }

    private async Task<PipelineStage> FindStageAsync(Guid pipelineId, Guid id)
    {
        var row = await Crm.FindAsync<PipelineStage>(id);
        if (row.PipelineId != pipelineId) throw new CrmException(404, "Stage was not found in this pipeline.");
        return row;
    }
}
