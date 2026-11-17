using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;
using PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Shared.Messaging;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.UtilityConsumption;

public sealed class UtilityInsights(AiRecommendationDbContext db, IUtilityConsumptionSource source,
    UtilityConsumptionAnalyzer analyzer, IUtilityExplanation explanation, UtilityInsightOptions options,
    TimeProvider clock, ILogger<UtilityInsights> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Fingerprint(UtilityConsumptionWindow window) => window.Fingerprint + ":" + analyzer.Signature;
    private static UtilityInsightView View(UtilityInsight x) => new(x.Id, x.ApartmentId, x.UnitNumber, x.Month,
        x.Fingerprint, x.Revision, x.IsFinal, x.Review, x.ReviewedAt, x.ReviewNote,
        JsonSerializer.Deserialize<UtilityAnalysis>(x.AnalysisJson, Json)!, x.AiExplanation, x.AiMode, x.CalculatedAt);
    public async Task<UtilityInsightPage> ListAsync(DateOnly month, int page, int size, string? search, CancellationToken ct)
    {
        if (page < 1 || size is < 1 or > 50 || (long)(page - 1) * size > int.MaxValue) throw Failure(400, "invalid_page", "Trang phải từ một, tối đa năm mươi dòng.");
        var apartments = (await source.ApartmentsAsync(ct)).Where(x => string.IsNullOrWhiteSpace(search) ||
            x.UnitNumber.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.UnitNumber).ToArray();
        var result = new List<UtilityInsightView>();
        foreach (var apartment in apartments.Skip((page - 1) * size).Take(size))
            result.Add(await GetAsync(apartment.Id, month, ct));
        return new(result.ToArray(), page, size, apartments.Length);
    }
    public async Task<UtilityInsightView> GetAsync(Guid apartment, DateOnly month, CancellationToken ct) => View(await GetEntityAsync(apartment, month, ct));
    private async Task<UtilityInsight> GetEntityAsync(Guid apartment, DateOnly month, CancellationToken ct)
    {
        var window = await source.ReadAsync(apartment, month, ct) ?? throw Failure(404, "utility_not_found", "Không tìm thấy kỳ điện nước được phép xem.");
        var existing = await db.UtilityInsights.SingleOrDefaultAsync(x => x.ApartmentId == apartment && x.Month == month, ct);
        if (existing is not null && existing.Fingerprint == Fingerprint(window) && existing.MethodVersion == UtilityConsumptionAnalyzer.Version) return existing;
        var analysis = analyzer.Analyze(window);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockAsync($"utility:{apartment}:{month:yyyy-MM-dd}", ct);
        if (existing is not null) await db.Entry(existing).ReloadAsync(ct);
        else existing = await db.UtilityInsights.SingleOrDefaultAsync(x => x.ApartmentId == apartment && x.Month == month, ct);
        if (existing is null) { existing = new(apartment, month); db.UtilityInsights.Add(existing); }
        if (existing.Fingerprint != Fingerprint(window) || existing.MethodVersion != UtilityConsumptionAnalyzer.Version)
        {
            var fresh = await source.ReadAsync(apartment, month, ct);
            if (fresh?.Fingerprint != window.Fingerprint) throw Failure(409, "utility_changed", "Số liệu vừa thay đổi. Vui lòng tải lại phân tích.");
            var current = window.Points.SingleOrDefault(x => x.Month == month);
            existing.Recalculate(window.UnitNumber, Fingerprint(window), UtilityConsumptionAnalyzer.Version,
                current?.IsFinal == true, current?.Recipients ?? [], JsonSerializer.Serialize(analysis, Json), clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return existing;
    }
    public async Task<UtilityInsightView[]> ResidentAsync(Guid userId, CancellationToken ct)
    {
        var periods = (await source.ResidentPeriodsAsync(userId, ct)).OrderByDescending(x => x.Month).Take(24).ToArray();
        var views = new List<UtilityInsightView>();
        foreach (var period in periods)
        {
            var window = await source.ReadAsync(period.ApartmentId, period.Month, ct);
            if (window?.Points.SingleOrDefault(x => x.Month == period.Month) is not { IsFinal: true } current || !current.Recipients.Contains(userId)) continue;
            views.Add(await GetAsync(period.ApartmentId, period.Month, ct));
        }
        return views.ToArray();
    }
    public async Task<UtilityInsightView> ReviewAsync(Guid apartment, DateOnly month, UtilityReviewInput input, Guid actor, CancellationToken ct)
    {
        if (input.Decision is not ("CONFIRMED" or "DISMISSED") || string.IsNullOrWhiteSpace(input.Note) || input.Note.Trim().Length > 500)
            throw Failure(400, "invalid_review", "Chọn xác nhận hoặc bỏ qua, kèm ghi chú tối đa năm trăm ký tự.");
        var entity = await GetEntityAsync(apartment, month, ct);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockAsync($"utility:{apartment}:{month:yyyy-MM-dd}", ct);
        await db.Entry(entity).ReloadAsync(ct);
        if (entity.Fingerprint != input.Fingerprint) throw Failure(409, "utility_changed", "Kết quả đã thay đổi. Tải lại trước khi xét duyệt.");
        var fresh = await source.ReadAsync(apartment, month, ct);
        if (fresh is null || Fingerprint(fresh) != entity.Fingerprint) throw Failure(409, "utility_changed", "Chỉ số hoặc người nhận vừa thay đổi. Vui lòng tải lại.");
        if (!entity.IsFinal) throw Failure(409, "utility_not_final", "Chỉ xét duyệt cảnh báo sau khi kỳ chỉ số đã có hóa đơn chốt.");
        if (entity.Review != "PENDING")
        {
            if (entity.Review != input.Decision) throw Failure(409, "utility_reviewed", "Kết quả này đã được xét duyệt.");
        }
        else
        {
            var analysis = View(entity).Analysis;
            if (!Abnormal(analysis)) throw Failure(409, "utility_no_alert", "Kỳ này không có cảnh báo đủ căn cứ để xét duyệt.");
            entity.RecordReview(input.Decision, input.Note.Trim(), actor, clock.GetUtcNow());
            db.UtilityInsightReviews.Add(new(entity));
            if (input.Decision == "CONFIRMED") db.UtilityNotices.Add(new OutboxEvent("UtilityConsumptionAlertConfirmed", entity.Id,
                JsonSerializer.Serialize(new NoticePayload(entity.Id, entity.Fingerprint, entity.Revision), Json), clock.GetUtcNow()));
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
        return View(entity);
    }
    public async Task<UtilityInsightView> ExplainAsync(Guid apartment, DateOnly month, UtilityExplainInput input, Guid actor, CancellationToken ct)
    {
        var entity = await GetEntityAsync(apartment, month, ct);
        if (input.Fingerprint != entity.Fingerprint) throw Failure(409, "utility_changed", "Số liệu đã thay đổi. Vui lòng tải lại.");
        if (!explanation.Ready) return View(entity) with { AiMode = "NOT_CONFIGURED" };
        if (entity.AiExplanation is not null) return View(entity);
        UtilityAiAttempt attempt;
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await LockAsync("utility-ai-quota", ct);
            var now = clock.GetUtcNow(); var since = now.AddDays(-1);
            if (await db.UtilityAiAttempts.CountAsync(x => x.CreatedAt >= since && x.Actor == actor, ct) >= options.AiRequestsPerUserDay ||
                await db.UtilityAiAttempts.CountAsync(x => x.CreatedAt >= since, ct) >= options.AiRequestsPerDay ||
                await db.UtilityAiAttempts.AnyAsync(x => x.InsightId == entity.Id && x.CreatedAt >= now.AddMinutes(-1), ct))
                throw Failure(429, "utility_ai_quota", "Đã đạt giới hạn giải thích AI hoặc vừa gửi yêu cầu. Vui lòng thử lại sau.");
            attempt = new(actor, entity.Id, now); db.UtilityAiAttempts.Add(attempt); await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        // No transaction or lock is held during the external AI call.
        var result = await explanation.ExplainAsync(View(entity).Analysis, ct);
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            await LockAsync($"utility:{apartment}:{month:yyyy-MM-dd}", ct); await db.Entry(entity).ReloadAsync(ct);
            var fresh = await source.ReadAsync(apartment, month, ct);
            attempt.Complete(result?.Model, result?.InputTokens, result?.OutputTokens);
            if (entity.Fingerprint != input.Fingerprint || fresh is null || Fingerprint(fresh) != input.Fingerprint)
            {
                await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
                throw Failure(409, "utility_changed", "Số liệu đã đổi trong khi AI trả lời. Kết quả cũ đã được bỏ qua.");
            }
            entity.Explain(result?.Text);
            await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        }
        logger.LogInformation("Utility explanation {InsightId}: {Mode}, input tokens {Input}, output tokens {Output}", entity.Id, entity.AiMode, attempt.InputTokens, attempt.OutputTokens);
        return View(entity);
    }
    public async Task DispatchAsync(IFinanceNoticePublisher publisher, CancellationToken ct)
    {
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await LockAsync("utility-notice-delivery", ct);
        var now = clock.GetUtcNow();
        var pending = await db.UtilityNotices.Where(x => x.Status == "Pending" && x.NextAttemptAt <= now).OrderBy(x => x.CreatedAt).Take(10).ToArrayAsync(ct);
        foreach (var item in pending)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<NoticePayload>(item.Payload, Json)!;
                var insight = await db.UtilityInsights.SingleOrDefaultAsync(x => x.Id == payload.InsightId, ct);
                var window = insight is null ? null : await source.ReadAsync(insight.ApartmentId, insight.Month, ct);
                if (insight is { Review: "CONFIRMED", IsFinal: true } && insight.Fingerprint == payload.Fingerprint &&
                    insight.Revision == payload.Revision && window is not null && Fingerprint(window) == payload.Fingerprint)
                {
                    foreach (var recipient in insight.Recipients)
                    {
                        var message = $"Mức tiêu thụ điện/nước căn hộ {insight.UnitNumber}, kỳ {insight.Month:MM/yyyy} có thay đổi đáng chú ý và đã được BQL kiểm tra. Xem số liệu và liên hệ BQL nếu cần hỗ trợ.";
                        await publisher.PublishAsync(new(item.Id, recipient, null, "Điện nước cần kiểm tra", message,
                            System.Net.WebUtility.HtmlEncode(message), "UTILITY_CONSUMPTION", insight.Id, "/resident/utilities"), ct);
                    }
                }
                item.Delivered(clock.GetUtcNow());
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { item.Failed(ex.GetType().Name, clock.GetUtcNow()); logger.LogWarning("Utility notice {EventId} deferred ({ErrorType})", item.Id, ex.GetType().Name); }
            await db.SaveChangesAsync(ct);
        }
        if (tx is not null) await tx.CommitAsync(ct);
    }
    public static bool Abnormal(UtilityAnalysis analysis) => analysis.Electricity.Severity is "WATCH" or "HIGH" || analysis.Water.Severity is "WATCH" or "HIGH";
    private Task LockAsync(string key, CancellationToken ct) => db.Database.IsRelational()
        ? db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 8))", ct) : Task.CompletedTask;
    private static UtilityInsightFailure Failure(int status, string code, string message) => new(status, code, message);
    private sealed record NoticePayload(Guid InsightId, string Fingerprint, int Revision);
}
