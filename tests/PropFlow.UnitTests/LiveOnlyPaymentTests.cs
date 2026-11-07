using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using PropFlow.Modules.Payments.Presentation;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-09")]
public sealed class LiveOnlyPaymentTests
{
    [Fact]
    public async Task ArchivedTrialMoneyNeverChangesResidentManagerOrInvoiceBalances()
    {
        await using var payments = new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var billing = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var fixture = new Dependencies();
        var totals = new ConfirmedPaymentSource(payments);
        var source = new ServiceRequestBilling(billing, fixture, totals);
        var request = Guid.NewGuid(); var owner = Guid.NewGuid();
        var invoice = await source.IssueAsync(new(request, "YC-LIVE", Guid.NewGuid(), Guid.NewGuid(), owner,
            "Cư dân", 100000, 0, null, Guid.NewGuid(), DateTimeOffset.UtcNow), default);
        payments.SePaySandboxConnections.Add(new() { DemoEnabled = true });
        payments.SandboxPaymentSimulations.Add(new() { InvoiceId = invoice!.InvoiceId, ServiceRequestId = request,
            UserId = owner, Amount = 100000, VerifiedAmount = 100000, TransferCode = "PS1234567890123456789012" });
        await payments.SaveChangesAsync();
        var resident = new ResidentServiceRequestPayments(payments, source, fixture, TimeProvider.System);
        var card = await resident.GetAsync(request, owner, default);
        Assert.Equal("LIVE", card.PaymentEnvironment);
        Assert.Equal("UNPAID", card.SettlementStatus);
        Assert.Equal(100000, card.OutstandingAmount);
        Assert.Null(card.TransferCode);
        var cycle = new BillingCycle(billing, fixture, totals, fixture, fixture);
        foreach (var actor in new Guid?[] { null, owner })
        {
            var row = Assert.Single(await cycle.InvoicesAsync(actor, default));
            Assert.Equal("LIVE", row.PaymentEnvironment); Assert.Equal(0, row.Paid); Assert.Equal(100000, row.Outstanding);
        }
        Assert.Equal("UNPAID", (await cycle.DetailAsync(invoice.InvoiceId, default))!.SettlementStatus);
        Assert.Equal(404, (await Assert.ThrowsAsync<ResidentPaymentFailure>(() => resident.GetAsync(request, Guid.NewGuid(), default))).Status);
        Assert.Empty(payments.Payments);
    }

    [Fact]
    public void TrialControllersAndResidentSimulationActionsAreNotExposed()
    {
        var routes = typeof(SePayWebhookController).Assembly.GetTypes()
            .Where(x => typeof(ControllerBase).IsAssignableFrom(x))
            .SelectMany(x => x.GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        Assert.DoesNotContain(routes, x => x.Template.Contains("sandbox", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(typeof(ResidentServiceRequestPaymentsController).GetMethods(), x => x.Name is "Simulate" or "LegacySimulation");
    }

    [Theory]
    [InlineData("https://payments.example.com", "API_KEY", true)]
    [InlineData("https://payments.example.com/", "HMAC", true)]
    [InlineData("http://payments.example.com", "API_KEY", false)]
    [InlineData("https://localhost:7101", "API_KEY", false)]
    [InlineData("https://payments.example.com/api", "API_KEY", false)]
    [InlineData("https://payments.example.com?key=secret", "API_KEY", false)]
    [InlineData("https://payments.example.com", "NONE", false)]
    public void EnabledLiveConfigurationRequiresPublicHttpsAndAuthenticatedWebhook(string origin, string mode, bool valid)
    {
        var options = new SePayOptions { Enabled = true, WebhookPublicBaseUrl = origin, AuthenticationMode = mode,
            WebhookApiKey = new('k', 40), WebhookSecret = new('h', 40) };
        Assert.Equal(valid, SePayLiveConfiguration.Valid(options));
        options.WebhookApiKey = "short"; options.WebhookSecret = "short";
        Assert.False(SePayLiveConfiguration.Valid(options));
    }

    private sealed class Dependencies : IBillingHouseholdDirectory, IPaymentNoticeSource, IFinanceNoticePublisher, IPaymentAccountProtector
    {
        public Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingHousehold>>([]);
        public Task<IReadOnlyList<PaymentNoticeSnapshot>> ListAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<PaymentNoticeSnapshot>>([]);
        public Task<PaymentNoticeSnapshot?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<PaymentNoticeSnapshot?>(null);
        public Task PublishAsync(FinanceNotice notice, CancellationToken ct) => Task.CompletedTask;
        public string Protect(string value) => value;
        public string Unprotect(string value) => value;
    }
}
