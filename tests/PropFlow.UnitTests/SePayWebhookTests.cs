using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

public sealed class SePayWebhookTests
{
    private static SePayOptions Options() => new() { Enabled = true, WebhookApiKey = new string('a', 40), Gateway = "Vietcombank", BankCode = "VCB" };

    [Fact]
    public void Authentication_FailsClosedWhenDisabledMissingOrIncorrect()
    {
        var options = Options();
        Assert.True(SePayVerification.Authorized("Apikey " + options.WebhookApiKey, options));
        Assert.False(SePayVerification.Authorized("Apikey " + new string('b', 40), options));
        Assert.False(SePayVerification.Authorized(null, options));
        options.Enabled = false;
        Assert.False(SePayVerification.Authorized("Apikey " + options.WebhookApiKey, options));
        options.Enabled = true;
        options.WebhookApiKey = "";
        Assert.False(SePayVerification.Authorized("Apikey ", options));
    }

    [Fact]
    public void TransferCode_IsShortEnoughForQrAndRequiresAnUnambiguousWholeCode()
    {
        var code = PaymentInstruction.CodeFor(Guid.NewGuid());
        Assert.Equal(24, code.Length);
        var transfer = new SePayTransfer(1, "Vietcombank", "123456", "in", 100000, null, "Chuyen tien " + code.ToLowerInvariant(), "BANKREF");
        Assert.Equal(code, SePayVerification.TransferCode(transfer));
        Assert.Equal(code, SePayVerification.TransferCode(transfer with { Code = code }));
        Assert.Null(SePayVerification.TransferCode(transfer with { Content = "X" + code }));
        Assert.Null(SePayVerification.TransferCode(transfer with { Content = code + " " + PaymentInstruction.CodeFor(Guid.NewGuid()) }));
        Assert.Null(SePayVerification.TransferCode(transfer with { Content = "DV-YC-TRUNCATED" }));
    }

    [Fact]
    public async Task Receiver_RequiresMatchingBankAccountAndIncomingFundsBeforeRecording()
    {
        await using var db = new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var destination = new PaymentDestination(1, "970436", "VCB", "Vietcombank", "encrypted", "3456", "NGUYEN VAN A", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var instruction = new PaymentInstruction(Guid.NewGuid(), destination.Id, DateTimeOffset.UtcNow);
        db.PaymentDestinations.Add(destination);
        db.PaymentInstructions.Add(instruction);
        await db.SaveChangesAsync();
        var finance = new FinanceFake();
        var handler = new SePayWebhook(db, new InvoiceFake(instruction.InvoiceId), finance, new ProtectorFake(), Microsoft.Extensions.Options.Options.Create(Options()));
        var transfer = new SePayTransfer(123, "Vietcombank", "123456", "in", 100000, null, instruction.TransferCode, "FT123");

        Assert.Equal("ACCOUNT_MISMATCH", (await handler.HandleAsync(transfer with { AccountNumber = "999999" }, default)).Outcome);
        Assert.Equal("ACCOUNT_MISMATCH", (await handler.HandleAsync(transfer with { Gateway = "OtherBank" }, default)).Outcome);
        Assert.Equal("IGNORED_OUTGOING", (await handler.HandleAsync(transfer with { TransferType = "out" }, default)).Outcome);
        Assert.Equal(0, finance.Calls);
        Assert.Equal("CONFIRMED", (await handler.HandleAsync(transfer, default)).Outcome);
        Assert.Equal(1, finance.Calls);
        Assert.Equal("sepay:123", finance.Key);
        Assert.Equal(instruction.InvoiceId, finance.Request!.InvoiceId);
        Assert.Equal(100000, finance.Request.Amount);
        finance.Outcome = MutationOutcome.Overpayment;
        Assert.Equal("Overpayment", (await handler.HandleAsync(transfer with { Id = 124, TransferAmount = 999999 }, default)).Outcome);
    }

    private sealed class ProtectorFake : IPaymentAccountProtector
    {
        public string Protect(string accountNumber) => "encrypted";
        public string Unprotect(string protectedAccountNumber) => "123456";
    }

    private sealed class InvoiceFake(Guid invoiceId) : IInvoiceFinancialSource
    {
        public Task<InvoiceFinancialSnapshot?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<InvoiceFinancialSnapshot?>(
            id == invoiceId ? new(id, "DV-TEST", Guid.NewGuid(), new(2026, 10, 1), new(2026, 10, 1), null, null, 100000, "ISSUED") : null);
        public Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FinanceFake : IPaymentFinanceStore
    {
        public int Calls;
        public string? Key;
        public CollectPaymentRequest? Request;
        public MutationOutcome Outcome = MutationOutcome.Success;
        public Task<MutationResult> CollectAsync(CollectPaymentRequest request, string key, Guid actorId, CancellationToken ct)
        { Calls++; Key = key; Request = request; return Task.FromResult(new MutationResult(Outcome)); }
        public Task<IReadOnlyList<PaymentRow>> ByInvoiceAsync(Guid invoiceId, CancellationToken ct) => Task.FromResult<IReadOnlyList<PaymentRow>>([]);
        public Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sortDirection, CancellationToken ct, string? filter = null) => throw new NotSupportedException();
        public Task<PaymentRow?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<MutationResult> ChangeAsync(Guid id, string action, string? reason, string key, Guid actorId, CancellationToken ct) => throw new NotSupportedException();
    }
}
