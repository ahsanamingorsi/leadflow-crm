using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.RegularExpressions;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

public sealed class WhatsAppSendRequest
{
    public Guid ContactId { get; init; }
    [Required, StringLength(100, MinimumLength = 8)] public string ClientKey { get; init; } = "";
    [StringLength(4096)] public string Body { get; init; } = "";
    [RegularExpression("^HX[0-9a-fA-F]{32}$")] public string? ContentSid { get; init; }
    public bool ConfirmedOptIn { get; init; }
}

[Route("api/whatsapp")]
public sealed class WhatsAppController(LeadflowDbContext db, CrmService crm, WorkspaceIntegrationService integrations,
    TwilioWhatsAppService twilio) : IntegrationApiControllerBase(crm)
{
    private IQueryable<WhatsAppMessage> Visible()
    {
        var rows = db.WhatsAppMessages.Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId);
        if (!Crm.Can(PermissionCatalog.LeadsViewAll))
            rows = rows.Where(x => db.Contacts.Any(c => c.Id == x.ContactId && c.WorkspaceId == x.WorkspaceId && !c.IsDeleted && c.OwnerUserId == Crm.Actor.Id)
                || db.Leads.Any(l => l.ContactId == x.ContactId && l.WorkspaceId == x.WorkspaceId && !l.IsDeleted && !l.IsClosed && l.OwnerUserId == Crm.Actor.Id));
        return rows;
    }

    [HttpGet("messages"), Authorize(Policy = PermissionCatalog.WhatsAppView)]
    public async Task<IActionResult> List([FromQuery] PageQuery page, Guid? contactId, CancellationToken ct)
    {
        var query = Visible().AsNoTracking();
        if (contactId.HasValue) query = query.Where(x => x.ContactId == contactId);
        var total = await query.CountAsync(ct);
        return Ok(new { items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToListAsync(ct), totalCount = total, page = page.Page, pageSize = page.PageSize });
    }

    [HttpPost("messages"), Authorize(Policy = PermissionCatalog.WhatsAppReply)]
    public async Task<IActionResult> Send(WhatsAppSendRequest request, CancellationToken ct)
    {
        var contact = await Crm.FindAsync<Contact>(request.ContactId);
        if (!Crm.Can(PermissionCatalog.LeadsViewAll) && contact.OwnerUserId != Crm.Actor.Id &&
            !await db.Leads.AnyAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.ContactId == contact.Id && x.OwnerUserId == Crm.Actor.Id && !x.IsClosed && !x.IsDeleted, ct))
            throw new CrmException(403, "Only the assigned employee or a workspace manager can message this contact.");
        if (contact.OptedOut) throw new CrmException(409, "This contact has opted out of communication.");
        if (string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.ContentSid)) throw new CrmException(400, "Enter a message or approved template SID.");
        var settings = await integrations.GetSettingsAsync<TwilioSettings>(Crm.Actor.WorkspaceId, "twilio", ct)
            ?? throw new CrmException(409, "Configure Twilio in Integrations first.");
        var phone = TwilioWhatsAppService.Phone(contact.Phone ?? "");
        if (await db.WhatsAppMessages.AnyAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.Phone == phone && x.IsOptOut && x.ContactId == null, ct))
            throw new CrmException(409, "This number has an unresolved WhatsApp opt-out. Resolve the contact match before messaging.");
        var existing = await db.WhatsAppMessages.SingleOrDefaultAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.ClientKey == request.ClientKey, ct);
        if (existing is not null)
        {
            if (existing.ContactId != contact.Id || existing.Body != request.Body.Trim() || existing.ContentSid != request.ContentSid)
                throw new CrmException(409, "This request key belongs to a different message.");
            return Ok(existing);
        }
        var inWindow = await db.WhatsAppMessages.AnyAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.Phone == phone &&
            x.Direction == "Inbound" && x.CreatedAtUtc > DateTime.UtcNow.AddHours(-24), ct);
        if (!inWindow && (string.IsNullOrEmpty(request.ContentSid) || !request.ConfirmedOptIn))
            throw new CrmException(409, "Outside the 24-hour reply window, select an approved template and confirm recorded customer consent.");
        var row = new WhatsAppMessage { WorkspaceId = Crm.Actor.WorkspaceId, ContactId = contact.Id, ActorUserId = Crm.Actor.Id,
            Phone = phone, Direction = "Outbound", Status = "pending", Body = request.Body.Trim(), ContentSid = request.ContentSid, ClientKey = request.ClientKey };
        db.WhatsAppMessages.Add(row);
        db.AuditLogs.Add(LeadWorkflowService.CreateAudit(row.WorkspaceId, Crm.Actor.Id, "whatsapp.send.requested", "WhatsAppMessage", row.Id,
            new { row.ContactId, request.ConfirmedOptIn, row.ContentSid }));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new CrmException(409, "The message request is already being processed. Reload the inbox."); }
        // Do not automatically resend an uncertain provider request; it may already have been accepted.
        (string? Sid, string Status, string? Error) result;
        try { result = await twilio.SendAsync(settings, phone, row.Body, row.ContentSid,
            $"{settings.PublicBaseUrl}/api/whatsapp/hooks/{row.WorkspaceId}/status/{row.Id}", ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        { result = (null, "unknown", "Delivery could not be confirmed. Check Twilio before sending again."); }
        await db.Entry(row).ReloadAsync(CancellationToken.None);
        row.ProviderMessageId ??= result.Sid;
        if (row.Status == "pending") { row.Status = result.Status; row.LastError = result.Error; }
        row.UpdatedAtUtc = DateTime.UtcNow;
        db.IntegrationSyncEvents.Add(new IntegrationSyncEvent { WorkspaceId = row.WorkspaceId, Provider = "twilio", EventType = "outbound." + row.Status,
            ExternalEventId = "send:" + row.Id, Status = row.LastError is null ? "Succeeded" : "Failed", Summary = "WhatsApp message submitted: " + row.Status });
        db.Set<CrmActivity>().Add(new CrmActivity { WorkspaceId = row.WorkspaceId, EntityType = CrmEntityType.Contact, RecordId = contact.Id,
            ActorUserId = Crm.Actor.Id, EventType = "whatsapp.outbound", Summary = "WhatsApp message: " + row.Status });
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (DbUpdateConcurrencyException) { await db.Entry(row).ReloadAsync(CancellationToken.None); }
        return Ok(row);
    }
}

