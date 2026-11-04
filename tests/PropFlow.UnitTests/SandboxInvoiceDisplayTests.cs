using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.UnitTests;

public sealed class SandboxInvoiceDisplayTests
{
    [Fact]
    public async Task SimulatingPaymentUpdatesResidentAndManagerInvoiceViewsWithoutARealReceipt()
    {
        using var f = new Fixture(); await f.Seed();
        var card = await f.Receive();
        Assert.Equal("PAID", card.SettlementStatus); Assert.Equal(100000, card.ConfirmedPaidAmount); Assert.Equal(0, card.OutstandingAmount);
        foreach (var actor in new Guid?[] { null, f.UserId })
        {
            var row = Assert.Single(await f.Cycle.InvoicesAsync(actor, default));
            Assert.Equal("SANDBOX", row.PaymentEnvironment); Assert.Equal(100000, row.Paid); Assert.Equal(0, row.Outstanding);
        }
        var detail = await f.Cycle.DetailAsync(card.InvoiceId, default);
        Assert.Equal("PAID", detail!.SettlementStatus); Assert.Equal("SANDBOX", detail.PaymentEnvironment);
        Assert.Equal(0, await f.Totals.TotalAsync(card.InvoiceId, default));
        Assert.Empty(await f.Payments.Payments.ToArrayAsync());
        var actual = await f.BillingSource.FindForUserAsync(f.RequestId, f.UserId, default);
        Assert.Equal("UNPAID", actual!.Status); Assert.Equal(100000, actual.OutstandingAmount);
    }

    [Fact]
    public async Task SwitchingToLiveRestoresActualBalancesAndPreservesTheSimulationForDemo()
    {
        using var f = new Fixture(); await f.Seed();
        var simulated = await f.Receive();
        var mode = await f.Payments.SePaySandboxConnections.SingleAsync(); mode.DemoEnabled = false; await f.Payments.SaveChangesAsync();
        var row = Assert.Single(await f.Cycle.InvoicesAsync(f.UserId, default));
        Assert.Equal("LIVE", row.PaymentEnvironment); Assert.Equal(0, row.Paid); Assert.Equal(100000, row.Outstanding);
        var card = await f.Resident.GetAsync(f.RequestId, f.UserId, default);
        Assert.Equal("UNPAID", card.SettlementStatus); Assert.Equal(0, card.ConfirmedPaidAmount);
        Assert.Single(await f.Payments.SandboxPaymentSimulations.ToArrayAsync());
        Assert.Equal(0, await f.Totals.TotalAsync(simulated.InvoiceId, default));
    }

    [Fact]
    public async Task ResidentInvoiceScopeIsPreservedWhenSandboxIsEnabled()
    {
        using var f = new Fixture(); await f.Seed();
        await f.Receive();
        Assert.Empty(await f.Cycle.InvoicesAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public void DisplayBalancesNeverExceedInvoiceTotal()
    {
        var balance = SandboxPaymentDisplay.Balance(100000, 20000, 100000);
        Assert.Equal(100000, balance.Paid); Assert.Equal(0, balance.Outstanding); Assert.Equal("PAID", balance.Status);
    }

    private sealed class Fixture : IDisposable, IBillingHouseholdDirectory, IPaymentNoticeSource, IFinanceNoticePublisher, IPaymentAccountProtector, IInvoiceFinancialSource
    {
        public readonly Guid UserId = Guid.NewGuid(), RequestId = Guid.NewGuid();
        public readonly PaymentsDbContext Payments = new(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly BillingDbContext billing = new(new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly ConfirmedPaymentSource Totals;
        public readonly ServiceRequestBilling BillingSource;
        public readonly ResidentServiceRequestPayments Resident;
        public readonly BillingCycle Cycle;
        private readonly EphemeralDataProtectionProvider protection = new();
        private readonly string key = new('k', 40);
        public Fixture()
        {
            Totals = new(Payments); BillingSource = new(billing, this, Totals);
            Resident = new(Payments, BillingSource, this, TimeProvider.System);
            Cycle = new(billing, this, Totals, this, this, sandbox: new SandboxPaymentDisplaySource(Payments));
        }
        public async Task Seed()
        {
            await BillingSource.IssueAsync(new(RequestId, "YC-SANDBOX", Guid.NewGuid(), Guid.NewGuid(), UserId,
                "Resident Demo", 100000, 0, null, Guid.NewGuid(), DateTimeOffset.UtcNow), default);
            Payments.SePaySandboxConnections.Add(new() { DemoEnabled = true, ProtectedApiToken = "encrypted", LastCheckSucceeded = true,
                ProtectedWebhookKey = protection.CreateProtector(SePaySandbox.WebhookKeyPurpose).Protect(key) });
            await Payments.SaveChangesAsync();
        }
        public Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingHousehold>>([]);
        public Task<IReadOnlyList<PaymentNoticeSnapshot>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<PaymentNoticeSnapshot>>([]);
        public Task<PaymentNoticeSnapshot?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<PaymentNoticeSnapshot?>(null);
        public Task PublishAsync(FinanceNotice notice, CancellationToken ct) => Task.CompletedTask;
        public string Protect(string number) => number;
        public string Unprotect(string number) => number;
        public async Task<ResidentServiceRequestPaymentView> Receive()
        {
            var invoice = await billing.Invoices.SingleAsync();
            Payments.SandboxPaymentSimulations.Add(new() { InvoiceId = invoice.Id, ServiceRequestId = RequestId, UserId = UserId,
                TransferCode = "PS1234567890123456789012", RequestedAmount = 100000, ProtectedAccountNumber = "123456789", GatewayAliases = "[\"VCB\"]" });
            await Payments.SaveChangesAsync();
            await new SePaySandboxWebhook(Payments, protection, this, this, TimeProvider.System).ReceiveAsync("Apikey " + key,
                new(1, "VCB", "123456789", "in", 100000, null, "PS1234567890123456789012", "SBTEST"), default);
            return await Resident.GetAsync(RequestId, UserId, default);
        }
        async Task<InvoiceFinancialSnapshot?> IInvoiceFinancialSource.FindAsync(Guid id, CancellationToken ct)
        {
            var i = await billing.Invoices.SingleOrDefaultAsync(x => x.Id == id, ct);
            return i is null ? null : new(i.Id, i.InvoiceNumber, i.ApartmentUnitId, i.BillingPeriodStart, i.BillingPeriodEnd, i.IssueDate, i.DueDate, i.TotalAmount, i.Status.ToString());
        }
        public Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) => throw new NotSupportedException();
        public void Dispose() { billing.Dispose(); Payments.Dispose(); }
    }
}
