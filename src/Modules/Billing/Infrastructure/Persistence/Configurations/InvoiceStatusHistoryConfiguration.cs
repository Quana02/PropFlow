using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;

namespace PropFlow.Modules.Billing.Infrastructure.Persistence.Configurations;

public class InvoiceStatusHistoryConfiguration : IEntityTypeConfiguration<InvoiceStatusHistory>
{
    public void Configure(EntityTypeBuilder<InvoiceStatusHistory> builder)
    {
        builder.ToTable("invoice_status_history", "billing");

        builder.HasKey(history => history.Id);
        builder.Property(history => history.Id).HasColumnName("id");

        builder.Property(history => history.InvoiceId).HasColumnName("invoice_id").IsRequired();
        builder.HasIndex(history => history.InvoiceId);

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

        builder.Property(history => history.ChangedBy).HasColumnName("changed_by").IsRequired();

        builder.Property(history => history.ChangedAt)
            .HasColumnName("changed_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()")
            .IsRequired();
        builder.HasIndex(history => history.ChangedAt);
    }
}
