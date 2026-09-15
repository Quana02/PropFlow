namespace PropFlow.Modules.Payments.Domain.Payments;

public class Payment
{
    private readonly List<PaymentStatusHistory> _statusHistory = new();

    private Payment()
    {
        PaymentNumber = string.Empty;
        PaymentMethodCode = string.Empty;
    }

    public Payment(
        string paymentNumber,
        Guid invoiceId,
        decimal amount,
        string paymentMethodCode,
        DateTimeOffset paymentDate,
        DateTimeOffset now,
        Guid? submittedBy = null,
        string? referenceNumber = null,
        string? note = null)
    {
        if (string.IsNullOrWhiteSpace(paymentNumber))
        {
            throw new ArgumentException("Payment number is required.", nameof(paymentNumber));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice id is required.", nameof(invoiceId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(paymentMethodCode))
        {
            throw new ArgumentException("Payment method code is required.", nameof(paymentMethodCode));
        }

        if (submittedBy == Guid.Empty)
        {
            throw new ArgumentException("Submitted by must not be empty when provided.", nameof(submittedBy));
        }

        var roundedAmount = decimal.Round(amount, 0, MidpointRounding.AwayFromZero);
        if (roundedAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Rounded payment amount must be greater than zero.");
        }

        Id = Guid.NewGuid();
        PaymentNumber = NormalizeCode(paymentNumber);
        InvoiceId = invoiceId;
        Amount = roundedAmount;
        PaymentMethodCode = NormalizeCode(paymentMethodCode);
        PaymentDate = paymentDate;
        ReferenceNumber = NormalizeOptionalCode(referenceNumber);
        Note = NormalizeOptionalText(note);
        Status = PaymentStatus.PENDING;
        SubmittedBy = submittedBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string PaymentNumber { get; private set; }
    public Guid InvoiceId { get; private set; }
    public decimal Amount { get; private set; }
    public string PaymentMethodCode { get; private set; }
    public DateTimeOffset PaymentDate { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public string? Note { get; private set; }
    public PaymentStatus Status { get; private set; }
    public Guid? SubmittedBy { get; private set; }
    public Guid? ConfirmedBy { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public Guid? RejectedBy { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<PaymentStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    public void Confirm(Guid confirmedBy, decimal remainingPayableAmount, DateTimeOffset now)
    {
        EnsurePending();

        if (confirmedBy == Guid.Empty)
        {
            throw new ArgumentException("Confirmed by is required.", nameof(confirmedBy));
        }

        if (remainingPayableAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remainingPayableAmount), "Remaining payable amount must not be negative.");
        }

        var roundedRemaining = decimal.Round(remainingPayableAmount, 0, MidpointRounding.AwayFromZero);
        if (Amount > roundedRemaining)
        {
            throw new InvalidOperationException("Confirmed payment amount must not exceed remaining payable balance.");
        }

        Status = PaymentStatus.CONFIRMED;
        ConfirmedBy = confirmedBy;
        ConfirmedAt = now;
        UpdatedAt = now;
    }

    public void Reject(Guid rejectedBy, string reason, DateTimeOffset now)
    {
        EnsurePending();

        if (rejectedBy == Guid.Empty)
        {
            throw new ArgumentException("Rejected by is required.", nameof(rejectedBy));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason is required.", nameof(reason));
        }

        Status = PaymentStatus.REJECTED;
        RejectedBy = rejectedBy;
        RejectedAt = now;
        RejectionReason = reason.Trim();
        UpdatedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != PaymentStatus.PENDING)
        {
            throw new InvalidOperationException("Only pending payments can be changed.");
        }
    }

    private static string NormalizeCode(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
