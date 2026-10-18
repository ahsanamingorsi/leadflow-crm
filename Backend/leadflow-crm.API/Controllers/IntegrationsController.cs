using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/integrations")]
public sealed class IntegrationsController(
    LeadflowDbContext db,
    CrmService crm,
    WorkspaceIntegrationService integrations,
    WorkspaceEmailSender emailSender,
    StripeCheckoutService stripe,
    IConfiguration configuration) : IntegrationApiControllerBase(crm)
{
    [HttpGet("status"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
    public async Task<ActionResult<IReadOnlyList<IntegrationStatusResponse>>> Status(CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var rows = await db.WorkspaceIntegrations.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).ToListAsync(cancellationToken);
        var connectedUsers = await db.CalendarConnections.AsNoTracking().Where(x => x.WorkspaceId == workspaceId).CountAsync(cancellationToken);
        var latest = await db.IntegrationSyncEvents.AsNoTracking().Where(x => x.WorkspaceId == workspaceId)
            .GroupBy(x => x.Provider).Select(x => new { Provider = x.Key, At = x.Max(e => e.OccurredAtUtc) }).ToListAsync(cancellationToken);
        var responses = new List<IntegrationStatusResponse>();
        foreach (var row in rows)
        {
            var status = Enum.TryParse<IntegrationStatus>(row.Status, out var parsed) ? parsed : IntegrationStatus.Error;
            var error = row.LastError;
            if (row.Provider == "stripe" && !stripe.HasTestConfiguration)
            {
                status = string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]) ? IntegrationStatus.NotConfigured : IntegrationStatus.Error;
                error = "Set both a Stripe sk_test_ key and a whsec_ webhook signing secret.";
            }
            responses.Add(new(row.Provider, status, row.ConnectedAtUtc, latest.FirstOrDefault(x => x.Provider == row.Provider)?.At, error));
        }
        if (!rows.Any(x => x.Provider == "stripe"))
            responses.Add(new("stripe", stripe.HasTestConfiguration ? IntegrationStatus.Configured :
                string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]) ? IntegrationStatus.NotConfigured : IntegrationStatus.Error,
                null, latest.FirstOrDefault(x => x.Provider == "stripe")?.At,
                stripe.HasTestConfiguration ? null : "Set both a Stripe sk_test_ key and a whsec_ webhook signing secret."));
        if (!rows.Any(x => x.Provider == "google-calendar"))
            responses.Add(new("google-calendar", connectedUsers > 0 ? IntegrationStatus.Connected : IntegrationStatus.NotConfigured,
                null, latest.FirstOrDefault(x => x.Provider == "google-calendar")?.At, null));
        return Ok(responses.OrderBy(x => x.Provider).ToArray());
    }

    [HttpGet("email"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
    public async Task<ActionResult<EmailIntegrationResponse>> GetEmail(CancellationToken cancellationToken)
    {
        var row = await integrations.FindAsync(Crm.Actor.WorkspaceId, "email", cancellationToken);
        var settings = await integrations.GetEmailSettingsAsync(Crm.Actor.WorkspaceId, cancellationToken);
        return Ok(integrations.ToEmailResponse(row, settings));
    }

    [HttpPut("email"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
    public async Task<ActionResult<EmailIntegrationResponse>> SaveEmail(EmailIntegrationRequest request, CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var prior = await integrations.GetEmailSettingsAsync(workspaceId, cancellationToken);
        var beforeState = prior is null ? null : new
        {
            prior.Host, prior.Port, prior.FromAddress, prior.EnableSsl,
            HasCredentials = !string.IsNullOrWhiteSpace(prior.Username),
            HasInboundWebhook = !string.IsNullOrWhiteSpace(prior.InboundWebhookSecret)
        };
        if (string.IsNullOrWhiteSpace(request.Host) || request.Host.Contains(' ') ||
            !Uri.CheckHostName(request.Host.Trim()).Equals(UriHostNameType.Dns) && !System.Net.IPAddress.TryParse(request.Host, out _))
            throw new CrmException(400, "Enter a valid SMTP host name or IP address.");
        var username = request.Username is null ? prior?.Username : CrmService.Clean(request.Username);
        var password = string.IsNullOrWhiteSpace(username) ? null : request.Password is null ? prior?.Password : CrmService.Clean(request.Password);
        if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(password))
            throw new CrmException(400, "Enter the SMTP password for the configured username.");
        var secret = request.InboundWebhookSecret is null ? prior?.InboundWebhookSecret : CrmService.Clean(request.InboundWebhookSecret);
        var saved = await integrations.SaveEmailSettingsAsync(workspaceId,
            new EmailSmtpSettings(request.Host.Trim(), request.Port, request.FromAddress.Trim(), request.EnableSsl,
                username, password, secret), cancellationToken);
        var settings = await integrations.GetEmailSettingsAsync(workspaceId, cancellationToken);
        db.AuditLogs.Add(LeadWorkflowService.CreateAudit(workspaceId, Crm.Actor.Id, "integration.email.settings-updated",
            "WorkspaceIntegration", saved.Id, new
            {
                Before = beforeState,
                After = new
                {
                    settings!.Host, settings.Port, settings.FromAddress, settings.EnableSsl,
                    HasCredentials = !string.IsNullOrWhiteSpace(settings.Username),
                    HasInboundWebhook = !string.IsNullOrWhiteSpace(settings.InboundWebhookSecret)
                }
            }));
        db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
        {
            WorkspaceId = workspaceId, Provider = "email", EventType = "configuration.updated",
            ExternalEventId = saved.Id.ToString("N"), Status = "Succeeded", Summary = "Workspace email settings were updated."
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(integrations.ToEmailResponse(saved, settings));
    }

    [HttpPost("email/test"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
    public async Task<ActionResult<EmailIntegrationResponse>> TestEmail(CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var settings = await integrations.GetEmailSettingsAsync(workspaceId, cancellationToken)
            ?? throw new CrmException(400, "Configure workspace email before sending a test message.");
        if (string.IsNullOrWhiteSpace(Crm.Actor.Email)) throw new CrmException(400, "Your account needs an email address to receive the test message.");
        try
        {
            await emailSender.SendAsync(workspaceId, Crm.Actor.Email, "Leadflow CRM email connection test",
                "Email delivery is connected for this workspace.", cancellationToken: cancellationToken);
            await integrations.MarkStatusAsync(workspaceId, "email", IntegrationStatus.Connected, cancellationToken: cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "email", EventType = "connection.tested",
                ExternalEventId = Guid.NewGuid().ToString("N"), Status = "Succeeded", Summary = "SMTP test email was delivered."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await integrations.MarkStatusAsync(workspaceId, "email", IntegrationStatus.Error,
                WorkspaceIntegrationService.SafeError(exception.Message), cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "email", EventType = "connection.test-failed",
                ExternalEventId = Guid.NewGuid().ToString("N"), Status = "Failed", Summary = "SMTP test email delivery failed."
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        var row = await integrations.FindAsync(workspaceId, "email", cancellationToken);
        return Ok(integrations.ToEmailResponse(row, settings));
    }

    [HttpGet("history"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
    public async Task<ActionResult<IReadOnlyList<IntegrationSyncEvent>>> History([FromQuery] string? provider,
        [FromQuery, Range(1, 200)] int limit = 50, CancellationToken cancellationToken = default)
    {
        var query = db.IntegrationSyncEvents.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId);
        if (!string.IsNullOrWhiteSpace(provider)) query = query.Where(x => x.Provider == provider.Trim().ToLowerInvariant());
        return Ok(await query.OrderByDescending(x => x.OccurredAtUtc).Take(limit).ToListAsync(cancellationToken));
    }
}
