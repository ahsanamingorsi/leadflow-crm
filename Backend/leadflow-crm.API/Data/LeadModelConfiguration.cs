using leadflow_crm.API.Models;
using leadflow_crm.API.Models.Crm;
using Microsoft.EntityFrameworkCore;

namespace leadflow_crm.API.Data;

public static class LeadModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>().Property(x => x.NotificationEmail).HasMaxLength(256);
        builder.Entity<Workspace>().Property(x => x.PublicIntakeTokenHash).HasMaxLength(64);
        builder.Entity<Workspace>().HasIndex(x => x.PublicIntakeTokenHash).IsUnique().HasFilter("[PublicIntakeTokenHash] IS NOT NULL");

        var availability = builder.Entity<EmployeeAvailability>();
        availability.ToTable("EmployeeAvailabilities");
        availability.HasKey(x => new { x.WorkspaceId, x.UserId });
        availability.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        availability.Property(x => x.RowVersion).IsRowVersion();
        availability.Property(x => x.ChangedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        availability.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        availability.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.UserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        availability.HasIndex(x => new { x.WorkspaceId, x.Status, x.UserId });

        var settings = builder.Entity<LeadAssignmentConfiguration>();
        settings.ToTable("LeadAssignmentConfigurations");
        settings.HasKey(x => x.WorkspaceId);
        settings.Property(x => x.RowVersion).IsRowVersion();
        settings.Property(x => x.EscalationUserIdsJson).HasColumnType("nvarchar(max)");
        settings.Property(x => x.EscalationIntervalsSecondsJson).HasColumnType("nvarchar(max)");
        settings.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        settings.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.RoundRobinLastUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);

        var intake = builder.Entity<LeadIntake>();
        intake.ToTable("LeadIntakes");
        intake.HasKey(x => x.Id);
        intake.HasAlternateKey(x => new { x.Id, x.WorkspaceId });
        intake.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
        intake.Property(x => x.ReceivedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        intake.Property(x => x.LastAttemptAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        intake.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        intake.HasOne<Contact>().WithMany().HasForeignKey(x => new { x.MatchedContactId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        intake.HasIndex(x => new { x.WorkspaceId, x.IdempotencyKeyHash }).IsUnique();
        intake.HasIndex(x => new { x.WorkspaceId, x.Status, x.ReceivedAtUtc });

        var lead = builder.Entity<Lead>();
        lead.ToTable("Leads");
        lead.HasKey(x => x.Id);
        lead.HasAlternateKey(x => new { x.Id, x.WorkspaceId });
        lead.Property(x => x.RowVersion).IsRowVersion();
        lead.Property(x => x.Source).HasMaxLength(20).IsRequired();
        lead.Property(x => x.SourceDetail).HasMaxLength(120);
        lead.Property(x => x.AttentionReason).HasMaxLength(500);
        lead.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        lead.Property(x => x.UpdatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        lead.Property(x => x.AssignedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        lead.Property(x => x.AcknowledgementDeadlineUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        lead.Property(x => x.AcknowledgedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        lead.Property(x => x.LastActionAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        lead.Property(x => x.NextEscalationAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        lead.Property(x => x.ClosedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        lead.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        lead.HasOne(x => x.Contact).WithMany().HasForeignKey(x => new { x.ContactId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        lead.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.OwnerUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        lead.HasOne<LeadIntake>().WithMany().HasForeignKey(x => new { x.IntakeId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        lead.HasIndex(x => new { x.WorkspaceId, x.OwnerUserId, x.IsClosed, x.AcknowledgedAtUtc });
        lead.HasIndex(x => new { x.WorkspaceId, x.IsClosed, x.NextEscalationAtUtc });
        lead.HasIndex(x => new { x.WorkspaceId, x.LastActionAtUtc, x.IsClosed });

        var history = builder.Entity<LeadAssignmentHistory>();
        history.ToTable("LeadAssignmentHistory", table => table.HasCheckConstraint("CK_LeadAssignmentHistory_ActorWorkspace",
            "([ActorUserId] IS NULL AND [ActorWorkspaceId] IS NULL) OR ([ActorUserId] IS NOT NULL AND [ActorWorkspaceId] = [WorkspaceId])"));
        history.HasKey(x => x.Id);
        history.Property(x => x.ActorType).HasMaxLength(20).IsRequired();
        history.Property(x => x.Reason).HasMaxLength(80).IsRequired();
        history.Property(x => x.AssignedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        history.Property(x => x.AcknowledgementDeadlineUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        history.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        history.HasOne<Lead>().WithMany().HasForeignKey(x => new { x.LeadId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.NewOwnerUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.PreviousOwnerUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        history.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.ActorUserId, x.ActorWorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        history.HasIndex(x => new { x.WorkspaceId, x.LeadId, x.AssignedAtUtc });

        var notification = builder.Entity<LeadNotification>();
        notification.ToTable("LeadNotifications");
        notification.HasKey(x => x.Id);
        notification.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        notification.Property(x => x.RowVersion).IsRowVersion();
        notification.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        notification.Property(x => x.NextAttemptAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        notification.Property(x => x.SentAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        notification.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        notification.HasOne<Lead>().WithMany().HasForeignKey(x => new { x.LeadId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        notification.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.RecipientUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        notification.HasIndex(x => new { x.Status, x.NextAttemptAtUtc, x.CreatedAtUtc });
        notification.HasIndex(x => new { x.WorkspaceId, x.CreatedAtUtc });

        var userNotification = builder.Entity<LeadUserNotification>();
        userNotification.ToTable("LeadUserNotifications");
        userNotification.HasKey(x => x.Id);
        userNotification.Property(x => x.RowVersion).IsRowVersion();
        userNotification.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        userNotification.Property(x => x.ReadAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        userNotification.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        userNotification.HasOne<Lead>().WithMany().HasForeignKey(x => new { x.LeadId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        userNotification.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.RecipientUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        userNotification.HasIndex(x => new { x.WorkspaceId, x.RecipientUserId, x.ReadAtUtc, x.CreatedAtUtc });
    }
}
