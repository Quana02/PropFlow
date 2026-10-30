namespace PropFlow.Modules.Payments.Contracts;

public sealed record ConfirmedPaymentTotal(Guid InvoiceId, decimal Amount);

public interface IConfirmedPaymentSource
{
    Task<IReadOnlyDictionary<Guid, decimal>> TotalsAsync(IReadOnlyCollection<Guid> invoiceIds, CancellationToken ct);
    Task<decimal> TotalAsync(Guid invoiceId, CancellationToken ct);
}

public sealed record ResidentServiceRequestPaymentView(
    Guid ServiceRequestId,
    Guid InvoiceId,
    string InvoiceNumber,
    decimal TotalAmount,
    decimal ConfirmedPaidAmount,
    decimal OutstandingAmount,
    string SettlementStatus,
    string? PaymentStatus,
    Guid? PaymentId,
    string? ReferenceNumber,
    string? RejectionReason,
    string? BankName,
    string? BankCode,
    string? MaskedAccountNumber,
    string? AccountName,
    string? QrImageUrl,
    bool AutomaticConfirmationEnabled = false,
    string? TransferCode = null,
    string PaymentEnvironment = "LIVE",
    bool SandboxSimulationAvailable = false,
    decimal? SimulatedTransferAmount = null);

public sealed record ReportResidentServiceRequestTransferRequest(
    string ReferenceNumber,
    DateTimeOffset? TransferredAt = null,
    string? Note = null);
