namespace PropFlow.Modules.Billing.Contracts;

public sealed record UtilityConsumptionPoint(DateOnly Month, decimal? Electricity, decimal? Water,
    bool ElectricityComparable, bool WaterComparable, Guid[] Recipients, bool IsFinal);
public sealed record UtilityConsumptionWindow(Guid ApartmentId, string UnitNumber, DateOnly Month,
    UtilityConsumptionPoint[] Points, string Fingerprint);
public sealed record UtilityConsumptionPeriod(Guid ApartmentId, DateOnly Month);
public interface IUtilityConsumptionSource
{
    Task<IReadOnlyList<BillingApartment>> ApartmentsAsync(CancellationToken ct);
    Task<UtilityConsumptionWindow?> ReadAsync(Guid apartmentId, DateOnly month, CancellationToken ct);
    Task<IReadOnlyList<UtilityConsumptionPeriod>> ResidentPeriodsAsync(Guid userId, CancellationToken ct);
}
