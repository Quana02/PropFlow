using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Shared;

namespace PropFlow.IntegrationTests;

public sealed partial class MaintenanceAuthorizationHttpTests
{
    [Fact]
    public async Task Schedule_mutations_require_manager_and_preserve_related_task_and_asset_state()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(Now.ToUnixTimeSeconds()).AddMinutes(-20);
        var command = new CreateScheduleCommand(null, "Bảo trì máy bơm", start, start.AddHours(2), facility.Id, equipment.Id, "Kiểm tra áp lực");
        await Denied(HttpMethod.Post, "schedules", command);
        await Reject(manager, HttpMethod.Post, "schedules", HttpStatusCode.BadRequest, command with { FacilityId = otherFacility.Id });
        await Reject(manager, HttpMethod.Post, "schedules", HttpStatusCode.BadRequest, command with { PlannedEndAt = start.AddMinutes(-1) });
        var schedule = await Call<ScheduleDto>(manager, HttpMethod.Post, "schedules", new
        {
            command.Title, command.PlannedStartAt, command.PlannedEndAt, command.FacilityId, command.EquipmentId,
            command.Description, CreatedBy = staffA.Id, Status = "CANCELLED"
        }, HttpStatusCode.Created);
        Assert.Equal(MaintenanceScheduleStatus.ACTIVE, schedule.Status);
        Assert.Equal(equipment.Id, schedule.EquipmentId);
        var route = $"schedules/{schedule.Id}";
        var update = new UpdateScheduleCommand("Bảo trì bơm nước", start, start.AddHours(3), facility.Id, equipment.Id, "Đã cập nhật công việc");
        await Denied(HttpMethod.Put, route, update);
        await Reject(manager, HttpMethod.Put, route, HttpStatusCode.NotFound, update with { FacilityId = Guid.NewGuid() });
        var updated = await Call<ScheduleDto>(manager, HttpMethod.Put, route, new
        {
            update.Title, update.Description, update.FacilityId, update.EquipmentId,
            update.PlannedStartAt, update.PlannedEndAt, UpdatedBy = staffA.Id
        });
        Assert.Equal(update.Title, updated.Title);
        Assert.Equal(schedule.Id, updated.Id);
        var task = await NewTask(new(null, "Từ lịch", ScheduleId: schedule.Id, Description: "Yêu cầu từ lịch", PriorityCode: "HIGH"));
        await Assign(task.Id, staffA);
        await Reject(manager, HttpMethod.Put, route, HttpStatusCode.Conflict, update with { Title = "Đổi tiêu đề sau khi tạo task" });

