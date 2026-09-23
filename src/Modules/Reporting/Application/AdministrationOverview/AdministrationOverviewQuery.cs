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
    IBuildingTimeZones buildings,
    IResidentOverviewSource residents,
    IServiceRequestOverviewSource requests,
    TimeProvider clock)
{
    private const int TrendDays = 30;

    public async Task<AdministrationOverviewDto> GetAsync(CancellationToken ct)
    {
        var buildingTimeZones = await buildings.GetAllAsync(ct);
        var zones = buildingTimeZones.ToDictionary(building => building.BuildingId);
        var instant = clock.GetUtcNow();
        var activeApartments = await apartments.GetActiveApartmentsAsync(ct);
        var occupancy = activeApartments.Select(group =>
        {
            if (!zones.TryGetValue(group.BuildingId, out var building))
                throw new InvalidOperationException("Active apartment references a building without a time zone.");
            var zone = TimeZoneInfo.FindSystemTimeZoneById(building.TimeZoneId);
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
            return new ApartmentOccupancyAtDate(localDate, group.ApartmentIds);
        }).ToArray();
        var residentCounts = await residents.GetCountsAsync(occupancy, ct);
        var totalApartments = activeApartments.Sum(group => group.ApartmentIds.Length);
        if (residentCounts.OccupiedActiveApartments > totalApartments)
            throw new InvalidOperationException("Occupancy count exceeds active apartment count.");
        var requestOverview = await requests.GetOverviewAsync(instant, TrendDays,
            buildingTimeZones.Select(building => new ServiceRequestBuildingTimeZone(building.BuildingId, building.TimeZoneId)).ToArray(), ct);
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
