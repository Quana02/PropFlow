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
        string timeZoneId,
        CancellationToken ct)
    {
        if (trendDays < 1)
            throw new ArgumentOutOfRangeException(nameof(trendDays));

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        var groupedStatuses = await db.ServiceRequests.AsNoTracking()
            .GroupBy(request => request.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Status, item => item.Count, ct);

        var statusCounts = Enum.GetValues<ServiceRequestStatus>()
            .Select(status => new ServiceRequestStatusCount(status.ToString(), groupedStatuses.GetValueOrDefault(status)))
            .ToArray();

        ServiceRequestDailyCount[] trend = [];
        
        var throughDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
        var fromDate = throughDate.AddDays(-(trendDays - 1));
        var startUtc = ToUtc(fromDate, zone);
        var endExclusiveUtc = ToUtc(throughDate.AddDays(1), zone);
        
        var created = await db.ServiceRequests.AsNoTracking()
            .Where(request => request.CreatedAt >= startUtc && request.CreatedAt < endExclusiveUtc)
            .Select(request => request.CreatedAt)
            .ToArrayAsync(ct);
            
        var localDates = created.Select(createdAt => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(createdAt, zone).DateTime))
            .Where(date => date >= fromDate && date <= throughDate);
            
        var groupedDates = localDates.GroupBy(date => date).ToDictionary(group => group.Key, group => group.Count());
        trend = Enumerable.Range(0, trendDays)
            .Select(offset => fromDate.AddDays(offset))
            .Select(date => new ServiceRequestDailyCount(date, groupedDates.GetValueOrDefault(date)))
            .ToArray();

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
