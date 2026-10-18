using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using leadflow_crm.API.Data;
using leadflow_crm.API.DTOs.Crm;
using leadflow_crm.API.Models.Crm;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Services;

public sealed class IntegrationSecretProtector(IDataProtectionProvider dataProtection)
{
    public string Protect(Guid workspaceId, string purpose, string value) =>
        dataProtection.CreateProtector("LeadFlow.Integrations.v1", workspaceId.ToString("N"), purpose).Protect(value);

    public string Unprotect(Guid workspaceId, string purpose, string value) =>
        dataProtection.CreateProtector("LeadFlow.Integrations.v1", workspaceId.ToString("N"), purpose).Unprotect(value);
}

public sealed record EmailSmtpSettings(string Host, int Port, string FromAddress, bool EnableSsl,
    string? Username, string? Password, string? InboundWebhookSecret);

public sealed class WorkspaceIntegrationService(
    LeadflowDbContext db,
    IntegrationSecretProtector protector)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<WorkspaceIntegration?> FindAsync(Guid workspaceId, string provider,
        CancellationToken cancellationToken = default) =>
        await db.WorkspaceIntegrations.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Provider == provider, cancellationToken);

    public async Task<EmailSmtpSettings?> GetEmailSettingsAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var integration = await FindAsync(workspaceId, "email", cancellationToken);
        return integration is null || string.IsNullOrWhiteSpace(integration.EncryptedSettingsJson)
            ? null
            : JsonSerializer.Deserialize<EmailSmtpSettings>(protector.Unprotect(workspaceId, "email.settings", integration.EncryptedSettingsJson), JsonOptions);
    }

    public async Task<WorkspaceIntegration> SaveEmailSettingsAsync(Guid workspaceId, EmailSmtpSettings settings,
        CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(workspaceId, "email", cancellationToken);
        if (row is null)
        {
            row = new WorkspaceIntegration { WorkspaceId = workspaceId, Provider = "email" };
            db.WorkspaceIntegrations.Add(row);
        }
        row.EncryptedSettingsJson = protector.Protect(workspaceId, "email.settings", JsonSerializer.Serialize(settings, JsonOptions));
        row.Status = nameof(IntegrationStatus.Configured);
        row.ConnectedAtUtc = null;
        row.LastError = null;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }

    public async Task MarkStatusAsync(Guid workspaceId, string provider, IntegrationStatus status,
        string? error = null, CancellationToken cancellationToken = default)
    {
        var row = await FindAsync(workspaceId, provider, cancellationToken);
        if (row is null)
        {
            row = new WorkspaceIntegration { WorkspaceId = workspaceId, Provider = provider };
            db.WorkspaceIntegrations.Add(row);
        }
        row.Status = status.ToString();
        row.LastError = SafeError(error);
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.ConnectedAtUtc = status == IntegrationStatus.Connected ? DateTime.UtcNow : row.ConnectedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    public EmailIntegrationResponse ToEmailResponse(WorkspaceIntegration? row, EmailSmtpSettings? settings)
    {
        var status = Enum.TryParse<IntegrationStatus>(row?.Status, out var parsed) ? parsed : IntegrationStatus.NotConfigured;
        return new(status, settings?.Host, settings?.Port, settings?.FromAddress, settings?.EnableSsl ?? true,
            !string.IsNullOrWhiteSpace(settings?.Username) && !string.IsNullOrWhiteSpace(settings.Password),
            !string.IsNullOrWhiteSpace(settings?.InboundWebhookSecret), row?.ConnectedAtUtc, row?.LastError);
    }

    public static string? SafeError(string? message) => string.IsNullOrWhiteSpace(message)
        ? null : message.Length > 1000 ? message[..1000] : message;
}

public sealed class WorkspaceEmailSender(WorkspaceIntegrationService integrations)
{
    public static string CreateMessageId(string fromAddress)
    {
        var domain = MailAddress.TryCreate(fromAddress, out var address) ? address.Host : "leadflow.local";
        return $"<{Guid.NewGuid():N}@{domain}>";
    }

    public async Task SendAsync(Guid workspaceId, string to, string subject, string body,
        string? inReplyTo = null, string? references = null, string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await integrations.GetEmailSettingsAsync(workspaceId, cancellationToken)
            ?? throw new InvalidOperationException("Email is not configured for this workspace.");
        using var message = new MailMessage(settings.FromAddress, to, subject, body);
        if (!string.IsNullOrWhiteSpace(inReplyTo)) message.Headers.Add("In-Reply-To", inReplyTo);
        if (!string.IsNullOrWhiteSpace(references)) message.Headers.Add("References", references);
        message.Headers.Add("Message-ID", messageId ?? CreateMessageId(settings.FromAddress));
        using var client = new SmtpClient(settings.Host)
        {
            Port = settings.Port,
            EnableSsl = settings.EnableSsl,
            Timeout = 20000
        };
        if (!string.IsNullOrWhiteSpace(settings.Username))
            client.Credentials = new System.Net.NetworkCredential(settings.Username, settings.Password);
        await client.SendMailAsync(message, cancellationToken);
    }
}

public static class EmailWebhookSignature
{
    public static bool IsValid(string rawBody, string timestampHeader, string signatureHeader, string secret,
        DateTime utcNow, TimeSpan? tolerance = null)
    {
        if (!long.TryParse(timestampHeader, out var timestamp) || string.IsNullOrWhiteSpace(signatureHeader)) return false;
        DateTime signedAt;
        try { signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return false; }
        if ((utcNow - signedAt).Duration() > (tolerance ?? TimeSpan.FromMinutes(5))) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signatureHeader); }
        catch (FormatException) { return false; }
        var payload = Encoding.UTF8.GetBytes($"{timestampHeader}.{rawBody}");
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload);
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected);
    }
}
