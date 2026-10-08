namespace PropFlow.Modules.Apartments.Contracts;



public interface IApartmentOverviewSource
{
    Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(CancellationToken ct);
    Task<IReadOnlyList<ActiveApartmentOption>> GetActiveApartmentsAsync(CancellationToken ct);
}

public sealed record ActiveApartmentOption(Guid Id, string UnitNumber, int FloorNumber);
