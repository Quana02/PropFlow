namespace PropFlow.Modules.ServiceRequests.Contracts;

public sealed record ServiceRequestStatusCount(string Status, int Count);
public sealed record ServiceRequestDailyCount(DateOnly Date, int Count);
public sealed record ServiceRequestBuildingTimeZone(Guid BuildingId, string TimeZoneId);
public sealed record ServiceRequestOverviewData(
    int OpenCount,
    IReadOnlyList<ServiceRequestStatusCount> StatusCounts,
    IReadOnlyList<ServiceRequestDailyCount> CreatedTrend);

public interface IServiceRequestOverviewSource
{
    Task<ServiceRequestOverviewData> GetOverviewAsync(
        DateTimeOffset instant,
        int trendDays,
        IReadOnlyList<ServiceRequestBuildingTimeZone> buildingTimeZones,
        CancellationToken ct);
}
