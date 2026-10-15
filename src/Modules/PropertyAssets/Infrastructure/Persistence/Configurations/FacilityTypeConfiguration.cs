using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.Configurations;

public sealed class FacilityTypeConfiguration : IEntityTypeConfiguration<FacilityType>
{
    public void Configure(EntityTypeBuilder<FacilityType> builder)
    {
        builder.ToTable("facility_types", "property_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.HasIndex(x => x.IsActive);
    }
}
