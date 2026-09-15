using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Communication.Domain.Announcements;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Configurations;

public class AnnouncementAudienceConfiguration : IEntityTypeConfiguration<AnnouncementAudience>
{
    public void Configure(EntityTypeBuilder<AnnouncementAudience> builder)
    {
        builder.ToTable("announcement_audiences", table =>
        {
            table.HasCheckConstraint(
                "CK_announcement_audiences_target",
                "(audience_type IN ('ALL_USERS', 'ALL_RESIDENTS') AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR " +
                "(audience_type = 'ROLE' AND role_id IS NOT NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR " +
                "(audience_type = 'BUILDING' AND role_id IS NULL AND building_id IS NOT NULL AND apartment_unit_id IS NULL AND resident_id IS NULL) OR " +
                "(audience_type = 'APARTMENT' AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NOT NULL AND resident_id IS NULL) OR " +
                "(audience_type = 'RESIDENT' AND role_id IS NULL AND building_id IS NULL AND apartment_unit_id IS NULL AND resident_id IS NOT NULL)");
        });

        builder.HasKey(audience => audience.Id);

        builder.Property(audience => audience.Id).HasColumnName("id");

        builder.Property(audience => audience.AnnouncementId)
            .HasColumnName("announcement_id")
            .IsRequired();

        builder.Property(audience => audience.AudienceType)
            .HasColumnName("audience_type")
            .HasConversion<string>()
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(audience => audience.RoleId).HasColumnName("role_id");
        builder.Property(audience => audience.BuildingId).HasColumnName("building_id");
        builder.Property(audience => audience.ApartmentUnitId).HasColumnName("apartment_unit_id");
        builder.Property(audience => audience.ResidentId).HasColumnName("resident_id");

        builder.Property(audience => audience.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(audience => audience.AnnouncementId);
        builder.HasIndex(audience => audience.AudienceType);
        builder.HasIndex(audience => audience.RoleId);
        builder.HasIndex(audience => audience.BuildingId);
        builder.HasIndex(audience => audience.ApartmentUnitId);
        builder.HasIndex(audience => audience.ResidentId);

        builder.HasIndex(audience => new { audience.AnnouncementId, audience.AudienceType })
            .IsUnique()
            .HasDatabaseName("UX_announcement_audiences_announcement_type_global")
            .HasFilter("audience_type IN ('ALL_USERS', 'ALL_RESIDENTS')");

        builder.HasIndex(audience => new { audience.AnnouncementId, audience.RoleId })
            .IsUnique()
            .HasDatabaseName("UX_announcement_audiences_announcement_role")
            .HasFilter("audience_type = 'ROLE' AND role_id IS NOT NULL");

        builder.HasIndex(audience => new { audience.AnnouncementId, audience.BuildingId })
            .IsUnique()
            .HasDatabaseName("UX_announcement_audiences_announcement_building")
            .HasFilter("audience_type = 'BUILDING' AND building_id IS NOT NULL");

        builder.HasIndex(audience => new { audience.AnnouncementId, audience.ApartmentUnitId })
            .IsUnique()
            .HasDatabaseName("UX_announcement_audiences_announcement_apartment")
            .HasFilter("audience_type = 'APARTMENT' AND apartment_unit_id IS NOT NULL");

        builder.HasIndex(audience => new { audience.AnnouncementId, audience.ResidentId })
            .IsUnique()
            .HasDatabaseName("UX_announcement_audiences_announcement_resident")
            .HasFilter("audience_type = 'RESIDENT' AND resident_id IS NOT NULL");
    }
}
