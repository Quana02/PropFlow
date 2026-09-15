using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Payments.Domain.Payments;

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Configurations;

public class PaymentStatusHistoryConfiguration : IEntityTypeConfiguration<PaymentStatusHistory>
{
    public void Configure(EntityTypeBuilder<PaymentStatusHistory> builder)
    {
        builder.ToTable("payment_status_history");

        builder.HasKey(history => history.Id);

        builder.Property(history => history.Id)
            .HasColumnName("id");

        builder.Property(history => history.PaymentId)
            .HasColumnName("payment_id")
            .IsRequired();

        builder.Property(history => history.FromStatus)
            .HasColumnName("from_status")
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(history => history.ToStatus)
            .HasColumnName("to_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(history => history.Reason)
            .HasColumnName("reason")
            .HasColumnType("text");

        builder.Property(history => history.ChangedBy)
            .HasColumnName("changed_by")
            .IsRequired();

        builder.Property(history => history.ChangedAt)
            .HasColumnName("changed_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(history => history.PaymentId);

        builder.HasIndex(history => history.ChangedAt);
    }
}
