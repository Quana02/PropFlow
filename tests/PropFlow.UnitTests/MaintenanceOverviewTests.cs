using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceOverviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task OverviewAsync_CountsOnlyActiveFutureSchedulesInCurrentBuildingMonth()
    {
        await using var db = CreateDb();
        db.MaintenanceSchedules.AddRange(
            Schedule("near", Now.AddDays(1)),
            Schedule("later", Now.AddDays(10)),
            Schedule("next-month", new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero)),
            CancelledSchedule("cancelled", Now.AddDays(2)));
        await db.SaveChangesAsync();

        var overview = await Service(db).OverviewAsync(default);

        Assert.Equal(2, overview.UpcomingScheduleCount);
        Assert.Equal("near", overview.NextSchedule!.Title);
        Assert.Equal(3, overview.RecentAndUpcomingItems.Count);
        Assert.All(overview.RecentAndUpcomingItems, item => Assert.Equal(MaintenanceOverviewItemType.SCHEDULE, item.Type));
    }

    [Fact]
    public async Task OverviewAsync_CountsCompletedAndClosedTasksOnceUsingCompletedAt()
    {
        await using var db = CreateDb();
        db.MaintenanceTasks.AddRange(
            CompletedTask("completed", Now.AddDays(-2), close: false),
            CompletedTask("closed", Now.AddDays(-1), close: true),
            CompletedTask("previous-month", new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero), close: false));
        await db.SaveChangesAsync();

        var overview = await Service(db).OverviewAsync(default);

        Assert.Equal(0, overview.UpcomingScheduleCount);
        Assert.Equal(2, overview.CompletedTaskCount);
        Assert.Equal(3, overview.RecentAndUpcomingItems.Count);
        Assert.All(overview.RecentAndUpcomingItems, item => Assert.Equal(MaintenanceOverviewItemType.TASK, item.Type));
    }

    [Fact]
    public async Task OverviewAsync_UsesBuildingTimezoneForMonthBoundary()
    {
        await using var db = CreateDb();
        var boundaryNow = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);
        // Both instants are in October in Asia/Ho_Chi_Minh, despite still being September in UTC.
        db.MaintenanceSchedules.Add(Schedule("local-october", boundaryNow.AddHours(1)));
        await db.SaveChangesAsync();

        var overview = await Service(db, boundaryNow).OverviewAsync(default);

        Assert.Equal(1, overview.UpcomingScheduleCount);
    }

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-overview-{Guid.NewGuid()}").Options);

    private static MaintenanceService Service(MaintenanceDbContext db, DateTimeOffset? now = null) => new(
        db, new Assets(), new StaffDirectory(), new Notifier(), new FixedClock(now ?? Now), buildingTimeZone: new BuildingTimeZone());

    private static MaintenanceSchedule Schedule(string title, DateTimeOffset start) =>
        new($"MS-{Guid.NewGuid():N}", title, start, Guid.NewGuid(), Now, description: "Kiểm tra định kỳ");

    private static MaintenanceSchedule CancelledSchedule(string title, DateTimeOffset start)
    {
        var schedule = Schedule(title, start);
        schedule.Cancel(Guid.NewGuid(), Now);
        return schedule;
    }

    private static MaintenanceTask CompletedTask(string title, DateTimeOffset completedAt, bool close)
    {
        var task = new MaintenanceTask($"MT-{Guid.NewGuid():N}", title, Guid.NewGuid(), Now, description: "Thực hiện", priorityCode: "HIGH");
        task.MarkAssigned(completedAt.AddHours(-2));
        task.Start(completedAt.AddHours(-1));
        task.Complete(completedAt);
        if (close) task.Close(Guid.NewGuid(), completedAt.AddMinutes(30));
        return task;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class BuildingTimeZone : ICurrentBuildingTimeZone { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh"); }
    private sealed class Assets : IMaintenanceAssetSource
    {
        public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>([]);
        public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(null);
        public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(null);
        public Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct) => Task.FromResult(false);
    }
    private sealed class StaffDirectory : IMaintenanceStaffDirectory
    {
        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>([]);
        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId, CancellationToken ct) => Task.FromResult<MaintenanceStaffRecord?>(null);
    }
    private sealed class Notifier : IMaintenanceNotifier
    {
        public Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default) => Task.CompletedTask;
    }
}
