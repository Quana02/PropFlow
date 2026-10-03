using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceSearchFilterTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TasksAsync_SearchesTaskNumberAndTitle_ServerSide()
    {
        await using var db = CreateDb();
        AddTask(db, "MT-PUMP", "Bảo trì máy bơm", null, null, BaseTime, BaseTime.AddHours(2));
        AddTask(db, "MT-LIGHT", "Kiểm tra đèn hành lang", null, null, BaseTime, BaseTime.AddHours(2));
        await db.SaveChangesAsync();
        var service = Service(db);

        Assert.Equal("MT-PUMP", Assert.Single((await service.TasksAsync(new("pump"), default)).Items).TaskNumber);
        Assert.Equal("MT-LIGHT", Assert.Single((await service.TasksAsync(new("đèn hành"), default)).Items).TaskNumber);
    }

    [Fact]
    public async Task TasksAsync_FiltersStatusFacilityEquipmentAndDateOverlap()
    {
        await using var db = CreateDb();
        var facility = Guid.NewGuid();
        var equipment = Guid.NewGuid();
        var matching = AddTask(db, "MT-MATCH", "Máy bơm", facility, equipment, BaseTime, BaseTime.AddHours(2));
        matching.MarkAssigned(BaseTime.AddMinutes(1));
        AddTask(db, "MT-OTHER", "Máy bơm", Guid.NewGuid(), Guid.NewGuid(), BaseTime.AddDays(2), BaseTime.AddDays(2).AddHours(1));
        await db.SaveChangesAsync();
        var service = Service(db);

        var page = await service.TasksAsync(new(Status: MaintenanceTaskStatus.ASSIGNED, FacilityId: facility, EquipmentId: equipment, From: BaseTime.AddHours(1), To: BaseTime.AddHours(3)), default);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(matching.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task TasksAsync_FiltersOnlyCurrentAssignee_NotHistoricalAssignment()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var task = AddTask(db, "MT-ASSIGN", "Kiểm tra", null, null, BaseTime, BaseTime.AddHours(1));
        task.MarkAssigned(BaseTime.AddMinutes(1));
        var oldAssignment = new MaintenanceAssignment(task.Id, staffA.UserId, Guid.NewGuid(), BaseTime.AddMinutes(1));
        oldAssignment.Reassign(BaseTime.AddMinutes(2));
        var currentAssignment = new MaintenanceAssignment(task.Id, staffB.UserId, Guid.NewGuid(), BaseTime.AddMinutes(2));
        AddTask(db, "MT-UNASSIGNED", "Chưa giao", null, null, BaseTime, BaseTime.AddHours(1));
        db.AddRange(oldAssignment, currentAssignment);
        await db.SaveChangesAsync();
        var service = Service(db, staffA, staffB);

        Assert.Empty((await service.TasksAsync(new(AssignedStaffUserId: staffA.UserId), default)).Items);
        var page = await service.TasksAsync(new(AssignedStaffUserId: staffB.UserId), default);
        Assert.Equal(task.Id, Assert.Single(page.Items).Id);
        Assert.Equal(staffB.UserId, page.Items[0].AssignedStaffUserId);
    }

    [Fact]
    public async Task TasksAsync_ExcludesUntimedOpenTaskWhenTimeFilterIsUsedAndReturnsNoMatchPage()
    {
        await using var db = CreateDb();
        AddTask(db, "MT-UNTIMED", "Không có thời gian", null, null, null, null);
        await db.SaveChangesAsync();

        var page = await Service(db).TasksAsync(new(From: BaseTime, To: BaseTime.AddDays(1)), default);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task TasksAsync_AppliesFiltersBeforePaginationAndFindsClosedAndReopenedStatuses()
    {
        await using var db = CreateDb();
        var facility = Guid.NewGuid();
        for (var index = 0; index < 3; index++) AddTask(db, $"MT-PAGE-{index}", "Theo bộ lọc", facility, null, BaseTime.AddMinutes(index), BaseTime.AddHours(1));
        var closed = AddTask(db, "MT-CLOSED", "Đã đóng", null, null, BaseTime, BaseTime.AddHours(1));
        closed.MarkAssigned(BaseTime); closed.Start(BaseTime.AddMinutes(1)); closed.Complete(BaseTime.AddMinutes(2)); closed.Close(Guid.NewGuid(), BaseTime.AddMinutes(3));
        var reopened = AddTask(db, "MT-REOPEN", "Mở lại", null, null, BaseTime, BaseTime.AddHours(1));
        reopened.MarkAssigned(BaseTime); reopened.Start(BaseTime.AddMinutes(1)); reopened.Complete(BaseTime.AddMinutes(2)); reopened.ReopenForFurtherWork(BaseTime.AddMinutes(3));
        await db.SaveChangesAsync();
        var service = Service(db);

        var page = await service.TasksAsync(new(FacilityId: facility, PageIndex: 2, PageSize: 1), default);
        Assert.Equal(3, page.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(MaintenanceTaskStatus.CLOSED, Assert.Single((await service.TasksAsync(new(Status: MaintenanceTaskStatus.CLOSED), default)).Items).Status);
        Assert.Equal(MaintenanceTaskStatus.ASSIGNED, Assert.Single((await service.TasksAsync(new(SearchKeyword: "REOPEN", Status: MaintenanceTaskStatus.ASSIGNED), default)).Items).Status);
    }

    [Fact]
    public async Task TasksAsync_RejectsInvalidDateRange()
    {
        await using var db = CreateDb();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).TasksAsync(new(From: BaseTime.AddDays(1), To: BaseTime), default));
    }

    [Fact]
    public async Task SchedulesAsync_SearchesAndFiltersStatusAssetAndServerSideWeekRange()
    {
        await using var db = CreateDb();
        var facility = Guid.NewGuid();
        var equipment = Guid.NewGuid();
        var matching = AddSchedule(db, "MS-PUMP", "Bảo trì máy bơm", facility, equipment, BaseTime, MaintenanceScheduleStatus.ACTIVE);
        AddSchedule(db, "MS-OLD", "Bảo trì cũ", facility, equipment, BaseTime.AddDays(-8), MaintenanceScheduleStatus.ACTIVE);
        AddSchedule(db, "MS-CANCEL", "Máy bơm hủy", facility, equipment, BaseTime, MaintenanceScheduleStatus.CANCELLED);
        await db.SaveChangesAsync();

        var page = await Service(db).SchedulesAsync(new("pump", facility, equipment, MaintenanceScheduleStatus.ACTIVE, BaseTime.AddDays(-1), BaseTime.AddDays(1)), default);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(matching.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task SchedulesAsync_PaginatesFilteredDatasetAndRejectsInvalidRange()
    {
        await using var db = CreateDb();
        var facility = Guid.NewGuid();
        for (var index = 0; index < 3; index++) AddSchedule(db, $"MS-{index}", "Kiểm tra", facility, null, BaseTime.AddHours(index), MaintenanceScheduleStatus.ACTIVE);
        await db.SaveChangesAsync();
        var service = Service(db);

        var page = await service.SchedulesAsync(new(FacilityId: facility, PageIndex: 2, PageSize: 1), default);
        Assert.Equal(3, page.TotalCount);
        Assert.Single(page.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SchedulesAsync(new(From: BaseTime.AddDays(1), To: BaseTime), default));
    }

    private static MaintenanceTask AddTask(MaintenanceDbContext db, string number, string title, Guid? facilityId, Guid? equipmentId, DateTimeOffset? start, DateTimeOffset? due)
    {
        var task = new MaintenanceTask(number, title, Guid.NewGuid(), BaseTime.AddDays(-1), facilityId: facilityId, equipmentId: equipmentId, plannedStartAt: start, dueAt: due);
        db.MaintenanceTasks.Add(task);
        return task;
    }

    private static MaintenanceSchedule AddSchedule(MaintenanceDbContext db, string code, string title, Guid? facilityId, Guid? equipmentId, DateTimeOffset start, MaintenanceScheduleStatus status)
    {
        var schedule = new MaintenanceSchedule(code, title, start, Guid.NewGuid(), BaseTime.AddDays(-1), facilityId, equipmentId);
        if (status == MaintenanceScheduleStatus.COMPLETED) schedule.Complete(Guid.NewGuid(), BaseTime);
        if (status == MaintenanceScheduleStatus.CANCELLED) schedule.Cancel(Guid.NewGuid(), BaseTime);
        db.MaintenanceSchedules.Add(schedule);
        return schedule;
    }

    private static MaintenanceService Service(MaintenanceDbContext db, params MaintenanceStaffRecord[] staff) => new(db, new AssetSource(), new StaffDirectory(staff));
    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>().UseInMemoryDatabase($"maintenance-filter-{Guid.NewGuid()}").Options);
    private static MaintenanceStaffRecord Staff(string name) => new(Guid.NewGuid(), name.Replace(" ", ".").ToLowerInvariant(), name);

    private sealed class AssetSource : IMaintenanceAssetSource
    {
        public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>([]);
        public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(null);
        public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(null);
        public Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class StaffDirectory(params MaintenanceStaffRecord[] staff) : IMaintenanceStaffDirectory
    {
        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>(staff);
        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId, CancellationToken ct) => Task.FromResult<MaintenanceStaffRecord?>(staff.SingleOrDefault(item => item.UserId == userId));
    }
}
