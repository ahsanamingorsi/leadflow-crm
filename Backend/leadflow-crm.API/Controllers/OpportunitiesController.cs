using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/opportunities")]
public sealed class OpportunitiesController(CrmService crm) : CrmControllerBase(crm)
{
    [HttpGet, Authorize(Policy = PermissionCatalog.OpportunitiesView)]
    public async Task<ActionResult<PageResult<OpportunityResponse>>> List([FromQuery] RecordQuery request)
    {
        var query = Crm.Records<Opportunity>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Title.Contains(request.Search.Trim()));
        if (request.ContactId.HasValue) query = query.Where(x => x.ContactId == request.ContactId);
        if (request.CompanyId.HasValue) query = query.Where(x => Crm.Db.Set<Contact>().Any(c =>
            c.WorkspaceId == Crm.Actor.WorkspaceId && c.Id == x.ContactId && c.CompanyId == request.CompanyId && !c.IsDeleted));
        if (request.OwnerUserId.HasValue) query = query.Where(x => x.OwnerUserId == request.OwnerUserId);
        if (request.PipelineId.HasValue) query = query.Where(x => x.PipelineId == request.PipelineId);
        if (request.StageId.HasValue) query = query.Where(x => x.StageId == request.StageId);
        query = CrmQueries.ByCreatedDate(query, request);
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        return Ok(new PageResult<OpportunityResponse>(rows.Select(Crm.View).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpGet("{id:guid}"), Authorize(Policy = PermissionCatalog.OpportunitiesView)]
    public async Task<ActionResult<OpportunityResponse>> Get(Guid id) => Ok(Crm.View(await Crm.FindAsync<Opportunity>(id)));

    [HttpPost, Authorize(Policy = PermissionCatalog.OpportunitiesCreate)]
    public async Task<ActionResult<OpportunityResponse>> Create(OpportunityRequest request)
    {
        var row = new Opportunity { WorkspaceId = Crm.Actor.WorkspaceId };
        await ApplyAsync(row, request);
        Crm.Db.Set<Opportunity>().Add(row);
        Crm.Track(row, "crm.opportunity.created", type: CrmEntityType.Opportunity, summary: "Opportunity created.");
        await Crm.SaveAsync();
        return CreatedAtAction(nameof(Get), new { id = row.Id }, Crm.View(row));
    }

    [HttpPut("{id:guid}"), Authorize(Policy = PermissionCatalog.OpportunitiesEdit)]
    public async Task<ActionResult<OpportunityResponse>> Update(Guid id, OpportunityRequest request)
    {
        var row = await Crm.FindAsync<Opportunity>(id);
        CrmService.CheckVersion(row, request.RowVersion);
        var before = CrmService.Snapshot(row);
        var previousStage = row.StageId;
        var previousOwner = row.OwnerUserId;
        await ApplyAsync(row, request);
        Crm.Track(row, "crm.opportunity.updated", before, CrmEntityType.Opportunity, "Opportunity details updated.");
        if (previousStage != row.StageId) Crm.AddActivity(CrmEntityType.Opportunity, id, "crm.opportunity.stage-changed", "Pipeline stage changed.");
        if (previousOwner != row.OwnerUserId) Crm.AddActivity(CrmEntityType.Opportunity, id, "crm.opportunity.owner-changed", "Opportunity owner changed.");
        await Crm.SaveAsync();
        return Ok(Crm.View(row));
    }

    [HttpPost("{id:guid}/move"), Authorize(Policy = PermissionCatalog.OpportunitiesEdit)]
    public async Task<ActionResult<OpportunityResponse>> Move(Guid id, MoveOpportunityRequest request)
    {
        var row = await Crm.FindAsync<Opportunity>(id);
        CrmService.CheckVersion(row, request.RowVersion);
        var stage = await Crm.FindAsync<PipelineStage>(request.StageId);
        await Crm.FindAsync<Pipeline>(stage.PipelineId);
        var before = CrmService.Snapshot(row);
        row.CustomFieldsJson = await Crm.ValidateFieldsAsync(CrmEntityType.Opportunity, stage.PipelineId,
            request.CustomFields ?? CrmService.Values(row.CustomFieldsJson), stage.Id);
        row.StageId = stage.Id; row.PipelineId = stage.PipelineId;
        Crm.Track(row, "crm.opportunity.stage-changed", before, CrmEntityType.Opportunity, $"Moved to stage '{stage.Name}'.");
        await Crm.SaveAsync();
        return Ok(Crm.View(row));
    }

    [HttpDelete("{id:guid}"), Authorize(Policy = PermissionCatalog.OpportunitiesDelete)]
    public async Task<IActionResult> Delete(Guid id, [FromHeader(Name = "If-Match")] string? rowVersion)
    {
        var row = await Crm.FindAsync<Opportunity>(id);
        CrmService.CheckHeaderVersion(row, rowVersion);
        var before = CrmService.Snapshot(row);
        row.IsDeleted = true;
        Crm.Track(row, "crm.opportunity.deleted", before, CrmEntityType.Opportunity, "Opportunity deleted.");
        await Crm.SaveAsync();
        return NoContent();
    }

    private async Task ApplyAsync(Opportunity row, OpportunityRequest request)
    {
        await Crm.FindAsync<Contact>(request.ContactId);
        await Crm.FindAsync<Pipeline>(request.PipelineId);
        var stage = await Crm.FindAsync<PipelineStage>(request.StageId);
        if (stage.PipelineId != request.PipelineId) throw new CrmException(400, "The selected stage belongs to another pipeline.");
        await Crm.ValidateOwnerAsync(request.OwnerUserId);
        if (request.Value.HasValue)
        {
            Crm.Require(PermissionCatalog.OpportunityValueView);
            if (decimal.Round(request.Value.Value, 2) != request.Value.Value) throw new CrmException(400, "PKR amounts must have at most two decimal places.");
            row.Value = request.Value.Value;
        }
        row.CustomFieldsJson = await Crm.ValidateFieldsAsync(CrmEntityType.Opportunity, request.PipelineId, request.CustomFields, stage.Id);
        row.Title = request.Title.Trim(); row.ContactId = request.ContactId; row.PipelineId = request.PipelineId;
        row.StageId = stage.Id; row.OwnerUserId = request.OwnerUserId; row.ExpectedCloseDate = request.ExpectedCloseDate;
        row.Description = CrmService.Clean(request.Description);
    }
}
