using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Configurations;

public class FacilityConfiguration : IEntityTypeConfiguration<Facility>
{
    public void Configure(EntityTypeBuilder<Facility> builder)
    {
        builder.ToTable("facilities", "property_assets");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasColumnName("id");

        builder.Property(f => f.BuildingId)
            .HasColumnName("building_id")
            .IsRequired();

        builder.Property(f => f.Code)
            .HasColumnName("code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(f => f.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(f => f.FacilityType)
            .HasColumnName("facility_type")
            .HasMaxLength(80);

        builder.Property(f => f.LocationDescription)
            .HasColumnName("location_description")
            .HasMaxLength(255);

        builder.Property(f => f.Description)
            .HasColumnName("description");

        builder.Property(f => f.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(MasterDataStatus.ACTIVE)
            .IsRequired();

        builder.Property(f => f.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(f => f.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(f => f.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Indexes
        builder.HasIndex(f => new { f.BuildingId, f.Code })
            .IsUnique();

        builder.HasIndex(f => f.BuildingId);
        builder.HasIndex(f => f.FacilityType);
        builder.HasIndex(f => f.Status);

        // Within-module relationships
        builder.HasOne(f => f.Building)
            .WithMany(b => b.Facilities)
            .HasForeignKey(f => f.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(f => f.Equipment)
            .WithOne(e => e.Facility)
            .HasForeignKey(e => e.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
