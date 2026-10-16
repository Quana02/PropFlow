using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Shared.Messaging;
namespace PropFlow.Modules.Maintenance.Infrastructure.Persistence.Configurations;
public sealed class ResidentOutboxConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> b)
    {
        b.ToTable("resident_notification_outbox"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        b.Property(x => x.SourceId).HasColumnName("source_id");
        b.Property(x => x.Name).HasColumnName("event_name").HasMaxLength(100);
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
        b.Property(x => x.Attempts).HasColumnName("attempts");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(x => x.LastErrorType).HasColumnName("last_error_type").HasMaxLength(200);
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
    }
}