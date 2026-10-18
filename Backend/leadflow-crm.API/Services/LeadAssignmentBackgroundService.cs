using leadflow_crm.API.Data;
using leadflow_crm.API.Models.Crm;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Services;

public sealed class LeadAssignmentBackgroundService(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<LeadAssignmentBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = Math.Clamp(configuration.GetValue("LeadWorkflow:PollIntervalSeconds", 5), 1, 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var workflow = scope.ServiceProvider.GetRequiredService<LeadWorkflowService>();
                await workflow.ProcessPendingIntakesAsync(stoppingToken);
                await workflow.ProcessDueLeadsAsync(stoppingToken);
                await SendPendingEmailsAsync(scope.ServiceProvider.GetRequiredService<LeadflowDbContext>(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Lead workflow background cycle failed; it will retry."); }

            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task SendPendingEmailsAsync(LeadflowDbContext db, CancellationToken cancellationToken)
    {
        var smtpHost = configuration["Notifications:Smtp:Host"];
        var from = configuration["Notifications:Smtp:FromAddress"];
        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(from)) return;

        var dueIds = await db.LeadNotifications.Where(x => x.Status == LeadNotificationStatus.Pending &&
                x.NextAttemptAtUtc <= DateTime.UtcNow)
            .OrderBy(x => x.NextAttemptAtUtc).Select(x => x.Id).Take(20).ToListAsync(cancellationToken);
        foreach (var id in dueIds)
        {
            var item = await db.LeadNotifications.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (item is null || item.Status != LeadNotificationStatus.Pending) continue;
            item.AttemptCount++;
            try
            {
                await SmtpLeadEmailSender.SendAsync(configuration, from, item.RecipientEmail, item.Subject, item.BodyText, cancellationToken);
                item.Status = LeadNotificationStatus.Sent;
                item.SentAtUtc = DateTime.UtcNow;
                item.LastError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                item.LastError = exception.Message.Length > 1000 ? exception.Message[..1000] : exception.Message;
                if (item.AttemptCount >= 3) item.Status = LeadNotificationStatus.Failed;
                else item.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(item.AttemptCount == 1 ? 15 : 60);
                logger.LogWarning("Email delivery {NotificationId} failed on attempt {AttemptCount}.", item.Id, item.AttemptCount);
            }
            try { await db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { db.Entry(item).State = EntityState.Detached; }
        }
    }
}

internal static class SmtpLeadEmailSender
{
    public static async Task SendAsync(IConfiguration configuration, string from, string to, string subject, string body,
        CancellationToken cancellationToken)
    {
        using var message = new System.Net.Mail.MailMessage(from, to, subject, body);
        using var client = new System.Net.Mail.SmtpClient(configuration["Notifications:Smtp:Host"]!)
        {
            Port = configuration.GetValue("Notifications:Smtp:Port", 587),
            EnableSsl = configuration.GetValue("Notifications:Smtp:EnableSsl", true),
            Timeout = 15000
        };
        var username = configuration["Notifications:Smtp:Username"];
        var password = configuration["Notifications:Smtp:Password"];
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new System.Net.NetworkCredential(username, password);
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}
