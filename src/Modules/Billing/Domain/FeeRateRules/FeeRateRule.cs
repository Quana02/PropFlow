using PropFlow.Modules.Billing.Domain.FeeTypes;

namespace PropFlow.Modules.Billing.Domain.FeeRateRules;

public class FeeRateRule
{
    private FeeRateRule()
    {
    }

    public FeeRateRule(
        Guid feeTypeId,
        string ruleName,
        string calculationMethodCode,
        string billingFrequencyCode,
        decimal unitRate,
        DateOnly effectiveFrom,
        Guid createdBy,
        DateTimeOffset now,
        
        string? unitName = null,
        decimal? minimumAmount = null,
        decimal? maximumAmount = null,
        string? ruleConfig = null,
        DateOnly? effectiveTo = null)
    {
        ThrowIfEmpty(feeTypeId, nameof(feeTypeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(calculationMethodCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(billingFrequencyCode);
        ThrowIfEmpty(createdBy, nameof(createdBy));
        EnsureNonNegative(unitRate, nameof(unitRate));
        EnsureNullableNonNegative(minimumAmount, nameof(minimumAmount));
        EnsureNullableNonNegative(maximumAmount, nameof(maximumAmount));
        EnsureDateRange(effectiveFrom, effectiveTo);
        EnsureAmountRange(minimumAmount, maximumAmount);

        Id = Guid.NewGuid();
        FeeTypeId = feeTypeId;
        
        RuleName = ruleName.Trim();
        CalculationMethodCode = calculationMethodCode.Trim().ToUpperInvariant();
        BillingFrequencyCode = billingFrequencyCode.Trim().ToUpperInvariant();
        UnitRate = unitRate;
        UnitName = string.IsNullOrWhiteSpace(unitName) ? null : unitName.Trim();
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
        RuleConfig = string.IsNullOrWhiteSpace(ruleConfig) ? null : ruleConfig.Trim();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        IsActive = true;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid FeeTypeId { get; private set; }
    
    public string RuleName { get; private set; } = null!;
    public string CalculationMethodCode { get; private set; } = null!;
    public string BillingFrequencyCode { get; private set; } = null!;
    public decimal UnitRate { get; private set; }
    public string? UnitName { get; private set; }
    public decimal? MinimumAmount { get; private set; }
    public decimal? MaximumAmount { get; private set; }
    public string? RuleConfig { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public FeeType? FeeType { get; private set; }

    public void UpdateRule(
        string ruleName,
        string calculationMethodCode,
        string billingFrequencyCode,
        decimal unitRate,
        DateOnly effectiveFrom,
        Guid updatedBy,
        DateTimeOffset now,
        
        string? unitName = null,
        decimal? minimumAmount = null,
        decimal? maximumAmount = null,
        string? ruleConfig = null,
        DateOnly? effectiveTo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(calculationMethodCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(billingFrequencyCode);
        ThrowIfEmpty(updatedBy, nameof(updatedBy));
        EnsureNonNegative(unitRate, nameof(unitRate));
        EnsureNullableNonNegative(minimumAmount, nameof(minimumAmount));
        EnsureNullableNonNegative(maximumAmount, nameof(maximumAmount));
        EnsureDateRange(effectiveFrom, effectiveTo);
        EnsureAmountRange(minimumAmount, maximumAmount);

        
        RuleName = ruleName.Trim();
        CalculationMethodCode = calculationMethodCode.Trim().ToUpperInvariant();
        BillingFrequencyCode = billingFrequencyCode.Trim().ToUpperInvariant();
        UnitRate = unitRate;
        UnitName = string.IsNullOrWhiteSpace(unitName) ? null : unitName.Trim();
        MinimumAmount = minimumAmount;
        MaximumAmount = maximumAmount;
        RuleConfig = string.IsNullOrWhiteSpace(ruleConfig) ? null : ruleConfig.Trim();
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Activate(Guid updatedBy, DateTimeOffset now)
    {
        ThrowIfEmpty(updatedBy, nameof(updatedBy));
        IsActive = true;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Deactivate(Guid updatedBy, DateTimeOffset now)
    {
        ThrowIfEmpty(updatedBy, nameof(updatedBy));
        IsActive = false;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    private static void EnsureDateRange(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveTo is not null && effectiveTo < effectiveFrom)
            throw new ArgumentException("Effective-to date must be greater than or equal to effective-from date.", nameof(effectiveTo));
    }

    private static void EnsureAmountRange(decimal? minimumAmount, decimal? maximumAmount)
    {
        if (minimumAmount is not null && maximumAmount is not null && minimumAmount > maximumAmount)
            throw new ArgumentException("Minimum amount cannot be greater than maximum amount.", nameof(minimumAmount));
    }

    private static void EnsureNonNegative(decimal value, string paramName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName, "Amount cannot be negative.");
    }

    private static void EnsureNullableNonNegative(decimal? value, string paramName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(paramName, "Amount cannot be negative.");
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
