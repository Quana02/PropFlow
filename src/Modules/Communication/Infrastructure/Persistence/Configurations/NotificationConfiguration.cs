using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Communication.Domain.Notifications;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", table =>
        {
            table.HasCheckConstraint("CK_notifications_read_state", "(is_read = false AND read_at IS NULL) OR (is_read = true AND read_at IS NOT NULL AND read_at >= created_at)");
            table.HasCheckConstraint("CK_notifications_source_reference", "(source_type IS NULL AND source_id IS NULL) OR (source_type IS NOT NULL AND source_id IS NOT NULL)");
            table.HasCheckConstraint("CK_notifications_title_not_blank", "length(btrim(title)) > 0");
            table.HasCheckConstraint("CK_notifications_message_not_blank", "length(btrim(message)) > 0");
        });

        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Id).HasColumnName("id");

        builder.Property(notification => notification.RecipientUserId)
            .HasColumnName("recipient_user_id")
            .IsRequired();

        builder.Property(notification => notification.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .HasDefaultValue(NotificationType.SYSTEM)
            .IsRequired();

        builder.Property(notification => notification.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(notification => notification.Message)
            .HasColumnName("message")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(notification => notification.SourceEventId)
            .HasColumnName("source_event_id");

        builder.Property(notification => notification.SourceType)
            .HasColumnName("source_type")
            .HasMaxLength(80);

        builder.Property(notification => notification.SourceId)
            .HasColumnName("source_id");

        builder.Property(notification => notification.ActionPath)
            .HasColumnName("action_path")
            .HasMaxLength(500);

        builder.Property(notification => notification.IsRead)
            .HasColumnName("is_read")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(notification => notification.ReadAt)
            .HasColumnName("read_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(notification => notification.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(notification => notification.RecipientUserId);
        builder.HasIndex(notification => new { notification.RecipientUserId, notification.IsRead });
        builder.HasIndex(notification => notification.CreatedAt);
        builder.HasIndex(notification => notification.Type);
        builder.HasIndex(notification => new { notification.SourceType, notification.SourceId });
        builder.HasIndex(notification => new { notification.RecipientUserId, notification.SourceEventId })
            .IsUnique()
            .HasFilter("source_event_id IS NOT NULL");
    }
}
