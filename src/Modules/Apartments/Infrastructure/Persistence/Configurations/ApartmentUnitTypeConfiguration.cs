using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Configurations;

public sealed class ApartmentUnitTypeConfiguration : IEntityTypeConfiguration<ApartmentUnitType>
{
    public void Configure(EntityTypeBuilder<ApartmentUnitType> builder)
    {
        builder.ToTable("apartment_unit_types", "apartments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
