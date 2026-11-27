namespace PropFlow.Modules.Payments.Contracts;

public sealed record InvoicePaymentInstructions(string BankName, string AccountName, string MaskedAccountNumber,
    decimal Amount, string TransferCode, byte[] QrPng);

// Internal document generation: the caller owns invoice/recipient authorization.
public interface IInvoicePaymentInstructions
{
    Task<InvoicePaymentInstructions?> ForDocumentAsync(Guid invoiceId, CancellationToken ct);
}
