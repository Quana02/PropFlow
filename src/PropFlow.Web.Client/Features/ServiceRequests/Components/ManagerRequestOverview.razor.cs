using Microsoft.AspNetCore.Components;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.ServiceRequests.Models;
using PropFlow.Web.Client.Features.ServiceRequests.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.ServiceRequests.Components;

public partial class ManagerRequestOverview : IDisposable
{
    [Inject] private IManagerServiceRequestApiClient Api { get; set; } = null!;
    [Parameter, EditorRequired] public ManagerServiceRequestDetailResponse Request { get; set; } = null!;
    [Parameter] public EventCallback RequestChanged { get; set; }

    private readonly CancellationTokenSource lifetime = new();
    private ManagerServiceRequestClassificationView? classification;
    private ManagerServiceRequestRecommendationView? recommendation;
    private DateTimeOffset? loadedVersion;
    private Guid? selectedCategory;
    private string priority = "";
    private string? action, resources, note, error;
    private bool maintenance, busy, loading, disposed, editingCategory, editingPlan;

    private ManagerClassificationSuggestion? PendingCategory => classification is { CanReview: true, Suggestion.Status: "SUCCESS" }
        ? classification.Suggestion : null;
    private ServiceRequestHandlingPlan? PendingPlan => recommendation?.CanReview == true ? recommendation.Suggestion?.Proposed : null;
    private ServiceRequestHandlingPlan? DisplayPlan => PendingPlan ??
        (recommendation is { CanAnalyze: false, Suggestion.Approved: { } approved } && approved.PriorityCode == Request.PriorityCode
            ? approved : null);
    private string DisplayCategory => PendingCategory is { } suggestion
        ? classification!.Categories.FirstOrDefault(x => x.Id == suggestion.SuggestedCategoryId)?.Name ?? "Chưa phân loại"
        : Request.CategoryName ?? "Chưa phân loại";
    private string? DisplayPriority => PendingPlan?.PriorityCode ?? Request.PriorityCode;
    private ServiceRequestHandlingPlan EditedPlan => new(priority, maintenance, Normalize(action), Normalize(resources));
    private bool PlanChanged => PendingPlan is { } proposed && EditedPlan != proposed;
    private bool CanSavePlan => ManagerRecommendationPriorities.Codes.Contains(priority) &&
        (!PlanChanged || !string.IsNullOrWhiteSpace(note));
    private string Location => Request.ApartmentUnitNumber is { } apartment
        ? $"{apartment}{(Request.FloorNumber is { } floor ? $" · Tầng {floor}" : "")}" : Request.Facility?.Name ?? "Theo thông tin yêu cầu";
    private string PreferredTime => Request.PreferredDate is { } date
        ? $"{date:dd/MM/yyyy} · {ServiceRequestAreaText.TimeSlot(Request.PreferredTimeCode)}"
        : Request.PreferredTimeCode is not null ? ServiceRequestAreaText.TimeSlot(Request.PreferredTimeCode) : "Không có yêu cầu riêng";

    protected override Task OnParametersSetAsync() =>
        !busy && !loading && loadedVersion != Request.UpdatedAt ? LoadAsync() : Task.CompletedTask;

    private async Task LoadAsync()
    {
        if (disposed || loading) return;
        loading = true;
        error = null;
        var version = Request.UpdatedAt;
        if (loadedVersion != version)
        {
            classification = null;
            recommendation = null;
            editingCategory = editingPlan = false;
        }
        try
        {
            var categoryTask = Api.ClassificationAsync(Request.Id, lifetime.Token);
            var planTask = Api.RecommendationAsync(Request.Id, lifetime.Token);
            await Task.WhenAll(categoryTask, planTask);
            if (disposed) return;
            ApplyClassification(await categoryTask);
            ApplyRecommendation(await planTask);
            loadedVersion = version;
        }
        catch (OperationCanceledException) when (disposed) { }
        finally { loading = false; }
        if (!disposed && loadedVersion != Request.UpdatedAt) await LoadAsync();
    }

    private Task AnalyzeAsync() => ExecuteAsync(async () =>
    {
        if (classification?.CanAnalyze == true)
            ApplyClassification(await Api.AnalyzeClassificationAsync(Request.Id, lifetime.Token));
        if (!disposed && recommendation?.CanAnalyze == true)
            ApplyRecommendation(await Api.AnalyzeRecommendationAsync(Request.Id, lifetime.Token));
        return false;
    });

