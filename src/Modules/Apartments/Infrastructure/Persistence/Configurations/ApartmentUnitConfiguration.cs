using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Configurations;

public class ApartmentUnitConfiguration : IEntityTypeConfiguration<ApartmentUnit>
{
    public void Configure(EntityTypeBuilder<ApartmentUnit> builder)
    {
        builder.ToTable("apartment_units", "apartments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id");

        

        builder.Property(a => a.UnitNumber)
            .HasColumnName("unit_number")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.FloorNumber)
            .HasColumnName("floor_number")
            .IsRequired();

        builder.Property(a => a.AreaM2)
            .HasColumnName("area_m2")
            .HasPrecision(10, 2);

        builder.Property(a => a.BedroomCount)
            .HasColumnName("bedroom_count");

        builder.Property(a => a.Description)
            .HasColumnName("description");

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(MasterDataStatus.ACTIVE)
            .IsRequired();

        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(a => a.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Indexes
        builder.HasIndex(a => a.UnitNumber)
            .IsUnique();

        builder.HasIndex(a => a.FloorNumber);
        builder.HasIndex(a => a.Status);
    }
}
