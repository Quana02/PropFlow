using PropFlow.Web.Client.Features.Maintenance.Models;
using PropFlow.Web.Client.Services.Api;
namespace PropFlow.Web.Client.Features.Maintenance.Services;
public interface IMaintenanceApiClient
{
    Task<ApiResult<Page<ScheduleModel>>> SchedulesAsync(ScheduleFilterModel filter, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<AssetModel>>> AssetsAsync(CancellationToken ct = default);
    Task<ApiResult<ScheduleModel>> CreateScheduleAsync(CreateScheduleModel model, CancellationToken ct = default);
    Task<ApiResult<ScheduleModel>> UpdateScheduleAsync(Guid id, UpdateScheduleModel model, CancellationToken ct = default);
    Task<ApiResult<ScheduleModel>> SetScheduleStatusAsync(Guid id, string action, CancellationToken ct = default);
    Task<ApiResult<ScheduleModel>> ExtendScheduleAsync(Guid id, ExtendScheduleModel model, CancellationToken ct = default);
    Task<ApiResult<MaintenanceTaskModel>> CreateTaskAsync(CreateMaintenanceTaskModel model, CancellationToken ct = default);
    Task<ApiResult<Page<MaintenanceTaskModel>>> TasksAsync(TaskFilterModel filter, CancellationToken ct = default);
    Task<ApiResult<MaintenanceHistoryListModel>> HistoryAsync(MaintenanceHistoryFilterModel filter, CancellationToken ct = default);
    Task<ApiResult<MaintenanceHistoryDetailModel>> HistoryDetailAsync(Guid taskId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<AssignableMaintenanceStaffModel>>> HistoryStaffAsync(CancellationToken ct = default);
    Task<ApiResult<MaintenanceTaskModel>> TaskAsync(Guid taskId, CancellationToken ct = default);
    Task<ApiResult<IReadOnlyList<AssignableMaintenanceStaffModel>>> AssignableStaffAsync(CancellationToken ct = default);
    Task<ApiResult<MaintenanceTaskModel>> AssignTaskAsync(Guid taskId, AssignMaintenanceTaskModel model, CancellationToken ct = default);
    Task<ApiResult<MaintenanceTaskModel>> ReviewResultAsync(Guid taskId, Guid resultId, ReviewMaintenanceResultModel model, CancellationToken ct = default);
    Task<ApiResult<MaintenanceTaskModel>> AssociateTaskAssetAsync(Guid taskId, AssociateMaintenanceTaskAssetModel model, CancellationToken ct = default);
}
