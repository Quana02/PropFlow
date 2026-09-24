using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.Reporting.Application.AdministrationOverview;

public sealed record AdministrationOverviewStatusDto(string Status, int Count);
public sealed record AdministrationOverviewTrendDto(DateOnly Date, int Count);
public sealed record AdministrationOverviewDto(
    int TotalApartments,
    int VacantApartments,
    int TotalResidents,
    int OpenServiceRequests,
    int OccupiedApartments,
    IReadOnlyList<AdministrationOverviewStatusDto> ServiceRequestsByStatus,
    IReadOnlyList<AdministrationOverviewTrendDto> ServiceRequestTrend);

public sealed class AdministrationOverviewQuery(
    IApartmentOverviewSource apartments,
    ICurrentBuildingTimeZone buildingTimeZone,
    IResidentOverviewSource residents,
    IServiceRequestOverviewSource requests,
    TimeProvider clock)
{
    private const int TrendDays = 30;

    public async Task<AdministrationOverviewDto> GetAsync(CancellationToken ct)
    {
        var timeZoneId = await buildingTimeZone.GetAsync(ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        
        var instant = clock.GetUtcNow();
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

        var activeApartmentIds = await apartments.GetActiveApartmentIdsAsync(ct);
        var residentCounts = await residents.GetCountsAsync(localDate, activeApartmentIds, ct);
        var totalApartments = activeApartmentIds.Count;
        
        if (residentCounts.OccupiedActiveApartments > totalApartments)
            throw new InvalidOperationException("Occupancy count exceeds active apartment count.");
            
        var requestOverview = await requests.GetOverviewAsync(instant, TrendDays, timeZoneId, ct);
        
        return new AdministrationOverviewDto(
            totalApartments,
            totalApartments - residentCounts.OccupiedActiveApartments,
            residentCounts.TotalActiveResidents,
            requestOverview.OpenCount,
            residentCounts.OccupiedActiveApartments,
            requestOverview.StatusCounts.Select(item => new AdministrationOverviewStatusDto(item.Status, item.Count)).ToArray(),
            requestOverview.CreatedTrend.Select(item => new AdministrationOverviewTrendDto(item.Date, item.Count)).ToArray());
    }
}