[ApiController, AllowAnonymous, Route("api/whatsapp/hooks/{workspaceId:guid}")]
public sealed class WhatsAppHooksController(LeadflowDbContext db, WorkspaceIntegrationService integrations, LeadWorkflowService workflow) : ControllerBase
{
    [HttpPost("inbound"), RequestSizeLimit(64000)]
    public async Task<IActionResult> Inbound(Guid workspaceId, CancellationToken ct)
    {
        var verified = await Verify(workspaceId, $"/api/whatsapp/hooks/{workspaceId}/inbound", ct);
        if (verified is null) return Unauthorized();
        var (settings, form) = verified.Value;
        if (form["To"] != "whatsapp:" + settings.FromNumber || !Regex.IsMatch(form["MessageSid"].ToString(), "^SM[0-9a-fA-F]{32}$")) return BadRequest();
        string phone;
        try { phone = TwilioWhatsAppService.Phone(form["From"].ToString()); } catch (CrmException) { return BadRequest(); }
        var sid = form["MessageSid"].ToString();
        var body = form["Body"].ToString();
        if (body.Length > 4096) return BadRequest();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var resource = $"whatsapp:{workspaceId}:{phone}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Messaging is busy. Retry callback.', 1;", ct);
        if (await db.WhatsAppMessages.AnyAsync(x => x.WorkspaceId == workspaceId && x.ProviderMessageId == sid, ct)) return Content("<Response/>", "text/xml");
        var national = phone.StartsWith("+92", StringComparison.Ordinal) ? "0" + phone[3..] : phone;
        var matches = await db.Contacts.Where(x => x.WorkspaceId == workspaceId && !x.IsDeleted && (x.Phone == phone || x.Phone == national))
            .Take(2).ToListAsync(ct);
        Contact? contact = matches.Count == 1 ? matches[0] : null;
        Guid? leadId = null;
        Guid? intakeId = null;
        if (contact is null)
        {
            var intakeRow = await db.LeadIntakes.Where(x => x.WorkspaceId == workspaceId && x.Source == "WhatsApp" && x.Phone == phone && x.Status != LeadIntakeStatus.Assigned)
                .OrderBy(x => x.ReceivedAtUtc).FirstOrDefaultAsync(ct);
            if (intakeRow is null)
            {
                var intake = await workflow.CreateIntegrationIntakeAsync(workspaceId, "whatsapp:" + sid,
                    new LeadIntakeRequest { ContactName = phone, Phone = phone, Message = body.Length > 4000 ? body[..4000] : body }, "WhatsApp", "Twilio WhatsApp inbound", ct);
                intakeRow = await db.LeadIntakes.SingleAsync(x => x.Id == intake.IntakeId, ct);
            }
            intakeId = intakeRow.Id;
            leadId = intakeRow.LeadId;
            if (intakeRow.MatchedContactId.HasValue) contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == intakeRow.MatchedContactId && x.WorkspaceId == workspaceId, ct);
        }
        var optOut = form["OptOutType"].ToString().Equals("STOP", StringComparison.OrdinalIgnoreCase) || new[] { "STOP", "UNSUBSCRIBE", "CANCEL", "END", "QUIT" }.Contains(body.Trim().ToUpperInvariant());
        if (contact is not null && optOut) contact.OptedOut = true;
        if (optOut) foreach (var match in matches) match.OptedOut = true;
        var row = new WhatsAppMessage { WorkspaceId = workspaceId, ContactId = contact?.Id, LeadId = leadId,
            IntakeId = intakeId, IsOptOut = optOut,
            Phone = phone, Body = string.IsNullOrEmpty(body) ? "[Media message received; open Twilio to view attachment]" : body,
            ProviderMessageId = sid, ClientKey = "inbound:" + sid };
        db.WhatsAppMessages.Add(row);
        db.IntegrationSyncEvents.Add(new IntegrationSyncEvent { WorkspaceId = workspaceId, Provider = "twilio", EventType = "inbound.received", ExternalEventId = sid, Summary = "WhatsApp inbound received." });
        if (contact is not null) db.Set<CrmActivity>().Add(new CrmActivity { WorkspaceId = workspaceId, EntityType = CrmEntityType.Contact, RecordId = contact.Id,
            EventType = "whatsapp.received", Summary = optOut ? "Customer opted out via WhatsApp." : "WhatsApp message received." });
        await db.SaveChangesAsync(ct);
        await integrations.MarkStatusAsync(workspaceId, "twilio", IntegrationStatus.Connected, cancellationToken: ct);
        await tx.CommitAsync(ct);
        return Content("<Response/>", "text/xml");
    }

    [HttpPost("status/{id:guid}"), RequestSizeLimit(64000)]
    public async Task<IActionResult> Status(Guid workspaceId, Guid id, CancellationToken ct)
    {
        var verified = await Verify(workspaceId, $"/api/whatsapp/hooks/{workspaceId}/status/{id}", ct);
        if (verified is null) return Unauthorized();
        var (settings, form) = verified.Value;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var row = await db.WhatsAppMessages.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId && x.Direction == "Outbound", ct);
        if (row is null) return NotFound();
        var sid = form["MessageSid"].ToString();
        if (!Regex.IsMatch(sid, "^SM[0-9a-fA-F]{32}$") || form["From"] != "whatsapp:" + settings.FromNumber ||
            form["To"] != "whatsapp:" + row.Phone || (row.ProviderMessageId is not null && row.ProviderMessageId != sid)) return BadRequest();
        row.ProviderMessageId = sid;
        var status = form["MessageStatus"].ToString();
        if (TwilioWhatsAppService.CanAdvance(row.Status, status))
        {
            row.Status = status;
            var code = Regex.Replace(form["ErrorCode"].ToString(), "[^0-9]", "");
            row.LastError = status is "failed" or "undelivered" ? "Twilio delivery failed. Error code: " + code[..Math.Min(10, code.Length)] : null;
            row.UpdatedAtUtc = DateTime.UtcNow;
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent { WorkspaceId = workspaceId, Provider = "twilio", EventType = "delivery." + status,
                ExternalEventId = sid + ":" + status, Status = row.LastError is null ? "Succeeded" : "Failed", Summary = "WhatsApp delivery: " + status });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok();
    }

    private async Task<(TwilioSettings, IFormCollection)?> Verify(Guid workspaceId, string path, CancellationToken ct)
    {
        if (!Request.HasFormContentType || Request.QueryString.HasValue) return null;
        var settings = await integrations.GetSettingsAsync<TwilioSettings>(workspaceId, "twilio", ct);
        if (settings is null) return null;
        var form = await Request.ReadFormAsync(ct);
        return form["AccountSid"] == settings.AccountSid && TwilioWhatsAppService.Verify(settings.PublicBaseUrl + path, form,
            Request.Headers["X-Twilio-Signature"].ToString(), settings.AuthToken) ? (settings, form) : null;
    }
}
