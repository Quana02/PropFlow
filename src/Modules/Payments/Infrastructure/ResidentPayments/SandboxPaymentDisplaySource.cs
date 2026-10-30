using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

public sealed class SandboxPaymentDisplaySource(PaymentsDbContext db) : ISandboxPaymentDisplaySource
{
    public async Task<SandboxDisplayPayments> ReadAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct)
    {
        if (!await db.SePaySandboxConnections.AsNoTracking().AnyAsync(x => x.DemoEnabled, ct)) return new(false, new Dictionary<Guid, decimal>());
        return new(true, await db.SandboxPaymentSimulations.AsNoTracking().Where(x => invoiceIds.Contains(x.InvoiceId))
            .ToDictionaryAsync(x => x.InvoiceId, x => x.VerifiedAmount, ct));
    }
}
