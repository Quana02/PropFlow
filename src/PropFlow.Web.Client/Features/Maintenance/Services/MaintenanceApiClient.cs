using PropFlow.Web.Client.Features.Maintenance.Models;
using PropFlow.Web.Client.Services.Api;
namespace PropFlow.Web.Client.Features.Maintenance.Services;
public sealed class MaintenanceApiClient(AuthenticatedApiClient api) : IMaintenanceApiClient
{
    public Task<ApiResult<Page<ScheduleModel>>> SchedulesAsync(ScheduleFilterModel filter, CancellationToken ct = default)
    {
        var query = new List<string> { $"pageIndex={filter.PageIndex}", $"pageSize={filter.PageSize}" };
        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.FacilityId.HasValue) query.Add($"facilityId={filter.FacilityId}");
        if (filter.EquipmentId.HasValue) query.Add($"equipmentId={filter.EquipmentId}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status.Value}");
        if (filter.From.HasValue) query.Add($"from={Uri.EscapeDataString(filter.From.Value.ToString("O"))}");
        if (filter.To.HasValue) query.Add($"to={Uri.EscapeDataString(filter.To.Value.ToString("O"))}");
        return api.SendAsync<Page<ScheduleModel>>(HttpMethod.Get, $"api/v1/maintenance/schedules?{string.Join("&", query)}", ct: ct);
    }
    public Task<ApiResult<IReadOnlyList<AssetModel>>> AssetsAsync(CancellationToken ct = default) => api.SendAsync<IReadOnlyList<AssetModel>>(HttpMethod.Get, "api/v1/maintenance/assets", ct: ct);
    public Task<ApiResult<ScheduleModel>> CreateScheduleAsync(CreateScheduleModel model, CancellationToken ct = default) => api.SendAsync<ScheduleModel>(HttpMethod.Post, "api/v1/maintenance/schedules", model, ct: ct);
    public Task<ApiResult<ScheduleModel>> UpdateScheduleAsync(Guid id, UpdateScheduleModel model, CancellationToken ct = default) => api.SendAsync<ScheduleModel>(HttpMethod.Put, $"api/v1/maintenance/schedules/{id}", model, ct: ct);
    public Task<ApiResult<ScheduleModel>> SetScheduleStatusAsync(Guid id, string action, CancellationToken ct = default) => api.SendAsync<ScheduleModel>(HttpMethod.Patch, $"api/v1/maintenance/schedules/{id}/status", new ScheduleStatusRequest(action), ct: ct);
    public Task<ApiResult<ScheduleModel>> ExtendScheduleAsync(Guid id, ExtendScheduleModel model, CancellationToken ct = default) => api.SendAsync<ScheduleModel>(HttpMethod.Post, $"api/v1/maintenance/schedules/{id}/extend", model, ct: ct);
    public Task<ApiResult<MaintenanceTaskModel>> CreateTaskAsync(CreateMaintenanceTaskModel model, CancellationToken ct = default) => api.SendAsync<MaintenanceTaskModel>(HttpMethod.Post, "api/v1/maintenance/tasks", model, ct: ct);
    public Task<ApiResult<Page<MaintenanceTaskModel>>> TasksAsync(TaskFilterModel filter, CancellationToken ct = default)
    {
        var query = new List<string> { $"pageIndex={filter.PageIndex}", $"pageSize={filter.PageSize}" };
        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status.Value}");
        if (filter.FacilityId.HasValue) query.Add($"facilityId={filter.FacilityId}");
        if (filter.EquipmentId.HasValue) query.Add($"equipmentId={filter.EquipmentId}");
        if (filter.AssignedStaffUserId.HasValue) query.Add($"assignedStaffUserId={filter.AssignedStaffUserId}");
        if (filter.From.HasValue) query.Add($"from={Uri.EscapeDataString(filter.From.Value.ToString("O"))}");
        if (filter.To.HasValue) query.Add($"to={Uri.EscapeDataString(filter.To.Value.ToString("O"))}");
        return api.SendAsync<Page<MaintenanceTaskModel>>(HttpMethod.Get, $"api/v1/maintenance/tasks?{string.Join("&", query)}", ct: ct);
    }
    public Task<ApiResult<MaintenanceHistoryListModel>> HistoryAsync(MaintenanceHistoryFilterModel filter, CancellationToken ct = default)
    {
        var query = new List<string> { $"pageIndex={filter.PageIndex}", $"pageSize={filter.PageSize}", $"sort={filter.Sort}" };
        if (!string.IsNullOrWhiteSpace(filter.SearchKeyword)) query.Add($"searchKeyword={Uri.EscapeDataString(filter.SearchKeyword)}");
        if (filter.Status.HasValue) query.Add($"status={filter.Status.Value}");
        if (filter.FacilityId.HasValue) query.Add($"facilityId={filter.FacilityId}");
        if (filter.EquipmentId.HasValue) query.Add($"equipmentId={filter.EquipmentId}");
        if (filter.StaffUserId.HasValue) query.Add($"staffUserId={filter.StaffUserId}");
        if (filter.From.HasValue) query.Add($"from={Uri.EscapeDataString(filter.From.Value.ToString("O"))}");
        if (filter.To.HasValue) query.Add($"to={Uri.EscapeDataString(filter.To.Value.ToString("O"))}");
        return api.SendAsync<MaintenanceHistoryListModel>(HttpMethod.Get, $"api/v1/maintenance/history?{string.Join("&", query)}", ct: ct);
    }
    public Task<ApiResult<MaintenanceHistoryDetailModel>> HistoryDetailAsync(Guid taskId, CancellationToken ct = default) => api.SendAsync<MaintenanceHistoryDetailModel>(HttpMethod.Get, $"api/v1/maintenance/history/{taskId}", ct: ct);
    public Task<ApiResult<IReadOnlyList<AssignableMaintenanceStaffModel>>> HistoryStaffAsync(CancellationToken ct = default) => api.SendAsync<IReadOnlyList<AssignableMaintenanceStaffModel>>(HttpMethod.Get, "api/v1/maintenance/history/staff", ct: ct);
    public Task<ApiResult<MaintenanceTaskModel>> TaskAsync(Guid taskId, CancellationToken ct = default) => api.SendAsync<MaintenanceTaskModel>(HttpMethod.Get, $"api/v1/maintenance/tasks/{taskId}", ct: ct);
    public Task<ApiResult<IReadOnlyList<AssignableMaintenanceStaffModel>>> AssignableStaffAsync(CancellationToken ct = default) => api.SendAsync<IReadOnlyList<AssignableMaintenanceStaffModel>>(HttpMethod.Get, "api/v1/maintenance/tasks/assignable-staff", ct: ct);
    public Task<ApiResult<MaintenanceTaskModel>> AssignTaskAsync(Guid taskId, AssignMaintenanceTaskModel model, CancellationToken ct = default) => api.SendAsync<MaintenanceTaskModel>(HttpMethod.Post, $"api/v1/maintenance/tasks/{taskId}/assignment", model, ct: ct);
    public Task<ApiResult<MaintenanceTaskModel>> ReviewResultAsync(Guid taskId, Guid resultId, ReviewMaintenanceResultModel model, CancellationToken ct = default) => api.SendAsync<MaintenanceTaskModel>(HttpMethod.Post, $"api/v1/maintenance/tasks/{taskId}/results/{resultId}/review", model, ct: ct);
    public Task<ApiResult<MaintenanceTaskModel>> AssociateTaskAssetAsync(Guid taskId, AssociateMaintenanceTaskAssetModel model, CancellationToken ct = default) => api.SendAsync<MaintenanceTaskModel>(HttpMethod.Patch, $"api/v1/maintenance/tasks/{taskId}/asset", model, ct: ct);
}
