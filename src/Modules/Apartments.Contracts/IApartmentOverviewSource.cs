namespace PropFlow.Modules.Apartments.Contracts;

public sealed record ActiveApartmentIds(Guid BuildingId, Guid[] ApartmentIds);

public interface IApartmentOverviewSource
{
    Task<IReadOnlyList<ActiveApartmentIds>> GetActiveApartmentsAsync(CancellationToken ct);
}
