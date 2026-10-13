using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceStaffWorkViewTests
{
    [Fact]
    public void IsTaskOverdue_UsesUtcInstantAndExcludesTerminalStatuses()
    {
        var due = new DateTimeOffset(2026, 10, 4, 20, 3, 0, TimeSpan.FromHours(7));
        var active = new MaintenanceTask("MT-DUE", "Kiểm tra", Guid.NewGuid(), due.AddHours(-1), dueAt: due);
        Assert.False(MaintenanceService.IsTaskOverdue(active, due.AddMinutes(-18)));
        Assert.False(MaintenanceService.IsTaskOverdue(active, due));
        Assert.True(MaintenanceService.IsTaskOverdue(active, due.AddMinutes(1)));

        active.MarkAssigned(due.AddHours(-1)); active.Start(due.AddMinutes(-30)); active.Complete(due.AddMinutes(-1));
        Assert.False(MaintenanceService.IsTaskOverdue(active, due.AddMinutes(1)));
        active.Close(Guid.NewGuid(), due);
        Assert.False(MaintenanceService.IsTaskOverdue(active, due.AddMinutes(1)));

        var cancelled = new MaintenanceTask("MT-CANCELLED", "Kiểm tra", Guid.NewGuid(), due.AddHours(-1), dueAt: due);
        cancelled.Cancel(due.AddMinutes(-1));
        Assert.False(MaintenanceService.IsTaskOverdue(cancelled, due.AddMinutes(1)));
    }
    [Fact]
    public async Task MyTasksAsync_ReturnsOnlyCurrentActiveAssignmentsForAuthenticatedStaff()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var direct = AddTask(db, "MT-DIRECT", "Kiểm tra máy bơm");
        var other = AddTask(db, "MT-OTHER", "Kiểm tra thang máy");
        Assign(db, direct, staffA.UserId);
        Assign(db, other, staffB.UserId);
        await db.SaveChangesAsync();
        var service = Service(db, staffA, staffB);

        var page = await service.MyTasksAsync(new(), staffA.UserId, default);

        Assert.Equal(direct.Id, Assert.Single(page.Items).Id);
        Assert.Equal(staffA.UserId, page.Items[0].AssignedStaffUserId);
    }

    [Fact]
    public async Task MyTasksAsync_ReassignmentRemovesHistoricalAssigneeAndKeepsNewAssignee()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var task = AddTask(db, "MT-REASSIGN", "Bảo trì máy phát điện");
        var old = new MaintenanceAssignment(task.Id, staffA.UserId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-2));
        old.Reassign(DateTimeOffset.UtcNow.AddMinutes(-1));
        db.MaintenanceAssignments.Add(old);
        Assign(db, task, staffB.UserId);
        await db.SaveChangesAsync();
        var service = Service(db, staffA, staffB);

        Assert.Empty((await service.MyTasksAsync(new(), staffA.UserId, default)).Items);
        Assert.Equal(task.Id, Assert.Single((await service.MyTasksAsync(new(), staffB.UserId, default)).Items).Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MyTaskAsync(task.Id, staffA.UserId, default));
        Assert.Equal(task.Id, (await service.MyTaskAsync(task.Id, staffB.UserId, default)).Id);
    }

    [Fact]
    public async Task MyTasksAsync_IncludesDirectAndScheduleTasksAndFiltersBeforePagination()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var schedule = new MaintenanceSchedule("LBT-01", "Lịch máy bơm", DateTimeOffset.UtcNow, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.MaintenanceSchedules.Add(schedule);
        var direct = AddTask(db, "MT-DIRECT", "Công việc trực tiếp", priority: "HIGH");
        var scheduled = AddTask(db, "MT-SCHEDULE", "Theo lịch", schedule.Id, "HIGH");
        Assign(db, direct, staff.UserId);
        Assign(db, scheduled, staff.UserId);
        await db.SaveChangesAsync();
        var service = Service(db, staff);

        var page = await service.MyTasksAsync(new(PriorityCode: "high", PageSize: 1), staff.UserId, default);

        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        var all = await service.MyTasksAsync(new(PriorityCode: "HIGH", PageSize: 10), staff.UserId, default);
        Assert.Contains(all.Items, item => item.Id == direct.Id && item.ScheduleId is null);
        Assert.Contains(all.Items, item => item.Id == scheduled.Id && item.ScheduleId == schedule.Id);
    }

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-staff-work-{Guid.NewGuid()}").Options);

    private static MaintenanceService Service(MaintenanceDbContext db, params MaintenanceStaffRecord[] staff) =>
        new(db, new AssetSource(), new StaffDirectory(staff), TimeProvider.System);

    private static MaintenanceStaffRecord Staff(string name) => new(Guid.NewGuid(), name.Replace(" ", ".").ToLowerInvariant(), name);

    private static MaintenanceTask AddTask(MaintenanceDbContext db, string number, string title, Guid? scheduleId = null, string? priority = null)
    {
        var task = new MaintenanceTask(number, title, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), scheduleId: scheduleId, priorityCode: priority);
        db.MaintenanceTasks.Add(task);
        return task;
    }

    private static void Assign(MaintenanceDbContext db, MaintenanceTask task, Guid staffId) =>
        db.MaintenanceAssignments.Add(new MaintenanceAssignment(task.Id, staffId, Guid.NewGuid(), DateTimeOffset.UtcNow));

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
