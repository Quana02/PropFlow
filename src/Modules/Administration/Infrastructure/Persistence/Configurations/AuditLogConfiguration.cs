using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Administration.Domain.AuditLogs;

namespace PropFlow.Modules.Administration.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", "administration");

        builder.HasKey(al => al.Id);

        builder.Property(al => al.Id)
            .HasColumnName("id");

        builder.Property(al => al.ActorUserId)
            .HasColumnName("actor_user_id");

        builder.Property(al => al.Action)
            .HasColumnName("action")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(al => al.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(al => al.EntityId)
            .HasColumnName("entity_id");

        builder.Property(al => al.OldValues)
            .HasColumnName("old_values")
            .HasColumnType("jsonb");

        builder.Property(al => al.NewValues)
            .HasColumnName("new_values")
            .HasColumnType("jsonb");

        builder.Property(al => al.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(al => al.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(100);

        builder.Property(al => al.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(al => al.ActorUserId);
        builder.HasIndex(al => new { al.EntityType, al.EntityId });
        builder.HasIndex(al => al.CreatedAt);
    }
}
