using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.PricingChanges;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;
public sealed class PricingChangeConfiguration : IEntityTypeConfiguration<PricingChange>
{
    public void Configure(EntityTypeBuilder<PricingChange> b)
    {
        b.ToTable("pricing_change_outbox"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        b.Property(x => x.ActorId).HasColumnName("actor_id");
        b.Property(x => x.EventName).HasColumnName("event_name").HasMaxLength(80);
        b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(200);
        b.Property(x => x.Message).HasColumnName("message").HasColumnType("text");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
        b.Property(x => x.Attempts).HasColumnName("attempts");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(x => x.LastErrorType).HasColumnName("last_error_type").HasMaxLength(200);
        b.HasIndex(x => new { x.Status, x.NextAttemptAt });
    }
}
