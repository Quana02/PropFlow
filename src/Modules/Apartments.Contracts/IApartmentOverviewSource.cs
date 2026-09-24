namespace PropFlow.Modules.Apartments.Contracts;



public interface IApartmentOverviewSource
{
    Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(CancellationToken ct);
}
