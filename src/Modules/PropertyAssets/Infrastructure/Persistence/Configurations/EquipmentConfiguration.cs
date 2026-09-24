using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Configurations;

public class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> builder)
    {
        builder.ToTable("equipment", "property_assets");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.FacilityId)
            .HasColumnName("facility_id");

        builder.Property(e => e.Code)
            .HasColumnName("code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.EquipmentType)
            .HasColumnName("equipment_type")
            .HasMaxLength(100);

        builder.Property(e => e.Manufacturer)
            .HasColumnName("manufacturer")
            .HasMaxLength(100);

        builder.Property(e => e.Model)
            .HasColumnName("model")
            .HasMaxLength(100);

        builder.Property(e => e.SerialNumber)
            .HasColumnName("serial_number")
            .HasMaxLength(100);

        builder.Property(e => e.InstallationDate)
            .HasColumnName("installation_date")
            .HasColumnType("date");

        builder.Property(e => e.WarrantyExpiryDate)
            .HasColumnName("warranty_expiry_date")
            .HasColumnType("date");

        builder.Property(e => e.LocationDescription)
            .HasColumnName("location_description")
            .HasMaxLength(255);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(EquipmentStatus.ACTIVE)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasColumnName("description");

        builder.Property(e => e.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(e => e.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Indexes
        builder.HasIndex(e => e.Code)
            .IsUnique();

        builder.HasIndex(e => e.FacilityId);
        builder.HasIndex(e => e.EquipmentType);
        builder.HasIndex(e => e.Status);

        // Within-module relationships
        builder.HasOne(e => e.Facility)
            .WithMany(f => f.Equipment)
            .HasForeignKey(e => e.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
