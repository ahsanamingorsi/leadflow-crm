using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/leads")]
public sealed class LeadManagementController(LeadflowDbContext db, CrmService crm, LeadWorkflowService workflow)
    : CrmControllerBase(crm)
{
    [HttpPost("intake"), Authorize(Policy = PermissionCatalog.LeadsCreate)]
    public async Task<ActionResult<IntakeQueuedResponse>> CreateManual([FromHeader(Name = "Idempotency-Key")] string key,
        CreateManualLeadRequest request, CancellationToken cancellationToken)
    {
        var result = await workflow.CreateManualIntakeAsync(key, request, cancellationToken);
        return StatusCode(result.IsDuplicate ? 200 : result.Status == "Assigned" ? 201 : 202, result);
    }

    [HttpGet, Authorize(Policy = PermissionCatalog.LeadsView)]
    public async Task<ActionResult<PageResult<LeadResponse>>> List([FromQuery] LeadListQuery request)
    {
        var query = VisibleLeads();
        if (request.OwnerUserId.HasValue) query = query.Where(x => x.OwnerUserId == request.OwnerUserId.Value);
        if (request.Acknowledged.HasValue) query = query.Where(x => x.AcknowledgedAtUtc.HasValue == request.Acknowledged.Value);
        if (request.Closed.HasValue) query = query.Where(x => x.IsClosed == request.Closed.Value);
        if (!string.IsNullOrWhiteSpace(request.Source)) query = query.Where(x => x.Source == request.Source.Trim());
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Contact.DisplayName.Contains(term) || (x.Contact.Email != null && x.Contact.Email.Contains(term)) ||
                (x.Contact.Phone != null && x.Contact.Phone.Contains(term)));
        }
        if (request.FromUtc > request.ToUtc) throw new CrmException(400, "The start date must be before the end date.");
        if (request.FromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= request.FromUtc.Value);
        if (request.ToUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < request.ToUtc.Value);
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        var ownerIds = rows.Select(x => x.OwnerUserId).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && ownerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        var contactIds = rows.Select(x => x.ContactId).Distinct().ToArray();
        var contacts = await db.Contacts.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);
        return Ok(new PageResult<LeadResponse>(rows.Select(x => View(x, contacts[x.ContactId], names[x.OwnerUserId])).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpGet("{id:guid}"), Authorize(Policy = PermissionCatalog.LeadsView)]
    public async Task<ActionResult<LeadResponse>> Get(Guid id)
    {
        var lead = await VisibleLeads().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new CrmException(404, "The lead was not found in your workspace.");
        var ownerName = await db.Users.Where(x => x.Id == lead.OwnerUserId && x.WorkspaceId == Crm.Actor.WorkspaceId)
            .Select(x => x.DisplayName).SingleAsync();
        var contact = await db.Contacts.AsNoTracking().SingleAsync(x => x.Id == lead.ContactId && x.WorkspaceId == Crm.Actor.WorkspaceId);
        return Ok(View(lead, contact, ownerName));
    }

    [HttpPost("{id:guid}/acknowledge"), Authorize(Policy = PermissionCatalog.LeadsAcknowledge)]
    public async Task<ActionResult<LeadResponse>> Acknowledge(Guid id, [FromBody] LeadVersionRequest request, CancellationToken cancellationToken)
    {
        var lead = await workflow.AcknowledgeAsync(id, request.RowVersion, cancellationToken);
        var owner = await db.Users.SingleAsync(x => x.Id == lead.OwnerUserId && x.WorkspaceId == Crm.Actor.WorkspaceId);
        var contact = await db.Contacts.SingleAsync(x => x.Id == lead.ContactId && x.WorkspaceId == Crm.Actor.WorkspaceId);
        return Ok(View(lead, contact, owner.DisplayName));
    }

    [HttpPut("{id:guid}/reassign"), Authorize(Policy = PermissionCatalog.LeadsReassign)]
    public async Task<IActionResult> Reassign(Guid id, ReassignLeadRequest request, CancellationToken cancellationToken)
    {
        await workflow.ReassignAsync(id, request.OwnerUserId, request.Reason, request.RowVersion, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/close"), Authorize(Policy = PermissionCatalog.LeadsEdit)]
    public async Task<IActionResult> Close(Guid id, [FromBody] LeadVersionRequest request, CancellationToken cancellationToken)
    {
        await workflow.CloseAsync(id, request.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/assignments"), Authorize(Policy = PermissionCatalog.LeadsView)]
    public async Task<ActionResult<IReadOnlyList<LeadAssignmentResponse>>> AssignmentHistory(Guid id)
    {
        var lead = await VisibleLeads().SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new CrmException(404, "The lead was not found in your workspace.");
        var history = await db.LeadAssignmentHistory.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.LeadId == lead.Id)
            .OrderBy(x => x.AssignedAtUtc).ToListAsync();
        var ids = history.SelectMany(x => new[] { x.PreviousOwnerUserId, x.NewOwnerUserId, x.ActorUserId }).OfType<Guid>().Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        return Ok(history.Select(x => new LeadAssignmentResponse(x.Id, x.PreviousOwnerUserId,
            x.PreviousOwnerUserId.HasValue ? names.GetValueOrDefault(x.PreviousOwnerUserId.Value) : null,
            x.NewOwnerUserId, names.GetValueOrDefault(x.NewOwnerUserId, "Inactive user"), x.ActorUserId,
            x.ActorType, x.Reason, x.AssignedAtUtc, x.AcknowledgementDeadlineUtc)).ToArray());
    }

    [HttpGet("recovery"), Authorize(Policy = PermissionCatalog.LeadsReassign)]
    public async Task<ActionResult<PageResult<RecoveryLeadResponse>>> Recovery([FromQuery] PageQuery request)
    {
        var now = DateTime.UtcNow;
        var config = await workflow.GetConfigurationAsync(Crm.Actor.WorkspaceId);
        var idleCutoff = now.AddSeconds(-config.IdleWindowSeconds);
        var query = db.Leads.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && !x.IsDeleted && !x.IsClosed &&
            ((!x.AcknowledgedAtUtc.HasValue && x.AcknowledgementDeadlineUtc <= now) || x.EscalationCount >= 2 || x.LastActionAtUtc <= idleCutoff));
        var count = await query.CountAsync();
        var rows = await query.OrderBy(x => x.AcknowledgementDeadlineUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        var owners = rows.Select(x => x.OwnerUserId).Distinct().ToArray();
        var ownerNames = await db.Users.Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && owners.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName);
        var contactIds = rows.Select(x => x.ContactId).Distinct().ToArray();
        var contacts = await db.Contacts.Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id);
        var items = rows.Select(x => new RecoveryLeadResponse(View(x, contacts[x.ContactId], ownerNames[x.OwnerUserId]),
            RecoveryReasons(x, now, idleCutoff))).ToArray();
        return Ok(new PageResult<RecoveryLeadResponse>(items, count, request.Page, request.PageSize));
    }

    [HttpGet("intakes"), Authorize(Policy = PermissionCatalog.AssignmentRulesManage)]
    public async Task<ActionResult<PageResult<PendingIntakeResponse>>> PendingIntakes([FromQuery] PageQuery request)
    {
        var query = db.LeadIntakes.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.Status != LeadIntakeStatus.Assigned);
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.ContactName.Contains(request.Search.Trim()) ||
            (x.Email != null && x.Email.Contains(request.Search.Trim())) || (x.Phone != null && x.Phone.Contains(request.Search.Trim())));
        var count = await query.CountAsync();
        var rows = await query.OrderBy(x => x.ReceivedAtUtc).ThenBy(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        return Ok(new PageResult<PendingIntakeResponse>(rows.Select(x => new PendingIntakeResponse(x.Id, x.Source, x.SourceDetail,
            x.ContactName, x.Email, x.Phone, x.CompanyName, x.Message, x.Status, x.AttemptCount, x.ReceivedAtUtc,
            x.LastAttemptAtUtc, x.LastError, x.RowVersion)).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpPost("intakes/{id:guid}/retry"), Authorize(Policy = PermissionCatalog.AssignmentRulesManage)]
    public async Task<ActionResult<IntakeQueuedResponse>> RetryIntake(Guid id, RetryIntakeRequest request, CancellationToken cancellationToken)
    {
        var result = await workflow.RetryIntakeAsync(id, request.ContactId, request.RowVersion, cancellationToken);
        await db.AuditLogs.AddAsync(LeadWorkflowService.CreateAudit(Crm.Actor.WorkspaceId, Crm.Actor.Id,
            "lead.intake.retry-requested", "LeadIntake", id, new { request.ContactId }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(result);
    }

    private IQueryable<Lead> VisibleLeads()
    {
        var query = db.Leads.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId);
        return Crm.Can(PermissionCatalog.LeadsViewAll) ? query : query.Where(x => x.OwnerUserId == Crm.Actor.Id);
    }
    private static LeadResponse View(Lead lead, string ownerName) => new(lead.Id, lead.ContactId, "", null, null,
        lead.OwnerUserId, ownerName, lead.Source, lead.SourceDetail, DateTime.SpecifyKind(lead.AssignedAtUtc, DateTimeKind.Utc),
        DateTime.SpecifyKind(lead.AcknowledgementDeadlineUtc, DateTimeKind.Utc), lead.AcknowledgedAtUtc,
        DateTime.SpecifyKind(lead.LastActionAtUtc, DateTimeKind.Utc), lead.EscalationCount, lead.IsClosed, lead.AttentionReason, lead.RowVersion);
    private static LeadResponse View(Lead lead, Contact contact, string ownerName) => View(lead, ownerName) with
    { ContactName = contact.DisplayName, Email = contact.Email, Phone = contact.Phone };
    private static IReadOnlyList<string> RecoveryReasons(Lead lead, DateTime now, DateTime idleCutoff)
    {
        var reasons = new List<string>();
        if (!lead.AcknowledgedAtUtc.HasValue && lead.AcknowledgementDeadlineUtc <= now) reasons.Add("Acknowledgement window expired");
        if (lead.EscalationCount >= 2) reasons.Add("Escalated two or more times");
        if (lead.LastActionAtUtc <= idleCutoff) reasons.Add("No recorded action during the configured idle period");
        if (!string.IsNullOrWhiteSpace(lead.AttentionReason)) reasons.Add(lead.AttentionReason);
        return reasons;
    }
}

[Route("api/workspace/assignment")]
public sealed class LeadAssignmentSettingsController(LeadflowDbContext db, CrmService crm, LeadWorkflowService workflow)
    : CrmControllerBase(crm)
{
    [HttpGet("settings"), Authorize(Policy = PermissionCatalog.AssignmentRulesManage)]
    public async Task<ActionResult<AssignmentSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        var value = await workflow.GetConfigurationAsync(Crm.Actor.WorkspaceId, cancellationToken);
        return Ok(View(value));
    }

    [HttpPut("settings"), Authorize(Policy = PermissionCatalog.AssignmentRulesManage)]
    public async Task<IActionResult> SetSettings(AssignmentSettingsRequest request, CancellationToken cancellationToken)
    {
        await workflow.UpdateConfigurationAsync(request, cancellationToken);
        return Ok(await GetSettings(cancellationToken));
    }

    [HttpPost("public-intake-key"), Authorize(Policy = PermissionCatalog.AssignmentRulesManage)]
    public async Task<IActionResult> RotatePublicKey(CancellationToken cancellationToken)
    {
        var key = LeadWorkflowService.NewPublicToken();
        var workspace = await db.Workspaces.SingleAsync(x => x.Id == Crm.Actor.WorkspaceId, cancellationToken);
        workspace.PublicIntakeTokenHash = LeadWorkflowService.Hash(key);
        db.AuditLogs.Add(LeadWorkflowService.CreateAudit(workspace.Id, Crm.Actor.Id, "lead.public-intake-key.rotated",
            "Workspace", workspace.Id, new { Rotated = true }));
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { publicKey = key, endpoint = "/api/public/leads" });
    }

    [HttpGet("availability"), Authorize(Policy = PermissionCatalog.LeadsView)]
    public async Task<ActionResult<IReadOnlyList<EmployeeAvailabilityResponse>>> ListAvailability()
    {
        var all = Crm.Can(PermissionCatalog.AvailabilityManage);
        var query = db.EmployeeAvailabilities.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId);
        if (!all) query = query.Where(x => x.UserId == Crm.Actor.Id);
        var rows = await (from availability in query
            join user in db.Users.AsNoTracking() on new { availability.UserId, availability.WorkspaceId } equals new { UserId = user.Id, user.WorkspaceId }
            where user.IsActive
            orderby user.DisplayName
            select new { Availability = availability, User = user }).ToListAsync();
        var ids = rows.Select(x => x.User.Id).ToArray();
        var counts = await db.Leads.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && ids.Contains(x.OwnerUserId) && !x.IsDeleted && !x.IsClosed)
            .GroupBy(x => x.OwnerUserId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        var settings = await workflow.GetConfigurationAsync(Crm.Actor.WorkspaceId);
        return Ok(rows.Select(x => new EmployeeAvailabilityResponse(x.User.Id, x.User.DisplayName, x.User.Email ?? "",
            x.Availability.Status, counts.GetValueOrDefault(x.User.Id), settings.AcknowledgementWindowSeconds,
            DateTime.SpecifyKind(x.Availability.ChangedAtUtc, DateTimeKind.Utc), x.Availability.RowVersion)).ToArray());
    }

    [HttpPut("availability/me"), Authorize(Policy = PermissionCatalog.AvailabilityEditOwn)]
    public async Task<IActionResult> SetOwnAvailability(AvailabilityRequest request, CancellationToken cancellationToken)
    {
        await workflow.SetAvailabilityAsync(Crm.Actor.Id, request.Status, request.RowVersion, false, cancellationToken);
        return Ok(await ListAvailability());
    }

    [HttpPut("availability/{userId:guid}"), Authorize(Policy = PermissionCatalog.AvailabilityManage)]
    public async Task<IActionResult> SetAvailability(Guid userId, AvailabilityRequest request, CancellationToken cancellationToken)
    {
        await workflow.SetAvailabilityAsync(userId, request.Status, request.RowVersion, true, cancellationToken);
        return Ok(await ListAvailability());
    }

    [HttpPut("my-notification-email"), Authorize]
    public async Task<IActionResult> SetNotificationEmail(NotificationEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(x => x.Id == Crm.Actor.Id && x.WorkspaceId == Crm.Actor.WorkspaceId, cancellationToken);
        user.NotificationEmail = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        db.AuditLogs.Add(LeadWorkflowService.CreateAudit(Crm.Actor.WorkspaceId, Crm.Actor.Id,
            "lead.notification-email.updated", "ApplicationUser", user.Id, new { Configured = user.NotificationEmail is not null }));
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("my-notifications"), Authorize(Policy = PermissionCatalog.LeadNotificationsView)]
    public async Task<ActionResult<PageResult<UserLeadNotificationResponse>>> Notifications([FromQuery] PageQuery request)
    {
        var query = db.LeadUserNotifications.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.RecipientUserId == Crm.Actor.Id);
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        return Ok(new PageResult<UserLeadNotificationResponse>(rows.Select(x => new UserLeadNotificationResponse(x.Id, x.LeadId,
            x.Title, x.Message, DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc), x.ReadAtUtc, x.RowVersion)).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpPost("my-notifications/{id:guid}/read"), Authorize(Policy = PermissionCatalog.LeadNotificationsView)]
    public async Task<IActionResult> MarkRead(Guid id, [FromBody] LeadVersionRequest request, CancellationToken cancellationToken)
    {
        var row = await db.LeadUserNotifications.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == Crm.Actor.WorkspaceId && x.RecipientUserId == Crm.Actor.Id, cancellationToken)
            ?? throw new CrmException(404, "Notification was not found.");
        if (request.RowVersion is null || request.RowVersion.Length != 8 || !row.RowVersion.SequenceEqual(request.RowVersion))
            throw new CrmException(409, "Notification changed. Reload it before retrying.");
        row.ReadAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("email-delivery"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
    public async Task<ActionResult<PageResult<LeadNotificationResponse>>> EmailDelivery([FromQuery] PageQuery request)
    {
        var query = db.LeadNotifications.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId);
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync();
        return Ok(new PageResult<LeadNotificationResponse>(rows.Select(x => new LeadNotificationResponse(x.Id, x.LeadId,
            x.RecipientUserId, x.RecipientEmail, x.Subject, x.Status, x.AttemptCount, DateTime.SpecifyKind(x.CreatedAtUtc, DateTimeKind.Utc),
            x.SentAtUtc, x.Status == LeadNotificationStatus.Pending ? x.NextAttemptAtUtc : null, x.LastError, x.RowVersion)).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpPost("email-delivery/{id:guid}/retry"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
    public async Task<IActionResult> RetryEmail(Guid id, [FromBody] LeadVersionRequest request, CancellationToken cancellationToken)
    {
        var item = await db.LeadNotifications.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == Crm.Actor.WorkspaceId, cancellationToken)
            ?? throw new CrmException(404, "Email notification was not found.");
        if (request.RowVersion is null || request.RowVersion.Length != 8 || !item.RowVersion.SequenceEqual(request.RowVersion))
            throw new CrmException(409, "Email delivery changed. Reload it before retrying.");
        if (item.Status != LeadNotificationStatus.Failed) throw new CrmException(409, "Only failed emails can be retried.");
        item.Status = LeadNotificationStatus.Pending; item.AttemptCount = 0; item.LastError = null; item.NextAttemptAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Accepted();
    }

    private static AssignmentSettingsResponse View(LeadAssignmentConfiguration x) => new(x.MaxOpenLeadsPerEmployee,
        x.AcknowledgementWindowSeconds, x.IdleWindowSeconds, x.RetryIntervalSeconds,
        JsonSerializer.Deserialize<List<Guid>>(x.EscalationUserIdsJson)!, JsonSerializer.Deserialize<List<int>>(x.EscalationIntervalsSecondsJson)!, x.RowVersion);
}
