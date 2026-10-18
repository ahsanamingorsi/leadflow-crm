using System.Security.Cryptography;
using System.Text;
using leadflow_crm.API.Authorization;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Controllers;

[Route("api/integrations/calendar")]
public sealed class CalendarController(
    LeadflowDbContext db,
    CrmService crm,
    GoogleCalendarService google,
    WorkspaceIntegrationService integrations) : IntegrationApiControllerBase(crm)
{
    [HttpGet("status"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<ActionResult<CalendarConnectionResponse>> Status(CancellationToken cancellationToken)
    {
        var row = await db.CalendarConnections.AsNoTracking().SingleOrDefaultAsync(x =>
            x.WorkspaceId == Crm.Actor.WorkspaceId && x.UserId == Crm.Actor.Id, cancellationToken);
        return Ok(row is null
            ? new CalendarConnectionResponse(false, null, null, null, null, google.IsConfigured ? null : "Google OAuth client credentials are not configured.")
            : new CalendarConnectionResponse(true, row.EmailAddress, row.CalendarId, row.ConnectedAtUtc, row.LastCheckedAtUtc, row.LastError));
    }

    [HttpPost("connect"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<ActionResult<object>> Connect(CancellationToken cancellationToken)
    {
        if (!google.IsConfigured) throw new CrmException(503, "Google Calendar OAuth credentials are not configured.");
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var redirectUri = google.RedirectUri(Request);
        if (redirectUri.Length > 512) throw new CrmException(500, "The configured Google OAuth redirect URI is too long.");
        db.CalendarOAuthStates.Add(new CalendarOAuthState
        {
            WorkspaceId = Crm.Actor.WorkspaceId,
            UserId = Crm.Actor.Id,
            StateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state))),
            RedirectUri = redirectUri,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        });
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { authorizationUrl = google.CreateAuthorizationUrl(state, redirectUri) });
    }

    [HttpDelete("connection"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        var row = await db.CalendarConnections.SingleOrDefaultAsync(x =>
            x.WorkspaceId == Crm.Actor.WorkspaceId && x.UserId == Crm.Actor.Id, cancellationToken);
        if (row is not null) db.CalendarConnections.Remove(row);
        var remaining = await db.CalendarConnections.AnyAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.UserId != Crm.Actor.Id, cancellationToken);
        if (!remaining) await integrations.MarkStatusAsync(Crm.Actor.WorkspaceId, "google-calendar", IntegrationStatus.Disabled, cancellationToken: cancellationToken);
        else if (row is not null) await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("appointments"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<ActionResult<IReadOnlyList<CalendarAppointmentResponse>>> Appointments(CancellationToken cancellationToken)
    {
        var rows = await db.CalendarAppointments.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId &&
                x.CreatedByUserId == Crm.Actor.Id)
            .OrderBy(x => x.StartsAtUtc).ThenBy(x => x.Id).Take(200).ToListAsync(cancellationToken);
        return Ok(rows.Select(ToResponse).ToArray());
    }

    [HttpPost("appointments"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<ActionResult<CalendarAppointmentResponse>> CreateAppointment(CalendarAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var workspaceId = Crm.Actor.WorkspaceId;
        var connection = await RequireConnectionAsync(workspaceId, Crm.Actor.Id, cancellationToken);
        var workspace = await db.Workspaces.AsNoTracking().SingleAsync(x => x.Id == workspaceId, cancellationToken);
        var (startUtc, endUtc) = ValidateRange(request);
        Guid? contactId = request.ContactId;
        if (request.OpportunityId.HasValue)
        {
            var opportunity = await Crm.FindAsync<Opportunity>(request.OpportunityId.Value);
            if (contactId.HasValue && contactId.Value != opportunity.ContactId)
                throw new CrmException(400, "The selected contact does not belong to the opportunity.");
            contactId ??= opportunity.ContactId;
        }
        Contact? contact = null;
        if (contactId.HasValue) contact = await Crm.FindAsync<Contact>(contactId.Value);
        var attendeeEmail = CrmService.Clean(request.AttendeeEmail) ?? contact?.Email;
        if (await HasLocalConflictAsync(Crm.Actor.Id, startUtc, endUtc, null, cancellationToken))
            throw new CrmException(409, "This time overlaps another appointment in your workspace calendar.");
        bool googleConflict;
        try { googleConflict = await google.HasConflictAsync(connection, startUtc, endUtc, workspace.TimeZoneId, cancellationToken); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await integrations.MarkStatusAsync(workspaceId, "google-calendar", IntegrationStatus.Error,
                WorkspaceIntegrationService.SafeError(exception.Message), cancellationToken);
            throw new CrmException(502, "Google Calendar availability could not be checked. Review the connection status and try again.");
        }
        if (googleConflict) throw new CrmException(409, "Google Calendar shows another event during this time.");

        var row = new CalendarAppointment
        {
            WorkspaceId = workspaceId, CreatedByUserId = Crm.Actor.Id, ContactId = contactId,
            OpportunityId = request.OpportunityId, Title = request.Title.Trim(), Description = CrmService.Clean(request.Description),
            AttendeeEmail = attendeeEmail, StartsAtUtc = startUtc, EndsAtUtc = endUtc,
            TimeZoneId = workspace.TimeZoneId, GoogleEventId = "lf" + Guid.NewGuid().ToString("N"),
            Status = nameof(AppointmentStatus.Scheduled)
        };
        db.CalendarAppointments.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        await SyncEventAsync(connection, row, "create", cancellationToken);
        return Ok(ToResponse(row));
    }

    [HttpPut("appointments/{id:guid}"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<ActionResult<CalendarAppointmentResponse>> Reschedule(Guid id, CalendarAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var row = await db.CalendarAppointments.SingleOrDefaultAsync(x => x.Id == id &&
            x.WorkspaceId == Crm.Actor.WorkspaceId && x.CreatedByUserId == Crm.Actor.Id, cancellationToken)
            ?? throw new CrmException(404, "The appointment was not found in your calendar.");
        if (row.Status == nameof(AppointmentStatus.Cancelled)) throw new CrmException(409, "A cancelled appointment cannot be changed.");
        var connection = await RequireConnectionAsync(row.WorkspaceId, Crm.Actor.Id, cancellationToken);
        var (startUtc, endUtc) = ValidateRange(request);
        bool googleConflict;
        try { googleConflict = await google.HasConflictAsync(connection, startUtc, endUtc, row.TimeZoneId, cancellationToken, row.GoogleEventId); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await integrations.MarkStatusAsync(row.WorkspaceId, "google-calendar", IntegrationStatus.Error,
                WorkspaceIntegrationService.SafeError(exception.Message), cancellationToken);
            throw new CrmException(502, "Google Calendar availability could not be checked. Review the connection status and try again.");
        }
        if (googleConflict) throw new CrmException(409, "Google Calendar shows another event during this time.");
        Guid? contactId = request.ContactId;
        if (request.OpportunityId.HasValue)
        {
            var opportunity = await Crm.FindAsync<Opportunity>(request.OpportunityId.Value);
            if (contactId.HasValue && contactId.Value != opportunity.ContactId)
                throw new CrmException(400, "The selected contact does not belong to the opportunity.");
            contactId ??= opportunity.ContactId;
        }
        Contact? contact = null;
        if (contactId.HasValue) contact = await Crm.FindAsync<Contact>(contactId.Value);
        if (await HasLocalConflictAsync(Crm.Actor.Id, startUtc, endUtc, row.Id, cancellationToken))
            throw new CrmException(409, "This time overlaps another appointment in your workspace calendar.");

        row.Title = request.Title.Trim(); row.Description = CrmService.Clean(request.Description);
        row.ContactId = contactId; row.OpportunityId = request.OpportunityId;
        row.AttendeeEmail = CrmService.Clean(request.AttendeeEmail) ?? contact?.Email;
        row.StartsAtUtc = startUtc; row.EndsAtUtc = endUtc; row.UpdatedAtUtc = DateTime.UtcNow;
        await SyncEventAsync(connection, row, "update", cancellationToken);
        return Ok(ToResponse(row));
    }

    [HttpDelete("appointments/{id:guid}"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<ActionResult<CalendarAppointmentResponse>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.CalendarAppointments.SingleOrDefaultAsync(x => x.Id == id &&
            x.WorkspaceId == Crm.Actor.WorkspaceId && x.CreatedByUserId == Crm.Actor.Id, cancellationToken)
            ?? throw new CrmException(404, "The appointment was not found in your calendar.");
        if (row.Status == nameof(AppointmentStatus.Cancelled)) return Ok(ToResponse(row));
        var connection = await RequireConnectionAsync(row.WorkspaceId, Crm.Actor.Id, cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(row.GoogleEventId))
                await google.DeleteEventAsync(connection, row.GoogleEventId, cancellationToken);
            row.Status = nameof(AppointmentStatus.Cancelled); row.LastError = null; row.UpdatedAtUtc = DateTime.UtcNow;
            await AddSyncLogAsync(row, "cancel", "Succeeded", "Calendar event cancelled.", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            row.LastError = WorkspaceIntegrationService.SafeError(exception.Message);
            await AddSyncLogAsync(row, "cancel", "Failed", "Calendar event cancellation failed.", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return StatusCode(StatusCodes.Status502BadGateway, ToResponse(row));
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(row));
    }

    [HttpPost("appointments/{id:guid}/retry"), Authorize(Policy = PermissionCatalog.CalendarUse)]
    public async Task<ActionResult<CalendarAppointmentResponse>> Retry(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.CalendarAppointments.SingleOrDefaultAsync(x => x.Id == id &&
            x.WorkspaceId == Crm.Actor.WorkspaceId && x.CreatedByUserId == Crm.Actor.Id, cancellationToken)
            ?? throw new CrmException(404, "The appointment was not found in your calendar.");
        if (row.Status == nameof(AppointmentStatus.Cancelled)) throw new CrmException(409, "A cancelled appointment cannot be retried.");
        var connection = await RequireConnectionAsync(row.WorkspaceId, Crm.Actor.Id, cancellationToken);
        await SyncEventAsync(connection, row, "update", cancellationToken);
        return Ok(ToResponse(row));
    }

    private async Task SyncEventAsync(CalendarConnection connection, CalendarAppointment row, string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = operation == "update"
                ? await google.UpdateEventAsync(connection, row, cancellationToken)
                : await google.CreateEventAsync(connection, row, cancellationToken);
            row.GoogleEventId = result.Id;
            row.HtmlLink = result.HtmlLink;
            row.Status = nameof(AppointmentStatus.Scheduled);
            row.LastError = null;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await AddSyncLogAsync(row, $"appointment.{operation}", "Succeeded", $"Calendar event {operation}d.", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            row.Status = nameof(AppointmentStatus.Failed);
            row.LastError = WorkspaceIntegrationService.SafeError(exception.Message);
            connection.LastError = row.LastError;
            await AddSyncLogAsync(row, $"appointment.{operation}", "Failed", $"Calendar event {operation} failed.", cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddSyncLogAsync(CalendarAppointment row, string eventType, string status, string summary,
        CancellationToken cancellationToken)
    {
        db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
        {
            WorkspaceId = row.WorkspaceId, Provider = "google-calendar", EventType = eventType,
            ExternalEventId = $"{row.Id:N}:{eventType}:{Guid.NewGuid():N}", Status = status, Summary = summary
        });
        if (status == "Succeeded")
            await integrations.MarkStatusAsync(row.WorkspaceId, "google-calendar", IntegrationStatus.Connected, cancellationToken: cancellationToken);
        else
            await integrations.MarkStatusAsync(row.WorkspaceId, "google-calendar", IntegrationStatus.Error, row.LastError, cancellationToken);
    }

    private async Task<CalendarConnection> RequireConnectionAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        await db.CalendarConnections.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.UserId == userId, cancellationToken)
        ?? throw new CrmException(409, "Connect your Google Calendar before managing appointments.");

    private async Task<bool> HasLocalConflictAsync(Guid userId, DateTime startUtc, DateTime endUtc, Guid? exceptId,
        CancellationToken cancellationToken) =>
        await db.CalendarAppointments.AnyAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.CreatedByUserId == userId &&
            x.Status == nameof(AppointmentStatus.Scheduled) && (!exceptId.HasValue || x.Id != exceptId.Value) &&
            x.StartsAtUtc < endUtc && x.EndsAtUtc > startUtc, cancellationToken);

    private static (DateTime StartUtc, DateTime EndUtc) ValidateRange(CalendarAppointmentRequest request)
    {
        var start = request.StartsAt.UtcDateTime;
        var end = request.EndsAt.UtcDateTime;
        if (start < DateTime.UtcNow.AddMinutes(-1)) throw new CrmException(400, "Appointment start time must be in the future.");
        if (end <= start || end - start > TimeSpan.FromHours(12)) throw new CrmException(400, "Appointment end must be after its start and no more than 12 hours later.");
        return (start, end);
    }

    private static CalendarAppointmentResponse ToResponse(CalendarAppointment row)
    {
        var status = Enum.TryParse<AppointmentStatus>(row.Status, out var parsed) ? parsed : AppointmentStatus.Failed;
        return new(row.Id, row.ContactId, row.OpportunityId, row.Title, row.Description, row.AttendeeEmail,
            row.StartsAtUtc, row.EndsAtUtc, row.TimeZoneId, row.HtmlLink, status, row.LastError);
    }
}

[ApiController]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[Route("api/integrations/calendar/callback")]
public sealed class GoogleCalendarCallbackController(
    LeadflowDbContext db,
    GoogleCalendarService google,
    WorkspaceIntegrationService integrations,
    IConfiguration configuration,
    ILogger<GoogleCalendarCallbackController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state)) return Redirect(FrontendUrl(configuration, "calendar=error"));
        var stateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
        var oauth = await db.CalendarOAuthStates.SingleOrDefaultAsync(x => x.StateHash == stateHash, cancellationToken);
        if (oauth is null || oauth.UsedAtUtc.HasValue || oauth.ExpiresAtUtc <= DateTime.UtcNow)
            return Redirect(FrontendUrl(configuration, "calendar=state-expired"));
        oauth.UsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(error))
        {
            await integrations.MarkStatusAsync(oauth.WorkspaceId, "google-calendar", IntegrationStatus.Error,
                "Google Calendar authorization was cancelled.", cancellationToken);
            return Redirect(FrontendUrl(configuration, "calendar=cancelled"));
        }
        if (string.IsNullOrWhiteSpace(code)) return Redirect(FrontendUrl(configuration, "calendar=error"));
        try
        {
            if (!await db.Users.AnyAsync(x => x.Id == oauth.UserId && x.WorkspaceId == oauth.WorkspaceId && x.IsActive, cancellationToken) ||
                !await db.Workspaces.AnyAsync(x => x.Id == oauth.WorkspaceId && x.IsActive, cancellationToken))
                return Redirect(FrontendUrl(configuration, "calendar=workspace-inactive"));
            var token = await google.ExchangeCodeAsync(code, oauth.RedirectUri, cancellationToken);
            var email = await google.GetUserEmailAsync(token.AccessToken, cancellationToken);
            var existing = await db.CalendarConnections.SingleOrDefaultAsync(x => x.WorkspaceId == oauth.WorkspaceId && x.UserId == oauth.UserId, cancellationToken);
            var refreshToken = token.RefreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken) && existing is not null)
                refreshToken = google.UnprotectRefreshToken(oauth.WorkspaceId, existing.EncryptedRefreshToken);
            if (string.IsNullOrWhiteSpace(refreshToken)) throw new InvalidOperationException("Google did not provide offline access. Reconnect and approve the requested permissions.");
            if (existing is null)
            {
                existing = new CalendarConnection { WorkspaceId = oauth.WorkspaceId, UserId = oauth.UserId };
                db.CalendarConnections.Add(existing);
            }
            existing.EmailAddress = email;
            existing.CalendarId = "primary";
            existing.EncryptedRefreshToken = google.ProtectRefreshToken(oauth.WorkspaceId, refreshToken);
            existing.ConnectedAtUtc = DateTime.UtcNow;
            existing.LastCheckedAtUtc = DateTime.UtcNow;
            existing.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            await integrations.MarkStatusAsync(oauth.WorkspaceId, "google-calendar", IntegrationStatus.Connected, cancellationToken: cancellationToken);
            return Redirect(FrontendUrl(configuration, "calendar=connected"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Google Calendar OAuth callback failed for workspace {WorkspaceId}.", oauth.WorkspaceId);
            await integrations.MarkStatusAsync(oauth.WorkspaceId, "google-calendar", IntegrationStatus.Error,
                WorkspaceIntegrationService.SafeError(exception.Message), cancellationToken);
            return Redirect(FrontendUrl(configuration, "calendar=error"));
        }
    }

    private static string FrontendUrl(IConfiguration configuration, string result)
    {
        var baseUrl = (configuration["Frontend:BaseUrl"] ?? "http://localhost:4200").TrimEnd('/');
        return $"{baseUrl}/?{result}";
    }
}