    private Task SaveCategoryAsync() => ExecuteAsync(async () =>
    {
        if (PendingCategory is not { } suggestion || selectedCategory is not { } category) return false;
        var result = await Api.ReviewClassificationAsync(Request.Id, new(suggestion.Id, category), lifetime.Token);
        ApplyClassification(result);
        if (result.StatusCode == 409) await ReloadAfterConflictAsync();
        return result.IsSuccess;
    });

    private Task SavePlanAsync() => ExecuteAsync(async () =>
    {
        if (!CanSavePlan || recommendation is not { } current || !(current.CanReview || current.CanSetPriority)) return false;
        var input = current.CanReview
            ? new ReviewManagerServiceRequestRecommendation(current.Suggestion!.Id, priority, maintenance, action, resources, PlanChanged ? note : null)
            : new ReviewManagerServiceRequestRecommendation(null, priority);
        var result = await Api.ReviewRecommendationAsync(Request.Id, input, lifetime.Token);
        ApplyRecommendation(result);
        if (result.StatusCode == 409) await ReloadAfterConflictAsync();
        if (result.IsSuccess) editingPlan = false;
        return result.IsSuccess;
    });

    private async Task ExecuteAsync(Func<Task<bool>> operation)
    {
        if (disposed || busy || loading) return;
        busy = true;
        error = null;
        var changed = false;
        try { changed = await operation(); }
        catch (OperationCanceledException) when (disposed) { }
        finally { busy = false; }
        if (disposed) return;
        if (changed) await RequestChanged.InvokeAsync();
        if (loadedVersion != Request.UpdatedAt) await LoadAsync();
    }

    private async Task ReloadAfterConflictAsync()
    {
        var message = error;
        await LoadAsync();
        error = message;
    }

    private void ApplyClassification(ApiResult<ManagerServiceRequestClassificationView> result)
    {
        if (disposed) return;
        if (!result.IsSuccess || result.Data is null) { AddError(result.Message); return; }
        var next = result.Data;
        if (classification?.Suggestion?.Id != next.Suggestion?.Id || classification?.CanReview != next.CanReview)
        {
            selectedCategory = next.Suggestion?.SuggestedCategoryId;
            editingCategory = false;
        }
        classification = next;
    }

    private void ApplyRecommendation(ApiResult<ManagerServiceRequestRecommendationView> result)
    {
        if (disposed) return;
        if (!result.IsSuccess || result.Data is null) { AddError(result.Message); return; }
        var next = result.Data;
        if (recommendation is null || recommendation.Suggestion?.Id != next.Suggestion?.Id ||
            recommendation.CanReview != next.CanReview || recommendation.CurrentPriority != next.CurrentPriority)
        {
            recommendation = next;
            ResetPlanEditor();
            editingPlan = false;
        }
        recommendation = next;
    }

    private void TogglePlanEditing()
    {
        if (editingPlan) ResetPlanEditor();
        editingPlan = !editingPlan;
    }

    private void ResetPlanEditor()
    {
        priority = PendingPlan?.PriorityCode ?? recommendation?.CurrentPriority ?? "";
        maintenance = PendingPlan?.MaintenanceRequired == true;
        action = PendingPlan?.Action;
        resources = PendingPlan?.Resources;
        note = null;
    }

    private void AddError(string? message)
    {
        var text = message ?? "Không tải được đề xuất AI. Bạn vẫn có thể xử lý phiếu thủ công.";
        error = error is null ? text : $"{error} {text}";
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string PriorityLabel(string? code) => code switch
    {
        "URGENT" => "Khẩn cấp", "HIGH" => "Cao", "NORMAL" => "Tiêu chuẩn", "LOW" => "Thấp", _ => "Chưa xác định"
    };
    private static string PriorityClass(string? code) => code switch
    {
        "URGENT" => "priority-urgent", "HIGH" => "priority-high", "NORMAL" or "LOW" => "priority-normal", _ => "priority-unset"
    };
    public void Dispose() { disposed = true; lifetime.Cancel(); lifetime.Dispose(); }
}
