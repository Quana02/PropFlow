using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.UserBuildingAccesses;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class UserBuildingAccessConfiguration : IEntityTypeConfiguration<UserBuildingAccess>
{
    public void Configure(EntityTypeBuilder<UserBuildingAccess> builder)
    {
        builder.ToTable("user_building_accesses", "administration");

        builder.HasKey(uba => uba.Id);

        builder.Property(uba => uba.Id)
            .HasColumnName("id");

        builder.Property(uba => uba.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(uba => uba.BuildingId)
            .HasColumnName("building_id")
            .IsRequired();

        builder.Property(uba => uba.GrantedBy)
            .HasColumnName("granted_by");

        builder.Property(uba => uba.GrantedAt)
            .HasColumnName("granted_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(uba => uba.RevokedBy)
            .HasColumnName("revoked_by");

        builder.Property(uba => uba.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(uba => uba.Reason)
            .HasColumnName("reason")
            .HasColumnType("text");

        builder.Property(uba => uba.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(uba => uba.UserId);
        builder.HasIndex(uba => uba.BuildingId);
        builder.HasIndex(uba => new { uba.UserId, uba.BuildingId, uba.GrantedAt });
        builder.HasIndex(uba => new { uba.UserId, uba.BuildingId })
            .IsUnique()
            .HasFilter("\"revoked_at\" IS NULL");
    }
}
