using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

public sealed class FacilityWeekendNotices(AiRecommendationDbContext db, IFacilityGuideSource source,
    ICurrentBuildingTimeZone timezone, TimeProvider clock, FacilityWeekendNoticeOptions options,
    IFacilityWeekendNoticeDelivery delivery)
{
    public async Task DispatchAsync(CancellationToken ct)
    {
        if (!options.Enabled) return;
        var now = clock.GetUtcNow();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await timezone.GetAsync(ct));
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var slot = FacilityWeekendPlanner.Slot(local, options);
        if (slot is null) return;
        var date = DateOnly.FromDateTime(local);
        var weekend = FacilityWeekendPlanner.WeekendStart(date);
        if (await db.FacilityWeekendNoticeRuns.AsNoTracking().AnyAsync(x => x.LocalDate == date && x.Slot == slot && x.CompletedAt != null, ct)) return;
        var facts = await source.ReadAsync(ct);
        FacilityRecommendationView view;
        FacilityWeekendNoticeRun run;
        Guid lease;
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            if (db.Database.IsRelational())
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('facility-weekend-notice', 8))", ct);
            run = (await db.FacilityWeekendNoticeRuns.SingleOrDefaultAsync(x => x.LocalDate == date && x.Slot == slot, ct))!;
            if (run is null)
            {
                var used = await db.FacilityWeekendNoticeRuns.Where(x => x.WeekendStart == weekend).Select(x => x.FacilityId).ToArrayAsync(ct);
                view = FacilityWeekendPlanner.View(facts, local, now, used, 1);
                if (view.Suggestions.Length == 0) return;
                var expires = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(slot.Value).AddMinutes(15), zone), TimeSpan.Zero);
                run = new FacilityWeekendNoticeRun(date, slot.Value, weekend, view.Suggestions[0].Facility.Id, now, expires);
            }
            else view = FacilityWeekendPlanner.View(facts with { Guides = facts.Guides.Where(x => x.FacilityId == run.FacilityId).ToArray() }, local, now, limit: 1);
            if (!run.CanClaim(now)) return;
            if (db.Entry(run).State == EntityState.Detached) db.FacilityWeekendNoticeRuns.Add(run);
            if (view.Suggestions.Length == 0)
            {
                run.Complete(now, 0);
                await db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);
                return;
            }
            lease = run.Claim(now);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        // No source transaction is held while Communication delivers individual idempotent notices.
        try
        {
            var notice = FacilityWeekendPlanner.Notice(run.Id, view, date)!;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var remaining = run.ExpiresAt - clock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) deadline.Cancel(); else deadline.CancelAfter(remaining);
            var recipients = await delivery.DeliverAsync(notice, deadline.Token);
            await db.Entry(run).ReloadAsync(ct);
            if (run.LeaseToken != lease) return;
            run.Complete(clock.GetUtcNow(), recipients);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            await db.Entry(run).ReloadAsync(ct);
            if (run.LeaseToken == lease)
            { run.Retry(clock.GetUtcNow()); await db.SaveChangesAsync(ct); }
            throw;
        }
    }
}

public sealed class FacilityWeekendNoticeWorker(IServiceScopeFactory scopes, FacilityWeekendNoticeOptions options,
    ILogger<FacilityWeekendNoticeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.PollSeconds));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<FacilityWeekendNotices>().DispatchAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Weekend facility notice deferred ({ErrorType})", error.GetType().Name); }
            try { if (!await timer.WaitForNextTickAsync(ct)) break; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
