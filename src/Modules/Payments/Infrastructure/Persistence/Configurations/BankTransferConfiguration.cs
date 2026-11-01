using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropFlow.Modules.Payments.Domain.BankTransfers;

namespace PropFlow.Modules.Payments.Infrastructure.Persistence.Configurations;

public sealed class BankTransferConfiguration : IEntityTypeConfiguration<BankTransfer>
{
    public void Configure(EntityTypeBuilder<BankTransfer> b)
    {
        b.ToTable("bank_transfers"); b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TransactionId).HasColumnName("transaction_id"); b.HasIndex(x => x.TransactionId).IsUnique();
        b.Property(x => x.PayloadHash).HasColumnName("payload_hash").HasMaxLength(64);
        b.Property(x => x.ProtectedPayload).HasColumnName("protected_payload");
        b.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(18,0)");
        b.Property(x => x.Gateway).HasColumnName("gateway").HasMaxLength(100);
        b.Property(x => x.AccountLast4).HasColumnName("account_last4").HasMaxLength(4);
        b.Property(x => x.TransferCode).HasColumnName("transfer_code").HasMaxLength(24);
        b.Property(x => x.BankReference).HasColumnName("bank_reference").HasMaxLength(100);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
        b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(80);
        b.Property(x => x.InvoiceId).HasColumnName("invoice_id");
        b.Property(x => x.PaymentId).HasColumnName("payment_id");
        b.Property(x => x.AllocatedAmount).HasColumnName("allocated_amount").HasColumnType("numeric(18,0)");
        b.Property(x => x.AssignedInvoiceId).HasColumnName("assigned_invoice_id");
        b.Property(x => x.RequestedAmount).HasColumnName("requested_amount").HasColumnType("numeric(18,0)");
        b.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
        b.Property(x => x.ReviewNote).HasColumnName("review_note").HasMaxLength(1000);
        b.Property(x => x.ReviewHistory).HasColumnName("review_history");
        b.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        b.Property(x => x.ReceivedAt).HasColumnName("received_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        b.Property(x => x.NextAttemptAt).HasColumnName("next_attempt_at");
        b.Property(x => x.Attempts).HasColumnName("attempts");
        b.Property(x => x.Revision).HasColumnName("revision").IsConcurrencyToken();
        b.HasIndex(x => new { x.Status, x.NextAttemptAt }); b.HasIndex(x => x.InvoiceId);
    }
}
