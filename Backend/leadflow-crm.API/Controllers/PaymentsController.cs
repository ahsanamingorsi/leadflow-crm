using System.Globalization;
using System.Text;
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

[Route("api/payments")]
public sealed class PaymentsController(
    LeadflowDbContext db,
    CrmService crm,
    StripeCheckoutService stripe,
    WorkspaceIntegrationService integrations) : IntegrationApiControllerBase(crm)
{
    [HttpGet("session/{sessionId}"), Authorize(Policy = PermissionCatalog.OpportunitiesView)]
    public async Task<ActionResult<PaymentResponse>> BySession(string sessionId, CancellationToken cancellationToken)
    {
        Crm.Require(PermissionCatalog.OpportunityValueView);
        var row = await db.PaymentRecords.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == Crm.Actor.WorkspaceId &&
            x.StripeCheckoutSessionId == sessionId, cancellationToken)
            ?? throw new CrmException(404, "The payment session was not found in this workspace.");
        return Ok(ToResponse(row));
    }

    [HttpGet("opportunities/{opportunityId:guid}"), Authorize(Policy = PermissionCatalog.OpportunitiesView)]
    public async Task<ActionResult<IReadOnlyList<PaymentResponse>>> ForOpportunity(Guid opportunityId, CancellationToken cancellationToken)
    {
        Crm.Require(PermissionCatalog.OpportunityValueView);
        _ = await Crm.FindAsync<Opportunity>(opportunityId);
        var rows = await db.PaymentRecords.AsNoTracking().Where(x => x.WorkspaceId == Crm.Actor.WorkspaceId && x.OpportunityId == opportunityId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        return Ok(rows.Select(ToResponse).ToArray());
    }

    [HttpPost("checkout"), Authorize(Policy = PermissionCatalog.OpportunitiesEdit)]
    public async Task<ActionResult<PaymentResponse>> CreateCheckout(CreateCheckoutRequest request, CancellationToken cancellationToken)
    {
        Crm.Require(PermissionCatalog.OpportunityValueView);
        var key = request.IdempotencyKey.Trim();
        if (key.Length is < 8 or > 255 || key.Any(char.IsControl)) throw new CrmException(400, "Idempotency-Key must contain 8 to 255 non-control characters.");
        var workspaceId = Crm.Actor.WorkspaceId;
        var existing = await db.PaymentRecords.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null) return Ok(ToResponse(existing));
        if (!stripe.HasTestConfiguration) throw new CrmException(503, "Stripe test mode is not configured. Add a Stripe sk_test_ key and whsec_ webhook secret before creating payment links.");

        var opportunity = await Crm.FindAsync<Opportunity>(request.OpportunityId);
        if (opportunity.Value <= 0) throw new CrmException(400, "Set an opportunity value greater than zero before creating a payment link.");
        if (opportunity.CurrencyCode != "PKR") throw new CrmException(400, "Payment links currently support PKR opportunities only.");
        var contact = await Crm.FindAsync<Contact>(opportunity.ContactId);
        var amountMinor = decimal.ToInt64(decimal.Round(opportunity.Value * 100m, 0, MidpointRounding.AwayFromZero));
        var payment = new PaymentRecord
        {
            WorkspaceId = workspaceId,
            OpportunityId = opportunity.Id,
            CreatedByUserId = Crm.Actor.Id,
            Amount = opportunity.Value,
            AmountMinor = amountMinor,
            CurrencyCode = "PKR",
            IdempotencyKey = key,
            Status = nameof(PaymentStatus.Pending)
        };
        db.PaymentRecords.Add(payment);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var session = await stripe.CreateCheckoutAsync(payment, opportunity, contact, cancellationToken);
            payment.StripeCheckoutSessionId = session.Id;
            payment.StripePaymentIntentId = session.PaymentIntentId;
            payment.CheckoutUrl = session.Url;
            payment.ExpiresAtUtc = session.ExpiresAtUtc;
            await integrations.MarkStatusAsync(workspaceId, "stripe", IntegrationStatus.Connected, cancellationToken: cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "stripe", EventType = "checkout.created",
                ExternalEventId = session.Id, Status = "Succeeded", Summary = $"Checkout link created for opportunity {opportunity.Id}."
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            payment.Status = nameof(PaymentStatus.Failed);
            payment.LastError = WorkspaceIntegrationService.SafeError(exception.Message);
            await integrations.MarkStatusAsync(workspaceId, "stripe", IntegrationStatus.Error, payment.LastError, cancellationToken);
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = workspaceId, Provider = "stripe", EventType = "checkout.failed",
                ExternalEventId = payment.Id.ToString("N"), Status = "Failed", Summary = "Checkout session creation failed."
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(payment));
    }

    [HttpPost("stripe/webhook"), AllowAnonymous, IgnoreAntiforgeryToken, RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> StripeWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        if (rawBody.Length > 1_000_000) return StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!stripe.VerifyWebhookSignature(rawBody, Request.Headers["Stripe-Signature"].ToString(), DateTime.UtcNow))
            return Unauthorized(new ProblemDetails { Status = 401, Title = "Stripe webhook signature is invalid or expired." });

        JsonDocument document;
        try { document = JsonDocument.Parse(rawBody); }
        catch (JsonException) { return BadRequest(new ProblemDetails { Status = 400, Title = "Invalid Stripe event payload." }); }
        using (document)
        {
            var root = document.RootElement;
            var eventId = GetString(root, "id");
            var eventType = GetString(root, "type");
            if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType) ||
                !root.TryGetProperty("data", out var data) || !data.TryGetProperty("object", out var item))
                return BadRequest(new ProblemDetails { Status = 400, Title = "Stripe event is missing required fields." });

            var objectId = GetString(item, "id");
            var paymentIntentId = GetStripeId(item, "payment_intent");
            Guid? metadataId = null;
            if (item.TryGetProperty("metadata", out var metadata) && Guid.TryParse(GetString(metadata, "paymentRecordId"), out var parsed)) metadataId = parsed;
            if (!metadataId.HasValue && Guid.TryParse(GetString(item, "client_reference_id"), out var referenceId)) metadataId = referenceId;

            var payment = metadataId.HasValue
                ? await db.PaymentRecords.SingleOrDefaultAsync(x => x.Id == metadataId.Value, cancellationToken)
                : await db.PaymentRecords.SingleOrDefaultAsync(x =>
                    (!string.IsNullOrEmpty(objectId) && x.StripeCheckoutSessionId == objectId) ||
                    (!string.IsNullOrEmpty(paymentIntentId) && x.StripePaymentIntentId == paymentIntentId), cancellationToken);
            if (payment is null) return Ok(new { received = true, ignored = true });

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var seen = await db.IntegrationSyncEvents.AnyAsync(x => x.WorkspaceId == payment.WorkspaceId &&
                x.Provider == "stripe" && x.ExternalEventId == eventId, cancellationToken);
            if (seen)
            {
                await transaction.CommitAsync(cancellationToken);
                return Ok(new { received = true, duplicate = true });
            }
            if (!IsValidPaymentObject(item, payment, eventType))
                return BadRequest(new ProblemDetails { Status = 400, Title = "Stripe payment details do not match the checkout record." });

            if (!string.IsNullOrWhiteSpace(objectId) && objectId.StartsWith("cs_", StringComparison.Ordinal))
                payment.StripeCheckoutSessionId = objectId;
            if (!string.IsNullOrWhiteSpace(paymentIntentId)) payment.StripePaymentIntentId = paymentIntentId;
            switch (eventType)
            {
                case "checkout.session.completed":
                case "checkout.session.async_payment_succeeded":
                case "payment_intent.succeeded":
                    if (eventType != "checkout.session.completed" || GetString(item, "payment_status") == "paid" || eventType == "payment_intent.succeeded")
                    {
                        payment.Status = nameof(PaymentStatus.Paid);
                        payment.PaidAtUtc ??= DateTime.UtcNow;
                        payment.LastError = null;
                    }
                    break;
                case "checkout.session.expired":
                    if (payment.Status == nameof(PaymentStatus.Pending)) payment.Status = nameof(PaymentStatus.Expired);
                    break;
                case "checkout.session.async_payment_failed":
                case "payment_intent.payment_failed":
                    if (payment.Status == nameof(PaymentStatus.Pending))
                    {
                        payment.Status = nameof(PaymentStatus.Failed);
                        payment.LastError = "Stripe reported that the payment failed.";
                    }
                    break;
                case "charge.refunded":
                    if (GetBoolean(item, "refunded") || GetLong(item, "amount_refunded") >= payment.AmountMinor)
                    {
                        payment.Status = nameof(PaymentStatus.Refunded);
                        payment.RefundedAtUtc = DateTime.UtcNow;
                        payment.LastError = null;
                    }
                    break;
            }
            db.IntegrationSyncEvents.Add(new IntegrationSyncEvent
            {
                WorkspaceId = payment.WorkspaceId, Provider = "stripe", EventType = eventType,
                ExternalEventId = eventId, Status = "Succeeded", Summary = $"Stripe event {eventType} processed for payment {payment.Id}."
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(new { received = true, paymentStatus = payment.Status });
        }
    }

    private static bool IsValidPaymentObject(JsonElement item, PaymentRecord payment, string eventType)
    {
        if (eventType.StartsWith("checkout.session.", StringComparison.Ordinal))
        {
            var amount = GetLong(item, "amount_total");
            var currency = GetString(item, "currency");
            return amount == payment.AmountMinor && string.Equals(currency, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase);
        }
        if (eventType.StartsWith("payment_intent.", StringComparison.Ordinal))
        {
            var amount = GetLong(item, "amount");
            var currency = GetString(item, "currency");
            return amount == payment.AmountMinor && string.Equals(currency, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase);
        }
        if (eventType == "charge.refunded")
            return GetString(item, "currency") is { } currency && string.Equals(currency, payment.CurrencyCode, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static string? GetStripeId(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) ? value.ValueKind == JsonValueKind.String ? value.GetString() : GetString(value, "id") : null;

    private static string? GetString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long GetLong(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.TryGetInt64(out var result) ? result : -1;

    private static bool GetBoolean(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static PaymentResponse ToResponse(PaymentRecord row)
    {
        var status = Enum.TryParse<PaymentStatus>(row.Status, out var parsed) ? parsed : PaymentStatus.Failed;
        return new(row.Id, row.OpportunityId, row.Amount, row.CurrencyCode, status, row.CheckoutUrl,
            row.CreatedAtUtc, row.PaidAtUtc, row.ExpiresAtUtc, row.RefundedAtUtc, row.LastError);
    }
}
