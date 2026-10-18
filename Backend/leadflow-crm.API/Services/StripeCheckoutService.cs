using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using leadflow_crm.API.Models.Crm;

namespace leadflow_crm.API.Services;

public sealed record StripeCheckoutSession(string Id, string Url, string? PaymentIntentId, DateTime? ExpiresAtUtc);

public sealed class StripeCheckoutService(HttpClient http, IConfiguration configuration)
{
    private const string ApiBase = "https://api.stripe.com/v1/";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool HasTestConfiguration => GetApiKey().StartsWith("sk_test_", StringComparison.Ordinal) &&
        (configuration["Stripe:WebhookSecret"]?.StartsWith("whsec_", StringComparison.Ordinal) ?? false);

    public async Task<StripeCheckoutSession> CreateCheckoutAsync(PaymentRecord payment, Opportunity opportunity,
        Contact contact, CancellationToken cancellationToken)
    {
        var key = GetApiKey();
        if (!key.StartsWith("sk_test_", StringComparison.Ordinal))
            throw new InvalidOperationException("Stripe test mode is required. Configure a Stripe sk_test_ key.");
        var successUrl = configuration["Stripe:SuccessUrl"] ?? "http://localhost:4200/payments/success?session_id={CHECKOUT_SESSION_ID}";
        var cancelUrl = configuration["Stripe:CancelUrl"] ?? "http://localhost:4200/payments/cancel";
        if (!Uri.TryCreate(successUrl.Replace("{CHECKOUT_SESSION_ID}", "test", StringComparison.Ordinal), UriKind.Absolute, out _) ||
            !Uri.TryCreate(cancelUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("Configure valid absolute Stripe success and cancel URLs.");

        var fields = new Dictionary<string, string>
        {
            ["mode"] = "payment",
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["client_reference_id"] = payment.Id.ToString("D"),
            ["metadata[paymentRecordId]"] = payment.Id.ToString("D"),
            ["metadata[opportunityId]"] = opportunity.Id.ToString("D"),
            ["payment_intent_data[metadata][paymentRecordId]"] = payment.Id.ToString("D"),
            ["payment_intent_data[metadata][opportunityId]"] = opportunity.Id.ToString("D"),
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = "pkr",
            ["line_items[0][price_data][unit_amount]"] = payment.AmountMinor.ToString(CultureInfo.InvariantCulture),
            ["line_items[0][price_data][product_data][name]"] = opportunity.Title
        };
        if (!string.IsNullOrWhiteSpace(contact.Email)) fields["customer_email"] = contact.Email;
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(ApiBase), "checkout/sessions"))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:")));
        request.Headers.Add("Idempotency-Key", payment.Id.ToString("N"));
        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var json = JsonDocument.Parse(body);
        if (!response.IsSuccessStatusCode)
        {
            var reason = json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                ? message.GetString() : "Stripe could not create the checkout session.";
            throw new InvalidOperationException(reason ?? "Stripe could not create the checkout session.");
        }
        var root = json.RootElement;
        var id = root.GetProperty("id").GetString() ?? throw new InvalidOperationException("Stripe returned no checkout session id.");
        var url = root.GetProperty("url").GetString() ?? throw new InvalidOperationException("Stripe returned no checkout URL.");
        var paymentIntentId = root.TryGetProperty("payment_intent", out var paymentIntent) && paymentIntent.ValueKind == JsonValueKind.String
            ? paymentIntent.GetString() : null;
        DateTime? expiresAt = root.TryGetProperty("expires_at", out var expires) && expires.TryGetInt64(out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : null;
        return new(id, url, paymentIntentId, expiresAt);
    }

    public bool VerifyWebhookSignature(string rawBody, string signatureHeader, DateTime utcNow)
    {
        var secret = configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret) || !secret.StartsWith("whsec_", StringComparison.Ordinal)) return false;
        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) continue;
            if (pair[0] == "t" && long.TryParse(pair[1], out var parsed)) timestamp = parsed;
            else if (pair[0] == "v1") signatures.Add(pair[1]);
        }
        if (!timestamp.HasValue || signatures.Count == 0) return false;
        DateTime signedAt;
        try { signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((utcNow - signedAt).Duration() > TimeSpan.FromMinutes(5)) return false;
        var payload = Encoding.UTF8.GetBytes($"{timestamp.Value}.{rawBody}");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        foreach (var signature in signatures)
        {
            try
            {
                var supplied = Convert.FromHexString(signature);
                if (supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected)) return true;
            }
            catch (FormatException) { }
        }
        return false;
    }

    private string GetApiKey() => configuration["Stripe:SecretKey"] ?? string.Empty;
}
