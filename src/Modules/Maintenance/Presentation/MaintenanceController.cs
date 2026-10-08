using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.Maintenance.Presentation;

[ApiController]
[Route("api/v1/maintenance")]
public sealed class MaintenanceController(MaintenanceService service) : ControllerBase
{
    [HttpGet("assets")][Authorize(Policy = MaintenanceAuthorizationPolicies.View)] public Task<IReadOnlyList<MaintenanceAssetReference>> Assets(CancellationToken ct) => service.AssetsAsync(ct);
    [HttpGet("tasks/assignable-staff")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<IReadOnlyList<AssignableMaintenanceStaffDto>> AssignableStaff(CancellationToken ct) => service.AssignableStaffAsync(ct);
    [HttpGet("history/staff")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<IReadOnlyList<AssignableMaintenanceStaffDto>> HistoryStaff(CancellationToken ct) => service.HistoryStaffAsync(ct);
    [HttpGet("schedules")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<Page<ScheduleDto>> Schedules([FromQuery] ScheduleQuery query, CancellationToken ct) => service.SchedulesAsync(query, ct);
    [HttpPost("schedules")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public async Task<ActionResult<ScheduleDto>> CreateSchedule(CreateScheduleCommand command, CancellationToken ct) => await Execute(() => service.CreateScheduleAsync(command, Actor(), ct), true);
    [HttpPost("tasks")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public async Task<ActionResult<MaintenanceTaskDto>> CreateTask(CreateMaintenanceTaskCommand command, CancellationToken ct) => await Execute(() => service.CreateTaskAsync(command, Actor(), ct), true);
    [HttpGet("tasks")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<Page<MaintenanceTaskDto>> Tasks([FromQuery] TaskQuery query, CancellationToken ct) => service.TasksAsync(query, ct);
    [HttpGet("my-tasks")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<Page<MaintenanceTaskDto>> MyTasks([FromQuery] MyMaintenanceTaskQuery query, CancellationToken ct) => service.MyTasksAsync(query, Actor(), ct);
    [HttpGet("my-tasks/{taskId:guid}")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<ActionResult<MaintenanceTaskDto>> MyTask(Guid taskId, CancellationToken ct) => Execute(() => service.MyTaskAsync(taskId, Actor(), ct));
    [HttpPost("my-tasks/{taskId:guid}/start")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<ActionResult<MaintenanceTaskDto>> StartMyTask(Guid taskId, CancellationToken ct) => Execute(() => service.StartMyTaskAsync(taskId, Actor(), ct));
    [HttpPost("my-tasks/{taskId:guid}/progress")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<ActionResult<MaintenanceTaskDto>> UpdateMyTaskProgress(Guid taskId, UpdateMaintenanceProgressCommand command, CancellationToken ct) => Execute(() => service.UpdateMyTaskProgressAsync(taskId, command, Actor(), ct));
    [HttpPost("my-tasks/{taskId:guid}/result")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<ActionResult<MaintenanceTaskDto>> SubmitMyTaskResult(Guid taskId, SubmitMaintenanceResultCommand command, CancellationToken ct) => Execute(() => service.SubmitMyTaskResultAsync(taskId, command, Actor(), ct));
    [HttpGet("my-tasks/{taskId:guid}/activities")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<IReadOnlyList<MaintenanceTaskActivityDto>> MyTaskActivities(Guid taskId, CancellationToken ct) => service.MyTaskActivitiesAsync(taskId, Actor(), ct);
    [HttpGet("my-history")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<MaintenanceHistoryListDto> MyHistory([FromQuery] MaintenanceHistoryQuery query, CancellationToken ct) => service.MyHistoryAsync(query, Actor(), ct);
    [HttpGet("my-history/{taskId:guid}")][Authorize(Policy = MaintenanceAuthorizationPolicies.Work)] public Task<ActionResult<MaintenanceHistoryDetailDto>> MyHistoryDetail(Guid taskId, CancellationToken ct) => Execute(() => service.MyHistoryDetailAsync(taskId, Actor(), ct));
    [HttpGet("history")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<MaintenanceHistoryListDto> History([FromQuery] MaintenanceHistoryQuery query, CancellationToken ct) => service.HistoryAsync(query, ct);
    [HttpGet("history/{taskId:guid}")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<MaintenanceHistoryDetailDto>> HistoryDetail(Guid taskId, CancellationToken ct) => Execute(() => service.HistoryDetailAsync(taskId, ct));
    [HttpGet("tasks/{taskId:guid}")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<MaintenanceTaskDto>> Task(Guid taskId, CancellationToken ct) => Execute(() => service.TaskAsync(taskId, ct));
    [HttpPost("tasks/{taskId:guid}/assignment")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<MaintenanceTaskDto>> AssignTask(Guid taskId, AssignMaintenanceTaskCommand command, CancellationToken ct) => Execute(() => service.AssignTaskAsync(taskId, command, Actor(), ct));
    [HttpPost("tasks/{taskId:guid}/results/{resultId:guid}/review")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<MaintenanceTaskDto>> ReviewResult(Guid taskId, Guid resultId, ReviewMaintenanceResultCommand command, CancellationToken ct) => Execute(() => service.ReviewResultAsync(taskId, resultId, command, Actor(), ct));
    [HttpPatch("tasks/{taskId:guid}/asset")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<MaintenanceTaskDto>> AssociateTaskAsset(Guid taskId, AssociateMaintenanceTaskAssetCommand command, CancellationToken ct) => Execute(() => service.AssociateTaskAssetAsync(taskId, command, ct));
    [HttpPut("schedules/{id:guid}")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<ScheduleDto>> UpdateSchedule(Guid id, UpdateScheduleCommand command, CancellationToken ct) => Execute(() => service.UpdateScheduleAsync(id, command, Actor(), ct));
    [HttpPatch("schedules/{id:guid}/status")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<ScheduleDto>> ScheduleStatus(Guid id, ScheduleStatusCommand command, CancellationToken ct) => Execute(() => service.SetScheduleStatusAsync(id, command.Action, Actor(), ct));
    [HttpPost("schedules/{id:guid}/extend")][Authorize(Policy = MaintenanceAuthorizationPolicies.Manage)] public Task<ActionResult<ScheduleDto>> ExtendSchedule(Guid id, ExtendScheduleCommand command, CancellationToken ct) => Execute(() => service.ExtendScheduleAsync(id, command, Actor(), ct));

    private Guid Actor() => Guid.Parse(User.FindFirst("sub")!.Value);
    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> action, bool created = false)
    {
        try { var value = await action(); return created ? StatusCode(StatusCodes.Status201Created, value) : Ok(value); }
        catch (UnauthorizedAccessException) { return StatusCode(403, Problem(403, "forbidden", "Bạn không có quyền thao tác công việc này.")); }
        catch (KeyNotFoundException ex) { return NotFound(Problem(404, "not_found", ex.Message)); }
        catch (ArgumentException ex) { return BadRequest(Problem(400, "invalid_maintenance_data", ex.Message)); }
        catch (InvalidOperationException ex) { return Conflict(Problem(409, "maintenance_conflict", ex.Message)); }
    }
    private ProblemDetails Problem(int status, string code, string detail) { var problem = new ProblemDetails { Status = status, Title = status == 403 ? "Không có quyền" : "Không thể xử lý yêu cầu", Detail = detail }; problem.Extensions["code"] = code; problem.Extensions["traceId"] = HttpContext.TraceIdentifier; return problem; }
}
