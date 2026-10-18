using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models;
using leadflow_crm.API.Models.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace leadflow_crm.API.Services;

public sealed class LeadWorkflowService(LeadflowDbContext db, CrmService crm, ILogger<LeadWorkflowService> logger,
    IConfiguration configuration)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string NewPublicToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public async Task<IntakeQueuedResponse> CreatePublicIntakeAsync(string token, string key, LeadIntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotencyKey(key);
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128) throw new CrmException(401, "The intake key is invalid.");
        var tokenHash = Hash(token.Trim());
        var workspace = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(x => x.PublicIntakeTokenHash == tokenHash && x.IsActive, cancellationToken)
            ?? throw new CrmException(401, "The intake key is invalid.");
        return await CreateIntakeAsync(workspace.Id, key, request, "Website", request.SourceDetail, null, cancellationToken);
    }

    public Task<IntakeQueuedResponse> CreateManualIntakeAsync(string key, CreateManualLeadRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateIdempotencyKey(key);
        if (request.Source is not ("Manual" or "Website")) throw new CrmException(400, "Source must be Manual or Website.");
        return CreateIntakeAsync(crm.Actor.WorkspaceId, key, request, request.Source, request.SourceDetail, request.ContactId, cancellationToken);
    }

    public Task<IntakeQueuedResponse> CreateIntegrationIntakeAsync(Guid workspaceId, string key,
        LeadIntakeRequest request, string source, string? sourceDetail, CancellationToken cancellationToken = default)
    {
        ValidateIdempotencyKey(key);
        if (source is not ("Email" or "Website")) throw new CrmException(400, "The integration source is invalid.");
        return CreateIntakeAsync(workspaceId, key, request, source, sourceDetail, null, cancellationToken);
    }

    private async Task<IntakeQueuedResponse> CreateIntakeAsync(Guid workspaceId, string key, LeadIntakeRequest request,
        string source, string? sourceDetail, Guid? contactId, CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            if (db.Database.CurrentTransaction is null)
                transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await LockAsync($"lead-intake:{workspaceId:N}:{Hash(key)}", cancellationToken);
            var keyHash = Hash(key);
            var existing = await db.LeadIntakes.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IdempotencyKeyHash == keyHash, cancellationToken);
            if (existing is not null)
            {
                var duplicate = new IntakeQueuedResponse(existing.Id, existing.Status.ToString(), existing.LeadId, existing.ReceivedAtUtc, existing.LastError, true);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return duplicate;
            }

            if (contactId.HasValue && !await db.Contacts.AnyAsync(x => x.Id == contactId && x.WorkspaceId == workspaceId && !x.IsDeleted, cancellationToken))
                throw new CrmException(404, "The contact was not found in your workspace.");
            var intake = new LeadIntake
            {
                WorkspaceId = workspaceId, IdempotencyKeyHash = keyHash, Source = source,
                SourceDetail = Clean(sourceDetail), ContactName = request.ContactName.Trim(),
                Email = Clean(request.Email), Phone = NormalizePhone(request.Phone), CompanyName = Clean(request.CompanyName),
                Message = Clean(request.Message), MatchedContactId = contactId
            };
            db.LeadIntakes.Add(intake);
            db.AuditLogs.Add(CreateAudit(workspaceId, crm.Actor?.Id, "lead.intake.received", "LeadIntake", intake.Id,
                new { Source = intake.Source, ReceivedAtUtc = intake.ReceivedAtUtc }));
            await db.SaveChangesAsync(cancellationToken);
            await ProcessIntakeUnderLockAsync(intake, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            var result = new IntakeQueuedResponse(intake.Id, intake.Status.ToString(), intake.LeadId, intake.ReceivedAtUtc, intake.LastError);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }
    }

    public async Task ProcessPendingIntakesAsync(CancellationToken cancellationToken)
    {
        var pending = await db.LeadIntakes.AsNoTracking().Where(x => x.Status == LeadIntakeStatus.WaitingForOwner)
            .OrderBy(x => x.ReceivedAtUtc).Select(x => x.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var id in pending)
        {
            try { await ProcessIntakeAsync(id, cancellationToken); }
            catch (Exception exception) { logger.LogWarning(exception, "Lead intake {IntakeId} will be retried.", id); }
        }
    }

    public async Task<IntakeQueuedResponse> RetryIntakeAsync(Guid intakeId, Guid? contactId, byte[]? version,
        CancellationToken cancellationToken)
    {
        var intake = await db.LeadIntakes.SingleOrDefaultAsync(x => x.Id == intakeId && x.WorkspaceId == crm.Actor.WorkspaceId)
            ?? throw new CrmException(404, "The intake was not found in your workspace.");
        if (version is null || !intake.RowVersion.SequenceEqual(version)) throw new CrmException(409, "The intake changed. Reload it before retrying.");
        if (intake.Status == LeadIntakeStatus.Assigned) return ToResponse(intake);
        if (contactId.HasValue && !await db.Contacts.AnyAsync(x => x.Id == contactId && x.WorkspaceId == crm.Actor.WorkspaceId && !x.IsDeleted))
            throw new CrmException(404, "The contact was not found in your workspace.");
        intake.MatchedContactId = contactId;
        intake.Status = LeadIntakeStatus.WaitingForOwner;
        intake.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        // The durable background worker retries the intake after this transaction commits.
        return ToResponse(intake);
    }

    private async Task ProcessIntakeAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var preliminary = await db.LeadIntakes.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.WorkspaceId }).SingleOrDefaultAsync(cancellationToken);
            if (preliminary is null) return;
            await LockAsync($"lead-intake:{preliminary.WorkspaceId:N}:{id:N}", cancellationToken);
            var intake = await db.LeadIntakes.SingleAsync(x => x.Id == id && x.WorkspaceId == preliminary.WorkspaceId, cancellationToken);
            if (intake.Status != LeadIntakeStatus.WaitingForOwner) { await transaction.CommitAsync(cancellationToken); return; }
            await ProcessIntakeUnderLockAsync(intake, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch { await transaction.RollbackAsync(cancellationToken); throw; }
    }

    private async Task ProcessIntakeUnderLockAsync(LeadIntake intake, CancellationToken cancellationToken)
    {
        var config = await GetConfigurationAsync(intake.WorkspaceId, cancellationToken);
        intake.LastAttemptAtUtc = DateTime.UtcNow;
        intake.AttemptCount++;
        var owner = await ChooseOwnerAsync(intake.WorkspaceId, config, [], cancellationToken);
        if (owner is null)
        {
            intake.LastError = "No available employee is under the open-lead limit. Intake is queued and will retry.";
            return;
        }
        Contact? contact = null;
        if (intake.MatchedContactId.HasValue)
            contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == intake.MatchedContactId && x.WorkspaceId == intake.WorkspaceId && !x.IsDeleted, cancellationToken);
        else
        {
            var matches = await db.Contacts.Where(x => x.WorkspaceId == intake.WorkspaceId && !x.IsDeleted &&
                ((intake.Email != null && x.Email == intake.Email) || (intake.Phone != null && x.Phone == intake.Phone)))
                .Take(3).ToListAsync(cancellationToken);
            if (matches.Count > 1)
            {
                intake.Status = LeadIntakeStatus.NeedsContactReview;
                intake.LastError = "The email and phone match different contacts. Select the right contact, then retry.";
                db.AuditLogs.Add(CreateAudit(intake.WorkspaceId, null, "lead.intake.contact-review", "LeadIntake", intake.Id,
                    new { Reason = "Multiple contact matches" }));
                return;
            }
            contact = matches.SingleOrDefault();
        }
        if (contact is null)
        {
            Guid? companyId = null;
            if (intake.CompanyName is not null)
            {
                var company = await db.Set<Company>().FirstOrDefaultAsync(x => x.WorkspaceId == intake.WorkspaceId && !x.IsDeleted && x.Name == intake.CompanyName, cancellationToken);
                if (company is null)
                {
                    company = new Company { WorkspaceId = intake.WorkspaceId, Name = intake.CompanyName };
                    db.Set<Company>().Add(company);
                    db.AuditLogs.Add(CreateAudit(intake.WorkspaceId, null, "crm.company.created", "Company", company.Id, new { company.Name }));
                }
                companyId = company.Id;
            }
            contact = new Contact { WorkspaceId = intake.WorkspaceId, DisplayName = intake.ContactName,
                Email = intake.Email, Phone = intake.Phone, CompanyId = companyId, OwnerUserId = owner.Id };
            db.Contacts.Add(contact);
            db.AuditLogs.Add(CreateAudit(intake.WorkspaceId, null, "crm.contact.created", "Contact", contact.Id,
                new { contact.DisplayName, contact.Email, contact.Phone, contact.CompanyId }));
        }
        var now = DateTime.UtcNow;
        var lead = new Lead { WorkspaceId = intake.WorkspaceId, ContactId = contact.Id, OwnerUserId = owner.Id,
            IntakeId = intake.Id, Source = intake.Source, SourceDetail = intake.SourceDetail,
            AssignedAtUtc = now, AcknowledgementDeadlineUtc = now.AddSeconds(config.AcknowledgementWindowSeconds),
            NextEscalationAtUtc = now.AddSeconds(config.AcknowledgementWindowSeconds), LastActionAtUtc = now };
        db.Leads.Add(lead);
        config.RoundRobinLastUserId = owner.Id;
        intake.Status = LeadIntakeStatus.Assigned;
        intake.LeadId = lead.Id;
        intake.LastError = null;
        db.LeadAssignmentHistory.Add(new LeadAssignmentHistory { WorkspaceId = intake.WorkspaceId, LeadId = lead.Id,
            PreviousOwnerUserId = null, NewOwnerUserId = owner.Id, ActorType = "System", Reason = "InitialAssignment",
            AssignedAtUtc = now, AcknowledgementDeadlineUtc = lead.AcknowledgementDeadlineUtc });
        db.Set<LeadUserNotification>().Add(new LeadUserNotification { WorkspaceId = intake.WorkspaceId,
            LeadId = lead.Id, RecipientUserId = owner.Id, Title = "New lead assigned",
            Message = $"{intake.ContactName} ({intake.Source}) needs a response by {lead.AcknowledgementDeadlineUtc:O}." });
        db.AuditLogs.Add(CreateAudit(intake.WorkspaceId, null, "lead.assigned", "Lead", lead.Id,
            new { OwnerUserId = owner.Id, lead.Source, lead.AcknowledgementDeadlineUtc }));
        db.Set<CrmActivity>().Add(new CrmActivity { WorkspaceId = intake.WorkspaceId, EntityType = CrmEntityType.Contact,
            RecordId = contact.Id, ActorUserId = null, EventType = "lead.assigned", Summary = "A new lead was captured and assigned." });
    }

    public async Task<LeadAssignmentConfiguration> GetConfigurationAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        await db.LeadAssignmentConfigurations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken)
        ?? throw new CrmException(409, "Lead assignment configuration has not been initialized for this workspace.");

    public async Task<EmployeeAvailability> SetAvailabilityAsync(Guid targetUserId, EmployeeAvailabilityStatus status,
        byte[]? version, bool management, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(status)) throw new CrmException(400, "Availability status is invalid.");
        if (!management && targetUserId != crm.Actor.Id) throw new CrmException(403, "You can only change your own availability.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockAsync($"lead-workspace:{crm.Actor.WorkspaceId:N}", cancellationToken);
        var record = await db.EmployeeAvailabilities.SingleOrDefaultAsync(x => x.WorkspaceId == crm.Actor.WorkspaceId && x.UserId == targetUserId, cancellationToken)
            ?? throw new CrmException(404, "Employee availability was not found in your workspace.");
        if (version is null || !record.RowVersion.SequenceEqual(version)) throw new CrmException(409, "Availability changed. Reload it before saving.");
        var old = record.Status;
        record.Status = status;
        record.ChangedAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(CreateAudit(crm.Actor.WorkspaceId, crm.Actor.Id, "lead.availability.changed", "EmployeeAvailability", targetUserId,
            new { Before = old, After = status }));
        if (old == EmployeeAvailabilityStatus.Available && status != EmployeeAvailabilityStatus.Available)
            await ReassignUnacknowledgedAsync(targetUserId, "OwnerBecameUnavailable", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return record;
    }

    public async Task UpdateConfigurationAsync(AssignmentSettingsRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = crm.Actor.WorkspaceId;
        await LockAsync($"lead-workspace:{workspaceId:N}", cancellationToken);
        var config = await GetConfigurationAsync(workspaceId, cancellationToken);
        if (request.RowVersion is null || !config.RowVersion.SequenceEqual(request.RowVersion))
            throw new CrmException(409, "Assignment settings changed. Reload them before saving.");
        var levels = request.EscalationUserIds.Distinct().ToArray();
        if (levels.Length != request.EscalationUserIds.Count) throw new CrmException(400, "An escalation employee can appear only once.");
        if (request.EscalationIntervalsSeconds.Count != request.EscalationUserIds.Count + 1)
            throw new CrmException(400, "Set one interval for each escalation level and one final retry interval.");
        if (request.EscalationIntervalsSeconds.Any(x => x < 1 || x > 86400))
            throw new CrmException(400, "Each escalation delay must be between 1 and 86,400 seconds.");
        foreach (var userId in levels)
            if (!await db.Users.AnyAsync(x => x.Id == userId && x.WorkspaceId == workspaceId && x.IsActive, cancellationToken))
                throw new CrmException(400, "Every backup or manager must be an active user in this workspace.");
        config.MaxOpenLeadsPerEmployee = request.MaxOpenLeadsPerEmployee;
        config.AcknowledgementWindowSeconds = request.AcknowledgementWindowSeconds;
        config.IdleWindowSeconds = request.IdleWindowSeconds;
        config.RetryIntervalSeconds = request.RetryIntervalSeconds;
        config.EscalationUserIdsJson = JsonSerializer.Serialize(levels);
        config.EscalationIntervalsSecondsJson = JsonSerializer.Serialize(request.EscalationIntervalsSeconds);
        await db.SaveChangesAsync(cancellationToken);
        await db.AuditLogs.AddAsync(CreateAudit(workspaceId, crm.Actor.Id, "lead.assignment-settings.updated", "LeadAssignmentConfiguration", workspaceId,
            new { config.MaxOpenLeadsPerEmployee, config.AcknowledgementWindowSeconds, config.IdleWindowSeconds,
                EscalationUserIds = levels, EscalationIntervalsSeconds = request.EscalationIntervalsSeconds }), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ProcessDueLeadsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var due = await db.Leads.AsNoTracking().Where(x => !x.IsDeleted && !x.IsClosed && x.NextEscalationAtUtc <= now)
            .OrderBy(x => x.NextEscalationAtUtc).Select(x => new { x.Id, x.WorkspaceId }).Take(100).ToListAsync(cancellationToken);
        foreach (var item in due)
        {
            try { await ProcessDueLeadAsync(item.WorkspaceId, item.Id, now, cancellationToken); }
            catch (Exception exception) { logger.LogWarning(exception, "Lead {LeadId} escalation will be retried.", item.Id); }
        }
    }

    private async Task ProcessDueLeadAsync(Guid workspaceId, Guid leadId, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await LockAsync($"lead-workspace:{workspaceId:N}", cancellationToken);
        var lead = await db.Leads.SingleOrDefaultAsync(x => x.Id == leadId && x.WorkspaceId == workspaceId && !x.IsDeleted, cancellationToken);
        if (lead is null || lead.IsClosed || !lead.NextEscalationAtUtc.HasValue || lead.NextEscalationAtUtc > now)
        { await transaction.CommitAsync(cancellationToken); return; }
        if (lead.AcknowledgedAtUtc.HasValue)
        { lead.NextEscalationAtUtc = null; await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return; }
        var config = await GetConfigurationAsync(workspaceId, cancellationToken);
        var delays = JsonSerializer.Deserialize<List<int>>(config.EscalationIntervalsSecondsJson)!;
        var historyOwners = await db.LeadAssignmentHistory.Where(x => x.WorkspaceId == workspaceId && x.LeadId == leadId)
            .Select(x => x.NewOwnerUserId).Distinct().ToListAsync(cancellationToken);
        var next = await ChooseOwnerAsync(workspaceId, config, historyOwners, cancellationToken);
        if (next is null)
        {
            lead.AttentionReason = "Acknowledgement expired; no available employee has capacity. Retry is queued.";
            lead.NextEscalationAtUtc = now.AddSeconds(config.RetryIntervalSeconds);
            db.AuditLogs.Add(CreateAudit(workspaceId, null, "lead.escalation.waiting-for-owner", "Lead", lead.Id,
                new { lead.OwnerUserId, Reason = lead.AttentionReason }));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        var escalationDelay = delays[Math.Min(lead.EscalationCount, delays.Count - 1)];
        await AssignAsync(lead, next.Id, "AcknowledgementExpired", null, now, config, cancellationToken, escalationDelay);
        lead.EscalationCount++;
        lead.AttentionReason = "Lead escalated after the acknowledgement window expired.";
        var targetEmail = next.NotificationEmail ?? next.Email;
        if (!string.IsNullOrWhiteSpace(targetEmail))
            await db.LeadNotifications.AddAsync(new LeadNotification { WorkspaceId = workspaceId, LeadId = lead.Id,
                RecipientUserId = next.Id, RecipientEmail = targetEmail,
                Subject = "Escalated lead requires your response",
                BodyText = BuildEscalationEmailBody(lead, configuration["Frontend:BaseUrl"] ?? "http://localhost:4200", now) }, cancellationToken);
        db.AuditLogs.Add(CreateAudit(workspaceId, null, "lead.escalated", "Lead", lead.Id,
            new { PreviousOwnerUserId = historyOwners.LastOrDefault(), NewOwnerUserId = next.Id, lead.EscalationCount, lead.AcknowledgementDeadlineUtc }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReassignUnacknowledgedAsync(Guid unavailableUserId, string reason, CancellationToken cancellationToken)
    {
        var leads = await db.Leads.Where(x => x.WorkspaceId == crm.Actor.WorkspaceId && x.OwnerUserId == unavailableUserId &&
            !x.IsDeleted && !x.IsClosed && !x.AcknowledgedAtUtc.HasValue).OrderBy(x => x.AssignedAtUtc).Take(500).ToListAsync(cancellationToken);
        var config = await GetConfigurationAsync(crm.Actor.WorkspaceId, cancellationToken);
        foreach (var lead in leads)
        {
            var owners = await db.LeadAssignmentHistory.Where(x => x.WorkspaceId == crm.Actor.WorkspaceId && x.LeadId == lead.Id)
                .Select(x => x.NewOwnerUserId).Distinct().ToListAsync(cancellationToken);
            owners.Add(unavailableUserId);
            var next = await ChooseOwnerAsync(crm.Actor.WorkspaceId, config, owners, cancellationToken);
            if (next is null)
            {
                lead.AttentionReason = "The owner is unavailable; reassignment is waiting for an eligible employee.";
                lead.NextEscalationAtUtc = DateTime.UtcNow.AddSeconds(config.RetryIntervalSeconds);
                db.AuditLogs.Add(CreateAudit(crm.Actor.WorkspaceId, crm.Actor.Id, "lead.reassignment.waiting-for-owner", "Lead", lead.Id,
                    new { lead.OwnerUserId, Reason = lead.AttentionReason }));
                continue;
            }
            await AssignAsync(lead, next.Id, reason, crm.Actor.Id, DateTime.UtcNow, config, cancellationToken);
            db.AuditLogs.Add(CreateAudit(crm.Actor.WorkspaceId, crm.Actor.Id, "lead.reassigned", "Lead", lead.Id,
                new { OwnerUserId = next.Id, Reason = reason }));
        }
    }

    public async Task ReassignAsync(Guid leadId, Guid newOwnerId, string reason, byte[]? version, CancellationToken cancellationToken)
    {
        await LockAsync($"lead-workspace:{crm.Actor.WorkspaceId:N}", cancellationToken);
        var lead = await db.Leads.SingleOrDefaultAsync(x => x.Id == leadId && x.WorkspaceId == crm.Actor.WorkspaceId && !x.IsDeleted && !x.IsClosed, cancellationToken)
            ?? throw new CrmException(404, "An open lead with that ID was not found in your workspace.");
        CrmService.CheckVersion(lead, version);
        if (!await IsEligibleAsync(crm.Actor.WorkspaceId, newOwnerId, lead.Id, cancellationToken))
            throw new CrmException(400, "The new owner must be Available and below the open-lead limit.");
        if (newOwnerId == lead.OwnerUserId) throw new CrmException(400, "Choose a different lead owner.");
        var config = await GetConfigurationAsync(crm.Actor.WorkspaceId, cancellationToken);
        await AssignAsync(lead, newOwnerId, $"Manual: {reason.Trim()}", crm.Actor.Id, DateTime.UtcNow, config, cancellationToken);
        await db.AuditLogs.AddAsync(CreateAudit(crm.Actor.WorkspaceId, crm.Actor.Id, "lead.reassigned", "Lead", lead.Id,
            new { OwnerUserId = newOwnerId, Reason = reason.Trim() }), cancellationToken);
    }

    public async Task<Lead> AcknowledgeAsync(Guid leadId, byte[]? version, CancellationToken cancellationToken)
    {
        await LockAsync($"lead-workspace:{crm.Actor.WorkspaceId:N}", cancellationToken);
        var lead = await db.Leads.SingleOrDefaultAsync(x => x.Id == leadId && x.WorkspaceId == crm.Actor.WorkspaceId && !x.IsDeleted && !x.IsClosed, cancellationToken)
            ?? throw new CrmException(404, "An open lead with that ID was not found in your workspace.");
        if (lead.OwnerUserId != crm.Actor.Id) throw new CrmException(403, "Only the current lead owner can acknowledge it.");
        CrmService.CheckVersion(lead, version);
        if (lead.AcknowledgedAtUtc.HasValue) return lead;
        var now = DateTime.UtcNow;
        lead.AcknowledgedAtUtc = now;
        lead.LastActionAtUtc = now;
        lead.NextEscalationAtUtc = null;
        lead.AttentionReason = null;
        db.AuditLogs.Add(CreateAudit(crm.Actor.WorkspaceId, crm.Actor.Id, "lead.acknowledged", "Lead", lead.Id, new { AtUtc = now }));
        db.Set<CrmActivity>().Add(new CrmActivity { WorkspaceId = crm.Actor.WorkspaceId, EntityType = CrmEntityType.Contact,
            RecordId = lead.ContactId, ActorUserId = crm.Actor.Id, EventType = "lead.acknowledged", Summary = "Lead acknowledged." });
        await db.SaveChangesAsync(cancellationToken);
        return lead;
    }

    public async Task CloseAsync(Guid leadId, byte[]? version, CancellationToken cancellationToken)
    {
        var lead = await db.Leads.SingleOrDefaultAsync(x => x.Id == leadId && x.WorkspaceId == crm.Actor.WorkspaceId && !x.IsDeleted && !x.IsClosed, cancellationToken)
            ?? throw new CrmException(404, "An open lead with that ID was not found in your workspace.");
        CrmService.CheckVersion(lead, version);
        if (lead.OwnerUserId != crm.Actor.Id && !crm.Can(PermissionCatalog.LeadsReassign))
            throw new CrmException(403, "Only the lead owner or an authorized manager can close it.");
        lead.IsClosed = true;
        lead.ClosedAtUtc = DateTime.UtcNow;
        lead.NextEscalationAtUtc = null;
        lead.LastActionAtUtc = DateTime.UtcNow;
        db.AuditLogs.Add(CreateAudit(crm.Actor.WorkspaceId, crm.Actor.Id, "lead.closed", "Lead", lead.Id, new { lead.ClosedAtUtc }));
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AssignAsync(Lead lead, Guid newOwnerId, string reason, Guid? actorId, DateTime now,
        LeadAssignmentConfiguration config, CancellationToken cancellationToken, int? acknowledgementWindowSeconds = null)
    {
        var previous = lead.OwnerUserId;
        lead.OwnerUserId = newOwnerId;
        config.RoundRobinLastUserId = newOwnerId;
        lead.AssignedAtUtc = now;
        lead.AcknowledgementDeadlineUtc = now.AddSeconds(acknowledgementWindowSeconds ?? config.AcknowledgementWindowSeconds);
        lead.AcknowledgedAtUtc = null;
        lead.NextEscalationAtUtc = lead.AcknowledgementDeadlineUtc;
        lead.LastActionAtUtc = now;
        lead.AttentionReason = null;
        db.LeadAssignmentHistory.Add(new LeadAssignmentHistory { WorkspaceId = lead.WorkspaceId, LeadId = lead.Id,
            PreviousOwnerUserId = previous, NewOwnerUserId = newOwnerId, ActorUserId = actorId,
            ActorWorkspaceId = actorId.HasValue ? lead.WorkspaceId : null, ActorType = actorId.HasValue ? "User" : "System",
            Reason = reason, AssignedAtUtc = now, AcknowledgementDeadlineUtc = lead.AcknowledgementDeadlineUtc });
        db.LeadUserNotifications.Add(new LeadUserNotification { WorkspaceId = lead.WorkspaceId, LeadId = lead.Id,
            RecipientUserId = newOwnerId, Title = "Lead assigned to you",
            Message = $"The lead requires a response by {lead.AcknowledgementDeadlineUtc:O}." });
        db.Set<CrmActivity>().Add(new CrmActivity { WorkspaceId = lead.WorkspaceId, EntityType = CrmEntityType.Contact,
            RecordId = lead.ContactId, ActorUserId = actorId, EventType = "lead.assignment.changed",
            Summary = $"Lead assignment changed. Reason: {reason}." });
    }

    private async Task<ApplicationUser?> ChooseOwnerAsync(Guid workspaceId, LeadAssignmentConfiguration config,
        IReadOnlyCollection<Guid> exclude, CancellationToken cancellationToken)
    {
        var candidates = await (from availability in db.EmployeeAvailabilities.AsNoTracking()
            join user in db.Users.AsNoTracking() on new { availability.UserId, availability.WorkspaceId } equals new { UserId = user.Id, user.WorkspaceId }
            where availability.WorkspaceId == workspaceId && availability.Status == EmployeeAvailabilityStatus.Available && user.IsActive
            orderby user.Id select user).ToListAsync(cancellationToken);
        var openCounts = await db.Leads.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && !x.IsDeleted && !x.IsClosed)
            .GroupBy(x => x.OwnerUserId).Select(group => new { OwnerId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Count, cancellationToken);
        var ids = JsonSerializer.Deserialize<List<Guid>>(config.EscalationUserIdsJson)!.ToHashSet();
        var eligible = candidates.Where(x => !exclude.Contains(x.Id) && openCounts.GetValueOrDefault(x.Id) < config.MaxOpenLeadsPerEmployee).ToList();
        var ordinary = eligible.Where(x => !ids.Contains(x.Id)).ToList();
        if (ordinary.Count > 0)
        {
            var after = ordinary.Where(x => config.RoundRobinLastUserId.HasValue && x.Id.CompareTo(config.RoundRobinLastUserId.Value) > 0).ToList();
            return after.FirstOrDefault() ?? ordinary.First();
        }
        var fallbackOrder = JsonSerializer.Deserialize<List<Guid>>(config.EscalationUserIdsJson)!;
        return fallbackOrder.Select(id => eligible.FirstOrDefault(x => x.Id == id)).FirstOrDefault(x => x is not null);
    }

    private async Task<bool> IsEligibleAsync(Guid workspaceId, Guid userId, Guid leadId, CancellationToken cancellationToken)
    {
        var status = await db.EmployeeAvailabilities.Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
            .Select(x => (EmployeeAvailabilityStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
        if (status != EmployeeAvailabilityStatus.Available) return false;
        var settings = await GetConfigurationAsync(workspaceId, cancellationToken);
        var count = await db.Leads.CountAsync(x => x.WorkspaceId == workspaceId && x.OwnerUserId == userId && !x.IsDeleted && !x.IsClosed && x.Id != leadId, cancellationToken);
        return count < settings.MaxOpenLeadsPerEmployee;
    }

    private async Task LockAsync(string resource, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 51000, 'Could not acquire the lead workflow lock.', 1;", cancellationToken);
    }

    public static AuditLogEntry CreateAudit(Guid workspaceId, Guid? actorId, string eventType, string targetType,
        Guid targetId, object after) => new()
    {
        WorkspaceId = workspaceId, ActorUserId = actorId, ActorWorkspaceId = actorId.HasValue ? workspaceId : null,
        ActorType = actorId.HasValue ? "User" : "System", EventType = eventType, TargetType = targetType,
        TargetId = targetId.ToString(), AfterJson = JsonSerializer.Serialize(after)
    };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildEscalationEmailBody(Lead lead, string frontendBaseUrl, DateTime now)
    {
        var baseUrl = frontendBaseUrl.TrimEnd('/');
        var pakistanTime = TimeZoneInfo.ConvertTimeFromUtc(lead.AcknowledgementDeadlineUtc, TimeZoneInfo.FindSystemTimeZoneById("Asia/Karachi"));
        var reason = string.IsNullOrWhiteSpace(lead.AttentionReason) ? "Lead requires a timely response." : lead.AttentionReason;
        return $"A {lead.Source} lead is now assigned to you.\n" +
            $"Attention reason: {reason}\n" +
            $"Acknowledgement deadline (Pakistan time): {pakistanTime:yyyy-MM-dd HH:mm} PKT\n" +
            $"Open lead: {baseUrl}/?lead={lead.Id:D}\n" +
            $"Notification created at: {now:O}";
    }
    private static string? NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value) ? null : new string(value.Trim().Where(c => !char.IsWhiteSpace(c)).ToArray());
    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length is < 8 or > 160 || key.Any(c => !(char.IsLetterOrDigit(c) || "._:-".Contains(c))))
            throw new CrmException(400, "Idempotency-Key must be 8 to 160 letters, numbers, dots, colons, underscores or dashes.");
    }
    public static IntakeQueuedResponse ToResponse(LeadIntake x) => new(x.Id, x.Status.ToString(), x.LeadId, x.ReceivedAtUtc, x.LastError);
}
