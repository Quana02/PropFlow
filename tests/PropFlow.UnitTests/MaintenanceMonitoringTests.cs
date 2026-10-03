using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceMonitoringTests
{
    [Fact]
    public async Task TaskAsync_ReturnsAssignmentAndEmptyProgressMonitoringTruthfully()
    {
        await using var db = CreateDb();
        var staff = new MaintenanceStaffRecord(Guid.NewGuid(), "staff.a", "Nhân viên A");
        var task = AddTask(db, DateTimeOffset.UtcNow.AddHours(1));
        var service = Service(db, staff);

        await service.AssignTaskAsync(task.Id, new(staff.UserId), Guid.NewGuid(), default);
        var detail = await service.TaskAsync(task.Id, default);

        Assert.Equal(MaintenanceTaskStatus.ASSIGNED, detail.Status);
        Assert.Equal(staff.UserId, detail.AssignedStaffUserId);
        Assert.NotNull(detail.AssignedAt);
        Assert.NotNull(detail.LastActivityAt);
        Assert.Equal(MaintenanceActivityType.ASSIGNED, detail.LastActivityType);
        Assert.Null(detail.LastProgressAt);
        Assert.False(detail.IsOverdue);
    }

    [Fact]
    public async Task TaskAsync_ReturnsLatestProgressActivityWithoutLoadingHistoryIntoList()
    {
        await using var db = CreateDb();
        var task = AddTask(db, DateTimeOffset.UtcNow.AddHours(1));
        var first = DateTimeOffset.UtcNow.AddMinutes(-10);
        var latest = DateTimeOffset.UtcNow.AddMinutes(-2);
        db.MaintenanceTaskActivities.AddRange(
            new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.PROGRESS_UPDATED, first),
            new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.WORK_LOG_ADDED, latest));
        await db.SaveChangesAsync();
        var service = Service(db);

        var detail = await service.TaskAsync(task.Id, default);
        var list = await service.TasksAsync(new(), default);

        Assert.Equal(latest, detail.LastProgressAt);
        Assert.Equal(MaintenanceActivityType.WORK_LOG_ADDED, detail.LastProgressActivityType);
        Assert.Null(Assert.Single(list.Items).LastProgressAt);
    }

    [Fact]
    public async Task Monitoring_DerivesOverdueOnlyForNonTerminalTasksWithPastDueAt()
    {
        await using var db = CreateDb();
        var overdue = AddTask(db, DateTimeOffset.UtcNow.AddMinutes(-1));
        var noDue = AddTask(db, null);
        var completed = AddTask(db, DateTimeOffset.UtcNow.AddMinutes(-1));
        completed.MarkAssigned(DateTimeOffset.UtcNow.AddHours(-3));
        completed.Start(DateTimeOffset.UtcNow.AddHours(-2));
        completed.Complete(DateTimeOffset.UtcNow.AddHours(-1));
        await db.SaveChangesAsync();
        var service = Service(db);

        var page = await service.TasksAsync(new(), default);

        Assert.True(page.Items.Single(item => item.Id == overdue.Id).IsOverdue);
        Assert.False(page.Items.Single(item => item.Id == noDue.Id).IsOverdue);
        Assert.False(page.Items.Single(item => item.Id == completed.Id).IsOverdue);
    }

    private static MaintenanceService Service(MaintenanceDbContext db, params MaintenanceStaffRecord[] staff) =>
        new(db, new AssetSource(), new StaffDirectory(staff));

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-monitoring-{Guid.NewGuid()}").Options);

    private static MaintenanceTask AddTask(MaintenanceDbContext db, DateTimeOffset? dueAt)
    {
        var task = new MaintenanceTask($"MT-{Guid.NewGuid():N}", "Kiểm tra", Guid.NewGuid(), DateTimeOffset.UtcNow,
            plannedStartAt: dueAt?.AddHours(-1), dueAt: dueAt);
        db.MaintenanceTasks.Add(task);
        db.SaveChanges();
        return task;
    }

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
