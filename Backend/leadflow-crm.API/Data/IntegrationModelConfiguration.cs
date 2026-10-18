using leadflow_crm.API.Models;
using leadflow_crm.API.Models.Crm;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Data;

public static class IntegrationModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        var integration = builder.Entity<WorkspaceIntegration>();
        integration.ToTable("WorkspaceIntegrations");
        integration.HasKey(x => x.Id);
        integration.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        integration.Property(x => x.Status).HasMaxLength(24).IsRequired();
        integration.Property(x => x.EncryptedSettingsJson).HasColumnType("nvarchar(max)");
        integration.Property(x => x.CreatedAtUtc).AsUtc();
        integration.Property(x => x.UpdatedAtUtc).AsUtc();
        integration.Property(x => x.ConnectedAtUtc).AsUtcNullable();
        integration.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        integration.HasIndex(x => new { x.WorkspaceId, x.Provider }).IsUnique();

        var sync = builder.Entity<IntegrationSyncEvent>();
        sync.ToTable("IntegrationSyncEvents");
        sync.HasKey(x => x.Id);
        sync.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        sync.Property(x => x.EventType).HasMaxLength(80).IsRequired();
        sync.Property(x => x.Status).HasMaxLength(16).IsRequired();
        sync.Property(x => x.Summary).HasMaxLength(1000).IsRequired();
        sync.Property(x => x.OccurredAtUtc).AsUtc();
        sync.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        sync.HasIndex(x => new { x.WorkspaceId, x.Provider, x.ExternalEventId }).IsUnique()
            .HasFilter("[ExternalEventId] IS NOT NULL");
        sync.HasIndex(x => new { x.WorkspaceId, x.OccurredAtUtc });

        var conversation = builder.Entity<EmailConversation>();
        conversation.ToTable("EmailConversations");
        conversation.HasKey(x => x.Id);
        conversation.HasAlternateKey(x => new { x.Id, x.WorkspaceId });
        conversation.Property(x => x.ExternalThreadId).HasMaxLength(255).IsRequired();
        conversation.Property(x => x.ParticipantEmail).HasMaxLength(256).IsRequired();
        conversation.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        conversation.Property(x => x.CreatedAtUtc).AsUtc();
        conversation.Property(x => x.LastMessageAtUtc).AsUtc();
        conversation.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        conversation.HasOne<Contact>().WithMany().HasForeignKey(x => new { x.ContactId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        conversation.HasOne<Lead>().WithMany().HasForeignKey(x => new { x.LeadId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        conversation.HasIndex(x => new { x.WorkspaceId, x.ExternalThreadId }).IsUnique();
        conversation.HasIndex(x => new { x.WorkspaceId, x.LastMessageAtUtc });

        var message = builder.Entity<EmailMessage>();
        message.ToTable("EmailMessages");
        message.HasKey(x => x.Id);
        message.Property(x => x.Direction).HasConversion<string>().HasMaxLength(12);
        message.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        message.Property(x => x.FromAddress).HasMaxLength(256).IsRequired();
        message.Property(x => x.ToAddress).HasMaxLength(256).IsRequired();
        message.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        message.Property(x => x.BodyText).HasColumnType("nvarchar(max)");
        message.Property(x => x.MessageId).HasMaxLength(998);
        message.Property(x => x.InReplyTo).HasMaxLength(998);
        message.Property(x => x.References).HasMaxLength(2000);
        message.Property(x => x.ProviderMessageId).HasMaxLength(1000);
        message.Property(x => x.CreatedAtUtc).AsUtc();
        message.Property(x => x.DeliveredAtUtc).AsUtcNullable();
        message.HasOne<EmailConversation>().WithMany(x => x.Messages)
            .HasForeignKey(x => new { x.ConversationId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        message.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        message.HasIndex(x => new { x.WorkspaceId, x.ConversationId, x.CreatedAtUtc });
        message.HasIndex(x => new { x.WorkspaceId, x.ProviderMessageId }).IsUnique()
            .HasFilter("[ProviderMessageId] IS NOT NULL");

        var payment = builder.Entity<PaymentRecord>();
        payment.ToTable("PaymentRecords");
        payment.HasKey(x => x.Id);
        payment.Property(x => x.Amount).HasPrecision(18, 2);
        payment.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        payment.Property(x => x.Status).HasMaxLength(20).IsRequired();
        payment.Property(x => x.IdempotencyKey).HasMaxLength(255).IsRequired();
        payment.Property(x => x.CreatedAtUtc).AsUtc();
        payment.Property(x => x.PaidAtUtc).AsUtcNullable();
        payment.Property(x => x.ExpiresAtUtc).AsUtcNullable();
        payment.Property(x => x.RefundedAtUtc).AsUtcNullable();
        payment.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        payment.HasOne<Opportunity>().WithMany().HasForeignKey(x => new { x.OpportunityId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        payment.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.CreatedByUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        payment.HasIndex(x => new { x.WorkspaceId, x.IdempotencyKey }).IsUnique();
        payment.HasIndex(x => new { x.WorkspaceId, x.OpportunityId, x.CreatedAtUtc });
        payment.HasIndex(x => new { x.WorkspaceId, x.StripeCheckoutSessionId }).IsUnique()
            .HasFilter("[StripeCheckoutSessionId] IS NOT NULL");
        payment.HasIndex(x => new { x.WorkspaceId, x.StripePaymentIntentId })
            .HasFilter("[StripePaymentIntentId] IS NOT NULL");

        var connection = builder.Entity<CalendarConnection>();
        connection.ToTable("CalendarConnections");
        connection.HasKey(x => x.Id);
        connection.Property(x => x.EmailAddress).HasMaxLength(256).IsRequired();
        connection.Property(x => x.CalendarId).HasMaxLength(255).IsRequired();
        connection.Property(x => x.EncryptedRefreshToken).HasColumnType("nvarchar(max)").IsRequired();
        connection.Property(x => x.ConnectedAtUtc).AsUtc();
        connection.Property(x => x.LastCheckedAtUtc).AsUtcNullable();
        connection.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        connection.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.UserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        connection.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique();

        var oauth = builder.Entity<CalendarOAuthState>();
        oauth.ToTable("CalendarOAuthStates");
        oauth.HasKey(x => x.Id);
        oauth.Property(x => x.StateHash).HasMaxLength(64).IsRequired();
        oauth.Property(x => x.RedirectUri).HasMaxLength(512).IsRequired();
        oauth.Property(x => x.ExpiresAtUtc).AsUtc();
        oauth.Property(x => x.UsedAtUtc).AsUtcNullable();
        oauth.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        oauth.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.UserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        oauth.HasIndex(x => x.StateHash).IsUnique();
        oauth.HasIndex(x => x.ExpiresAtUtc);

        var appointment = builder.Entity<CalendarAppointment>();
        appointment.ToTable("CalendarAppointments");
        appointment.HasKey(x => x.Id);
        appointment.Property(x => x.Title).HasMaxLength(200).IsRequired();
        appointment.Property(x => x.Description).HasMaxLength(4000);
        appointment.Property(x => x.AttendeeEmail).HasMaxLength(256);
        appointment.Property(x => x.TimeZoneId).HasMaxLength(80).IsRequired();
        appointment.Property(x => x.Status).HasMaxLength(20).IsRequired();
        appointment.Property(x => x.StartsAtUtc).AsUtc();
        appointment.Property(x => x.EndsAtUtc).AsUtc();
        appointment.Property(x => x.CreatedAtUtc).AsUtc();
        appointment.Property(x => x.UpdatedAtUtc).AsUtc();
        appointment.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        appointment.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.CreatedByUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        appointment.HasOne<Contact>().WithMany().HasForeignKey(x => new { x.ContactId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        appointment.HasOne<Opportunity>().WithMany().HasForeignKey(x => new { x.OpportunityId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        appointment.HasIndex(x => new { x.WorkspaceId, x.StartsAtUtc, x.Status });
        appointment.HasIndex(x => new { x.WorkspaceId, x.GoogleEventId }).IsUnique()
            .HasFilter("[GoogleEventId] IS NOT NULL");
    }

    private static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime> AsUtc(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime> property) =>
        property.HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime?> AsUtcNullable(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTime?> property) =>
        property.HasConversion(value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);
}
