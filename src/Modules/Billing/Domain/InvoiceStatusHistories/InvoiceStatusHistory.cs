using PropFlow.Modules.Billing.Domain.Invoices;

namespace PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;

public class InvoiceStatusHistory
{
    private InvoiceStatusHistory()
    {
    }

    public InvoiceStatusHistory(
        Guid invoiceId,
        InvoiceStatus toStatus,
        Guid changedBy,
        DateTimeOffset now,
        InvoiceStatus? fromStatus = null,
        string? reason = null)
    {
        ThrowIfEmpty(invoiceId, nameof(invoiceId));
        ThrowIfEmpty(changedBy, nameof(changedBy));

        Id = Guid.NewGuid();
        InvoiceId = invoiceId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ChangedBy = changedBy;
        ChangedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public InvoiceStatus? FromStatus { get; private set; }
    public InvoiceStatus ToStatus { get; private set; }
    public string? Reason { get; private set; }
    public Guid ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    public Invoice? Invoice { get; private set; }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
