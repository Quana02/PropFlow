using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Communication.Domain.Announcements;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Configurations;

public class AnnouncementVersionConfiguration : IEntityTypeConfiguration<AnnouncementVersion>
{
    public void Configure(EntityTypeBuilder<AnnouncementVersion> builder)
    {
        builder.ToTable("announcement_versions", table =>
        {
            table.HasCheckConstraint("CK_announcement_versions_version_no", "version_no >= 1");
            table.HasCheckConstraint("CK_announcement_versions_title_not_blank", "length(btrim(title_snapshot)) > 0");
            table.HasCheckConstraint("CK_announcement_versions_content_not_blank", "length(btrim(content_snapshot)) > 0");
        });

        builder.HasKey(version => version.Id);

        builder.Property(version => version.Id).HasColumnName("id");

        builder.Property(version => version.AnnouncementId)
            .HasColumnName("announcement_id")
            .IsRequired();

        builder.Property(version => version.VersionNo)
            .HasColumnName("version_no")
            .IsRequired();

        builder.Property(version => version.TitleSnapshot)
            .HasColumnName("title_snapshot")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(version => version.ContentSnapshot)
            .HasColumnName("content_snapshot")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(version => version.StatusSnapshot)
            .HasColumnName("status_snapshot")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(version => version.AudienceSnapshotJson)
            .HasColumnName("audience_snapshot")
            .HasColumnType("jsonb");

        builder.Property(version => version.ChangedBy)
            .HasColumnName("changed_by")
            .IsRequired();

        builder.Property(version => version.ChangeReason)
            .HasColumnName("change_reason")
            .HasColumnType("text");

        builder.Property(version => version.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(version => new { version.AnnouncementId, version.VersionNo }).IsUnique();
        builder.HasIndex(version => version.ChangedBy);
        builder.HasIndex(version => version.CreatedAt);
    }
}
