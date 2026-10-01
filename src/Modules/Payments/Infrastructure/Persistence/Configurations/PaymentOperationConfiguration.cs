using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Payments.Domain.Payments;
namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Configurations;
public sealed class PaymentOperationConfiguration : IEntityTypeConfiguration<PaymentOperation>
{
    public void Configure(EntityTypeBuilder<PaymentOperation> b)
    {
        b.ToTable("payment_operations"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.Key).HasColumnName("idempotency_key").HasMaxLength(100).IsRequired();
        b.Property(x => x.PaymentId).HasColumnName("payment_id"); b.Property(x => x.Action).HasColumnName("action").HasMaxLength(20);
        b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64); b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasIndex(x => x.Key).IsUnique();
    }
}
