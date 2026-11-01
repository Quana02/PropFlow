using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

// Demo evidence is deliberately separate from confirmed payment receipts and bank-transfer inboxes.
public sealed class SandboxPaymentSimulation
{
    public Guid InvoiceId { get; set; }
    public Guid ServiceRequestId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public decimal VerifiedAmount { get; set; }
    public decimal RequestedAmount { get; set; }
    public string? TransferCode { get; set; }
    public string? ProtectedAccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankCode { get; set; }
    public string? GatewayAliases { get; set; }
    public string? AccountLast4 { get; set; }
    public string? AccountName { get; set; }
}

public sealed class SandboxPaymentSimulationConfiguration : IEntityTypeConfiguration<SandboxPaymentSimulation>
{
    public void Configure(EntityTypeBuilder<SandboxPaymentSimulation> b)
    {
        b.ToTable("sandbox_payment_simulations"); b.HasKey(x => x.InvoiceId);
        b.Property(x => x.Amount).HasColumnType("numeric(18,0)");
        b.Property(x => x.VerifiedAmount).HasColumnType("numeric(18,0)");
        b.Property(x => x.RequestedAmount).HasColumnType("numeric(18,0)");
        b.Property(x => x.TransferCode).HasMaxLength(24);
        b.HasIndex(x => x.TransferCode).IsUnique().HasFilter("\"TransferCode\" IS NOT NULL");
    }
}
