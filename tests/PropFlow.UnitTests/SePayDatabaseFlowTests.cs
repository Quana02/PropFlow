using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.UnitTests;

public sealed class PaymentDatabaseFactAttribute : FactAttribute
{
    public PaymentDatabaseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROPFLOW_PAYMENT_TEST_DATABASE")))
            Skip = "Requires an explicitly configured migrated payment test database.";
    }
}

public sealed class SePayDatabaseFlowTests
{
    [PaymentDatabaseFact]
    public async Task RealDatabase_ConcurrentDeliveryPartialSettlementManualExcessAndCleanup()
    {
        var connection = Environment.GetEnvironmentVariable("PROPFLOW_PAYMENT_TEST_DATABASE")!;
        var options = new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql(connection).Options;
        await using var db = new PaymentsDbContext(options);
        var source = new Source();
        var protection = new EphemeralDataProtectionProvider();
        var settings = Options.Create(new SePayOptions { Enabled = true, WebhookApiKey = new string('k', 40) });
        var destination = new PaymentDestination(1, "970436", "VCB", "Vietcombank", "fixture-only", "6789", "TEST BANK OWNER",
            Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());
        // Existing destination versions have unique/global indexes; this fixture uses an inactive archived version.
        destination.Deactivate(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var version = (await db.PaymentDestinations.MaxAsync(x => (int?)x.Version) ?? 0) + 1;
        typeof(PaymentDestination).GetProperty(nameof(PaymentDestination.Version))!.SetValue(destination, version);
        var instruction = new PaymentInstruction(source.Id, destination.Id, DateTimeOffset.UtcNow);
        var second = new PaymentInstruction(source.SecondId, destination.Id, DateTimeOffset.UtcNow);
        var transactionId = 8_000_000_000_000_000L + Random.Shared.NextInt64(1_000_000_000);
        var ids = new[] { transactionId, transactionId + 1, transactionId + 2 };
        var invoiceIds = new[] { source.Id, source.SecondId };
        var userId = Guid.NewGuid();
        var claim = new Payment("TEST-" + Guid.NewGuid().ToString("N"), source.Id, 100000, "BANK_TRANSFER", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, userId);
        try
        {
            db.PaymentDestinations.Add(destination); db.PaymentInstructions.AddRange(instruction, second); db.Payments.Add(claim); await db.SaveChangesAsync();
            SePayInbox Inbox(PaymentsDbContext context) => new(context, protection, TimeProvider.System,
                new SePayWebhook(context, source, new PaymentFinanceStore(context, source), new Protector(), settings));
            var transfer = new SePayTransfer(transactionId, "Vietcombank", "123456789", "in", 25000, null, instruction.TransferCode, "TEST-REF");
            await using (var firstContext = new PaymentsDbContext(options))
            await using (var otherContext = new PaymentsDbContext(options))
                await Task.WhenAll(Inbox(firstContext).ReceiveAsync(transfer, default), Inbox(otherContext).ReceiveAsync(transfer, default));
            var rowId = await db.BankTransfers.Where(x => x.TransactionId == transactionId).Select(x => x.Id).SingleAsync();
            await using (var firstContext = new PaymentsDbContext(options))
            await using (var otherContext = new PaymentsDbContext(options))
                await Task.WhenAll(Inbox(firstContext).ProcessAsync(rowId, default), Inbox(otherContext).ProcessAsync(rowId, default));
            db.ChangeTracker.Clear();
            Assert.Equal(25000, await new ConfirmedPaymentSource(db).TotalAsync(source.Id, default));
            Assert.Equal(PaymentStatus.REJECTED, (await db.Payments.SingleAsync(x => x.Id == claim.Id)).Status);
            Assert.Equal(1, await db.Payments.CountAsync(x => x.InvoiceId == source.Id && x.Status == PaymentStatus.CONFIRMED));
            var billing = new Settlement(source, db);
            var resident = new ResidentServiceRequestPayments(db, billing, new Protector(), TimeProvider.System, settings);
            var partial = await resident.GetAsync(source.Id, userId, default);
            Assert.Equal(75000, partial.OutstandingAmount);
            Assert.Equal("PARTIAL", partial.SettlementStatus);
            Assert.True(partial.AutomaticConfirmationEnabled);
            Assert.Equal(instruction.TransferCode, partial.TransferCode);
            Assert.NotEmpty(await resident.QrAsync(source.Id, userId, default));

            var inbox = Inbox(db);
            await inbox.ReceiveAsync(transfer with { Id = transactionId + 1, TransferAmount = 75000 }, default);
            var finalId = await db.BankTransfers.Where(x => x.TransactionId == transactionId + 1).Select(x => x.Id).SingleAsync();
            await inbox.ProcessAsync(finalId, default);
            var paid = await resident.GetAsync(source.Id, userId, default);
            Assert.Equal("PAID", paid.SettlementStatus); Assert.Equal(0, paid.OutstandingAmount); Assert.Null(paid.QrImageUrl);
            await inbox.ReceiveAsync(transfer, default); await inbox.ProcessAsync(rowId, default);
            Assert.Equal(100000, await new ConfirmedPaymentSource(db).TotalAsync(source.Id, default));

            var excess = transfer with { Id = transactionId + 2, TransferAmount = 150000, Content = second.TransferCode };
            await inbox.ReceiveAsync(excess, default);
            var excessId = await db.BankTransfers.Where(x => x.TransactionId == transactionId + 2).Select(x => x.Id).SingleAsync();
            await inbox.ProcessAsync(excessId, default);
            var excessRow = await db.BankTransfers.SingleAsync(x => x.Id == excessId);
            Assert.Equal("REVIEW", excessRow.Status);
            var review = new BankTransferReview(db, source, protection, TimeProvider.System);
            await review.AssignAsync(excessId, new(source.SecondId, 100000, "Fixture allocation; remaining funds reviewed", excessRow.Revision), userId, default);
            await inbox.ProcessAsync(excessId, default);
            excessRow = await db.BankTransfers.SingleAsync(x => x.Id == excessId);
            Assert.Equal("EXCESS_REQUIRES_REVIEW", excessRow.Outcome);
            Assert.Equal(100000, excessRow.AllocatedAmount);
            Assert.Equal(100000, await new ConfirmedPaymentSource(db).TotalAsync(source.SecondId, default));
            await review.DismissAsync(excessId, new("Fixture excess resolved with refund reference TEST-REFUND", excessRow.Revision), userId, default);
            await inbox.ReceiveAsync(excess, default); await inbox.ProcessAsync(excessId, default);
            Assert.Equal(100000, await new ConfirmedPaymentSource(db).TotalAsync(source.SecondId, default));
        }
        finally
        {
            // Remove only this fixture's random IDs; never truncate or modify application records.
            db.ChangeTracker.Clear();
            var payments = await db.Payments.Where(x => invoiceIds.Contains(x.InvoiceId)).Select(x => x.Id).ToArrayAsync();
            await db.PaymentStatusHistory.Where(x => payments.Contains(x.PaymentId)).ExecuteDeleteAsync();
            await db.PaymentOperations.Where(x => payments.Contains(x.PaymentId)).ExecuteDeleteAsync();
            await db.Payments.Where(x => payments.Contains(x.Id)).ExecuteDeleteAsync();
            await db.BankTransfers.Where(x => ids.Contains(x.TransactionId)).ExecuteDeleteAsync();
            await db.PaymentInstructions.Where(x => invoiceIds.Contains(x.InvoiceId)).ExecuteDeleteAsync();
            await db.PaymentDestinations.Where(x => x.Id == destination.Id).ExecuteDeleteAsync();
        }
    }

    private sealed class Protector : IPaymentAccountProtector { public string Protect(string value) => "fixture-only"; public string Unprotect(string value) => "123456789"; }
    private sealed class Source : IInvoiceFinancialSource
    {
        public readonly Guid Id = Guid.NewGuid(), SecondId = Guid.NewGuid();
        public Task<InvoiceFinancialSnapshot?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<InvoiceFinancialSnapshot?>(
            id == Id || id == SecondId ? Snapshot(id) : null);
        public Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InvoiceFinancialSnapshot>>([Snapshot(Id), Snapshot(SecondId)]);
        private static InvoiceFinancialSnapshot Snapshot(Guid id) => new(id, "DV-TEST-" + id.ToString("N")[..8], Guid.NewGuid(), new(2026,10,1), new(2026,10,1), null, null, 100000, "ISSUED");
    }
    private sealed class Settlement(Source source, PaymentsDbContext db) : IServiceRequestBilling
    {
        public async Task<ServiceRequestSettlementSnapshot?> FindForUserAsync(Guid id, Guid user, CancellationToken ct)
        { var invoice = await source.FindAsync(id, ct); if (invoice is null) return null; var paid = await new ConfirmedPaymentSource(db).TotalAsync(id, ct);
            return new(id, id, invoice.InvoiceNumber, invoice.TotalAmount, paid, invoice.TotalAmount - paid, paid >= invoice.TotalAmount ? "PAID" : paid > 0 ? "PARTIAL" : "UNPAID"); }
        public Task<ServiceRequestSettlementSnapshot?> FindForResidentAsync(Guid id, Guid resident, CancellationToken ct) => FindForUserAsync(id, resident, ct);
        public Task<ServiceRequestSettlementSnapshot?> IssueAsync(ServiceRequestInvoiceCommand command, CancellationToken ct) => throw new NotSupportedException();
    }
}
