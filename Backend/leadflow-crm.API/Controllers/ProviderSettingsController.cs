using System.Text.RegularExpressions;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/integrations"), Authorize(Policy = PermissionCatalog.IntegrationsManage)]
public sealed class ProviderSettingsController(CrmService crm, WorkspaceIntegrationService integrations, LeadflowDbContext db) : IntegrationApiControllerBase(crm)
{
    [HttpGet("twilio")]
    public async Task<IActionResult> Twilio(CancellationToken ct)
    {
        var s = await integrations.GetSettingsAsync<TwilioSettings>(Crm.Actor.WorkspaceId, "twilio", ct);
        return Ok(new { configured = s is not null, accountSid = s?.AccountSid, fromNumber = s?.FromNumber, publicBaseUrl = s?.PublicBaseUrl,
            inboundUrl = s is null ? null : $"{s.PublicBaseUrl}/api/whatsapp/hooks/{Crm.Actor.WorkspaceId}/inbound" });
    }

    [HttpPut("twilio")]
    public async Task<IActionResult> SaveTwilio(TwilioSettings request, CancellationToken ct)
    {
        var previous = await integrations.GetSettingsAsync<TwilioSettings>(Crm.Actor.WorkspaceId, "twilio", ct);
        var token = string.IsNullOrEmpty(request.AuthToken) ? previous?.AuthToken : request.AuthToken;
        if (!Regex.IsMatch(request.AccountSid ?? "", "^AC[0-9a-fA-F]{32}$") || token is null || !Regex.IsMatch(token, "^[0-9a-fA-F]{32}$"))
            throw new CrmException(400, "Enter the Twilio Account SID and Auth Token.");
        var settings = new TwilioSettings(request.AccountSid!, token, TwilioWhatsAppService.Phone(request.FromNumber ?? ""), PublicUrl(request.PublicBaseUrl));
        await integrations.SaveSettingsAsync(Crm.Actor.WorkspaceId, "twilio", settings, ct);
        db.AuditLogs.Add(LeadWorkflowService.CreateAudit(Crm.Actor.WorkspaceId, Crm.Actor.Id, "integration.settings.updated", "WorkspaceIntegration", Crm.Actor.WorkspaceId, new { Provider = "twilio" }));
        await db.SaveChangesAsync(ct);
        return await Twilio(ct);
    }

    public static string PublicUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new CrmException(400, "Public URL must be an HTTPS origin with no path, credentials, query or fragment.");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
