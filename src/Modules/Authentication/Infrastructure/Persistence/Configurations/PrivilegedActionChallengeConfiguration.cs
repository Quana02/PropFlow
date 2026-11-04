using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Authentication.Domain.PrivilegedActions;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence.Configurations;

public sealed class PrivilegedActionChallengeConfiguration : IEntityTypeConfiguration<PrivilegedActionChallenge>
{
    public void Configure(EntityTypeBuilder<PrivilegedActionChallenge> builder)
    {
        builder.ToTable("privileged_action_challenges", "auth");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(item => item.Purpose).HasColumnName("purpose").HasMaxLength(80).IsRequired();
        builder.Property(item => item.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64).IsRequired();
        builder.Property(item => item.CodeHash).HasColumnName("code_hash").HasMaxLength(200).IsRequired();
        builder.Property(item => item.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
        builder.Property(item => item.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(item => item.UsedAt).HasColumnName("used_at");
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.UserId, item.Purpose, item.CreatedAt });
        builder.HasIndex(item => item.ExpiresAt);
    }
}
