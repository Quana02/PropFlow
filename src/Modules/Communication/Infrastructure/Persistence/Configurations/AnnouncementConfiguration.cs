using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Communication.Domain.Announcements;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Configurations;

public class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("announcements", table =>
        {
            table.HasCheckConstraint("CK_announcements_title_not_blank", "length(btrim(title)) > 0");
            table.HasCheckConstraint("CK_announcements_content_not_blank", "length(btrim(content)) > 0");
            table.HasCheckConstraint("CK_announcements_updated_at", "updated_at >= created_at");
            table.HasCheckConstraint("CK_announcements_published_state", "(status = 'DRAFT' AND published_at IS NULL AND withdrawn_at IS NULL AND withdrawal_reason IS NULL) OR (status = 'PUBLISHED' AND published_at IS NOT NULL AND withdrawn_at IS NULL AND withdrawal_reason IS NULL AND published_at >= created_at) OR (status = 'WITHDRAWN' AND published_at IS NOT NULL AND withdrawn_at IS NOT NULL AND withdrawal_reason IS NOT NULL AND published_at >= created_at AND withdrawn_at >= published_at)");
        });

        builder.HasKey(announcement => announcement.Id);

        builder.Property(announcement => announcement.Id).HasColumnName("id");

        builder.Property(announcement => announcement.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(announcement => announcement.Content)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(announcement => announcement.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(AnnouncementStatus.DRAFT)
            .IsRequired();

        builder.Property(announcement => announcement.PublishedAt)
            .HasColumnName("published_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(announcement => announcement.WithdrawnAt)
            .HasColumnName("withdrawn_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(announcement => announcement.WithdrawalReason)
            .HasColumnName("withdrawal_reason")
            .HasColumnType("text");

        builder.Property(announcement => announcement.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(announcement => announcement.UpdatedBy)
            .HasColumnName("updated_by");

        builder.Property(announcement => announcement.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(announcement => announcement.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasMany(announcement => announcement.Audiences)
            .WithOne(audience => audience.Announcement)
            .HasForeignKey(audience => audience.AnnouncementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(announcement => announcement.Versions)
            .WithOne(version => version.Announcement)
            .HasForeignKey(version => version.AnnouncementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Navigation(announcement => announcement.Audiences)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(announcement => announcement.Versions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(announcement => announcement.Status);
        builder.HasIndex(announcement => announcement.CreatedBy);
        builder.HasIndex(announcement => announcement.CreatedAt);
        builder.HasIndex(announcement => announcement.PublishedAt);
    }
}
