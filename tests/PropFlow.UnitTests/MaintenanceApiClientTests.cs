using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Maintenance.Models;
using PropFlow.Web.Client.Features.Maintenance.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class MaintenanceApiClientTests
{
    private sealed class Transport(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(send(request));
    }

    [Fact]
    public async Task Schedules_deserializes_numeric_status_returned_by_api()
    {
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[{\"id\":\"11111111-1111-4111-8111-111111111111\",\"scheduleCode\":\"MS-001\",\"title\":\"Kiểm tra\",\"plannedStartAt\":\"2026-09-26T00:00:00+00:00\",\"status\":0}],\"totalCount\":1,\"pageIndex\":1,\"pageSize\":20}", Encoding.UTF8, "application/json") })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        var result = await new MaintenanceApiClient(api).SchedulesAsync(new ScheduleFilterModel());

        Assert.True(result.IsSuccess);
        Assert.Equal(MaintenanceScheduleStatus.ACTIVE, result.Data!.Items.Single().Status);
    }

    [Fact]
    public async Task Create_task_posts_only_create_task_contract()
    {
        HttpRequestMessage? captured = null;
        string? payload = null;
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            captured = request;
            using var reader = new StreamReader(request.Content!.ReadAsStream());
            payload = reader.ReadToEnd();
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"taskNumber\":\"MT-001\",\"title\":\"Kiểm tra\",\"status\":0,\"createdBy\":\"11111111-1111-4111-8111-111111111111\",\"createdAt\":\"2026-09-26T00:00:00+00:00\"}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);
        var result = await new MaintenanceApiClient(api).CreateTaskAsync(new CreateMaintenanceTaskModel { TaskNumber = "MT-001", Title = "Kiểm tra" });
        Assert.True(result.IsSuccess); Assert.Equal(MaintenanceTaskStatus.OPEN, result.Data!.Status); Assert.Equal(HttpMethod.Post, captured!.Method); Assert.Equal("/api/v1/maintenance/tasks", captured.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("createdBy", payload, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("status", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Associate_task_asset_patches_only_asset_identifiers()
    {
        HttpRequestMessage? captured = null;
        string? payload = null;
        var facilityId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            captured = request;
            using var reader = new StreamReader(request.Content!.ReadAsStream());
            payload = reader.ReadToEnd();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"{taskId}\",\"taskNumber\":\"MT-001\",\"facilityId\":\"{facilityId}\",\"equipmentId\":\"{equipmentId}\",\"title\":\"Kiểm tra\",\"status\":0,\"createdBy\":\"{Guid.NewGuid()}\",\"createdAt\":\"2026-09-26T00:00:00+00:00\"}}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        var result = await new MaintenanceApiClient(api).AssociateTaskAssetAsync(taskId, new AssociateMaintenanceTaskAssetModel { FacilityId = facilityId, EquipmentId = equipmentId });

        Assert.True(result.IsSuccess);
        Assert.Equal(facilityId, result.Data!.FacilityId);
        Assert.Equal(equipmentId, result.Data.EquipmentId);
        Assert.Equal(HttpMethod.Patch, captured!.Method);
        Assert.Equal($"/api/v1/maintenance/tasks/{taskId}/asset", captured.RequestUri!.AbsolutePath);
        Assert.Contains("facilityId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("equipmentId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("buildingId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdBy", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_task_from_schedule_omits_client_asset_and_time_fields()
    {
        string? payload = null;
        var scheduleId = Guid.NewGuid();
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            using var reader = new StreamReader(request.Content!.ReadAsStream());
            payload = reader.ReadToEnd();
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent($"{{\"id\":\"{Guid.NewGuid()}\",\"taskNumber\":\"MT-002\",\"scheduleId\":\"{scheduleId}\",\"title\":\"Theo lịch\",\"status\":0,\"createdBy\":\"{Guid.NewGuid()}\",\"createdAt\":\"2026-09-26T00:00:00+00:00\"}}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        var result = await new MaintenanceApiClient(api).CreateTaskAsync(new CreateMaintenanceTaskModel { TaskNumber = "MT-002", Title = "Theo lịch", ScheduleId = scheduleId });

        Assert.True(result.IsSuccess);
        Assert.Contains("scheduleId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("facilityId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("equipmentId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plannedStartAt", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dueAt", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assign_task_posts_only_selected_staff_identifier()
    {
        string? payload = null;
        var taskId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            using var reader = new StreamReader(request.Content!.ReadAsStream());
            payload = reader.ReadToEnd();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"{taskId}\",\"taskNumber\":\"MT-003\",\"title\":\"Kiểm tra\",\"status\":1,\"createdBy\":\"{Guid.NewGuid()}\",\"createdAt\":\"2026-09-27T00:00:00+00:00\",\"assignedStaffUserId\":\"{staffId}\"}}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        var result = await new MaintenanceApiClient(api).AssignTaskAsync(taskId, new AssignMaintenanceTaskModel { StaffUserId = staffId });

        Assert.True(result.IsSuccess);
        Assert.Equal(staffId, result.Data!.AssignedStaffUserId);
        Assert.Contains("staffUserId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assignedBy", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("assignedAt", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("managerId", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Review_result_posts_only_manager_decision_and_note()
    {
        HttpRequestMessage? captured = null;
        string? payload = null;
        var taskId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            captured = request;
            using var reader = new StreamReader(request.Content!.ReadAsStream());
            payload = reader.ReadToEnd();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"{taskId}\",\"taskNumber\":\"MT-004\",\"title\":\"Kiểm tra\",\"status\":5,\"createdBy\":\"{Guid.NewGuid()}\",\"createdAt\":\"2026-09-27T00:00:00+00:00\"}}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        var response = await new MaintenanceApiClient(api).ReviewResultAsync(taskId, resultId, new ReviewMaintenanceResultModel { Decision = "approve", ReviewNote = "Đạt yêu cầu" });

        Assert.True(response.IsSuccess);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal($"/api/v1/maintenance/tasks/{taskId}/results/{resultId}/review", captured.RequestUri!.AbsolutePath);
        Assert.Contains("decision", payload, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reviewNote", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reviewedBy", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("closedBy", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("role", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permission", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tasks_serializes_only_populated_server_side_filter_parameters()
    {
        HttpRequestMessage? captured = null;
        var facilityId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"totalCount\":0,\"pageIndex\":2,\"pageSize\":10}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        await new MaintenanceApiClient(api).TasksAsync(new TaskFilterModel
        {
            SearchKeyword = "máy bơm", Status = MaintenanceTaskStatus.ASSIGNED, FacilityId = facilityId, EquipmentId = equipmentId,
            AssignedStaffUserId = staffId, From = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), To = new DateTimeOffset(2026, 9, 29, 23, 59, 59, TimeSpan.Zero), PageIndex = 2, PageSize = 10
        });

        var query = captured!.RequestUri!.Query;
        Assert.Contains("searchKeyword=", query); Assert.Contains("status=ASSIGNED", query);
        Assert.Contains($"facilityId={facilityId}", query); Assert.Contains($"equipmentId={equipmentId}", query);
        Assert.Contains($"assignedStaffUserId={staffId}", query); Assert.Contains("from=", query); Assert.Contains("to=", query);
        Assert.Contains("pageIndex=2", query); Assert.DoesNotContain("buildingId", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Schedules_serializes_date_range_with_existing_filter_parameters()
    {
        HttpRequestMessage? captured = null;
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":20}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        await new MaintenanceApiClient(api).SchedulesAsync(new ScheduleFilterModel { SearchKeyword = "bơm", Status = MaintenanceScheduleStatus.ACTIVE, From = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), To = new DateTimeOffset(2026, 10, 4, 23, 59, 59, TimeSpan.Zero) });

        var query = captured!.RequestUri!.Query;
        Assert.Contains("searchKeyword=", query); Assert.Contains("status=ACTIVE", query); Assert.Contains("from=", query); Assert.Contains("to=", query);
    }

    [Fact]
    public async Task History_serializes_history_filters_without_building_scope()
    {
        HttpRequestMessage? captured = null;
        var staffId = Guid.NewGuid();
        var api = new AuthenticatedApiClient(new HttpClient(new Transport(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"page\":{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":20},\"summary\":{\"total\":0,\"open\":0,\"assigned\":0,\"inProgress\":0,\"completed\":0,\"closed\":0,\"cancelled\":0}}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://test.invalid/") }, NullLogger<ApiClient>.Instance);

        var response = await new MaintenanceApiClient(api).HistoryAsync(new MaintenanceHistoryFilterModel { SearchKeyword = "MT-001", Status = MaintenanceTaskStatus.CLOSED, StaffUserId = staffId, From = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), To = new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero) });

        Assert.True(response.IsSuccess);
        var query = captured!.RequestUri!.Query;
        Assert.Contains("staffUserId=", query); Assert.Contains("sort=NEWEST", query); Assert.Contains("from=", query); Assert.Contains("to=", query);
        Assert.DoesNotContain("building", query, StringComparison.OrdinalIgnoreCase);
    }
}
