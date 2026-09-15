using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Domain.Invoices;

namespace PropFlow.Modules.Billing.Domain.InvoiceItems;

public class InvoiceItem
{
    private InvoiceItem()
    {
    }

    public InvoiceItem(
        Guid invoiceId,
        string feeCodeSnapshot,
        string feeNameSnapshot,
        string calculationMethodCodeSnapshot,
        decimal quantity,
        decimal unitRate,
        DateTimeOffset now,
        Guid? feeTypeId = null,
        Guid? feeRateRuleId = null,
        string? description = null,
        string? unitName = null)
    {
        ThrowIfEmpty(invoiceId, nameof(invoiceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(feeCodeSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(feeNameSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(calculationMethodCodeSnapshot);
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        if (unitRate < 0)
            throw new ArgumentOutOfRangeException(nameof(unitRate), "Unit rate cannot be negative.");

        Id = Guid.NewGuid();
        InvoiceId = invoiceId;
        FeeTypeId = feeTypeId;
        FeeRateRuleId = feeRateRuleId;
        FeeCodeSnapshot = feeCodeSnapshot.Trim().ToUpperInvariant();
        FeeNameSnapshot = feeNameSnapshot.Trim();
        CalculationMethodCodeSnapshot = calculationMethodCodeSnapshot.Trim().ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Quantity = quantity;
        UnitName = string.IsNullOrWhiteSpace(unitName) ? null : unitName.Trim();
        UnitRate = unitRate;
        LineAmount = decimal.Round(quantity * unitRate, 0, MidpointRounding.AwayFromZero);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid? FeeTypeId { get; private set; }
    public Guid? FeeRateRuleId { get; private set; }
    public string FeeCodeSnapshot { get; private set; } = null!;
    public string FeeNameSnapshot { get; private set; } = null!;
    public string CalculationMethodCodeSnapshot { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal Quantity { get; private set; }
    public string? UnitName { get; private set; }
    public decimal UnitRate { get; private set; }
    public decimal LineAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Invoice? Invoice { get; private set; }
    public FeeType? FeeType { get; private set; }
    public FeeRateRule? FeeRateRule { get; private set; }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