        var extend = new ExtendScheduleCommand(30, "minute");
        await Denied(HttpMethod.Post, route + "/extend", extend);
        await Reject(manager, HttpMethod.Post, route + "/extend", HttpStatusCode.BadRequest, extend with { Amount = -1 });
        await Reject(manager, HttpMethod.Post, route + "/extend", HttpStatusCode.BadRequest, extend with { Unit = "invalid" });
        var extended = await Call<ScheduleDto>(manager, HttpMethod.Post, route + "/extend", extend);
        Assert.Equal(updated.PlannedEndAt!.Value.AddMinutes(30), extended.PlannedEndAt);
        var extendedAgain = await Call<ScheduleDto>(manager, HttpMethod.Post, route + "/extend", extend);
        Assert.Equal(extended.PlannedEndAt!.Value.AddMinutes(30), extendedAgain.PlannedEndAt);
        var cancel = new ScheduleStatusCommand("cancel");
        await Denied(HttpMethod.Patch, route + "/status", cancel);
        await Reject(manager, HttpMethod.Patch, route + "/status", HttpStatusCode.BadRequest, new ScheduleStatusCommand("invalid"));
        var stopped = await Call<ScheduleDto>(manager, HttpMethod.Patch, route + "/status", cancel);
        Assert.Equal(MaintenanceScheduleStatus.CANCELLED, stopped.Status);
        await Reject(manager, HttpMethod.Patch, route + "/status", HttpStatusCode.Conflict, cancel);
        await Reject(manager, HttpMethod.Post, route + "/extend", HttpStatusCode.Conflict, extend);
        await Reject(manager, HttpMethod.Put, route, HttpStatusCode.Conflict, update);

        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var row = await db.MaintenanceSchedules.SingleAsync();
        Assert.Equal(manager.Id, row.CreatedBy);
        Assert.Equal(manager.Id, row.UpdatedBy);
        Assert.Equal(MaintenanceTaskStatus.CANCELLED, (await db.MaintenanceTasks.SingleAsync()).Status);
        Assert.False((await db.MaintenanceAssignments.SingleAsync()).IsActive);
        Assert.Equal(manager.Id, (await db.MaintenanceTaskActivities.SingleAsync(x => x.ToStatus == MaintenanceTaskStatus.CANCELLED)).PerformedBy);
        var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        Assert.Equal(EquipmentStatus.ACTIVE, (await assets.Equipment.SingleAsync(row => row.Id == equipment.Id)).Status);
    }

    [Fact]
    public async Task Task_creation_asset_association_and_assignment_check_relationships_and_roles()
    {
        var command = new CreateMaintenanceTaskCommand(null, "Kiểm tra máy bơm", Description: "Kiểm tra định kỳ",
            PriorityCode: "HIGH", PlannedStartAt: Now, DueAt: Now.AddHours(1), FacilityId: facility.Id, EquipmentId: equipment.Id);
        await Denied(HttpMethod.Post, "tasks", command);
        await Reject(manager, HttpMethod.Post, "tasks", HttpStatusCode.BadRequest, command with { FacilityId = otherFacility.Id });
        await Reject(manager, HttpMethod.Post, "tasks", HttpStatusCode.NotFound, command with { ScheduleId = Guid.NewGuid() });
        var task = await Call<MaintenanceTaskDto>(manager, HttpMethod.Post, "tasks", new
        {
            command.Title, command.Description, command.FacilityId, command.EquipmentId,
            command.PriorityCode, command.PlannedStartAt, command.DueAt, CreatedBy = staffA.Id,
            AssignedStaffUserId = staffA.Id, Status = "CLOSED"
        }, HttpStatusCode.Created);
        Assert.Equal(MaintenanceTaskStatus.OPEN, task.Status);
        Assert.Equal(manager.Id, task.CreatedBy);
        Assert.Null(task.AssignedStaffUserId);
        Assert.Equal(equipment.Id, task.EquipmentId);

        var route = $"tasks/{task.Id}/asset";
        var associate = new AssociateMaintenanceTaskAssetCommand(otherFacility.Id, otherEquipment.Id);
        await Denied(HttpMethod.Patch, route, associate);
        await Reject(manager, HttpMethod.Patch, route, HttpStatusCode.BadRequest, associate with { FacilityId = facility.Id });
        await Reject(manager, HttpMethod.Patch, route, HttpStatusCode.NotFound, associate with { EquipmentId = Guid.NewGuid() });
        var linked = await Call<MaintenanceTaskDto>(manager, HttpMethod.Patch, route, associate);
        Assert.Equal(otherEquipment.Id, linked.EquipmentId);
        Assert.Equal(otherFacility.Id, linked.FacilityId);
        Assert.Equal(otherEquipment.Id, (await Call<MaintenanceTaskDto>(manager, HttpMethod.Patch, route, associate)).EquipmentId);

        var assignment = new AssignMaintenanceTaskCommand(staffA.Id);
        await Denied(HttpMethod.Post, $"tasks/{task.Id}/assignment", assignment);
        var assigned = await Assign(task.Id, staffA);
        Assert.Equal(staffA.Id, assigned.AssignedStaffUserId);
        await Reject(unrelated, HttpMethod.Get, $"my-tasks/{task.Id}", HttpStatusCode.NotFound);

        var emergency = await NewTask(command with { IsEmergency = true, Title = "Bảo trì khẩn cấp" });
        Assert.Equal(MaintenanceTaskStatus.OPEN, emergency.Status);
        await Reject(manager, HttpMethod.Post, "tasks", HttpStatusCode.Conflict, command with { IsEmergency = true });
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        Assert.Equal(EquipmentStatus.UNDER_MAINTENANCE, (await assets.Equipment.SingleAsync(row => row.Id == equipment.Id)).Status);
        Assert.Equal(EquipmentStatus.ACTIVE, (await assets.Equipment.SingleAsync(row => row.Id == otherEquipment.Id)).Status);
        Assert.Equal(staffA.Id, (await db.MaintenanceAssignments.SingleAsync()).StaffUserId);
        Assert.Single(await db.ResidentEvents.ToListAsync());
    }

    [Fact]
    public async Task Cancellation_before_staff_progress_or_upload_preserves_assignments_and_calls_no_storage()
    {
        var task = await NewTask();
        await Assign(task.Id, staffA);
        await Call<MaintenanceTaskDto>(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/start");
        var route = $"tasks/{task.Id}/cancel";
        await Denied(HttpMethod.Post, route);
        using var scope = database.Factory.Services.CreateScope();
        using var observerScope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var observer = observerScope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})",
            [IdentifierCodeNormalizer.AdvisoryLockKey(MaintenanceTaskLock.Scope(task.Id))]);
        var cancellation = Send(manager.Client, HttpMethod.Post, route);
        await WaitForLocks(observer, 1);
        var progress = Send(staffA.Client, HttpMethod.Post, $"my-tasks/{task.Id}/progress", new UpdateMaintenanceProgressCommand("Đang làm"));
        var upload = Send(staffA.Client, HttpMethod.Post, $"my-tasks/{task.Id}/result", ResultForm());
        await WaitForLocks(observer, 3);
        Assert.Empty(storage.Uploads);
        await tx.CommitAsync();
        using var cancelled = await cancellation;
        using var deniedProgress = await progress;
        using var deniedUpload = await upload;
        await ExpectStatus(cancelled, HttpStatusCode.OK);
        await ExpectStatus(deniedProgress, HttpStatusCode.Forbidden);
        await ExpectStatus(deniedUpload, HttpStatusCode.Forbidden);
        Assert.Equal(MaintenanceTaskStatus.CANCELLED, (await db.MaintenanceTasks.SingleAsync()).Status);
        Assert.False((await db.MaintenanceAssignments.SingleAsync()).IsActive);
        Assert.Equal(manager.Id, (await db.MaintenanceTaskActivities.SingleAsync(row => row.ToStatus == MaintenanceTaskStatus.CANCELLED)).PerformedBy);
        Assert.Empty(await db.MaintenanceResults.ToListAsync());
        Assert.Empty(storage.Uploads);
        Assert.Empty(storage.Deletes);
        await Reject(manager, HttpMethod.Post, route, HttpStatusCode.Conflict);
        await Reject(manager, HttpMethod.Patch, $"tasks/{task.Id}/asset", HttpStatusCode.Conflict,
            new AssociateMaintenanceTaskAssetCommand(facility.Id, equipment.Id));
    }

    [Fact]
    public async Task Cancellation_waits_for_an_authorized_upload_and_keeps_committed_evidence()
    {
        var task = await NewTask();
        await Assign(task.Id, staffA);
        await Call<MaintenanceTaskDto>(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/start");
        var storageReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishStorage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.BeforeStoreReturns = async () =>
        {
            storageReached.TrySetResult();
            await finishStorage.Task.WaitAsync(TimeSpan.FromSeconds(15));
        };
        var upload = Send(staffA.Client, HttpMethod.Post, $"my-tasks/{task.Id}/result", ResultForm());
        await storageReached.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var cancel = Send(manager.Client, HttpMethod.Post, $"tasks/{task.Id}/cancel");
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        try
        {
            await WaitForLocks(db, 1);
            Assert.False(cancel.IsCompleted);
            Assert.False(await db.MaintenanceResults.AnyAsync());
        }
        finally
        {
            finishStorage.TrySetResult();
        }
        using var uploaded = await upload;
        using var cancelled = await cancel;
        await ExpectStatus(cancelled, HttpStatusCode.OK);
        Assert.Contains(uploaded.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.NotFound });
        var result = await db.MaintenanceResults.SingleAsync(row => row.MaintenanceTaskId == task.Id);
        var attachment = await db.MaintenanceResultAttachments.SingleAsync(row => row.MaintenanceResultId == result.Id);
        Assert.Equal(staffA.Id, result.SubmittedBy);
        Assert.Single(storage.Uploads);
        Assert.Empty(storage.Deletes);
        await PrivateImage(manager, $"tasks/{task.Id}/results/{result.Id}/attachments/{attachment.Id}");
    }

    [Fact]
    public async Task Concurrent_service_request_linkage_keeps_one_relationship_and_one_activity()
    {
        var task = await NewTask(new(null, "Liên kết yêu cầu", Description: "Kiểm tra tại chỗ", FacilityId: facility.Id, EquipmentId: equipment.Id));
        await Assign(task.Id, staffA);
        var first = await SeedRequest(facility.Id, equipment.Id);
        var second = await SeedRequest(otherFacility.Id, otherEquipment.Id);
        var expired = await SeedRequest(facility.Id, equipment.Id, cancelled: true);
        var link = new LinkMaintenanceTaskServiceRequestCommand(first.Id);
        var route = $"tasks/{task.Id}/service-request";
        await Denied(HttpMethod.Post, route, link);
        await Reject(manager, HttpMethod.Post, route, HttpStatusCode.BadRequest, link with { ServiceRequestId = Guid.NewGuid() });
        await Reject(manager, HttpMethod.Post, route, HttpStatusCode.Conflict, link with { ServiceRequestId = expired.Id });
        using var scope = database.Factory.Services.CreateScope();
        using var observerScope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        var observer = observerScope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM maintenance.maintenance_tasks WHERE id = {task.Id} FOR UPDATE");
        var firstLink = Send(manager.Client, HttpMethod.Post, route, link);
        await WaitForLocks(observer, 1);
        var otherLink = Send(manager.Client, HttpMethod.Post, route, link with { ServiceRequestId = second.Id });
        await WaitForLocks(observer, 2);
        await tx.CommitAsync();
        using var firstResult = await firstLink;
        using var secondResult = await otherLink;
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, new[] { firstResult.StatusCode, secondResult.StatusCode }.Order());
        var linked = await db.MaintenanceTasks.SingleAsync(row => row.Id == task.Id);
        Assert.Contains(linked.SourceServiceRequestId, new Guid?[] { first.Id, second.Id });
        Assert.Equal(equipment.Id, linked.EquipmentId);
        var activity = Assert.Single(await db.MaintenanceTaskActivities.Where(x => x.ActivityType == MaintenanceActivityType.SERVICE_REQUEST_LINKED).ToListAsync());
        Assert.Equal(manager.Id, activity.PerformedBy);
        await Reject(manager, HttpMethod.Post, route, HttpStatusCode.Conflict, link);

        using var staffRequest = await staffA.Client.GetAsync($"/api/v1/staff/service-requests/{first.RequestNumber}");
        Assert.Equal(HttpStatusCode.NotFound, staffRequest.StatusCode);
        var requests = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var original = await requests.ServiceRequests.SingleAsync(x => x.Id == first.Id);
        Assert.Equal(first.ResidentId, original.ResidentId);
        Assert.Equal(ServiceRequestStatus.SUBMITTED, original.Status);
    }

    [Fact]
    public async Task Review_and_private_evidence_respect_the_task_result_and_participation_intervals()
    {
        var task = await NewTask();
        var other = await NewTask();
        await Assign(task.Id, staffA);
        await Call<MaintenanceTaskDto>(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/start");
        await Call<MaintenanceTaskDto>(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/progress", new UpdateMaintenanceProgressCommand("Phần việc A đợt đầu"));
        await Assign(task.Id, staffB);
        await Reject(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/progress", HttpStatusCode.Forbidden, new UpdateMaintenanceProgressCommand("Tiến độ của Staff cũ"));
        await Call<MaintenanceTaskDto>(staffB, HttpMethod.Post, $"my-tasks/{task.Id}/progress", new UpdateMaintenanceProgressCommand("Phần việc B riêng"));
        var old = (await Call<MaintenanceTaskDto>(staffB, HttpMethod.Post, $"my-tasks/{task.Id}/result", ResultForm())).LatestResult!;
        var revise = new ReviewMaintenanceResultCommand("request-revision", "Cần kiểm tra lại độ rung");
        var approve = new ReviewMaintenanceResultCommand("approve", "Kết quả đạt");
        string Review(Guid taskId, Guid resultId) => $"tasks/{taskId}/results/{resultId}/review";
        await Denied(HttpMethod.Post, Review(task.Id, old.Id), revise);
        var revised = await Call<MaintenanceTaskDto>(manager, HttpMethod.Post, Review(task.Id, old.Id), revise);
        Assert.Equal(MaintenanceTaskStatus.ASSIGNED, revised.Status);
        Assert.Equal(MaintenanceResultStatus.REVISION_REQUIRED, revised.LatestResult!.ResultStatus);
        await Assign(task.Id, staffA);
        await Call<MaintenanceTaskDto>(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/start");
        await Call<MaintenanceTaskDto>(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/progress", new UpdateMaintenanceProgressCommand("Phần việc A đợt mới"));
        var current = (await Call<MaintenanceTaskDto>(staffA, HttpMethod.Post, $"my-tasks/{task.Id}/result", ResultForm())).LatestResult!;
        await Assign(other.Id, unrelated);
        await Call<MaintenanceTaskDto>(unrelated, HttpMethod.Post, $"my-tasks/{other.Id}/start");
        var otherResult = (await Call<MaintenanceTaskDto>(unrelated, HttpMethod.Post, $"my-tasks/{other.Id}/result", ResultForm())).LatestResult!;
        await Reject(manager, HttpMethod.Post, Review(task.Id, otherResult.Id), HttpStatusCode.NotFound, approve);
        await Reject(manager, HttpMethod.Post, Review(task.Id, old.Id), HttpStatusCode.Conflict, approve);
        await Reject(manager, HttpMethod.Post, Review(task.Id, current.Id), HttpStatusCode.BadRequest, approve with { Decision = "invalid" });
        await Denied(HttpMethod.Post, Review(task.Id, current.Id), approve);

        var review = new
        {
            approve.Decision, approve.ReviewNote, ReviewedBy = staffA.Id, Status = "ASSIGNED"
        };
        var reviews = await Task.WhenAll(
            Send(manager.Client, HttpMethod.Post, Review(task.Id, current.Id), review),
            Send(manager.Client, HttpMethod.Post, Review(task.Id, current.Id), review));
        MaintenanceTaskDto response;
        try
        {
            Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, reviews.Select(result => result.StatusCode).Order());
            response = (await reviews.Single(result => result.IsSuccessStatusCode).Content.ReadFromJsonAsync<MaintenanceTaskDto>())!;
        }
        finally
        {
            foreach (var result in reviews) result.Dispose();
        }
        Assert.Equal(MaintenanceTaskStatus.CLOSED, response.Status);
        Assert.Equal(manager.Id, response.LatestResult!.ReviewedBy);
        Assert.Equal(MaintenanceResultStatus.APPROVED, response.LatestResult!.ResultStatus);
        await Reject(manager, HttpMethod.Post, Review(task.Id, current.Id), HttpStatusCode.Conflict, approve);
        await Reject(manager, HttpMethod.Post, $"tasks/{task.Id}/cancel", HttpStatusCode.Conflict);

        var historyA = await Call<MaintenanceHistoryDetailDto>(staffA, HttpMethod.Get, $"my-history/{task.Id}");
        Assert.Equal(2, historyA.Assignments.Count);
        Assert.All(historyA.Assignments, row => Assert.Equal(staffA.Id, row.StaffUserId));
        Assert.DoesNotContain(historyA.Timeline, row => row.Detail == "Phần việc B riêng");
        Assert.Contains(historyA.Timeline, row => row.Detail == "Phần việc A đợt đầu");
        Assert.Contains(historyA.Timeline, row => row.Detail == "Phần việc A đợt mới");
        Assert.DoesNotContain(historyA.Results, row => row.Id == old.Id);
        Assert.Contains(historyA.Results, row => row.Id == current.Id);

        var historyB = await Call<MaintenanceHistoryDetailDto>(staffB, HttpMethod.Get, $"my-history/{task.Id}");
        Assert.Contains(historyB.Results, row => row.Id == old.Id);
        Assert.DoesNotContain(historyB.Results, row => row.Id == current.Id);
        await Reject(unrelated, HttpMethod.Get, $"my-history/{task.Id}", HttpStatusCode.NotFound);
        await PrivateImage(manager, $"tasks/{task.Id}/results/{old.Id}/attachments/{Assert.Single(old.Attachments!).Id}");
        await PrivateImage(staffB, $"my-tasks/{task.Id}/results/{old.Id}/attachments/{Assert.Single(old.Attachments!).Id}");
        var unseen = $"my-tasks/{task.Id}/results/{current.Id}/attachments/{Assert.Single(current.Attachments!).Id}";
        await Reject(staffB, HttpMethod.Get, unseen, HttpStatusCode.NotFound);
        await Reject(manager, HttpMethod.Get, $"tasks/{other.Id}/results/{current.Id}/attachments/{Assert.Single(current.Attachments!).Id}", HttpStatusCode.NotFound);
        await Reject(manager, HttpMethod.Get, $"tasks/{task.Id}/results/{old.Id}/attachments/{Assert.Single(current.Attachments!).Id}", HttpStatusCode.NotFound);
        var cancelled = await Call<MaintenanceTaskDto>(manager, HttpMethod.Post, $"tasks/{other.Id}/cancel");
        Assert.Equal(MaintenanceTaskStatus.CANCELLED, cancelled.Status);
        await PrivateImage(manager, $"tasks/{other.Id}/results/{otherResult.Id}/attachments/{Assert.Single(otherResult.Attachments!).Id}");
        Assert.Equal(3, storage.Uploads.Count);
        Assert.Empty(storage.Deletes);
    }

    private static async Task PrivateImage(Actor actor, string route)
    {
        using var response = await Send(actor.Client, HttpMethod.Get, route);
        await ExpectStatus(response, HttpStatusCode.OK);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(EvidenceStorage.Image, await response.Content.ReadAsByteArrayAsync());
    }

    private async Task<ServiceRequest> SeedRequest(Guid facilityId, Guid equipmentId, bool cancelled = false)
    {
        var home = await database.SeedResidentAsync($"b4-resident-{Guid.NewGuid():N}@example.invalid");
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var request = new ServiceRequest($"B4-{Guid.NewGuid():N}"[..20], home.ResidentId, home.ResidencyId!.Value,
            "Yêu cầu thử", "Cần kiểm tra bơm", Now, apartmentUnitId: home.ApartmentId, facilityId: facilityId, equipmentId: equipmentId);
        if (cancelled) request.Cancel(Now);
        db.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private Task<MaintenanceTaskDto> NewTask(CreateMaintenanceTaskCommand? command = null)
    {
        command ??= new(null, "Bảo trì B4", Description: "Kiểm tra máy bơm", FacilityId: facility.Id);
        return Call<MaintenanceTaskDto>(manager, HttpMethod.Post, "tasks", command with
        {
            PriorityCode = command.PriorityCode ?? "HIGH",
            PlannedStartAt = command.PlannedStartAt ?? Now,
            DueAt = command.DueAt ?? Now.AddHours(1)
        }, HttpStatusCode.Created);
    }

    private Task<MaintenanceTaskDto> Assign(Guid id, Actor assignee) =>
        Call<MaintenanceTaskDto>(manager, HttpMethod.Post, $"tasks/{id}/assignment", new AssignMaintenanceTaskCommand(assignee.Id));

    private static async Task WaitForLocks(MaintenanceDbContext observer, int expected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (await observer.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM pg_stat_activity
            WHERE datname = current_database() AND wait_event_type = 'Lock'
            """).SingleAsync(deadline.Token) < expected)
            await Task.Delay(25, deadline.Token);
    }
}
