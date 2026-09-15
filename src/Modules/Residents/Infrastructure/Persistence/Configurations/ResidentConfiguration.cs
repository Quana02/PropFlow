using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Residents.Domain.Residents;

namespace PropFlow.Modules.Residents.Infrastructure.Persistence.Configurations;

public class ResidentConfiguration : IEntityTypeConfiguration<Resident>
{
    public void Configure(EntityTypeBuilder<Resident> builder)
    {
        builder.ToTable("residents", "residents");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id");

        // Cross-module scalar ID to auth.users.id
        builder.Property(r => r.UserId)
            .HasColumnName("user_id");

        builder.Property(r => r.ResidentCode)
            .HasColumnName("resident_code")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(r => r.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20);

        builder.Property(r => r.Email)
            .HasColumnName("email")
            .HasMaxLength(255);

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(ResidentStatus.ACTIVE)
            .IsRequired();

        builder.Property(r => r.Note)
            .HasColumnName("note");

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(r => r.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Indexes
        builder.HasIndex(r => r.ResidentCode)
            .IsUnique();

        builder.HasIndex(r => r.UserId)
            .IsUnique();

        builder.HasIndex(r => r.FullName);
        builder.HasIndex(r => r.PhoneNumber);
        builder.HasIndex(r => r.Status);

        // Within-module relationships
        builder.HasMany(r => r.ResidentApartments)
            .WithOne(ra => ra.Resident)
            .HasForeignKey(ra => ra.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
