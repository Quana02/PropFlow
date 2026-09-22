namespace PropFlow.Modules.Residents.Contracts;

public sealed record EligibleResident(Guid Id, Guid? ApartmentUnitId);
public interface IResidentOnboarding
{
    Task<EligibleResident?> FindEligibleAsync(string normalizedEmail, DateOnly today, CancellationToken ct);
    Task<bool> LinkEligibleAsync(Guid residentId, Guid userId, string normalizedEmail, DateOnly today, DateTimeOffset now, CancellationToken ct);
}

public sealed record ApartmentOccupancyAtDate(DateOnly Date, IReadOnlyCollection<Guid> ActiveApartmentIds);
public sealed record ResidentOverviewCounts(int TotalActiveResidents, int OccupiedActiveApartments);

public interface IResidentOverviewSource
{
    Task<ResidentOverviewCounts> GetCountsAsync(IReadOnlyList<ApartmentOccupancyAtDate> apartments, CancellationToken ct);
}
