using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PropFlow.Modules.AiClassification.Application.ServiceRequestClassification;
using PropFlow.Modules.AiClassification.Domain.Classifications;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class ManagerServiceRequestClassification(ServiceRequestsDbContext requests, AiClassificationDbContext results,
    ResidentsDbContext residents, IServiceRequestClassifier classifier, IServiceRequestRealtimeNotifier realtime,
    ILogger<ManagerServiceRequestClassification> logger)
{
    private const string PromptVersion = "FE10-V1";
    private static readonly ConcurrentDictionary<Guid, byte> Running = new();

    public async Task<ClassificationResult> GetAsync(Guid id, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        return request is null ? Missing() : new(View(request, await LatestAsync(id, ct), await CategoriesAsync(ct)));
    }

    public async Task<ClassificationResult> AnalyzeAsync(Guid id, CancellationToken ct)
    {
        var request = await FindAsync(id, ct);
        if (request is null) return Missing();
        if (!ServiceRequestAiInput.CanReview(request.Status)) return Conflict();
        if (!Running.TryAdd(id, 0))
            return new(null, 409, "classification_in_progress", "Phiếu này đang được AI phân tích. Vui lòng thử lại sau.");

        try
        {
            var categories = await CategoriesAsync(ct);
            var latest = await LatestAsync(id, ct);
            var current = View(request, latest, categories);
            if (!current.CanAnalyze) return new(current);
            var residentName = await residents.Residents.AsNoTracking().Where(x => x.Id == request.ResidentId)
                .Select(x => x.FullName).SingleOrDefaultAsync(ct);
            var title = ServiceRequestAiInput.Clean(request.Title, residentName);
            var description = ServiceRequestAiInput.Clean(request.Description, residentName);
            var started = Stopwatch.GetTimestamp();
            var prediction = await classifier.ClassifyAsync(title, description, categories.Select(category =>
                new ClassificationCategory(category.Code, category.Name, category.Description)).ToArray(), ct);
            var predicted = categories.FirstOrDefault(category => category.Code == prediction?.CategoryCode);
            var hash = InputHash(request);
            var fresh = await FindAsync(id, ct);
            if (fresh is null) return Missing();
            if (!ServiceRequestAiInput.CanReview(fresh.Status) || InputHash(fresh) != hash) return Conflict();

            var now = DateTimeOffset.UtcNow;
            var attempt = (latest?.AttemptNo ?? 0) + 1;
            var latency = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var reason = prediction?.Reason is { } value ? ServiceRequestAiInput.Clean(value, residentName) : string.Empty;
            var row = predicted is not null && !string.IsNullOrWhiteSpace(reason)
                ? AiRequestClassification.CreateSuccess(id, attempt, "GEMINI", classifier.Model, predicted.Id, now,
                    reasoningSummary: reason[..Math.Min(reason.Length, 600)], promptVersion: PromptVersion, inputHash: hash,
                    inputTokens: prediction?.InputTokens, outputTokens: prediction?.OutputTokens, totalTokens: prediction?.TotalTokens, latencyMs: latency)
                : AiRequestClassification.CreateFailure(id, attempt, "GEMINI", classifier.Model,
                    prediction is null ? "AI hiện không khả dụng. Bạn vẫn có thể xử lý phiếu thủ công."
                        : "AI chưa có đủ căn cứ để đề xuất danh mục. Hãy tiếp tục xem xét phiếu thủ công.", now,
                    promptVersion: PromptVersion, inputHash: hash, inputTokens: prediction?.InputTokens,
                    outputTokens: prediction?.OutputTokens, totalTokens: prediction?.TotalTokens, latencyMs: latency);

            results.AiRequestClassifications.Add(row);
            try { await results.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            { return Conflict(); }
            logger.LogInformation("FE-10 attempt {Status} used {Tokens} tokens in {LatencyMs} ms.", row.RunStatus, row.TotalTokens, latency);
            return new(View(fresh, row, await CategoriesAsync(ct)));
        }
        finally { Running.TryRemove(id, out _); }
    }

    public async Task<ClassificationResult> ReviewAsync(Guid id, Guid actor, ReviewManagerServiceRequestClassification input, CancellationToken ct)
    {
        if (input.CategoryId == Guid.Empty || input.SuggestionId == Guid.Empty)
            return new(null, 400, "classification_category_required", "Vui lòng chọn một danh mục dịch vụ.");

        // Use one local PostgreSQL transaction for the audit decision and FE-05's final category.
        results.Database.SetDbConnection(requests.Database.GetDbConnection());
        await using var transaction = await requests.Database.BeginTransactionAsync(ct);
        await results.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);
        ManagerServiceRequestClassificationView updated;
        try
        {
            var request = await requests.ServiceRequests.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (request is null) return Missing();
            var latest = await results.AiRequestClassifications.Include(x => x.Review).Where(x => x.ServiceRequestId == id)
                .OrderByDescending(x => x.AttemptNo).FirstOrDefaultAsync(ct);
            var categories = await CategoriesAsync(ct);
            if (latest?.Id != input.SuggestionId) return Conflict();
            if (latest.Review is { } review)
                return review.FinalCategoryId == input.CategoryId ? new(View(request, latest, categories)) : Conflict();
            if (!View(request, latest, categories).CanReview) return Conflict();
            if (!categories.Any(category => category.Id == input.CategoryId))
                return new(null, 400, "classification_category_inactive", "Danh mục đã ngừng sử dụng. Vui lòng chọn danh mục khác.");

            var now = DateTimeOffset.UtcNow;
            var decision = latest.PredictedCategoryId == input.CategoryId
                ? latest.ConfirmSuggestion(actor, now) : latest.CorrectClassification(input.CategoryId, actor, now);
            results.AiClassificationReviews.Add(decision);
            request.UpdateDetails(request.Title, request.Description, input.CategoryId, request.FinalPriorityCode, now);
            await requests.SaveChangesAsync(ct);
            await results.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            updated = View(request, latest, categories);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(); }
        finally { await results.Database.UseTransactionAsync(null, CancellationToken.None); }

        await realtime.NotifyChangedAsync(id, ct: ct);
        return new(updated);
    }

    private Task<ServiceRequest?> FindAsync(Guid id, CancellationToken ct) =>
        requests.ServiceRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    private Task<AiRequestClassification?> LatestAsync(Guid id, CancellationToken ct) => results.AiRequestClassifications.AsNoTracking()
        .Include(x => x.Review).Where(x => x.ServiceRequestId == id).OrderByDescending(x => x.AttemptNo).FirstOrDefaultAsync(ct);

    private Task<Category[]> CategoriesAsync(CancellationToken ct) => requests.ServiceRequestCategories.AsNoTracking()
        .Where(x => x.IsActive).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code)
        .Select(x => new Category(x.Id, x.Code, x.Name, x.Description)).ToArrayAsync(ct);

    private ManagerServiceRequestClassificationView View(ServiceRequest request, AiRequestClassification? latest, Category[] categories)
    {
        var open = ServiceRequestAiInput.CanReview(request.Status);
        var eligible = latest is { RunStatus: AiRunStatus.SUCCESS } && latest.InputHash == InputHash(request);
        var validSuggestion = eligible && categories.Any(x => x.Id == latest?.PredictedCategoryId);
        var reviewable = open && validSuggestion && latest?.Review is null;
        var reusable = validSuggestion || eligible && latest?.Review is not null;
        var unavailable = !open ? "Chỉ phân loại các phiếu đang được xử lý."
            : categories.Length == 0 ? "Chưa có danh mục dịch vụ đang hoạt động."
            : !classifier.Ready ? "AI phân loại chưa được cấu hình." : null;
        return new(unavailable is null && !reusable, reviewable, unavailable,
            latest is null ? null : new(latest.Id, latest.RunStatus.ToString(), latest.PredictedCategoryId, latest.ReasoningSummary,
                latest.ErrorMessage, latest.Review?.Decision.ToString(), latest.Review?.FinalCategoryId),
            categories.Select(x => new ManagerClassificationCategory(x.Id, x.Name)).ToArray());
    }

    private static string InputHash(ServiceRequest request) => ServiceRequestAiInput.Hash(PromptVersion, request.Title, request.Description);

    private static ClassificationResult Missing() => new(null, 404, "service_request_not_found", "Không tìm thấy yêu cầu dịch vụ.");
    private static ClassificationResult Conflict() => new(null, 409, "classification_changed", "Phiếu hoặc gợi ý đã thay đổi. Vui lòng tải lại và kiểm tra.");
    private sealed record Category(Guid Id, string Code, string Name, string? Description);
}

public sealed record ClassificationResult(ManagerServiceRequestClassificationView? View, int Status = 200,
    string? Code = null, string? Message = null);
