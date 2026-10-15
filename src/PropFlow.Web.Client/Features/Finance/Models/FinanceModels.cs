namespace PropFlow.Web.Client.Features.Finance.Models;

public sealed record PagedFinanceResult<T>(int Total, int Page, int PageSize, T[] Items, Dictionary<string,int>? Counts = null);

public sealed record InvoiceBalance(
    Guid Id, string InvoiceNumber, Guid ApartmentUnitId,
    DateOnly BillingPeriodStart, DateOnly BillingPeriodEnd,
    DateOnly? IssueDate, DateOnly? DueDate,
    decimal TotalAmount, decimal ConfirmedPaidAmount, decimal OutstandingAmount,
    string Status, bool IsOverdue, int DaysOverdue, string? InvoiceType = null, string? ApartmentNumber = null, string? ResidentName = null);

public sealed record PaymentRow(
    Guid Id, string PaymentNumber, Guid InvoiceId, decimal Amount,
    string PaymentMethodCode, DateTimeOffset PaymentDate,
    string? ReferenceNumber, string? Note, string Status,
    DateTimeOffset? ConfirmedAt, string? InvoiceNumber = null, string? ApartmentNumber = null, string? ResidentName = null);

public sealed record RejectPaymentRequest(string Reason);
