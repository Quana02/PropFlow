using System.Net;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
namespace PropFlow.UnitTests;
public sealed class ResidentSandboxPaymentTests
{
    [Fact]
    public async Task PreparingNeverPaysUntilAuthenticatedWebhookAndReplayNeverDoubleCounts()
    {
        using var f = new Fixture(); await f.Enable();
        var pending = await f.Service.PrepareSandboxAsync(f.RequestId, f.UserId, default);
        Assert.Equal("UNPAID", pending.SettlementStatus); Assert.Null(pending.SimulatedTransferAmount); Assert.StartsWith("PS", pending.TransferCode!);
        Assert.Equal(pending.TransferCode, (await f.Service.PrepareSandboxAsync(f.RequestId, f.UserId, default)).TransferCode);
        var transfer = f.Transfer(pending.TransferCode!);
        Assert.Equal(401, (await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Webhook.ReceiveAsync("Apikey wrong", transfer, default))).Status);
        await f.Webhook.ReceiveAsync(f.Auth, transfer, default); await f.Webhook.ReceiveAsync(f.Auth, transfer, default);
        var paid = await f.Service.GetAsync(f.RequestId, f.UserId, default);
        Assert.Equal("PAID", paid.SettlementStatus); Assert.Equal(0, paid.OutstandingAmount); Assert.Equal(1000, paid.ConfirmedPaidAmount);
        Assert.Single(await f.Db.SandboxSePayReceipts.ToArrayAsync()); Assert.Empty(await f.Db.Payments.ToArrayAsync()); Assert.Empty(await f.Db.BankTransfers.ToArrayAsync());
        var mode = await f.Db.SePaySandboxConnections.SingleAsync(); mode.DemoEnabled = false; await f.Db.SaveChangesAsync();
        Assert.Equal("UNPAID", (await f.Service.GetAsync(f.RequestId, f.UserId, default)).SettlementStatus);
    }
    [Fact]
    public async Task LegacyInternalResultsAreIgnoredAndRealQrAndOtherResidentAreBlocked()
    {
        using var f = new Fixture(); await f.Enable();
        f.Db.SandboxPaymentSimulations.Add(new() { InvoiceId = f.InvoiceId, UserId = f.UserId, ServiceRequestId = f.RequestId, Amount = 1000 }); await f.Db.SaveChangesAsync();
        Assert.Equal("UNPAID", (await f.Service.GetAsync(f.RequestId, f.UserId, default)).SettlementStatus);
        await Assert.ThrowsAsync<ResidentPaymentFailure>(() => f.Service.QrAsync(f.RequestId, f.UserId, default));
        await Assert.ThrowsAsync<ResidentPaymentFailure>(() => f.Service.ReportAsync(f.RequestId, f.UserId, new("REF"), default));
        Assert.Equal(404, (await Assert.ThrowsAsync<ResidentPaymentFailure>(() => f.Service.PrepareSandboxAsync(f.RequestId, Guid.NewGuid(), default))).Status);
    }
    [Fact]
    public async Task MismatchesAndOverpaymentsNeverConfirmAndPartialTransfersAccumulate()
    {
        using var f = new Fixture(); await f.Enable(); var pending = await f.Service.PrepareSandboxAsync(f.RequestId, f.UserId, default);
        var t = f.Transfer(pending.TransferCode!);
        Assert.Equal("ACCOUNT_MISMATCH", await f.Webhook.ReceiveAsync(f.Auth, t with { AccountNumber = "999999" }, default));
        Assert.Equal("ACCOUNT_MISMATCH", await f.Webhook.ReceiveAsync(f.Auth, t with { Id = 2, Gateway = "OtherBank" }, default));
        Assert.Equal("UNMATCHED_CODE", await f.Webhook.ReceiveAsync(f.Auth, t with { Id = 3, Content = "PF1234567890123456789012" }, default));
        Assert.Equal("OVERPAYMENT", await f.Webhook.ReceiveAsync(f.Auth, t with { Id = 4, TransferAmount = 1001 }, default));
        await f.Webhook.ReceiveAsync(f.Auth, t with { Id = 5, TransferAmount = 400 }, default);
        Assert.Equal("PARTIAL", (await f.Service.GetAsync(f.RequestId, f.UserId, default)).SettlementStatus);
        Assert.Equal(409, (await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Webhook.ReceiveAsync(f.Auth, t with { Id = 5, TransferAmount = 600 }, default))).Status);
        await f.Webhook.ReceiveAsync(f.Auth, t with { Id = 6, TransferAmount = 600 }, default);
        Assert.Equal("PAID", (await f.Service.GetAsync(f.RequestId, f.UserId, default)).SettlementStatus);
    }
    private sealed class Fixture : IDisposable, IServiceRequestBilling, IInvoiceFinancialSource, IPaymentAccountProtector, IPrivilegedActionOtp
    {
        public readonly Guid RequestId = Guid.NewGuid(), UserId = Guid.NewGuid(), InvoiceId = Guid.NewGuid();
        public readonly PaymentsDbContext Db = new(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        private readonly EphemeralDataProtectionProvider protection = new();
        private readonly HttpClient http = new(new Handler());
        private readonly string key = new('k', 40);
        public string Auth => "Apikey " + key;
        public readonly ResidentServiceRequestPayments Service;
        public readonly SePaySandboxWebhook Webhook;
        public Fixture() { Service = new(Db, this, this, TimeProvider.System, sandboxSetup: new SePaySandbox(http, Db, protection, this, TimeProvider.System)); Webhook = new(Db, protection, this, this, TimeProvider.System); }
        public async Task Enable() { Db.SePaySandboxConnections.Add(new() { DemoEnabled = true, LastCheckSucceeded = true,
            ProtectedApiToken = protection.CreateProtector("PropFlow.SePay.Sandbox.ApiToken.v1").Protect("test-token"),
            ProtectedWebhookKey = protection.CreateProtector(SePaySandbox.WebhookKeyPurpose).Protect(key) }); await Db.SaveChangesAsync(); }
        public SePayTransfer Transfer(string code) => new(1, "Vietcombank", "123456789", "in", 1000, null, code, "SBREF");
        public Task<ServiceRequestSettlementSnapshot?> FindForUserAsync(Guid id, Guid user, CancellationToken ct) => Task.FromResult<ServiceRequestSettlementSnapshot?>(id == RequestId && user == UserId ? new(RequestId, InvoiceId, "TEST", 1000, 0, 1000, "UNPAID") : null);
        public Task<InvoiceFinancialSnapshot?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<InvoiceFinancialSnapshot?>(id == InvoiceId ? new(id, "TEST", Guid.NewGuid(), new(2026, 10, 1), new(2026, 10, 1), null, null, 1000, "ISSUED") : null);
        public Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<ServiceRequestSettlementSnapshot?> IssueAsync(ServiceRequestInvoiceCommand command, CancellationToken ct) => throw new NotSupportedException();
        public Task<ServiceRequestSettlementSnapshot?> FindForResidentAsync(Guid id, Guid user, CancellationToken ct) => throw new NotSupportedException();
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct) => throw new NotSupportedException();
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct) => throw new NotSupportedException();
        public string Protect(string number) => number; public string Unprotect(string number) => number;
        public void Dispose() { Db.Dispose(); http.Dispose(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("""{"status":"success","data":[{"id":"test-bank","bank_full_name":"Vietcombank","bank_code":"VCB","bank_short_name":"Vietcombank","account_holder_name":"TEST OWNER","account_number":"123456789","active":true}]}""", Encoding.UTF8, "application/json") });
    }
}
