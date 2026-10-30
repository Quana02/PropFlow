namespace PropFlow.Modules.Payments.Contracts;

public sealed record BankTransferRow(Guid Id, long TransactionId, decimal Amount, string Gateway,
    string MaskedAccountNumber, string? TransferCode, string? BankReference, string Status, string Outcome,
    Guid? InvoiceId, string? InvoiceNumber, Guid? PaymentId, decimal AllocatedAmount, string? ReviewNote,
    Guid? ReviewedBy, DateTimeOffset ReceivedAt, DateTimeOffset UpdatedAt, int Revision, string? Content = null);
public sealed record PagedBankTransfers(int Total, int Page, int PageSize, IReadOnlyList<BankTransferRow> Items);
public sealed record BankTransferInvoiceOption(Guid Id, string InvoiceNumber, string? ApartmentNumber,
    string? ResidentName, decimal OutstandingAmount);
public sealed record AssignBankTransferRequest(Guid InvoiceId, decimal Amount, string Note, int ExpectedRevision);
public sealed record DismissBankTransferRequest(string Note, int ExpectedRevision);
