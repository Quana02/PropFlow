using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

public sealed class SandboxSePayReceipt
{
    public long TransactionId { get; set; }
    public string PayloadHash { get; set; } = "";
    public string ProtectedPayload { get; set; } = "";
    public decimal Amount { get; set; }
    public Guid? InvoiceId { get; set; }
    public string Outcome { get; set; } = "RECEIVED";
    public DateTimeOffset ReceivedAt { get; set; }
}
public sealed class SandboxSePayReceiptConfiguration : IEntityTypeConfiguration<SandboxSePayReceipt>
{
    public void Configure(EntityTypeBuilder<SandboxSePayReceipt> b)
    {
        b.ToTable("sandbox_sepay_receipts"); b.HasKey(x => x.TransactionId); b.Property(x => x.TransactionId).ValueGeneratedNever();
        b.Property(x => x.Amount).HasColumnType("numeric(18,0)"); b.Property(x => x.PayloadHash).HasMaxLength(64);
        b.HasIndex(x => x.InvoiceId);
    }
}
