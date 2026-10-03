using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Apartments.Domain.ApartmentUnits;

namespace PropFlow.Modules.Apartments.Infrastructure.Persistence.Configurations;

public sealed class ApartmentOwnershipConfiguration : IEntityTypeConfiguration<ApartmentOwnership>
{
    public void Configure(EntityTypeBuilder<ApartmentOwnership> builder)
    {
        builder.ToTable("apartment_ownerships", "apartments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ApartmentUnitId).HasColumnName("apartment_unit_id").IsRequired();
        builder.Property(x => x.OwnerResidentId).HasColumnName("owner_resident_id").IsRequired();
        builder.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
        builder.Property(x => x.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        // Ownership periods of different residents may overlap. PostgreSQL migration enforces one current row per apartment/resident pair.
        builder.HasIndex(x => new { x.ApartmentUnitId, x.StartDate });
        builder.HasIndex(x => x.OwnerResidentId);
        builder.HasOne<ApartmentUnit>().WithMany().HasForeignKey(x => x.ApartmentUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
