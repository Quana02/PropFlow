using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PropFlow.Modules.AiRecommendation.Application.ServiceRequestRecommendation;
using PropFlow.Modules.AiRecommendation.Domain.Recommendations;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class ManagerServiceRequestRecommendation(ServiceRequestsDbContext requests, AiRecommendationDbContext results,
    ResidentsDbContext residents, IMaintenanceAssetSource assets, IServiceRequestAdvisor advisor, IServiceRequestRealtimeNotifier realtime,
    ILogger<ManagerServiceRequestRecommendation> logger)
{
    private const string PromptVersion = "FE11-V1";
    private static readonly ConcurrentDictionary<Guid, byte> Running = new();

    public async Task<RecommendationResult> GetAsync(Guid id, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        return request is null ? Missing() : new(View(request, await LatestAsync(id, ct)));
    }

    public async Task<RecommendationResult> AnalyzeAsync(Guid id, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (request is null) return Missing();
        if (!ServiceRequestAiInput.CanReview(request.Status)) return Conflict();
        if (!Running.TryAdd(id, 0))
            return new(null, 409, "recommendation_in_progress", "Phiếu này đang được AI phân tích. Vui lòng thử lại sau.");

        try
        {
            var latest = await LatestAsync(id, ct);
            var current = View(request, latest);
            if (!current.CanAnalyze) return new(current);

            var residentName = await residents.Residents.AsNoTracking().Where(x => x.Id == request.ResidentId)
                .Select(x => x.FullName).SingleOrDefaultAsync(ct);
            var category = request.CategoryId is { } categoryId ? await requests.ServiceRequestCategories.AsNoTracking()
                .Where(x => x.Id == categoryId).Select(x => x.Name).SingleOrDefaultAsync(ct) : null;
            var facility = request.FacilityId is { } facilityId ? await assets.GetFacilityAsync(facilityId, ct) : null;
            var equipment = request.EquipmentId is { } equipmentId ? await assets.GetEquipmentAsync(equipmentId, ct) : null;
            var started = Stopwatch.GetTimestamp();
            var prediction = await advisor.RecommendAsync(new RecommendationInput(
                ServiceRequestAiInput.Clean(request.Title, residentName), ServiceRequestAiInput.Clean(request.Description, residentName),
                Clean(category, residentName, 150), Clean(facility?.Name, residentName, 150), Clean(equipment?.Name, residentName, 150)),
                ManagerRecommendationPriorities.Codes, ct);
            var latency = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var hash = InputHash(request);
            var fresh = await FindAsync(id, ct);
            if (fresh is null) return Missing();
            if (!ServiceRequestAiInput.CanReview(fresh.Status) || InputHash(fresh) != hash) return Conflict();

            var priority = prediction?.PriorityCode;
            var action = Clean(prediction?.Action, residentName, 800);
            var resources = Clean(prediction?.Resources, residentName, 500);
            var reason = Clean(prediction?.Reason, residentName, 600);
            var attempt = (latest?.AttemptNo ?? 0) + 1;
            var now = DateTimeOffset.UtcNow;
            var row = priority is not null && ManagerRecommendationPriorities.Codes.Contains(priority) &&
                prediction?.MaintenanceRecommended is { } maintenance && action is not null && resources is not null && reason is not null
                ? AiRequestRecommendation.CreateSuccess(id, attempt, "GEMINI", advisor.Model, priority, now, maintenance, action, resources,
                    reasoningSummary: reason, promptVersion: PromptVersion, inputHash: hash, inputTokens: prediction.InputTokens,
                    outputTokens: prediction.OutputTokens, totalTokens: prediction.TotalTokens, latencyMs: latency)
                : AiRequestRecommendation.CreateFailure(id, attempt, "GEMINI", advisor.Model,
                    prediction is null ? "AI hiện không khả dụng. Bạn vẫn có thể chọn mức ưu tiên thủ công."
                        : "AI chưa có đủ căn cứ để đề xuất. Hãy xem xét phiếu và chọn mức ưu tiên thủ công.", now,
                    promptVersion: PromptVersion, inputHash: hash, inputTokens: prediction?.InputTokens,
                    outputTokens: prediction?.OutputTokens, totalTokens: prediction?.TotalTokens, latencyMs: latency);

            results.AiRequestRecommendations.Add(row);
            try { await results.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { return Conflict(); }
            logger.LogInformation("FE-11 attempt {Status} used {Tokens} tokens in {LatencyMs} ms.", row.RunStatus, row.TotalTokens, latency);
            return new(View(fresh, row));
        }
        finally { Running.TryRemove(id, out _); }
    }

    public async Task<RecommendationResult> ReviewAsync(Guid id, Guid actor, ReviewManagerServiceRequestRecommendation input, CancellationToken ct)
    {
        var priority = input.PriorityCode?.Trim().ToUpperInvariant();
        if (priority is null || !ManagerRecommendationPriorities.Codes.Contains(priority))
            return Invalid("recommendation_priority_invalid", "Vui lòng chọn mức ưu tiên hợp lệ.");
        if (input.Action?.Length > 800 || input.Resources?.Length > 500 || input.ReviewNote?.Length > 500)
            return Invalid("recommendation_text_too_long", "Hướng xử lý tối đa 800 ký tự, nguồn lực và lý do tối đa 500 ký tự mỗi mục.");

        var plan = new ServiceRequestHandlingPlan(priority, input.MaintenanceRequired, Normalize(input.Action), Normalize(input.Resources));
        var note = Normalize(input.ReviewNote);
        results.Database.SetDbConnection(requests.Database.GetDbConnection());
        ManagerServiceRequestRecommendationView updated;
        await using (var transaction = await requests.Database.BeginTransactionAsync(ct))
        {
            await results.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
            try
            {
                var request = await requests.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
                if (request is null) return Missing();
                var latest = await LatestQuery(id).FirstOrDefaultAsync(ct);
                var state = View(request, latest);
                if (input.RecommendationId.HasValue)
                {
                    if (latest?.Id != input.RecommendationId.Value) return Conflict();
                    if (latest.Review is { } existing) return Approved(existing) == plan ? new(state) : Conflict();
                    if (!state.CanReview) return Conflict();
                    var same = Proposed(latest) == plan;
                    if (!same && note is null)
                        return Invalid("recommendation_note_required", "Nhập lý do khi chỉnh đề xuất của AI.");

                    var reviewedAt = DateTimeOffset.UtcNow;
                    var review = same ? latest.ConfirmSuggestion(actor, reviewedAt, note)
                        : latest.CorrectRecommendation(priority, actor, reviewedAt, note!, plan.MaintenanceRequired, plan.Action, plan.Resources);
                    results.AiRecommendationReviews.Add(review);
                }
                else if (!state.CanSetPriority) return Conflict();

                request.UpdateDetails(request.Title, request.Description, request.CategoryId, priority, DateTimeOffset.UtcNow);
                await requests.SaveChangesAsync(ct);
                await results.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                updated = View(request, latest);
            }
            catch (DbUpdateConcurrencyException) { return Conflict(); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { return Conflict(); }
            finally { await results.Database.UseTransactionAsync(null, CancellationToken.None); }
        }

        await realtime.NotifyChangedAsync(id, ct: ct);
        return new(updated);
    }

    private IQueryable<AiRequestRecommendation> LatestQuery(Guid id) => results.AiRequestRecommendations.Include(x => x.Review)
        .Where(x => x.ServiceRequestId == id).OrderByDescending(x => x.AttemptNo);

    private Task<AiRequestRecommendation?> LatestAsync(Guid id, CancellationToken ct) => LatestQuery(id).AsNoTracking().FirstOrDefaultAsync(ct);
    private Task<ServiceRequest?> FindAsync(Guid id, CancellationToken ct) => requests.ServiceRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    private ManagerServiceRequestRecommendationView View(ServiceRequest request, AiRequestRecommendation? latest)
    {
        var open = ServiceRequestAiInput.CanReview(request.Status);
        var reusable = latest is { RunStatus: AiRunStatus.SUCCESS } && latest.InputHash == InputHash(request);
        var reviewable = open && reusable && latest?.Review is null;
        var unavailable = !open ? "Chỉ đề xuất cho phiếu đang được xử lý."
            : !advisor.Ready ? "AI đề xuất chưa được cấu hình. Bạn vẫn có thể chọn mức ưu tiên thủ công." : null;
        return new(unavailable is null && !reusable, reviewable, open && !reusable, unavailable, request.FinalPriorityCode,
            latest is null ? null : new(latest.Id, latest.RunStatus.ToString(), Proposed(latest), latest.ReasoningSummary,
                latest.ErrorMessage, latest.Review?.Decision.ToString(), Approved(latest.Review), latest.Review?.ReviewNote));
    }

    private static ServiceRequestHandlingPlan? Proposed(AiRequestRecommendation row) => row.SuggestedPriorityCode is { } code
        ? new(code, row.MaintenanceRecommended, row.RecommendedAction, row.RecommendedResourceSummary) : null;

    private static ServiceRequestHandlingPlan? Approved(AiRecommendationReview? row) => row?.FinalPriorityCode is { } code
        ? new(code, row.FinalMaintenanceRequired, row.FinalAction, row.FinalResourcePlan) : null;

    internal static string InputHash(ServiceRequest request) => ServiceRequestAiInput.Hash(PromptVersion, request.Title, request.Description,
        request.CategoryId?.ToString(), request.FacilityId?.ToString(), request.EquipmentId?.ToString());

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static string? Clean(string? text, string? name, int max)
    {
        if (text is null) return null;
        var clean = ServiceRequestAiInput.Clean(text, name);
        return Normalize(clean[..Math.Min(clean.Length, max)]);
    }

    private static RecommendationResult Missing() => new(null, 404, "service_request_not_found", "Không tìm thấy yêu cầu dịch vụ.");
    private static RecommendationResult Invalid(string code, string message) => new(null, 400, code, message);
    private static RecommendationResult Conflict() => new(null, 409, "recommendation_changed", "Phiếu hoặc đề xuất đã thay đổi. Vui lòng tải lại và kiểm tra.");
}

public sealed record RecommendationResult(ManagerServiceRequestRecommendationView? View, int Status = 200,
    string? Code = null, string? Message = null);
