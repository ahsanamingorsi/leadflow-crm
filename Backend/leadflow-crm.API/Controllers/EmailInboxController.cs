using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/email/conversations")]
public sealed class EmailInboxController(
    LeadflowDbContext db,
    CrmService crm,
    WorkspaceIntegrationService integrations,
    WorkspaceEmailSender emailSender) : IntegrationApiControllerBase(crm)
{
    [HttpGet, Authorize(Policy = PermissionCatalog.EmailInboxView)]
    public async Task<ActionResult<PageResult<EmailConversationResponse>>> List([FromQuery] PageQuery request,
        CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var query = db.EmailConversations.AsNoTracking().Where(x => x.WorkspaceId == workspaceId);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();
            query = query.Where(x => x.Subject.Contains(term) || x.ParticipantEmail.Contains(term));
        }
        var count = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.LastMessageAtUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(x => x.Id).ToArray();
        var messages = await db.EmailMessages.AsNoTracking().Where(x => x.WorkspaceId == workspaceId && ids.Contains(x.ConversationId))
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        return Ok(new PageResult<EmailConversationResponse>(rows.Select(row => ToResponse(row,
            messages.Where(x => x.ConversationId == row.Id).ToArray())).ToArray(), count, request.Page, request.PageSize));
    }

    [HttpGet("{id:guid}"), Authorize(Policy = PermissionCatalog.EmailInboxView)]
    public async Task<ActionResult<EmailConversationResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var row = await db.EmailConversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, cancellationToken)
            ?? throw new CrmException(404, "The email conversation was not found in this workspace.");
        var messages = await db.EmailMessages.AsNoTracking().Where(x => x.ConversationId == id && x.WorkspaceId == workspaceId)
            .OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        return Ok(ToResponse(row, messages));
    }

    [HttpPost("{id:guid}/reply"), Authorize(Policy = PermissionCatalog.EmailInboxReply)]
    public async Task<ActionResult<EmailMessageResponse>> Reply(Guid id, EmailReplyRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var conversation = await db.EmailConversations.SingleOrDefaultAsync(x => x.Id == id && x.WorkspaceId == workspaceId, cancellationToken)
            ?? throw new CrmException(404, "The email conversation was not found in this workspace.");
        if (conversation.ContactId.HasValue && await db.Contacts.AnyAsync(x => x.Id == conversation.ContactId &&
                x.WorkspaceId == workspaceId && x.OptedOut && !x.IsDeleted, cancellationToken))
            throw new CrmException(409, "This contact has opted out of email communication.");
        var integration = await integrations.FindAsync(workspaceId, "email", cancellationToken);
        var settings = await integrations.GetEmailSettingsAsync(workspaceId, cancellationToken);
        if (settings is null || integration?.Status is nameof(IntegrationStatus.Disabled))
            throw new CrmException(409, "Workspace email is not connected. Configure and test it before replying.");

        var previous = await db.EmailMessages.AsNoTracking().Where(x => x.ConversationId == id && x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        var parentMessageId = previous?.MessageId ?? previous?.ProviderMessageId;
        var refs = string.Join(' ', new[] { previous?.References, parentMessageId }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (refs.Length > 2000) refs = refs[^2000..];
        var subject = conversation.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? conversation.Subject : $"Re: {conversation.Subject}";
        var messageId = WorkspaceEmailSender.CreateMessageId(settings.FromAddress);
        var message = new EmailMessage
        {
            WorkspaceId = workspaceId, ConversationId = id, Direction = EmailMessageDirection.Outbound,
            Status = EmailDeliveryStatus.Pending, FromAddress = settings.FromAddress,
            ToAddress = conversation.ParticipantEmail, Subject = subject, BodyText = request.BodyText.Trim(),
            MessageId = messageId, InReplyTo = parentMessageId, References = refs.Length == 0 ? null : refs,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.EmailMessages.Add(message);
        conversation.LastMessageAtUtc = message.CreatedAtUtc;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAsync(workspaceId, message.ToAddress, message.Subject, message.BodyText,
                message.InReplyTo, message.References, message.MessageId, cancellationToken);
            message.Status = EmailDeliveryStatus.Sent;
            message.DeliveredAtUtc = DateTime.UtcNow;
            message.LastError = null;
            await integrations.MarkStatusAsync(workspaceId, "email", IntegrationStatus.Connected, cancellationToken: cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "email", EventType = "outbound.sent",
                ExternalEventId = message.Id.ToString("N"), Status = "Succeeded", Summary = $"Email reply sent to {message.ToAddress}."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            message.Status = EmailDeliveryStatus.Failed;
            message.LastError = WorkspaceIntegrationService.SafeError(exception.Message);
            await integrations.MarkStatusAsync(workspaceId, "email", IntegrationStatus.Error, message.LastError, cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "email", EventType = "outbound.failed",
                ExternalEventId = message.Id.ToString("N"), Status = "Failed", Summary = "Email reply delivery failed."
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(message));
    }

    [HttpPost("{conversationId:guid}/messages/{messageId:guid}/retry"), Authorize(Policy = PermissionCatalog.EmailInboxReply)]
    public async Task<ActionResult<EmailMessageResponse>> Retry(Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var conversation = await db.EmailConversations.SingleOrDefaultAsync(x => x.Id == conversationId && x.WorkspaceId == workspaceId, cancellationToken)
            ?? throw new CrmException(404, "The email conversation was not found in this workspace.");
        var message = await db.EmailMessages.SingleOrDefaultAsync(x => x.Id == messageId && x.ConversationId == conversationId &&
            x.WorkspaceId == workspaceId, cancellationToken)
            ?? throw new CrmException(404, "The email message was not found in this workspace.");
        if (message.Direction != EmailMessageDirection.Outbound || message.Status is not (EmailDeliveryStatus.Failed or EmailDeliveryStatus.Pending))
            throw new CrmException(409, "Only failed or interrupted outbound emails can be retried.");
        if (conversation.ContactId.HasValue && await db.Contacts.AnyAsync(x => x.Id == conversation.ContactId &&
                x.WorkspaceId == workspaceId && x.OptedOut && !x.IsDeleted, cancellationToken))
            throw new CrmException(409, "This contact has opted out of email communication.");
        var integration = await integrations.FindAsync(workspaceId, "email", cancellationToken);
        var settings = await integrations.GetEmailSettingsAsync(workspaceId, cancellationToken);
        if (settings is null || integration?.Status is nameof(IntegrationStatus.Disabled))
            throw new CrmException(409, "Workspace email is not connected. Configure and test it before retrying.");
        message.Status = EmailDeliveryStatus.Pending;
        message.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            await emailSender.SendAsync(workspaceId, message.ToAddress, message.Subject, message.BodyText,
                message.InReplyTo, message.References, message.MessageId, cancellationToken);
            message.Status = EmailDeliveryStatus.Sent;
            message.DeliveredAtUtc = DateTime.UtcNow;
            await integrations.MarkStatusAsync(workspaceId, "email", IntegrationStatus.Connected, cancellationToken: cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "email", EventType = "outbound.retry-sent",
                ExternalEventId = $"{message.Id:N}:{Guid.NewGuid():N}", Status = "Succeeded", Summary = $"Retried email reply sent to {message.ToAddress}."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            message.Status = EmailDeliveryStatus.Failed;
            message.LastError = WorkspaceIntegrationService.SafeError(exception.Message);
            await integrations.MarkStatusAsync(workspaceId, "email", IntegrationStatus.Error, message.LastError, cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "email", EventType = "outbound.retry-failed",
                ExternalEventId = $"{message.Id:N}:{Guid.NewGuid():N}", Status = "Failed", Summary = "Email reply retry failed."
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(message));
    }

    private static EmailConversationResponse ToResponse(EmailConversation row, IReadOnlyList<EmailMessage> messages) =>
        new(row.Id, row.ContactId, row.LeadId, row.ParticipantEmail, row.Subject, row.LastMessageAtUtc, messages.Select(ToResponse).ToArray());

    private static EmailMessageResponse ToResponse(EmailMessage row) => new(row.Id, row.Direction, row.Status,
        row.FromAddress, row.ToAddress, row.Subject, row.BodyText, row.MessageId, row.InReplyTo,
        row.CreatedAtUtc, row.DeliveredAtUtc, row.LastError);
}

[ApiController]
[AllowAnonymous]
[Route("api/integrations/email/{workspaceId:guid}/inbound")]
public sealed class InboundEmailWebhookController(
    LeadflowDbContext db,
    WorkspaceIntegrationService integrations,
    LeadWorkflowService workflow,
    ILogger<InboundEmailWebhookController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost, RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> Receive(Guid workspaceId, CancellationToken cancellationToken)
    {
        var settings = await integrations.GetEmailSettingsAsync(workspaceId, cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.InboundWebhookSecret))
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Inbound email is not configured." });
        if (!await db.Workspaces.AnyAsync(x => x.Id == workspaceId && x.IsActive, cancellationToken)) return NotFound();

        using var reader = new StreamReader(Request.Body, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        if (raw.Length > 1_000_000) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        var timestamp = Request.Headers["X-Leadflow-Timestamp"].ToString();
        var signature = Request.Headers["X-Leadflow-Signature"].ToString();
        if (!EmailWebhookSignature.IsValid(raw, timestamp, signature, settings.InboundWebhookSecret, DateTime.UtcNow))
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Inbound email webhook signature is invalid or expired." });

        InboundEmailWebhookRequest? request;
        try { request = JsonSerializer.Deserialize<InboundEmailWebhookRequest>(raw, JsonOptions); }
        catch (JsonException) { return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid inbound email payload." }); }
        if (request is null) return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid inbound email payload." });
        var validation = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validation, validateAllProperties: true))
            return BadRequest(new ValidationProblemDetails(validation.GroupBy(x => x.MemberNames.FirstOrDefault() ?? "payload")
                .ToDictionary(x => x.Key, x => x.Select(y => y.ErrorMessage ?? "Invalid value.").ToArray())));
        if (new[] { request.MessageId, request.ThreadId, request.Subject, request.InReplyTo, request.References }
            .Any(value => value?.Any(char.IsControl) == true))
            return BadRequest(new ProblemDetails { Status = 400, Title = "Email headers must not contain control characters." });

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var messageHash = LeadWorkflowService.Hash(request.MessageId.Trim());
        var prior = await db.IntegrationSyncEvents.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
            x.Provider == "email" && x.ExternalEventId == messageHash, cancellationToken);
        if (prior is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Ok(new { accepted = true, duplicate = true });
        }

        try
        {
            var sender = request.FromAddress.Trim().ToLowerInvariant();
            var recipient = request.ToAddress.Trim().ToLowerInvariant();
            var contact = await db.Contacts.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && !x.IsDeleted &&
                x.Email != null && x.Email.ToLower() == sender, cancellationToken);
            Guid? leadId = null;
            if (contact is null)
            {
                var displayName = sender[..sender.IndexOf('@')].Replace('.', ' ').Replace('_', ' ').Trim();
                if (displayName.Length < 2) displayName = sender;
                var intake = await workflow.CreateIntegrationIntakeAsync(workspaceId,
                    $"email:{LeadWorkflowService.Hash(request.MessageId)}",
                    new LeadIntakeRequest { ContactName = displayName, Email = sender, Message = request.TextBody,
                        SourceDetail = "Inbound email" }, "Email", "Inbound email", cancellationToken);
                var intakeRecord = await db.LeadIntakes.SingleAsync(x => x.Id == intake.IntakeId && x.WorkspaceId == workspaceId, cancellationToken);
                if (intakeRecord.MatchedContactId.HasValue)
                    contact = await db.Contacts.SingleAsync(x => x.Id == intakeRecord.MatchedContactId && x.WorkspaceId == workspaceId, cancellationToken);
                leadId = intake.LeadId;
            }
            if (contact is not null && !leadId.HasValue)
                leadId = await db.Leads.Where(x => x.WorkspaceId == workspaceId && x.ContactId == contact.Id && !x.IsDeleted)
                    .OrderByDescending(x => x.CreatedAtUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);

            var threadId = request.ThreadId.Trim();
            var conversation = await db.EmailConversations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
                x.ExternalThreadId == threadId, cancellationToken);
            if (conversation is null && !string.IsNullOrWhiteSpace(request.InReplyTo))
            {
                var parent = await db.EmailMessages.Where(x => x.WorkspaceId == workspaceId && x.MessageId == request.InReplyTo)
                    .Select(x => (Guid?)x.ConversationId).FirstOrDefaultAsync(cancellationToken);
                if (parent.HasValue) conversation = await db.EmailConversations.SingleOrDefaultAsync(x => x.Id == parent && x.WorkspaceId == workspaceId, cancellationToken);
            }
            var isNewConversation = conversation is null;
            conversation ??= new EmailConversation
            {
                WorkspaceId = workspaceId, ExternalThreadId = threadId,
                ParticipantEmail = sender, Subject = request.Subject.Trim(), ContactId = contact?.Id,
                LeadId = leadId, CreatedAtUtc = request.ReceivedAt?.UtcDateTime ?? DateTime.UtcNow
            };
            if (isNewConversation) db.EmailConversations.Add(conversation);
            else if (db.Entry(conversation).State == EntityState.Detached) db.EmailConversations.Attach(conversation);
            if (!conversation.ContactId.HasValue) conversation.ContactId = contact?.Id;
            if (!conversation.LeadId.HasValue) conversation.LeadId = leadId;
            var receivedAt = request.ReceivedAt?.UtcDateTime ?? DateTime.UtcNow;
            conversation.LastMessageAtUtc = receivedAt;
            var message = new EmailMessage
            {
                WorkspaceId = workspaceId, ConversationId = conversation.Id,
                Direction = EmailMessageDirection.Inbound, Status = EmailDeliveryStatus.Received,
                FromAddress = sender, ToAddress = recipient, Subject = request.Subject.Trim(), BodyText = request.TextBody,
                MessageId = request.MessageId.Trim(), ProviderMessageId = request.MessageId.Trim(),
                InReplyTo = request.InReplyTo, References = request.References, CreatedAtUtc = receivedAt, DeliveredAtUtc = receivedAt
            };
            db.EmailMessages.Add(message);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "email", EventType = "inbound.received",
                ExternalEventId = messageHash, Status = "Succeeded", Summary = $"Inbound email received from {sender}.", OccurredAtUtc = receivedAt
            });
            await db.SaveChangesAsync(cancellationToken);
            await integrations.MarkStatusAsync(workspaceId, "email", IntegrationStatus.Connected, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(new { accepted = true, duplicate = false, conversationId = conversation.Id, contactId = contact?.Id, leadId });
        }
        catch (DbUpdateException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogWarning(exception, "Inbound email {MessageId} could not be persisted for workspace {WorkspaceId}.", request.MessageId, workspaceId);
            return Conflict(new ProblemDetails { Status = 409, Title = "Inbound email could not be recorded safely." });
        }
    }
}
