using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Communication.Domain.EmergencyCalls;

namespace PropFlow.Modules.Communication.Infrastructure.Persistence.Configurations;

public sealed class EmergencyCallConfiguration : IEntityTypeConfiguration<EmergencyCall>, IEntityTypeConfiguration<EmergencyConnection>
{
    public void Configure(EntityTypeBuilder<EmergencyCall> b)
    {
        b.ToTable("emergency_calls", t => t.HasCheckConstraint("ck_emergency_call_state", "\"Status\" IN ('WAITING','CONNECTING','CONNECTED','ENDED') AND ((\"Status\" = 'ENDED') = (\"EndedAt\" IS NOT NULL)) AND (\"Status\" NOT IN ('CONNECTING','CONNECTED') OR (\"ManagerUserId\" IS NOT NULL AND \"ManagerConnectionId\" IS NOT NULL)) AND \"Revision\" > 0"));
        b.HasKey(x => x.Id); b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ResidentName).HasMaxLength(200).IsRequired(); b.Property(x => x.ManagerName).HasMaxLength(200);
        b.Property(x => x.Apartment).HasMaxLength(150); b.Property(x => x.ResidentConnectionId).HasMaxLength(128); b.Property(x => x.ManagerConnectionId).HasMaxLength(128);
        b.HasIndex(x => new { x.ResidentUserId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => x.ResidentUserId).IsUnique().HasFilter("\"Status\" <> 'ENDED'");
        b.HasIndex(x => x.ManagerUserId).IsUnique().HasFilter("\"Status\" IN ('CONNECTING','CONNECTED')");
        b.HasIndex(x => new { x.Status, x.CreatedAt });
    }
    public void Configure(EntityTypeBuilder<EmergencyConnection> b)
    {
        b.ToTable("emergency_connections"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasMaxLength(128);
        b.Property(x => x.Role).HasMaxLength(20).IsRequired(); b.HasIndex(x => new { x.UserId, x.ExpiresAt });
    }
}
