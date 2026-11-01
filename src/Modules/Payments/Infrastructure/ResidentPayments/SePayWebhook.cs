using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

public sealed class SePayOptions
{
    public bool Enabled { get; set; }
    public string ApiToken { get; set; } = "";
    public string WebhookPublicBaseUrl { get; set; } = "";
    public string WebhookApiKey { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string BankCode { get; set; } = "";
    public string AuthenticationMode { get; set; } = "API_KEY";
    public string WebhookSecret { get; set; } = "";
    // Dedicated non-login audit identity for automated bank reconciliation.
    public static readonly Guid AuditActor = Guid.Parse("7bba1639-1f4c-46c3-9716-3792634925ca");
}

public sealed record SePayTransfer(long Id, string? Gateway, string? AccountNumber, string? TransferType,
    decimal TransferAmount, string? Code, string? Content, string? ReferenceCode,
    string? TransactionDate = null, string? SubAccount = null);
public sealed record SePayProcessingResult(string Outcome, Guid? InvoiceId = null, Guid? PaymentId = null, decimal AllocatedAmount = 0);
public sealed record SePayAllocation(Guid InvoiceId, decimal Amount, Guid Actor);

public static partial class SePayVerification
{
    public static bool HasCredential(SePayOptions options) => options.AuthenticationMode == "HMAC"
        ? options.WebhookSecret.Length >= 32 : options.AuthenticationMode == "API_KEY" && options.WebhookApiKey.Length >= 32;
    public static bool Authorized(string? authorization, SePayOptions options)
    {
        if (!options.Enabled || options.AuthenticationMode != "API_KEY" || options.WebhookApiKey.Length < 32 ||
            authorization is null || !authorization.StartsWith("Apikey ", StringComparison.Ordinal)) return false;
        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(options.WebhookApiKey)),
            SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..])));
    }

    public static bool Signed(ReadOnlySpan<byte> body, string? signature, string? timestamp, SePayOptions options, DateTimeOffset now)
    {
        if (!options.Enabled || options.AuthenticationMode != "HMAC" || options.WebhookSecret.Length < 32 ||
            !long.TryParse(timestamp, out var seconds) || Math.Abs((decimal)now.ToUnixTimeSeconds() - seconds) > 300 ||
            signature is null || !signature.StartsWith("sha256=", StringComparison.Ordinal) || signature.Length != 71) return false;
        byte[] supplied;
        try { supplied = Convert.FromHexString(signature[7..]); } catch (FormatException) { return false; }
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var signed = new byte[prefix.Length + body.Length]; prefix.CopyTo(signed, 0); body.CopyTo(signed.AsSpan(prefix.Length));
        return CryptographicOperations.FixedTimeEquals(supplied, HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.WebhookSecret), signed));
    }

    public static string? TransferCode(SePayTransfer transfer)
    {
        var matches = CodePattern().Matches($"{transfer.Code} {transfer.Content}".ToUpperInvariant())
            .Select(x => x.Value).Distinct().ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    [GeneratedRegex(@"(?<![A-Z0-9])PF[A-F0-9]{22}(?![A-Z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}

public sealed class SePayWebhook(PaymentsDbContext db, IInvoiceFinancialSource invoices,
    IPaymentFinanceStore payments, IPaymentAccountProtector protector, IOptions<SePayOptions> options, IPaymentBankDirectory? banks = null)
{
    public async Task<SePayProcessingResult> HandleAsync(SePayTransfer transfer, CancellationToken ct, SePayAllocation? allocation = null)
    {
        if (transfer.Id <= 0 || transfer.TransferAmount <= 0 || transfer.TransferAmount != decimal.Round(transfer.TransferAmount) ||
            transfer.TransferAmount >= 1000000000000000000m) return new("INVALID_TRANSFER");
        if (transfer.TransferType != "in") return new("IGNORED_OUTGOING");
        if (allocation is not null && (allocation.Amount <= 0 || allocation.Amount > transfer.TransferAmount || allocation.Amount != decimal.Round(allocation.Amount)))
            return new("INVALID_ALLOCATION", allocation.InvoiceId);
        var code = SePayVerification.TransferCode(transfer);
        if (code is null && allocation is null) return new("UNMATCHED_CODE");
        var candidates = db.PaymentInstructions.AsNoTracking();
        candidates = allocation is not null ? candidates.Where(x => x.InvoiceId == allocation.InvoiceId)
            : candidates.Where(x => x.TransferCode == code);
        var matches = await candidates
            .Select(x => new { x.Id, x.InvoiceId, x.PaymentDestinationId }).Take(2).ToArrayAsync(ct);
        if (matches.Length != 1) return new("UNMATCHED_CODE");
        var instruction = matches[0];
        var destination = await db.PaymentDestinations.AsNoTracking().SingleAsync(x => x.Id == instruction.PaymentDestinationId, ct);
        var gateway = transfer.Gateway?.Trim();
        var bankMatches = string.Equals(gateway, destination.BankCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(gateway, destination.BankName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Value.BankCode, destination.BankCode, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(options.Value.Gateway) && string.Equals(gateway, options.Value.Gateway, StringComparison.OrdinalIgnoreCase);
        if (!bankMatches && banks is not null)
        {
            var bank = (await banks.ListAsync(ct)).SingleOrDefault(x => x.Bin == destination.BankBin);
            bankMatches = bank is not null && (string.Equals(bank.ShortName, gateway, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(bank.Name, gateway, StringComparison.OrdinalIgnoreCase) || string.Equals(bank.Code, gateway, StringComparison.OrdinalIgnoreCase));
        }
        if (!bankMatches || !string.Equals(protector.Unprotect(destination.ProtectedAccountNumber), transfer.AccountNumber?.Trim(), StringComparison.Ordinal))
            return new("ACCOUNT_MISMATCH", instruction.InvoiceId);
        if (await invoices.FindAsync(instruction.InvoiceId, ct) is null) return new("INVOICE_UNAVAILABLE", instruction.InvoiceId);
        // CollectAsync uses the same invoice lock as accountant confirmations and deduplicates provider retries.
        var amount = allocation?.Amount ?? transfer.TransferAmount;
        var result = await payments.CollectVerifiedBankAsync(new(instruction.InvoiceId, amount, "BANK_TRANSFER",
            $"SEPAY-{transfer.Id}", $"SePay {transfer.Id}; bank reference: {transfer.ReferenceCode}"),
            $"sepay:{transfer.Id}", allocation?.Actor ?? SePayOptions.AuditActor, ct);
        if (result.Outcome != MutationOutcome.Success) return new(result.Outcome.ToString(), instruction.InvoiceId);
        return new("CONFIRMED", instruction.InvoiceId, result.Payment?.Id, amount);
    }
}
