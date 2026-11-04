using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

public sealed class SePaySandboxWebhook(PaymentsDbContext db, IDataProtectionProvider protection,
    IPaymentAccountProtector accounts, IInvoiceFinancialSource invoices, TimeProvider clock)
{
    private readonly IDataProtector keys = protection.CreateProtector(SePaySandbox.WebhookKeyPurpose);
    private readonly IDataProtector payloads = protection.CreateProtector("PropFlow.SePay.Sandbox.Receipts.v1");

    public async Task<string> ReceiveAsync(string? authorization, SePayTransfer transfer, CancellationToken ct)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(728405193)", ct);
        var config = await db.SePaySandboxConnections.AsNoTracking().SingleOrDefaultAsync(ct);
        if (config?.DemoEnabled != true || config.ProtectedWebhookKey is null || config.ProtectedApiToken is null)
            throw new PaymentSettingsFailure(503, "sandbox_webhook_disabled", "Webhook Sandbox chưa được bật.");
        var key = keys.Unprotect(config.ProtectedWebhookKey);
        if (!SePayVerification.Authorized(authorization, new() { Enabled = true, WebhookApiKey = key }))
            throw new PaymentSettingsFailure(401, "sandbox_webhook_unauthorized", "Khóa webhook Sandbox không hợp lệ.");
        if (transfer.Id <= 0 || transfer.TransferAmount <= 0 || transfer.TransferAmount >= 1000000000000000000m ||
            transfer.TransferAmount != decimal.Round(transfer.TransferAmount) || transfer.TransferType is not ("in" or "out") ||
            string.IsNullOrWhiteSpace(transfer.AccountNumber) || string.IsNullOrWhiteSpace(transfer.Gateway) ||
            transfer.AccountNumber.Length > 50 || transfer.Gateway.Length > 100 || transfer.Content?.Length > 4000 ||
            transfer.Code?.Length > 100 || transfer.ReferenceCode?.Length > 100 || transfer.SubAccount?.Length > 100 || transfer.TransactionDate?.Length > 50)
            throw new PaymentSettingsFailure(400, "invalid_sandbox_transfer", "Giao dịch Sandbox không hợp lệ.");
        var json = JsonSerializer.Serialize(transfer with { TransferAmount = decimal.Round(transfer.TransferAmount, 0) });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var prior = await db.SandboxSePayReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.TransactionId == transfer.Id, ct);
        if (prior is not null)
        {
            if (prior.PayloadHash != hash) throw new PaymentSettingsFailure(409, "sandbox_transaction_conflict", "Mã giao dịch đã tồn tại với dữ liệu khác.");
            if (tx is not null) await tx.CommitAsync(ct);
            return prior.Outcome;
        }
        var receipt = new SandboxSePayReceipt { TransactionId = transfer.Id, Amount = transfer.TransferAmount,
            PayloadHash = hash, ProtectedPayload = payloads.Protect(json), ReceivedAt = clock.GetUtcNow() };
        var codes = Regex.Matches($"{transfer.Code} {transfer.Content}".ToUpperInvariant(), @"(?<![A-Z0-9])PS[A-F0-9]{22}(?![A-Z0-9])")
            .Select(x => x.Value).Distinct().ToArray();
        var code = codes.Length == 1 ? codes[0] : null;
        var instruction = code is null ? null : await db.SandboxPaymentSimulations.SingleOrDefaultAsync(x => x.TransferCode == code, ct);
        if (transfer.TransferType != "in") receipt.Outcome = "IGNORED_OUTGOING";
        else if (instruction is null || instruction.ProtectedAccountNumber is null) receipt.Outcome = "UNMATCHED_CODE";
        else
        {
            receipt.InvoiceId = instruction.InvoiceId;
            var aliases = JsonSerializer.Deserialize<string[]>(instruction.GatewayAliases ?? "[]") ?? [];
            var invoice = await invoices.FindAsync(instruction.InvoiceId, ct);
            if (invoice?.Status != "ISSUED") receipt.Outcome = "INVOICE_UNAVAILABLE";
            else if (accounts.Unprotect(instruction.ProtectedAccountNumber) != transfer.AccountNumber.Trim() ||
                !aliases.Any(x => x.Equals(transfer.Gateway.Trim(), StringComparison.OrdinalIgnoreCase))) receipt.Outcome = "ACCOUNT_MISMATCH";
            else if (transfer.TransferAmount > instruction.RequestedAmount - instruction.VerifiedAmount) receipt.Outcome = "OVERPAYMENT";
            else { instruction.VerifiedAmount += transfer.TransferAmount; receipt.Outcome = "CONFIRMED"; }
        }
        db.SandboxSePayReceipts.Add(receipt); await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return receipt.Outcome;
    }
}
