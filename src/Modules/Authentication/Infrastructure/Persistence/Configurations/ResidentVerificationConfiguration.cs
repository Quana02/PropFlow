using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Configurations;

public class ResidentVerificationConfiguration : IEntityTypeConfiguration<ResidentVerification>
{
    public void Configure(EntityTypeBuilder<ResidentVerification> builder)
    {
        builder.ToTable("resident_verifications", "auth");

        builder.HasKey(rv => rv.Id);
        builder.Property(rv => rv.Id)
            .HasColumnName("id");

        builder.Property(rv => rv.UserId)
            .HasColumnName("user_id");

        builder.Property(rv => rv.RegistrationUsername)
            .HasColumnName("registration_username")
            .HasMaxLength(50);
        builder.Property(rv => rv.RegistrationDisplayName)
            .HasColumnName("registration_display_name")
            .HasMaxLength(150);
        builder.Property(rv => rv.RegistrationEmail)
            .HasColumnName("registration_email")
            .HasMaxLength(255);
        builder.Property(rv => rv.RegistrationPasswordHash)
            .HasColumnName("registration_password_hash");

        // Cross-module scalar IDs: no cross-module navigation property
        builder.Property(rv => rv.ResidentId)
            .HasColumnName("resident_id")
            .IsRequired();

        builder.Property(rv => rv.ApartmentUnitId)
            .HasColumnName("apartment_unit_id");

        builder.Property(rv => rv.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(VerificationStatus.PENDING)
            .IsRequired();

        builder.Property(rv => rv.VerificationCodeHash)
            .HasColumnName("verification_code_hash")
            .IsRequired();

        builder.Property(rv => rv.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(rv => rv.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(rv => rv.VerifiedAt)
            .HasColumnName("verified_at");

        builder.Property(rv => rv.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(rv => rv.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Indexes
        builder.HasIndex(rv => rv.UserId);
        builder.HasIndex(rv => rv.ResidentId);
        builder.HasIndex(rv => rv.ApartmentUnitId);
        builder.HasIndex(rv => rv.Status);
        builder.HasIndex(rv => rv.RegistrationUsername);
        builder.HasIndex(rv => rv.RegistrationEmail);

        // Within-module relationships
        builder.HasOne(rv => rv.User)
            .WithMany()
            .HasForeignKey(rv => rv.UserId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
