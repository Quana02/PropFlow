using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Application;

public sealed record ResidentManagementSummary(
    int TotalResidents,
    int ActiveResidents,
    int LinkedAccounts,
    int NewResidentsThisMonth);

public sealed class ResidentManagementSummaryQuery(
    ResidentsDbContext db, ICurrentBuildingTimeZone buildingTimeZone, TimeProvider clock)
{
    public async Task<ResidentManagementSummary> ExecuteAsync(CancellationToken ct)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await buildingTimeZone.GetAsync(ct));
        var localNow = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
        var month = new DateTime(localNow.Year, localNow.Month, 1);
        var start = ToUtc(month, zone);
        var end = ToUtc(month.AddMonths(1), zone);

        return await db.Residents
            .GroupBy(_ => 1)
            .Select(residents => new ResidentManagementSummary(
                residents.Count(),
                residents.Count(r => r.Status == ResidentStatus.ACTIVE),
                residents.Count(r => r.UserId != null),
                residents.Count(r => r.CreatedAt >= start && r.CreatedAt < end)))
            .SingleOrDefaultAsync(ct) ?? new ResidentManagementSummary(0, 0, 0, 0);
    }

    private static DateTimeOffset ToUtc(DateTime localMidnight, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(localMidnight))
            localMidnight = localMidnight.AddMinutes(1);

        return zone.IsAmbiguousTime(localMidnight)
            ? new DateTimeOffset(localMidnight, zone.GetAmbiguousTimeOffsets(localMidnight).Max()).ToUniversalTime()
            : new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localMidnight, zone));
    }
}
