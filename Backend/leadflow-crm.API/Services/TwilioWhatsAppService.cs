using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace leadflow_crm.API.Services;

public sealed record TwilioSettings(string AccountSid, string AuthToken, string FromNumber, string PublicBaseUrl);

public sealed class TwilioWhatsAppService(HttpClient http)
{
    public static string Phone(string input)
    {
        var phone = input.Replace("whatsapp:", "", StringComparison.Ordinal).Replace(" ", "").Replace("-", "");
        if (Regex.IsMatch(phone, "^03[0-9]{9}$")) phone = "+92" + phone[1..];
        if (!Regex.IsMatch(phone, "^\\+[1-9][0-9]{7,14}$")) throw new CrmException(400, "Use an international phone number, for example +923001234567.");
        return phone;
    }

    public static bool Verify(string url, IFormCollection form, string signature, string token)
    {
        // Twilio signs the configured public URL and every submitted form parameter.
        var text = new StringBuilder(url);
        foreach (var key in form.Keys.Order(StringComparer.Ordinal))
            foreach (var value in form[key].Distinct().Order(StringComparer.Ordinal)) text.Append(key).Append(value);
        var expected = HMACSHA1.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(text.ToString()));
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(signature)); }
        catch (FormatException) { return false; }
    }

    public async Task<(string? Sid, string Status, string? Error)> SendAsync(TwilioSettings settings,
        string phone, string body, string? contentSid, string callback, CancellationToken ct)
    {
        var fields = new Dictionary<string, string> { ["From"] = "whatsapp:" + settings.FromNumber,
            ["To"] = "whatsapp:" + phone, ["StatusCallback"] = callback };
        if (string.IsNullOrWhiteSpace(contentSid)) fields["Body"] = body;
        else fields["ContentSid"] = contentSid;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{settings.AccountSid}/Messages.json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.AccountSid + ":" + settings.AuthToken)));
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await http.SendAsync(request, ct);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!response.IsSuccessStatusCode)
        {
            var code = json.RootElement.TryGetProperty("code", out var value) ? value.ToString() : ((int)response.StatusCode).ToString();
            return (null, (int)response.StatusCode >= 500 ? "unknown" : "failed", "Twilio rejected the request (code " + code + ").");
        }
        var sid = json.RootElement.GetProperty("sid").GetString();
        if (sid is null || !Regex.IsMatch(sid, "^SM[0-9a-fA-F]{32}$")) throw new HttpRequestException("Invalid Twilio response.");
        return (sid, "queued", null);
    }

    public static bool CanAdvance(string current, string next)
    {
        if (current == next || current == "read") return false;
        if (next == "read") return current is "pending" or "sent" or "delivered" or "queued" or "sending" or "unknown";
        if (next == "delivered") return current is "pending" or "queued" or "sending" or "sent" or "unknown";
        if (current is "delivered" or "failed" or "undelivered") return false;
        if (next is "failed" or "undelivered") return true;
        var ranks = new Dictionary<string, int> { ["unknown"] = 0, ["pending"] = 0, ["queued"] = 1, ["sending"] = 2, ["sent"] = 3 };
        return ranks.TryGetValue(current, out var before) && ranks.TryGetValue(next, out var after) && after > before;
    }
}
