using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Configurations;

public sealed class ApartmentUnitTypeConfiguration : IEntityTypeConfiguration<ApartmentUnitType>
{
    public void Configure(EntityTypeBuilder<ApartmentUnitType> builder)
    {
        builder.ToTable("apartment_unit_types", "apartments", table =>
        {
            table.HasCheckConstraint("ck_apartment_type_default_area", "default_usable_area_m2 IS NULL OR default_usable_area_m2 >= 25");
            table.HasCheckConstraint("ck_apartment_type_default_bedrooms", "default_bedroom_count IS NULL OR default_bedroom_count >= 0");
            table.HasCheckConstraint("ck_apartment_type_default_bathrooms", "default_bathroom_count IS NULL OR default_bathroom_count >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.DefaultUsableAreaM2).HasColumnName("default_usable_area_m2").HasPrecision(10, 2);
        builder.Property(x => x.DefaultBedroomCount).HasColumnName("default_bedroom_count");
        builder.Property(x => x.DefaultBathroomCount).HasColumnName("default_bathroom_count");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
