using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Residents.Domain.ResidentApartments;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Configurations;

public class ResidentApartmentConfiguration : IEntityTypeConfiguration<ResidentApartment>
{
    public void Configure(EntityTypeBuilder<ResidentApartment> builder)
    {
        builder.ToTable("resident_apartments", "residents");

        builder.HasKey(ra => ra.Id);
        builder.Property(ra => ra.Id)
            .HasColumnName("id");

        builder.Property(ra => ra.ResidentId)
            .HasColumnName("resident_id")
            .IsRequired();

        // Cross-module scalar ID to apartments.apartment_units.id
        builder.Property(ra => ra.ApartmentUnitId)
            .HasColumnName("apartment_unit_id")
            .IsRequired();

        builder.Property(ra => ra.RelationshipTypeCode)
            .HasColumnName("relationship_type_code")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(ra => ra.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(ra => ra.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(ra => ra.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("date");

        builder.Property(ra => ra.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ResidencyStatus.ACTIVE)
            .IsRequired();

        builder.Property(ra => ra.Note)
            .HasColumnName("note");

        builder.Property(ra => ra.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(ra => ra.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(ra => ra.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(ra => ra.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Indexes
        builder.HasIndex(ra => new { ra.ResidentId, ra.ApartmentUnitId, ra.StartDate })
            .IsUnique();

        builder.HasIndex(ra => ra.ResidentId);
        builder.HasIndex(ra => ra.ApartmentUnitId);
        builder.HasIndex(ra => ra.Status);

        // Within-module relationship
        builder.HasOne(ra => ra.Resident)
            .WithMany(r => r.ResidentApartments)
            .HasForeignKey(ra => ra.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
