using leadflow_crm.API.Models.Crm;
using leadflow_crm.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace leadflow_crm.API.Data;

public sealed class SavedListModelConfiguration : IEntityTypeConfiguration<SavedListRecord>
{
    public void Configure(EntityTypeBuilder<SavedListRecord> entity)
    {
        entity.ToTable("SavedLists");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
        entity.Property(x => x.EntityType).HasMaxLength(24).IsRequired();
        entity.Property(x => x.FiltersJson).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(x => x.RowVersion).IsRowVersion();
        entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => new { x.OwnerUserId, x.WorkspaceId })
            .HasPrincipalKey(x => new { x.Id, x.WorkspaceId }).OnDelete(DeleteBehavior.Restrict);
        entity.HasIndex(x => new { x.WorkspaceId, x.OwnerUserId, x.Name }).IsUnique();
        entity.HasIndex(x => new { x.WorkspaceId, x.EntityType, x.IsShared, x.UpdatedAtUtc });
    }
}
