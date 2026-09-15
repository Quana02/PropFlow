using PropFlow.Modules.Billing.Domain.FeeRateRules;

namespace PropFlow.Modules.Billing.Domain.FeeTypes;

public class FeeType
{
    private readonly List<FeeRateRule> _rateRules = [];

    private FeeType()
    {
    }

    public FeeType(
        string code,
        string name,
        string defaultCalculationMethodCode,
        string defaultBillingFrequencyCode,
        Guid createdBy,
        DateTimeOffset now,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCalculationMethodCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBillingFrequencyCode);
        ThrowIfEmpty(createdBy, nameof(createdBy));

        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultCalculationMethodCode = defaultCalculationMethodCode.Trim().ToUpperInvariant();
        DefaultBillingFrequencyCode = defaultBillingFrequencyCode.Trim().ToUpperInvariant();
        Status = MasterDataStatus.ACTIVE;
        CreatedBy = createdBy;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string DefaultCalculationMethodCode { get; private set; } = null!;
    public string DefaultBillingFrequencyCode { get; private set; } = null!;
    public MasterDataStatus Status { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<FeeRateRule> RateRules => _rateRules.AsReadOnly();

    public void Update(
        string name,
        string defaultCalculationMethodCode,
        string defaultBillingFrequencyCode,
        Guid updatedBy,
        DateTimeOffset now,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultCalculationMethodCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultBillingFrequencyCode);
        ThrowIfEmpty(updatedBy, nameof(updatedBy));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefaultCalculationMethodCode = defaultCalculationMethodCode.Trim().ToUpperInvariant();
        DefaultBillingFrequencyCode = defaultBillingFrequencyCode.Trim().ToUpperInvariant();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Activate(Guid updatedBy, DateTimeOffset now)
    {
        ThrowIfEmpty(updatedBy, nameof(updatedBy));
        Status = MasterDataStatus.ACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void Deactivate(Guid updatedBy, DateTimeOffset now)
    {
        ThrowIfEmpty(updatedBy, nameof(updatedBy));
        Status = MasterDataStatus.INACTIVE;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
