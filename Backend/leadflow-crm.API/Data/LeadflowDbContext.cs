using leadflow_crm.API.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using leadflow_crm.API.Models.Crm;

namespace leadflow_crm.API.Data;

public sealed class LeadflowDbContext(
    DbContextOptions<LeadflowDbContext> options)
    : IdentityDbContext<
        ApplicationUser,
        ApplicationRole,
        Guid,
        IdentityUserClaim<Guid>,
        ApplicationUserRole,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<WorkspaceInvitation> WorkspaceInvitations => Set<WorkspaceInvitation>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadIntake> LeadIntakes => Set<LeadIntake>();
    public DbSet<LeadAssignmentHistory> LeadAssignmentHistory => Set<LeadAssignmentHistory>();
    public DbSet<LeadAssignmentConfiguration> LeadAssignmentConfigurations => Set<LeadAssignmentConfiguration>();
    public DbSet<EmployeeAvailability> EmployeeAvailabilities => Set<EmployeeAvailability>();
    public DbSet<LeadNotification> LeadNotifications => Set<LeadNotification>();
    public DbSet<LeadUserNotification> LeadUserNotifications => Set<LeadUserNotification>();
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        CrmModelConfiguration.Configure(builder);
        LeadModelConfiguration.Configure(builder);

        builder.Entity<Workspace>(entity =>
        {
            entity.ToTable("Workspaces");
            entity.HasKey(workspace => workspace.Id);
            entity.Property(workspace => workspace.Name).HasMaxLength(120).IsRequired();
            entity.Property(workspace => workspace.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(workspace => workspace.TimeZoneId).HasMaxLength(80).IsRequired();
            entity.HasIndex(workspace => workspace.Name);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName).HasMaxLength(120).IsRequired();
            entity.HasAlternateKey(user => new { user.Id, user.WorkspaceId });
            entity.HasOne(user => user.Workspace)
                .WithMany(workspace => workspace.Users)
                .HasForeignKey(user => user.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(user => new { user.WorkspaceId, user.IsActive });
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.Property(role => role.DisplayName).HasMaxLength(80).IsRequired();
            entity.HasAlternateKey(role => new { role.Id, role.WorkspaceId });
            entity.HasOne(role => role.Workspace)
                .WithMany(workspace => workspace.Roles)
                .HasForeignKey(role => role.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(role => new { role.WorkspaceId, role.NormalizedName }).IsUnique();
            entity.HasIndex(role => new { role.WorkspaceId, role.DisplayName }).IsUnique();
        });

        builder.Entity<ApplicationUserRole>(entity =>
        {
            entity.Property(userRole => userRole.WorkspaceId).IsRequired();
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(userRole => new { userRole.UserId, userRole.WorkspaceId })
                .HasPrincipalKey(user => new { user.Id, user.WorkspaceId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationRole>()
                .WithMany()
                .HasForeignKey(userRole => new { userRole.RoleId, userRole.WorkspaceId })
                .HasPrincipalKey(role => new { role.Id, role.WorkspaceId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasKey(permission => new
            {
                permission.WorkspaceId,
                permission.RoleId,
                permission.PermissionKey
            });
            entity.Property(permission => permission.PermissionKey).HasMaxLength(120).IsRequired();
            entity.HasOne(permission => permission.Role)
                .WithMany(role => role.Permissions)
                .HasForeignKey(permission => new { permission.RoleId, permission.WorkspaceId })
                .HasPrincipalKey(role => new { role.Id, role.WorkspaceId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(permission => new { permission.WorkspaceId, permission.PermissionKey });
        });

        builder.Entity<AuditLogEntry>(entity =>
        {
            entity.ToTable("AuditLogs", table => table.HasCheckConstraint(
                "CK_AuditLogs_ActorWorkspace",
                "([ActorUserId] IS NULL AND [ActorWorkspaceId] IS NULL) OR " +
                "([ActorUserId] IS NOT NULL AND [ActorWorkspaceId] = [WorkspaceId])"));
            entity.HasKey(entry => entry.Id);
            entity.Property(entry => entry.ActorType).HasMaxLength(20).IsRequired();
            entity.Property(entry => entry.EventType).HasMaxLength(120).IsRequired();
            entity.Property(entry => entry.TargetType).HasMaxLength(80).IsRequired();
            entity.Property(entry => entry.TargetId).HasMaxLength(120).IsRequired();
            entity.Property(entry => entry.BeforeJson).HasColumnType("nvarchar(max)");
            entity.Property(entry => entry.AfterJson).HasColumnType("nvarchar(max)");
            entity.HasOne(entry => entry.Workspace)
                .WithMany()
                .HasForeignKey(entry => entry.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(entry => entry.ActorUser)
                .WithMany()
                .HasForeignKey(entry => new { entry.ActorUserId, entry.ActorWorkspaceId })
                .HasPrincipalKey(user => new { user.Id, user.WorkspaceId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(entry => new { entry.WorkspaceId, entry.OccurredAtUtc });
            entity.HasIndex(entry => new { entry.WorkspaceId, entry.EventType, entry.OccurredAtUtc });
        });

        builder.Entity<WorkspaceInvitation>(entity =>
        {
            entity.ToTable("WorkspaceInvitations", table => table.HasCheckConstraint(
                "CK_WorkspaceInvitations_InviterWorkspace",
                "[InvitedByWorkspaceId] = [WorkspaceId]"));
            entity.HasKey(invitation => invitation.Id);
            entity.Property(invitation => invitation.Email).HasMaxLength(256).IsRequired();
            entity.Property(invitation => invitation.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(invitation => invitation.TokenHash).IsUnique();
            entity.HasIndex(invitation => new
            {
                invitation.WorkspaceId,
                invitation.Email,
                invitation.AcceptedAtUtc,
                invitation.RevokedAtUtc
            });
            entity.HasOne(invitation => invitation.Workspace)
                .WithMany()
                .HasForeignKey(invitation => invitation.WorkspaceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(invitation => invitation.Role)
                .WithMany()
                .HasForeignKey(invitation => new { invitation.RoleId, invitation.WorkspaceId })
                .HasPrincipalKey(role => new { role.Id, role.WorkspaceId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(invitation => invitation.InvitedByUser)
                .WithMany()
                .HasForeignKey(invitation => new
                {
                    invitation.InvitedByUserId,
                    invitation.InvitedByWorkspaceId
                })
                .HasPrincipalKey(user => new { user.Id, user.WorkspaceId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
