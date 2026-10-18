using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using leadflow_crm.API.Data;
using leadflow_crm.API.Models.Crm;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Services;

public sealed record GoogleOAuthToken(string AccessToken, string? RefreshToken, DateTime ExpiresAtUtc);
public sealed record GoogleCalendarEvent(string Id, string? HtmlLink);

public sealed class GoogleCalendarService(
    HttpClient http,
    IConfiguration configuration,
    LeadflowDbContext db,
    IntegrationSecretProtector protector)
{
    private const string CalendarApi = "https://www.googleapis.com/calendar/v3/";
    private const string TokenApi = "https://oauth2.googleapis.com/token";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly SemaphoreSlim TokenRefreshLock = new(1, 1);
    private string? cachedToken;
    private DateTime cachedTokenExpiryUtc;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["GoogleCalendar:ClientId"]) &&
        !string.IsNullOrWhiteSpace(configuration["GoogleCalendar:ClientSecret"]);

    public string RedirectUri(HttpRequest request)
    {
        var configured = configuration["GoogleCalendar:RedirectUri"];
        return string.IsNullOrWhiteSpace(configured)
            ? $"{request.Scheme}://{request.Host}/api/integrations/calendar/callback"
            : configured;
    }

    public string CreateAuthorizationUrl(string state, string redirectUri)
    {
        var clientId = configuration["GoogleCalendar:ClientId"];
        if (!IsConfigured || string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Google Calendar OAuth client credentials are not configured.");
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "openid email https://www.googleapis.com/auth/calendar.events https://www.googleapis.com/auth/calendar.freebusy",
            ["access_type"] = "offline",
            ["include_granted_scopes"] = "true",
            ["prompt"] = "consent",
            ["state"] = state
        };
        return "https://accounts.google.com/o/oauth2/v2/auth?" + string.Join('&', query.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
    }

    public async Task<GoogleOAuthToken> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = configuration["GoogleCalendar:ClientId"] ?? string.Empty,
            ["client_secret"] = configuration["GoogleCalendar:ClientSecret"] ?? string.Empty,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });
        using var response = await http.PostAsync(TokenApi, content, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadProviderError(json.RootElement, "Google OAuth token exchange failed."));
        var root = json.RootElement;
        var accessToken = root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google did not return an access token.");
        var refreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var seconds) ? seconds : 3600;
        return new(accessToken, refreshToken, DateTime.UtcNow.AddSeconds(expiresIn));
    }

    public string ProtectRefreshToken(Guid workspaceId, string value) =>
        protector.Protect(workspaceId, "google-calendar.refresh-token", value);

    public string UnprotectRefreshToken(Guid workspaceId, string value) =>
        protector.Unprotect(workspaceId, "google-calendar.refresh-token", value);

    public async Task<string> GetUserEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = Authorized(HttpMethod.Get, new Uri("https://www.googleapis.com/oauth2/v2/userinfo"), accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadProviderError(json.RootElement, "Google user information could not be loaded."));
        return json.RootElement.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String
            ? email.GetString() ?? throw new InvalidOperationException("Google did not return an email address.")
            : throw new InvalidOperationException("Google did not return an email address.");
    }

    public async Task<bool> HasConflictAsync(CalendarConnection connection, DateTime startUtc, DateTime endUtc,
        string timeZoneId, CancellationToken cancellationToken, string? excludeEventId = null)
    {
        var token = await GetAccessTokenAsync(connection, cancellationToken);
        if (!string.IsNullOrWhiteSpace(excludeEventId))
        {
            var query = $"?timeMin={Uri.EscapeDataString(new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToString("O"))}" +
                $"&timeMax={Uri.EscapeDataString(new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToString("O"))}" +
                "&singleEvents=true&showDeleted=false&maxResults=2500";
            using var listRequest = Authorized(HttpMethod.Get, CalendarEventsUri(connection.CalendarId, query), token);
            using var listResponse = await http.SendAsync(listRequest, cancellationToken);
            using var listJson = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!listResponse.IsSuccessStatusCode) throw new InvalidOperationException(ReadProviderError(listJson.RootElement, "Google Calendar could not check conflicts."));
            if (!listJson.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return false;
            foreach (var item in items.EnumerateArray())
            {
                if (string.Equals(item.TryGetProperty("id", out var id) ? id.GetString() : null, excludeEventId, StringComparison.Ordinal)) continue;
                if (item.TryGetProperty("status", out var status) && status.GetString() == "cancelled") continue;
                if (!TryGetEventRange(item, timeZoneId, out var eventStartUtc, out var eventEndUtc)) continue;
                if (eventStartUtc < endUtc && eventEndUtc > startUtc) return true;
            }
            return false;
        }
        using var request = Authorized(HttpMethod.Post, new Uri(new Uri(CalendarApi), "freeBusy"), token);
        request.Content = JsonContent(new
        {
            timeMin = startUtc.ToString("O"), timeMax = endUtc.ToString("O"), timeZone = timeZoneId,
            items = new[] { new { id = connection.CalendarId } }
        });
        using var response = await http.SendAsync(request, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadProviderError(json.RootElement, "Google Calendar could not check availability."));
        if (!json.RootElement.TryGetProperty("calendars", out var calendars) ||
            !calendars.TryGetProperty(connection.CalendarId, out var calendar))
            throw new InvalidOperationException("Google Calendar did not return availability for the connected calendar.");
        if (calendar.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            throw new InvalidOperationException("Google Calendar could not read availability for the connected calendar.");
        return calendar.TryGetProperty("busy", out var busy) && busy.GetArrayLength() > 0;
    }

    public async Task<GoogleCalendarEvent> CreateEventAsync(CalendarConnection connection, CalendarAppointment appointment,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(connection, cancellationToken);
        var uri = CalendarEventsUri(connection.CalendarId, $"?sendUpdates=all");
        using var request = Authorized(HttpMethod.Post, uri, token);
        request.Content = JsonContent(EventBody(appointment, appointment.GoogleEventId));
        using var response = await http.SendAsync(request, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict && !string.IsNullOrWhiteSpace(appointment.GoogleEventId))
            return await GetEventAsync(connection, appointment.GoogleEventId, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadProviderError(json.RootElement, "Google Calendar could not create the event."));
        return ParseEvent(json.RootElement);
    }

    public async Task<GoogleCalendarEvent> UpdateEventAsync(CalendarConnection connection, CalendarAppointment appointment,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(connection, cancellationToken);
        var uri = CalendarEventsUri(connection.CalendarId, $"/{Uri.EscapeDataString(appointment.GoogleEventId ?? string.Empty)}?sendUpdates=all");
        using var request = Authorized(HttpMethod.Put, uri, token);
        request.Content = JsonContent(EventBody(appointment, appointment.GoogleEventId));
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return await CreateEventAsync(connection, appointment, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadProviderError(json.RootElement, "Google Calendar could not update the event."));
        return ParseEvent(json.RootElement);
    }

    public async Task DeleteEventAsync(CalendarConnection connection, string eventId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(connection, cancellationToken);
        var uri = CalendarEventsUri(connection.CalendarId, $"/{Uri.EscapeDataString(eventId)}?sendUpdates=all");
        using var request = Authorized(HttpMethod.Delete, uri, token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        throw new InvalidOperationException(ReadProviderError(json.RootElement, "Google Calendar could not cancel the event."));
    }

    private async Task<GoogleCalendarEvent> GetEventAsync(CalendarConnection connection, string eventId, CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(connection, cancellationToken);
        using var request = Authorized(HttpMethod.Get,
            CalendarEventsUri(connection.CalendarId, $"/{Uri.EscapeDataString(eventId)}"), token);
        using var response = await http.SendAsync(request, cancellationToken);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadProviderError(json.RootElement, "Google Calendar could not retrieve the event."));
        return ParseEvent(json.RootElement);
    }

    private async Task<string> GetAccessTokenAsync(CalendarConnection connection, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(cachedToken) && cachedTokenExpiryUtc > DateTime.UtcNow.AddMinutes(1)) return cachedToken;
        await TokenRefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(cachedToken) && cachedTokenExpiryUtc > DateTime.UtcNow.AddMinutes(1)) return cachedToken;
            var refreshToken = UnprotectRefreshToken(connection.WorkspaceId, connection.EncryptedRefreshToken);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = configuration["GoogleCalendar:ClientId"] ?? string.Empty,
                ["client_secret"] = configuration["GoogleCalendar:ClientSecret"] ?? string.Empty,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token"
            });
            using var response = await http.PostAsync(TokenApi, content, cancellationToken);
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                connection.LastError = WorkspaceIntegrationService.SafeError(ReadProviderError(json.RootElement, "Google Calendar token refresh failed."));
                connection.LastCheckedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException(connection.LastError);
            }
            var root = json.RootElement;
            cachedToken = root.GetProperty("access_token").GetString() ?? throw new InvalidOperationException("Google did not return an access token.");
            var expiresIn = root.TryGetProperty("expires_in", out var expiry) && expiry.TryGetInt32(out var seconds) ? seconds : 3600;
            cachedTokenExpiryUtc = DateTime.UtcNow.AddSeconds(expiresIn);
            connection.LastCheckedAtUtc = DateTime.UtcNow;
            connection.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            return cachedToken;
        }
        finally { TokenRefreshLock.Release(); }
    }

    private Uri CalendarEventsUri(string calendarId, string tail) =>
        new(new Uri(CalendarApi), $"calendars/{Uri.EscapeDataString(calendarId)}/events{tail}");

    private static HttpRequestMessage Authorized(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static StringContent JsonContent(object value) => new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static object EventBody(CalendarAppointment appointment, string? eventId) => new
    {
        id = eventId,
        summary = appointment.Title,
        description = appointment.Description,
        start = new { dateTime = new DateTimeOffset(DateTime.SpecifyKind(appointment.StartsAtUtc, DateTimeKind.Utc)).ToString("O"), timeZone = appointment.TimeZoneId },
        end = new { dateTime = new DateTimeOffset(DateTime.SpecifyKind(appointment.EndsAtUtc, DateTimeKind.Utc)).ToString("O"), timeZone = appointment.TimeZoneId },
        attendees = string.IsNullOrWhiteSpace(appointment.AttendeeEmail) ? null : new[] { new { email = appointment.AttendeeEmail } }
    };

    private static GoogleCalendarEvent ParseEvent(JsonElement root) => new(
        root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
        root.TryGetProperty("htmlLink", out var link) ? link.GetString() : null);

    private static bool TryGetEventRange(JsonElement item, string timeZoneId, out DateTime startUtc, out DateTime endUtc)
    {
        startUtc = default; endUtc = default;
        if (!item.TryGetProperty("start", out var start) || !item.TryGetProperty("end", out var end)) return false;
        startUtc = ParseGoogleDateTime(start, timeZoneId);
        endUtc = ParseGoogleDateTime(end, timeZoneId);
        return startUtc != default && endUtc > startUtc;
    }

    private static DateTime ParseGoogleDateTime(JsonElement value, string timeZoneId)
    {
        if (value.TryGetProperty("dateTime", out var dateTime) && dateTime.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(dateTime.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
            return instant.UtcDateTime;
        if (value.TryGetProperty("date", out var allDay) && allDay.ValueKind == JsonValueKind.String &&
            DateOnly.TryParseExact(allDay.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        return default;
    }

    private static string ReadProviderError(JsonElement root, string fallback)
    {
        if (root.TryGetProperty("error_description", out var description) && description.ValueKind == JsonValueKind.String)
            return description.GetString() ?? fallback;
        if (root.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? fallback;
            if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? fallback;
        }
        return fallback;
    }
}
