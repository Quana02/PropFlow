using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class ServiceRequestOverviewSource(ServiceRequestsDbContext db) : IServiceRequestOverviewSource
{
    public async Task<ServiceRequestOverviewData> GetOverviewAsync(
        DateTimeOffset instant,
        int trendDays,
        IReadOnlyList<ServiceRequestBuildingTimeZone> buildingTimeZones,
        CancellationToken ct)
    {
        if (trendDays < 1)
            throw new ArgumentOutOfRangeException(nameof(trendDays));

        var zones = buildingTimeZones.ToDictionary(
            building => building.BuildingId,
            building => TimeZoneInfo.FindSystemTimeZoneById(building.TimeZoneId));

        var groupedStatuses = await db.ServiceRequests.AsNoTracking()
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, ct);

        var statusCounts = Enum.GetValues<ServiceRequestStatus>()
            .Select(status => new ServiceRequestStatusCount(status.ToString(), groupedStatuses.GetValueOrDefault(status)))
            .ToArray();

        ServiceRequestDailyCount[] trend = [];
        if (zones.Count > 0)
        {
            var throughDate = zones.Values
                .Select(zone => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime))
                .Max();
            var fromDate = throughDate.AddDays(-(trendDays - 1));
            var startUtc = zones.Values.Select(zone => ToUtc(fromDate, zone)).Min();
            var endExclusiveUtc = zones.Values.Select(zone => ToUtc(throughDate.AddDays(1), zone)).Max();
            var created = await db.ServiceRequests.AsNoTracking()
                .Where(request => request.CreatedAt >= startUtc && request.CreatedAt < endExclusiveUtc)
                .Select(request => new { request.BuildingId, request.CreatedAt })
                .ToArrayAsync(ct);
            var localDates = created.Select(request =>
            {
                if (!zones.TryGetValue(request.BuildingId, out var zone))
                    throw new InvalidOperationException("A service request references a building without a configured time zone.");
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.CreatedAt, zone).DateTime);
            }).Where(date => date >= fromDate && date <= throughDate);
            var groupedDates = localDates.GroupBy(date => date).ToDictionary(group => group.Key, group => group.Count());
            trend = Enumerable.Range(0, trendDays)
                .Select(offset => fromDate.AddDays(offset))
                .Select(date => new ServiceRequestDailyCount(date, groupedDates.GetValueOrDefault(date)))
                .ToArray();
        }

        var openCount = statusCounts
            .Where(item => item.Status is not (nameof(ServiceRequestStatus.CLOSED) or nameof(ServiceRequestStatus.CANCELLED)))
            .Sum(item => item.Count);
        return new ServiceRequestOverviewData(openCount, statusCounts, trend);
    }

    private static DateTimeOffset ToUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }
}
