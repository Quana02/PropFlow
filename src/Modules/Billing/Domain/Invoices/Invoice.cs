using PropFlow.Modules.Billing.Domain.InvoiceItems;
using PropFlow.Modules.Billing.Domain.InvoiceStatusHistories;

namespace PropFlow.Modules.Billing.Domain.Invoices;

public class Invoice
{
    private readonly List<InvoiceItem> _items = [];
    private readonly List<InvoiceStatusHistory> _statusHistory = [];

    private Invoice()
    {
    }

    public Invoice(
        string invoiceNumber,
        Guid apartmentUnitId,
        DateOnly billingPeriodStart,
        DateOnly billingPeriodEnd,
        Guid createdBy,
        DateTimeOffset now,
        DateOnly? issueDate = null,
        DateOnly? dueDate = null,
        string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invoiceNumber);
        ThrowIfEmpty(apartmentUnitId, nameof(apartmentUnitId));
        ThrowIfEmpty(createdBy, nameof(createdBy));
        EnsureBillingPeriod(billingPeriodStart, billingPeriodEnd);
        EnsureDueDate(issueDate, dueDate);

        Id = Guid.NewGuid();
        InvoiceNumber = invoiceNumber.Trim();
        ApartmentUnitId = apartmentUnitId;
        BillingPeriodStart = billingPeriodStart;
        BillingPeriodEnd = billingPeriodEnd;
        IssueDate = issueDate;
        DueDate = dueDate;
        Subtotal = 0m;
        TotalAmount = 0m;
        Status = InvoiceStatus.DRAFT;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string InvoiceNumber { get; private set; } = null!;
    public Guid ApartmentUnitId { get; private set; }
    public DateOnly BillingPeriodStart { get; private set; }
    public DateOnly BillingPeriodEnd { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal TotalAmount { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset? IssuedAt { get; private set; }
    public Guid? IssuedBy { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public string? CancellationReason { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<InvoiceItem> Items => _items.AsReadOnly();
    public IReadOnlyCollection<InvoiceStatusHistory> StatusHistory => _statusHistory.AsReadOnly();

    public void UpdateDraft(
        DateOnly billingPeriodStart,
        DateOnly billingPeriodEnd,
        Guid updatedBy,
        DateTimeOffset now,
        DateOnly? issueDate = null,
        DateOnly? dueDate = null,
        string? note = null)
    {
        EnsureDraft();
        ThrowIfEmpty(updatedBy, nameof(updatedBy));
        EnsureBillingPeriod(billingPeriodStart, billingPeriodEnd);
        EnsureDueDate(issueDate, dueDate);

        BillingPeriodStart = billingPeriodStart;
        BillingPeriodEnd = billingPeriodEnd;
        IssueDate = issueDate;
        DueDate = dueDate;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public InvoiceItem AddItem(
        string feeCodeSnapshot,
        string feeNameSnapshot,
        string calculationMethodCodeSnapshot,
        decimal quantity,
        decimal unitRate,
        Guid updatedBy,
        DateTimeOffset now,
        Guid? feeTypeId = null,
        Guid? feeRateRuleId = null,
        string? description = null,
        string? unitName = null)
    {
        EnsureDraft();
        ThrowIfEmpty(updatedBy, nameof(updatedBy));

        var item = new InvoiceItem(
            Id,
            feeCodeSnapshot,
            feeNameSnapshot,
            calculationMethodCodeSnapshot,
            quantity,
            unitRate,
            now,
            feeTypeId,
            feeRateRuleId,
            description,
            unitName);

        _items.Add(item);
        RecalculateTotals();
        UpdatedBy = updatedBy;
        UpdatedAt = now;

        return item;
    }

    public void RemoveDraftItem(Guid invoiceItemId, Guid updatedBy, DateTimeOffset now)
    {
        EnsureDraft();
        ThrowIfEmpty(invoiceItemId, nameof(invoiceItemId));
        ThrowIfEmpty(updatedBy, nameof(updatedBy));

        var item = _items.SingleOrDefault(existing => existing.Id == invoiceItemId)
            ?? throw new InvalidOperationException("Invoice item does not belong to this invoice.");
        _items.Remove(item);
        RecalculateTotals();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Issue(Guid issuedBy, DateOnly issueDate, DateTimeOffset now, DateOnly? dueDate = null)
    {
        EnsureDraft();
        ThrowIfEmpty(issuedBy, nameof(issuedBy));
        if (_items.Count == 0)
            throw new InvalidOperationException("Invoice must contain at least one item before issuance.");
        EnsureDueDate(issueDate, dueDate);

        IssueDate = issueDate;
        DueDate = dueDate;
        Status = InvoiceStatus.ISSUED;
        IssuedBy = issuedBy;
        IssuedAt = now;
        UpdatedBy = issuedBy;
        UpdatedAt = now;
    }

    public void Cancel(Guid cancelledBy, string reason, DateTimeOffset now)
    {
        EnsureNotCancelled();
        ThrowIfEmpty(cancelledBy, nameof(cancelledBy));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Status = InvoiceStatus.CANCELLED;
        CancelledBy = cancelledBy;
        CancelledAt = now;
        CancellationReason = reason.Trim();
        UpdatedBy = cancelledBy;
        UpdatedAt = now;
    }

    private void RecalculateTotals()
    {
        Subtotal = _items.Sum(item => item.LineAmount);
        TotalAmount = Subtotal;
    }

    private void EnsureDraft()
    {
        if (Status != InvoiceStatus.DRAFT)
            throw new InvalidOperationException("Only draft invoices can be modified.");
    }

    private void EnsureNotCancelled()
    {
        if (Status == InvoiceStatus.CANCELLED)
            throw new InvalidOperationException("Cancelled invoices cannot be modified.");
    }

    private static void EnsureBillingPeriod(DateOnly start, DateOnly end)
    {
        if (end < start)
            throw new ArgumentException("Billing period end must be greater than or equal to start.", nameof(end));
    }

    private static void EnsureDueDate(DateOnly? issueDate, DateOnly? dueDate)
    {
        if (issueDate is not null && dueDate is not null && dueDate < issueDate)
            throw new ArgumentException("Due date cannot be before issue date.", nameof(dueDate));
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
