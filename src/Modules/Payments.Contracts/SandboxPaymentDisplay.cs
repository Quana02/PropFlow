namespace PropFlow.Modules.Payments.Contracts;

// Read-only demo projection. Never use this source to collect real funds or authorize real settlement.
public interface ISandboxPaymentDisplaySource
{
    Task<SandboxDisplayPayments> ReadAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);
}

public sealed record SandboxDisplayPayments(bool Enabled, IReadOnlyDictionary<Guid, decimal> Amounts);
public sealed record SandboxDisplayBalance(decimal Paid, decimal Outstanding, string Status);

public static class SandboxPaymentDisplay
{
    public static SandboxDisplayBalance Balance(decimal total, decimal realPaid, decimal simulated)
    {
        var paid = Math.Min(total, Math.Max(0, realPaid) + Math.Max(0, simulated));
        var outstanding = Math.Max(0, total - paid);
        return new(paid, outstanding, outstanding == 0 ? "PAID" : paid > 0 ? "PARTIAL" : "UNPAID");
    }
}
