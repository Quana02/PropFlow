using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.UnitTests;

public sealed class SePayInboxTests
{
    [Fact]
    public async Task WebhookEndpointRejectsUnauthenticatedBodyBeforePersisting()
    {
        await using var f = await Fixture.CreateAsync();
        var config = new SePayOptions { Enabled = true, WebhookApiKey = new string('k', 40) };
        var controller = new PropFlow.Modules.Payments.Presentation.SePayWebhookController(f.Inbox, Options.Create(config), f.Clock);
        var payload = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(f.Transfer(11, 100000)));
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext(); context.Request.Body = new MemoryStream(payload);
        context.Request.Headers.Authorization = "Apikey wrong";
        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = context };
        Assert.IsType<Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult>(await controller.Receive(default));
        Assert.Empty(await f.Db.BankTransfers.ToArrayAsync());
        context.Request.Body = new MemoryStream(payload); context.Request.Headers.Authorization = "Apikey " + config.WebhookApiKey;
        var accepted = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(await controller.Receive(default));
        Assert.Contains("true", System.Text.Json.JsonSerializer.Serialize(accepted.Value));
        Assert.Single(await f.Db.BankTransfers.ToArrayAsync());
        Assert.Equal(0, f.Finance.Calls);
    }

    [Fact]
    public async Task WebhookIsDurableEncryptedAndDuplicateDeliveryCannotCollectTwice()
    {
        await using var f = await Fixture.CreateAsync();
        var transfer = f.Transfer(1, 100000);
        await f.Inbox.ReceiveAsync(transfer, default);
        await f.Inbox.ReceiveAsync(transfer, default);
        var row = await f.Db.BankTransfers.SingleAsync();
        Assert.Equal("RECEIVED", row.Status);
        Assert.DoesNotContain("123456789", row.ProtectedPayload);
        Assert.Equal(0, f.Finance.Calls);
        await f.Inbox.ProcessAsync(row.Id, default);
        await f.Inbox.ProcessAsync(row.Id, default);
        Assert.Equal(1, f.Finance.Calls);
        Assert.Equal("CONFIRMED", (await f.Db.BankTransfers.SingleAsync()).Status);
        var error = await Assert.ThrowsAsync<PaymentSettingsFailure>(() => f.Inbox.ReceiveAsync(transfer with { TransferAmount = 50000 }, default));
        Assert.Equal("sepay_transaction_conflict", error.Code);
    }

    [Fact]
    public async Task ExcessRequiresExplicitAllocationAndRemainingMoneyStaysInReview()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Inbox.ReceiveAsync(f.Transfer(2, 150000), default);
        var row = await f.Db.BankTransfers.SingleAsync();
        await f.Inbox.ProcessAsync(row.Id, default);
        row = await f.Db.BankTransfers.SingleAsync();
        Assert.Equal("REVIEW", row.Status);
        Assert.Equal("Overpayment", row.Outcome);
        var review = new BankTransferReview(f.Db, f.Invoices, f.Protection, f.Clock);
        var actor = Guid.NewGuid();
        await review.AssignAsync(row.Id, new(f.Invoices.Id, 100000, "Áp dụng đủ hóa đơn, giữ tiền dư để hoàn", row.Revision), actor, default);
        await f.Inbox.ProcessAsync(row.Id, default);
        row = await f.Db.BankTransfers.SingleAsync();
        Assert.Equal("REVIEW", row.Status);
        Assert.Equal("EXCESS_REQUIRES_REVIEW", row.Outcome);
        Assert.Equal(100000, row.AllocatedAmount);
        Assert.NotNull(row.PaymentId);
        await Assert.ThrowsAsync<PaymentSettingsFailure>(() => review.AssignAsync(row.Id, new(f.Invoices.Id, 50000, "Không được ghi nhận lần hai", row.Revision), actor, default));
        await review.DismissAsync(row.Id, new("Đã hoàn phần tiền dư theo chứng từ FT123", row.Revision), actor, default);
        Assert.Equal("DISMISSED", row.Status);
        using var audit = System.Text.Json.JsonDocument.Parse(row.ReviewHistory);
        Assert.Contains(audit.RootElement.EnumerateArray(), entry => entry.GetProperty("Note").GetString()!.Contains("giữ tiền dư"));
        Assert.Contains("FT123", row.ReviewHistory);
        Assert.Equal(100000, row.AllocatedAmount);
    }

    [Fact]
    public async Task UnmatchedTransferCanBeAssignedButCannotBypassReceivingAccountCheck()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Inbox.ReceiveAsync(f.Transfer(3, 50000) with { Content = "Không có mã hóa đơn", AccountNumber = "999999" }, default);
        var row = await f.Db.BankTransfers.SingleAsync();
        await f.Inbox.ProcessAsync(row.Id, default);
        row = await f.Db.BankTransfers.SingleAsync();
        Assert.Equal("UNMATCHED_CODE", row.Outcome);
        var review = new BankTransferReview(f.Db, f.Invoices, f.Protection, f.Clock);
        await review.AssignAsync(row.Id, new(f.Invoices.Id, 50000, "Kiểm tra gắn hóa đơn", row.Revision), Guid.NewGuid(), default);
        await f.Inbox.ProcessAsync(row.Id, default);
        row = await f.Db.BankTransfers.SingleAsync();
        Assert.Equal("ACCOUNT_MISMATCH", row.Outcome);
        Assert.Equal("REVIEW", row.Status);
        Assert.Equal(0, f.Finance.Calls);
    }

    [Fact]
    public async Task FailedProcessingIsRetriedAfterLeaseWithoutLosingPayload()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Inbox.ReceiveAsync(f.Transfer(4, 100000), default);
        var row = await f.Db.BankTransfers.SingleAsync();
        f.Finance.FailOnce = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => f.Inbox.ProcessAsync(row.Id, default));
        row = await f.Db.BankTransfers.SingleAsync();
        Assert.Equal("RETRY", row.Status);
        f.Clock.Now = f.Clock.Now.AddMinutes(1);
        await f.Inbox.ProcessAsync(row.Id, default);
        Assert.Equal("CONFIRMED", (await f.Db.BankTransfers.SingleAsync()).Status);
        Assert.Equal(2, f.Finance.Calls);
    }

    [Fact]
    public void HmacAuthenticatesRawBodyRejectsTamperingReplayAndApiKeyFallback()
    {
        var config = new SePayOptions { Enabled = true, AuthenticationMode = "HMAC", WebhookSecret = new string('s', 40), WebhookApiKey = new string('k', 40) };
        var now = DateTimeOffset.UtcNow;
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var body = Encoding.UTF8.GetBytes("{\"id\":1, \"transferAmount\":100000}");
        var signed = Encoding.UTF8.GetBytes(timestamp + ".").Concat(body).ToArray();
        var signature = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.WebhookSecret), signed));
        Assert.True(SePayVerification.Signed(body, signature, timestamp, config, now));
        Assert.False(SePayVerification.Signed(Encoding.UTF8.GetBytes("{\"id\":1,\"transferAmount\":100000}"), signature, timestamp, config, now));
        Assert.False(SePayVerification.Signed(body, signature, timestamp, config, now.AddMinutes(6)));
        Assert.False(SePayVerification.Authorized("Apikey " + config.WebhookApiKey, config));
    }

    [Fact]
    public void QrPayloadMatchesPublishedVietQrCrcVector()
    {
        var qr = ResidentServiceRequestPayments.VietQrPayload("970415", "113366668888", 79000, "Ung Ho Quy Vac Xin");
        Assert.Equal("00020101021238560010A0000007270126000697041501121133666688880208QRIBFTTA53037045405790005802VN62220818Ung Ho Quy Vac Xin63043ACF", qr);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required PaymentsDbContext Db;
        public readonly EphemeralDataProtectionProvider Protection = new();
        public readonly Clock Clock = new();
        public readonly Invoices Invoices = new();
        public readonly Finance Finance = new();
        public required SePayInbox Inbox;
        public required PaymentInstruction Instruction;
        public static async Task<Fixture> CreateAsync()
        {
            var db = new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var placeholder = new Fixture { Db = db, Inbox = null!, Instruction = null! };
            var destination = new PaymentDestination(1, "970436", "VCB", "Vietcombank", "encrypted", "6789", "NGUYEN VAN A", Guid.NewGuid(), placeholder.Clock.Now, Guid.NewGuid());
            var instruction = new PaymentInstruction(placeholder.Invoices.Id, destination.Id, placeholder.Clock.Now);
            db.PaymentDestinations.Add(destination); db.PaymentInstructions.Add(instruction); await db.SaveChangesAsync();
            var processor = new SePayWebhook(db, placeholder.Invoices, placeholder.Finance, new Protector(), Options.Create(new SePayOptions()));
            placeholder.Inbox = new SePayInbox(db, placeholder.Protection, placeholder.Clock, processor); placeholder.Instruction = instruction;
            return placeholder;
        }
        public SePayTransfer Transfer(long id, decimal amount) => new(id, "Vietcombank", "123456789", "in", amount, null, Instruction.TransferCode, "FT" + id);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Protector : IPaymentAccountProtector { public string Protect(string value) => "encrypted"; public string Unprotect(string value) => "123456789"; }
    private sealed class Invoices : IInvoiceFinancialSource
    {
        public readonly Guid Id = Guid.NewGuid();
        public Task<InvoiceFinancialSnapshot?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<InvoiceFinancialSnapshot?>(id == Id ? Snapshot() : null);
        public Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InvoiceFinancialSnapshot>>([Snapshot()]);
        private InvoiceFinancialSnapshot Snapshot() => new(Id, "DV-TEST", Guid.NewGuid(), new(2026,10,1), new(2026,10,1), null, null, 100000, "ISSUED");
    }
    private sealed class Finance : IPaymentFinanceStore
    {
        public int Calls; public bool FailOnce;
        public Task<MutationResult> CollectAsync(CollectPaymentRequest request, string key, Guid actor, CancellationToken ct)
        {
            Calls++; if (FailOnce) { FailOnce = false; throw new HttpRequestException(); }
            if (request.Amount > 100000) return Task.FromResult(new MutationResult(MutationOutcome.Overpayment));
            return Task.FromResult(new MutationResult(MutationOutcome.Success, new PaymentRow(Guid.NewGuid(), "BANK-TEST", request.InvoiceId, request.Amount,
                "BANK_TRANSFER", DateTimeOffset.UtcNow, request.ReferenceNumber, request.Note, "CONFIRMED", DateTimeOffset.UtcNow)));
        }
        public Task<IReadOnlyList<PaymentRow>> ByInvoiceAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string direction, CancellationToken ct, string? filter = null) => throw new NotSupportedException();
        public Task<PaymentRow?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<MutationResult> ChangeAsync(Guid id, string action, string? reason, string key, Guid actor, CancellationToken ct) => throw new NotSupportedException();
    }
}
