using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Authentication.Domain.Tokens;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens", "auth");

        builder.HasKey(prt => prt.Id);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);
        builder.Property(x => x.ProofHash).HasColumnName("proof_hash");
        builder.Property(x => x.ProofExpiresAt).HasColumnName("proof_expires_at");
        builder.Property(x => x.VerifiedAt).HasColumnName("verified_at");
        builder.Property(prt => prt.Id)
            .HasColumnName("id");

        builder.Property(prt => prt.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(prt => prt.TokenHash)
            .HasColumnName("token_hash")
            .IsRequired();

        builder.Property(prt => prt.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(prt => prt.UsedAt)
            .HasColumnName("used_at");

        builder.Property(prt => prt.RequestedIp)
            .HasColumnName("requested_ip")
            .HasMaxLength(45);

        builder.Property(prt => prt.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        // Indexes
        builder.HasIndex(prt => prt.TokenHash)
            .IsUnique();

        builder.HasIndex(prt => prt.UserId);
        builder.HasIndex(prt => prt.ExpiresAt);

        // Within-module relationships
        builder.HasOne(prt => prt.User)
            .WithMany(u => u.PasswordResetTokens)
            .HasForeignKey(prt => prt.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
