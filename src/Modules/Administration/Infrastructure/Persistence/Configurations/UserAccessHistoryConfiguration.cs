using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.UserAccessHistories;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class UserAccessHistoryConfiguration : IEntityTypeConfiguration<UserAccessHistory>
{
    public void Configure(EntityTypeBuilder<UserAccessHistory> builder)
    {
        builder.ToTable("user_access_history", "administration");

        builder.HasKey(uah => uah.Id);

        builder.Property(uah => uah.Id)
            .HasColumnName("id");

        builder.Property(uah => uah.TargetUserId)
            .HasColumnName("target_user_id")
            .IsRequired();

        builder.Property(uah => uah.Action)
            .HasColumnName("action")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(uah => uah.OldRoleId)
            .HasColumnName("old_role_id");

        builder.Property(uah => uah.NewRoleId)
            .HasColumnName("new_role_id");

        builder.Property(uah => uah.OldStatus)
            .HasColumnName("old_status")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(uah => uah.NewStatus)
            .HasColumnName("new_status")
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(uah => uah.Reason)
            .HasColumnName("reason")
            .HasColumnType("text");

        builder.Property(uah => uah.PerformedBy)
            .HasColumnName("performed_by");

        builder.Property(uah => uah.Metadata)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(uah => uah.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne(uah => uah.OldRole)
            .WithMany()
            .HasForeignKey(uah => uah.OldRoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(uah => uah.NewRole)
            .WithMany()
            .HasForeignKey(uah => uah.NewRoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(uah => uah.TargetUserId);
        builder.HasIndex(uah => uah.PerformedBy);
        builder.HasIndex(uah => uah.CreatedAt);
    }
}
