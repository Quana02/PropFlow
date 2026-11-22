using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

public sealed class FacilityLiveSessions(AiRecommendationDbContext db, IFacilityResidentAccess access,
    ICurrentBuildingTimeZone timezone, TimeProvider clock, IFacilityLiveProvider provider,
    FacilityLiveOptions options, FacilityRecommendationOptions quota, ILogger<FacilityLiveSessions> logger)
{
    public async Task<FacilityLiveSession> CreateAsync(Guid actor, CancellationToken ct)
    {
        if (!await access.ActiveAsync(actor, ct)) throw new FacilityRecommendationFailure(403, "facility_resident_inactive", "Cần có hồ sơ cư trú hiện hành để trò chuyện.");
        if (!provider.Ready) throw new FacilityRecommendationFailure(503, "facility_live_unavailable", "Hội thoại trực tiếp chưa sẵn sàng. Bạn vẫn có thể nhập nhu cầu để tìm tiện ích.");
        var now = clock.GetUtcNow();
        FacilityRecommendationRun run;
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            // Share the recommendation ledger and lock: issuing connections cannot bypass the existing AI budget.
            if (db.Database.IsRelational()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('facility-recommendation-quota',8))", ct);
            var since = now.AddDays(-1);
            if (await db.FacilityRecommendationRuns.CountAsync(x => x.Actor == actor && x.CreatedAt >= since, ct) >= quota.AiRequestsPerUserDay ||
                await db.FacilityRecommendationRuns.CountAsync(x => x.CreatedAt >= since, ct) >= quota.AiRequestsPerDay ||
                await db.FacilityRecommendationRuns.AnyAsync(x => x.Actor == actor && x.Model == options.Model && x.CreatedAt >= now.AddSeconds(-15), ct))
                throw new FacilityRecommendationFailure(429, "facility_live_quota", "Bạn đang kết nối quá nhanh hoặc đã đạt hạn mức AI. Vui lòng thử lại sau.");
            run = new(actor, "LIVE-" + Guid.NewGuid().ToString("N"), now);
            run.Complete(null, options.Model, null, null, now);
            db.FacilityRecommendationRuns.Add(run); await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(await timezone.GetAsync(ct))).DateTime;
        var session = await provider.CreateAsync(local, now.AddMinutes(options.SessionMinutes), ct);
        if (session is null) throw new FacilityRecommendationFailure(503, "facility_live_connection_failed", "Chưa kết nối được hội thoại Gemini Live. Bạn có thể thử lại hoặc nhập để tìm tiện ích.");
        if (!await access.ActiveAsync(actor, ct)) throw new FacilityRecommendationFailure(403, "facility_resident_inactive", "Hồ sơ cư trú không còn đủ điều kiện trò chuyện.");
        logger.LogInformation("Facility live connection {RunId}, model {Model}, maximum minutes {Minutes}, output limit {OutputLimit}", run.Id, options.Model, options.SessionMinutes, options.MaxOutputTokens);
        return session;
    }
}
