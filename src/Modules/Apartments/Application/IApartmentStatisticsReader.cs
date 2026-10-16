namespace PropFlow.Modules.Apartments.Application;

public interface IApartmentStatisticsReader
{
    Task<int> GetTotalApartmentsAsync(CancellationToken cancellationToken = default);
    Task<int> GetHighestApartmentFloorAsync(CancellationToken cancellationToken = default);
}
