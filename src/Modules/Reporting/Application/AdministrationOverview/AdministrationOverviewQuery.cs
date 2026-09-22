using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Modules.Reporting.Application.AdministrationOverview;

public sealed record AdministrationOverviewDto(int TotalApartments, int VacantApartments, int TotalResidents, int OpenServiceRequests);

public sealed class AdministrationOverviewQuery(
    IApartmentOverviewSource apartments,
    IBuildingTimeZones buildings,
    IResidentOverviewSource residents,
    IServiceRequestOverviewSource requests,
    TimeProvider clock)
{
    public async Task<AdministrationOverviewDto> GetAsync(CancellationToken ct)
    {
        var activeApartments = await apartments.GetActiveApartmentsAsync(ct);
        var buildingIds = activeApartments.Select(group => group.BuildingId).ToArray();
        var zones = (await buildings.GetAsync(buildingIds, ct)).ToDictionary(building => building.BuildingId);
        var instant = clock.GetUtcNow();
        var occupancy = activeApartments.Select(group =>
        {
            if (!zones.TryGetValue(group.BuildingId, out var building))
                throw new InvalidOperationException("Active apartment references a building without a time zone.");
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(building.TimeZoneId)).DateTime);
            return new ApartmentOccupancyAtDate(localDate, group.ApartmentIds);
        }).ToArray();
        var residentCounts = await residents.GetCountsAsync(occupancy, ct);
        var totalApartments = activeApartments.Sum(group => group.ApartmentIds.Length);
        if (residentCounts.OccupiedActiveApartments > totalApartments)
            throw new InvalidOperationException("Occupancy count exceeds active apartment count.");
        var openRequests = await requests.CountOpenAsync(ct);
        return new AdministrationOverviewDto(totalApartments, totalApartments - residentCounts.OccupiedActiveApartments,
            residentCounts.TotalActiveResidents, openRequests);
    }
}
