using leadflow_crm.API.Authorization;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api")]
public sealed class CrmActivitiesController(CrmService crm) : CrmControllerBase(crm)
{
    [HttpGet("contacts/{id:guid}/activities"), Authorize(Policy = PermissionCatalog.ContactsView)]
    public async Task<ActionResult<PageResult<ActivityResponse>>> ContactHistory(Guid id, [FromQuery] PageQuery request)
    {
        await Crm.FindAsync<Contact>(id);
        var includeOpportunities = Crm.Can(PermissionCatalog.OpportunitiesView);
        var query = Crm.Db.Set<CrmActivity>().AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId &&
            ((x.EntityType == CrmEntityType.Contact && x.RecordId == id) || (includeOpportunities && x.EntityType == CrmEntityType.Opportunity &&
                Crm.Db.Set<Opportunity>().Any(o => o.WorkspaceId == Crm.Actor.WorkspaceId && o.ContactId == id && o.Id == x.RecordId))));
        return Ok(await ReadAsync(query, request));
    }

    [HttpGet("opportunities/{id:guid}/activities"), Authorize(Policy = PermissionCatalog.OpportunitiesView)]
    public async Task<ActionResult<PageResult<ActivityResponse>>> OpportunityHistory(Guid id, [FromQuery] PageQuery request)
    {
        await Crm.FindAsync<Opportunity>(id);
        return Ok(await ReadAsync(Crm.Db.Set<CrmActivity>().AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId &&
            x.EntityType == CrmEntityType.Opportunity && x.RecordId == id), request));
    }

    [HttpPost("contacts/{id:guid}/notes"), Authorize(Policy = PermissionCatalog.ContactsEdit)]
    public async Task<ActionResult<ActivityResponse>> AddContactNote(Guid id, NoteRequest request) =>
        await AddNoteAsync(await Crm.FindAsync<Contact>(id), CrmEntityType.Contact, request.Text);

    [HttpPost("opportunities/{id:guid}/notes"), Authorize(Policy = PermissionCatalog.OpportunitiesEdit)]
    public async Task<ActionResult<ActivityResponse>> AddOpportunityNote(Guid id, NoteRequest request) =>
        await AddNoteAsync(await Crm.FindAsync<Opportunity>(id), CrmEntityType.Opportunity, request.Text);

    private async Task<ActionResult<ActivityResponse>> AddNoteAsync(CrmRecord record, CrmEntityType type, string text)
    {
        var activity = Crm.AddActivity(type, record.Id, "crm.note.added", text.Trim());
        var contactId = type == CrmEntityType.Contact ? record.Id : ((Opportunity)record).ContactId;
        var activeLeads = await Crm.Db.Leads.Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.ContactId == contactId &&
            x.OwnerUserId == Crm.Actor.Id && !x.IsDeleted && !x.IsClosed && !x.AcknowledgedAtUtc.HasValue).ToListAsync();
        foreach (var lead in activeLeads)
        {
            lead.AcknowledgedAtUtc = DateTime.UtcNow;
            lead.LastActionAtUtc = lead.AcknowledgedAtUtc.Value;
            lead.NextEscalationAtUtc = null;
            lead.AttentionReason = null;
            Crm.Db.AuditLogs.Add(LeadWorkflowService.CreateAudit(Crm.Actor.WorkspaceId, Crm.Actor.Id,
                "lead.acknowledged-by-crm-activity", "Lead", lead.Id, new { ActivityId = activity.Id }));
        }
        var audit = Crm.Track(record, "crm.note.added");
        audit.AfterJson = CrmService.Snapshot(new { NoteId = activity.Id, Text = activity.Summary });
        await Crm.SaveAsync();
        return Created($"/api/{(type == CrmEntityType.Contact ? "contacts" : "opportunities")}/{record.Id}/activities",
            new ActivityResponse(activity.Id, type, record.Id, Crm.Actor.Id, Crm.Actor.DisplayName,
                activity.EventType, activity.Summary, activity.OccurredAtUtc));
    }

    private async Task<PageResult<ActivityResponse>> ReadAsync(IQueryable<CrmActivity> query, PageQuery request)
    {
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Summary.Contains(request.Search.Trim()));
        var count = await query.CountAsync();
        var rows = await (from activity in query
            from user in Crm.Db.Users.Where(user => user.Id == activity.ActorUserId && user.WorkspaceId == activity.WorkspaceId).DefaultIfEmpty()
            orderby activity.OccurredAtUtc descending, activity.Id
            select new ActivityResponse(activity.Id, activity.EntityType, activity.RecordId, activity.ActorUserId,
                user == null ? "System" : user.DisplayName, activity.EventType, activity.Summary, activity.OccurredAtUtc))
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        return new(rows, count, request.Page, request.PageSize);
    }
}
