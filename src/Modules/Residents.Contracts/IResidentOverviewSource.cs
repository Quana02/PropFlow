namespace PropFlow.Modules.Residents.Contracts;

public sealed record ResidentOverviewCounts(int TotalActiveResidents, int OccupiedActiveApartments);

public interface IResidentOverviewSource
{
    Task<ResidentOverviewCounts> GetCountsAsync(
        DateOnly date,
        IReadOnlyCollection<Guid> activeApartmentIds,
        CancellationToken ct);
}
