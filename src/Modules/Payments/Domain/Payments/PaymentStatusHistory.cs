namespace PropFlow.Modules.Payments.Domain.Payments;

public class PaymentStatusHistory
{
    private PaymentStatusHistory()
    {
    }

    public PaymentStatusHistory(
        Guid paymentId,
        PaymentStatus toStatus,
        Guid changedBy,
        DateTimeOffset now,
        PaymentStatus? fromStatus = null,
        string? reason = null)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("Payment id is required.", nameof(paymentId));
        }

        if (changedBy == Guid.Empty)
        {
            throw new ArgumentException("Changed by is required.", nameof(changedBy));
        }

        Id = Guid.NewGuid();
        PaymentId = paymentId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ChangedBy = changedBy;
        ChangedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public PaymentStatus? FromStatus { get; private set; }
    public PaymentStatus ToStatus { get; private set; }
    public string? Reason { get; private set; }
    public Guid ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    public Payment? Payment { get; private set; }
}
