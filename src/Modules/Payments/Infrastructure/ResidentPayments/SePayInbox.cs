using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.BankTransfers;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

public sealed class SePayInbox(PaymentsDbContext db, IDataProtectionProvider protection, TimeProvider clock, SePayWebhook processor)
{
    private readonly IDataProtector payloadProtector = protection.CreateProtector("PropFlow.Payments.SePay.Inbox.v1");

    public async Task ReceiveAsync(SePayTransfer transfer, CancellationToken ct)
    {
        if (transfer.Id <= 0 || transfer.TransferAmount <= 0 || transfer.TransferAmount >= 1000000000000000000m ||
            transfer.TransferAmount != decimal.Round(transfer.TransferAmount) || transfer.TransferType is not ("in" or "out") ||
            string.IsNullOrWhiteSpace(transfer.AccountNumber) || string.IsNullOrWhiteSpace(transfer.Gateway))
            throw new PaymentSettingsFailure(400, "invalid_bank_transfer", "Giao dịch ngân hàng không hợp lệ.");
        var payload = JsonSerializer.Serialize(transfer with { TransferAmount = decimal.Round(transfer.TransferAmount, 0) });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var existing = await db.BankTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.TransactionId == transfer.Id, ct);
        if (existing is not null) { CheckHash(existing, hash); return; }
        var account = transfer.AccountNumber.Trim();
        db.BankTransfers.Add(new BankTransfer(transfer.Id, hash, payloadProtector.Protect(payload), transfer.TransferAmount,
            transfer.Gateway.Trim(), account.Length >= 4 ? account[^4..] : "", SePayVerification.TransferCode(transfer), transfer.ReferenceCode, clock.GetUtcNow()));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            existing = await db.BankTransfers.AsNoTracking().SingleAsync(x => x.TransactionId == transfer.Id, ct);
            CheckHash(existing, hash);
        }
    }

    private static void CheckHash(BankTransfer existing, string hash)
    {
        if (existing.PayloadHash != hash) throw new PaymentSettingsFailure(409, "sepay_transaction_conflict", "Mã giao dịch đã tồn tại với dữ liệu khác.");
    }

    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var row = await db.BankTransfers.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null || !row.Claim(clock.GetUtcNow())) return;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return; }
        var revision = row.Revision;
        try
        {
            var transfer = JsonSerializer.Deserialize<SePayTransfer>(payloadProtector.Unprotect(row.ProtectedPayload))
                ?? throw new InvalidOperationException("Invalid encrypted bank payload.");
            var allocation = row.AssignedInvoiceId is { } invoiceId && row.RequestedAmount is { } amount && row.ReviewedBy is { } actor
                ? new SePayAllocation(invoiceId, amount, actor) : null;
            var result = await processor.HandleAsync(transfer, ct, allocation);
            row.Complete(result.Outcome, result.InvoiceId, result.PaymentId, result.AllocatedAmount, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        catch
        {
            db.ChangeTracker.Clear();
            var retry = await db.BankTransfers.SingleAsync(x => x.Id == id, ct);
            if (retry.Revision == revision)
            {
                retry.RetryLater(clock.GetUtcNow());
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            }
            throw;
        }
    }
}

public sealed class SePayInboxWorker(IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<SePayOptions> options, ILogger<SePayInboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
                var now = clock.GetUtcNow();
                var ids = await db.BankTransfers.AsNoTracking().Where(x =>
                        (options.Value.Enabled || x.AssignedInvoiceId != null) &&
                        (x.Status == "RECEIVED" || x.Status == "RETRY" || x.Status == "PROCESSING") && x.NextAttemptAt <= now)
                    .OrderBy(x => x.ReceivedAt).Select(x => x.Id).Take(10).ToArrayAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try { await scope.ServiceProvider.GetRequiredService<SePayInbox>().ProcessAsync(id, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                    catch (Exception ex) { logger.LogWarning("SePay inbox {InboxId} deferred ({ErrorType}).", id, ex.GetType().Name); }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("SePay inbox worker deferred ({ErrorType}).", ex.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) return; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
}
