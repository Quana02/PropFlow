using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.BillingCycleTests;

public sealed class FinanceCalendarTests
{
    [Fact]
    public async Task Unpaid_excludes_overdue_before_counts_filters_and_paging_and_moves_after_midnight()
    {
        var due = new DateOnly(2026, 10, 10);
        var invoice = new InvoiceFinancialSnapshot(Guid.NewGuid(), "HD-BOUNDARY", Guid.NewGuid(),
            new(2026, 10, 1), new(2026, 10, 31), new(2026, 10, 1), due, 730000, "ISSUED", "SERVICE_FEE");
        var clock = new MutableClock(new(2026, 10, 10, 16, 59, 0, TimeSpan.Zero));
        var queries = new BillingFinanceQueries(new Invoices(invoice), new Payments(), new Zone(), clock);
        Assert.Single((await queries.UnpaidAsync(null, 1, 1, "desc", default)).Items);
        Assert.Empty((await queries.OverdueAsync(null, 1, 1, "desc", default)).Items);
        clock.Now = clock.Now.AddMinutes(2);
        foreach (var filter in new[] { "ALL", "UNPAID", "PARTIAL", "DUE_SOON" })
        {
            var unpaid = await queries.UnpaidAsync(null, 1, 1, "desc", default, filter);
            Assert.Empty(unpaid.Items);
            Assert.Equal(0, unpaid.Total);
            Assert.All(unpaid.Counts!.Values, count => Assert.Equal(0, count));
        }
        var overdue = Assert.Single((await queries.OverdueAsync(null, 1, 1, "desc", default)).Items);
        Assert.Equal(730000, overdue.OutstandingAmount);
        Assert.Equal(1, overdue.DaysOverdue);
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Overdue_list_and_detail_use_the_building_calendar_after_local_midnight()
    {
        var due = new DateOnly(2026, 10, 10);
        var invoice = new InvoiceFinancialSnapshot(Guid.NewGuid(), "HD-CALENDAR", Guid.NewGuid(),
            new(2026, 10, 1), new(2026, 10, 31), new(2026, 10, 1), due, 700000, "ISSUED", "SERVICE_FEE");
        var queries = new BillingFinanceQueries(new Invoices(invoice), new Payments(), new Zone(), new Clock());
        var overdue = await queries.OverdueAsync(null, 1, 20, "desc", default);
        var row = Assert.Single(overdue.Items);
        Assert.True(row.IsOverdue);
        Assert.Equal(1, row.DaysOverdue);
        var detail = await queries.DetailAsync(invoice.Id, default);
        Assert.Equal(row, detail);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 10, 17, 30, 0, TimeSpan.Zero);
    }
    private sealed class Zone : ICurrentBuildingTimeZone
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
    }
    private sealed class Invoices(InvoiceFinancialSnapshot invoice) : IInvoiceFinancialSource
    {
        public Task<InvoiceFinancialSnapshot?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<InvoiceFinancialSnapshot?>(id == invoice.Id ? invoice : null);
        public Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InvoiceFinancialSnapshot>>([invoice]);
    }
    private sealed class Payments : IConfirmedPaymentSource
    {
        public Task<decimal> TotalAsync(Guid id, CancellationToken ct) => Task.FromResult(0m);
        public Task<IReadOnlyDictionary<Guid, decimal>> TotalsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }
}
