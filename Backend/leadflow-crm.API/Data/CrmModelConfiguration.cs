using leadflow_crm.API.Models;
using leadflow_crm.API.Models.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace leadflow_crm.API.Data;

public static class CrmModelConfiguration
{
    public static void Configure(ModelBuilder builder)
    {
        ConfigureRecord<Company>(builder, "CrmCompanies");
        ConfigureRecord<Contact>(builder, "CrmContacts");
        ConfigureRecord<Pipeline>(builder, "CrmPipelines");
        ConfigureRecord<PipelineStage>(builder, "CrmPipelineStages");
        ConfigureRecord<Opportunity>(builder, "CrmOpportunities");
        ConfigureRecord<CustomFieldDefinition>(builder, "CrmCustomFields");

        builder.Entity<Contact>().HasOne<Company>().WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Contact>().HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => new { x.OwnerUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Contact>().HasIndex(x => new { x.WorkspaceId, x.Email });
        builder.Entity<Contact>().HasIndex(x => new { x.WorkspaceId, x.Phone });
        builder.Entity<Pipeline>().HasIndex(x => x.WorkspaceId).IsUnique()
            .HasFilter("[IsDefault] = 1 AND [IsDeleted] = 0");
        builder.Entity<Pipeline>().HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.Entity<PipelineStage>().HasOne<Pipeline>().WithMany()
            .HasForeignKey(x => new { x.PipelineId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PipelineStage>().HasAlternateKey(x => new { x.Id, x.PipelineId, x.WorkspaceId });
        builder.Entity<PipelineStage>().Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Entity<PipelineStage>().HasIndex(x => new { x.WorkspaceId, x.PipelineId, x.Name })
            .IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Entity<PipelineStage>().HasIndex(x => new { x.WorkspaceId, x.PipelineId, x.Position });
        builder.Entity<Opportunity>().HasOne<Contact>().WithMany()
            .HasForeignKey(x => new { x.ContactId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Opportunity>().HasOne<PipelineStage>().WithMany()
            .HasForeignKey(x => new { x.StageId, x.PipelineId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.PipelineId, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Opportunity>().HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => new { x.OwnerUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Opportunity>().Property(x => x.Value).HasPrecision(18, 2);
        builder.Entity<Opportunity>().ToTable("CrmOpportunities", t =>
            t.HasCheckConstraint("CK_CrmOpportunities_Value", "[Value] >= 0 AND [CurrencyCode] = 'PKR'"));
        builder.Entity<Opportunity>().HasIndex(x => new { x.WorkspaceId, x.OwnerUserId, x.StageId });
        builder.Entity<CustomFieldDefinition>().HasOne<Pipeline>().WithMany()
            .HasForeignKey(x => new { x.PipelineId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CustomFieldDefinition>().Property(x => x.EntityType).HasConversion<string>().HasMaxLength(20);
        builder.Entity<CustomFieldDefinition>().Property(x => x.FieldType).HasConversion<string>().HasMaxLength(20);
        builder.Entity<CustomFieldDefinition>().HasIndex(x => new { x.WorkspaceId, x.EntityType, x.PipelineId, x.Name })
            .IsUnique().HasFilter("[IsDeleted] = 0");
        var required = builder.Entity<StageRequiredField>();
        required.ToTable("CrmStageRequiredFields");
        required.HasKey(x => new { x.WorkspaceId, x.StageId, x.FieldId });
        required.HasOne<PipelineStage>().WithMany().HasForeignKey(x => new { x.StageId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        required.HasOne<CustomFieldDefinition>().WithMany().HasForeignKey(x => new { x.FieldId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        var activity = builder.Entity<CrmActivity>();
        activity.ToTable("CrmActivities");
        activity.HasKey(x => x.Id);
        activity.Property(x => x.OccurredAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        activity.Property(x => x.EntityType).HasConversion<string>().HasMaxLength(20);
        activity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.ActorUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        activity.HasIndex(x => new { x.WorkspaceId, x.EntityType, x.RecordId, x.OccurredAtUtc });
    }

    private static EntityTypeBuilder<T> ConfigureRecord<T>(ModelBuilder builder, string table) where T : CrmRecord
    {
        var entity = builder.Entity<T>();
        entity.HasBaseType((Type?)null);
        entity.ToTable(table);
        entity.HasKey(x => x.Id);
        entity.HasAlternateKey(x => new { x.Id, x.WorkspaceId });
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        entity.Property(x => x.UpdatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.WorkspaceId, x.IsDeleted, x.CreatedAtUtc });
        return entity;
    }
}
