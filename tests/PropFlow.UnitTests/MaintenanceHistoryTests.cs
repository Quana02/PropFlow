using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceResults;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceHistoryTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HistoryAsync_FiltersSearchStatusAssetHistoricalStaffAndPaginatesBeforeMaterialization()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var facility = Guid.NewGuid();
        var equipment = Guid.NewGuid();
        var matching = AddTask(db, "MT-HISTORY-1", "Bảo trì máy bơm", facility, equipment);
        matching.MarkAssigned(At.AddMinutes(1));
        var first = new MaintenanceAssignment(matching.Id, staffA.UserId, Guid.NewGuid(), At.AddMinutes(1));
        first.Reassign(At.AddMinutes(2));
        var second = new MaintenanceAssignment(matching.Id, staffB.UserId, Guid.NewGuid(), At.AddMinutes(2));
        db.AddRange(first, second, new MaintenanceTaskActivity(matching.Id, MaintenanceActivityType.REASSIGNED, At.AddMinutes(3), second.Id, MaintenanceTaskStatus.ASSIGNED, MaintenanceTaskStatus.ASSIGNED));
        AddTask(db, "MT-HISTORY-2", "Bảo trì máy bơm", facility, equipment);
        AddTask(db, "MT-OTHER", "Kiểm tra đèn", Guid.NewGuid(), null);
        await db.SaveChangesAsync();
        var service = Service(db, staffA, staffB);

        var filtered = await service.HistoryAsync(new("bơm", MaintenanceTaskStatus.ASSIGNED, facility, equipment, staffA.UserId, At, At.AddHours(1), MaintenanceHistorySort.NEWEST, 1, 20), default);
        var paged = await service.HistoryAsync(new(FacilityId: facility, PageIndex: 2, PageSize: 1), default);

        Assert.Equal(matching.Id, Assert.Single(filtered.Page.Items).TaskId);
        Assert.Contains(filtered.Page.Items[0].Participants, participant => participant.UserId == staffA.UserId);
        Assert.Contains(filtered.Page.Items[0].Participants, participant => participant.UserId == staffB.UserId);
        Assert.Equal(2, paged.Summary.Total);
        Assert.Equal(2, paged.Page.TotalCount);
        Assert.Single(paged.Page.Items);
    }

    [Fact]
    public async Task HistoryAsync_SortsHistoryTimestampAndHandlesNoMatchAndInvalidRange()
    {
        await using var db = CreateDb();
        var older = AddTask(db, "MT-OLD", "Cũ", null, null);
        var newer = AddTask(db, "MT-NEW", "Mới", null, null);
        db.AddRange(new MaintenanceTaskActivity(older.Id, MaintenanceActivityType.ASSIGNED, At.AddMinutes(1)), new MaintenanceTaskActivity(newer.Id, MaintenanceActivityType.ASSIGNED, At.AddMinutes(10)));
        await db.SaveChangesAsync();
        var service = Service(db);

        var newest = await service.HistoryAsync(new(Sort: MaintenanceHistorySort.NEWEST), default);
        var oldest = await service.HistoryAsync(new(Sort: MaintenanceHistorySort.OLDEST), default);
        var noMatch = await service.HistoryAsync(new(SearchKeyword: "không-tồn-tại"), default);

        Assert.Equal(newer.Id, newest.Page.Items[0].TaskId);
        Assert.Equal(older.Id, oldest.Page.Items[0].TaskId);
        Assert.Empty(noMatch.Page.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => service.HistoryAsync(new(From: At.AddDays(1), To: At), default));
    }

    [Fact]
    public async Task HistoryAsync_NpgsqlProvider_DoesNotFailQueryTranslation()
    {
        await using var db = new MaintenanceDbContext(new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=propflow_history_translation;Username=test;Password=test;Timeout=1")
            .Options);

        var exception = await Record.ExceptionAsync(() => Service(db).HistoryAsync(new(), default));

        Assert.False(exception is InvalidOperationException { Message: var message }
            && message.Contains("could not be translated", StringComparison.OrdinalIgnoreCase),
            exception?.ToString());
    }

    [Fact]
    public async Task HistoryAsync_ProjectsPersistedPriorityAssetAndActualExecutionTimes()
    {
        await using var db = CreateDb();
        var facilityId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var plannedStart = At.AddHours(1);
        var dueAt = At.AddHours(4);
        var task = new MaintenanceTask("MT-READ-MODEL", "Kiểm tra thực tế", Guid.NewGuid(), At,
            facilityId: facilityId, equipmentId: equipmentId, priorityCode: "high", plannedStartAt: plannedStart, dueAt: dueAt);
        task.MarkAssigned(At.AddMinutes(5));
        task.Start(At.AddHours(2));
        task.Complete(At.AddHours(3));
        db.MaintenanceTasks.Add(task);
        await db.SaveChangesAsync();

        var item = Assert.Single((await Service(db).HistoryAsync(new(), default)).Page.Items);

        Assert.Equal("HIGH", item.PriorityCode);
        Assert.Equal(facilityId, item.FacilityId);
        Assert.Equal(equipmentId, item.EquipmentId);
        Assert.Equal(plannedStart, item.PlannedStartAt);
        Assert.Equal(dueAt, item.DueAt);
        Assert.Equal(At.AddHours(2), item.StartedAt);
        Assert.Equal(At.AddHours(3), item.CompletedAt);
        Assert.NotEqual(item.DueAt, item.CompletedAt);
    }

    [Fact]
    public async Task HistoryDetailAsync_PreservesSequentialAssignmentsResultAndChronologicalReviewReopenTimeline()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var task = AddTask(db, "MT-TRACE", "Truy vết", Guid.NewGuid(), Guid.NewGuid());
        task.MarkAssigned(At.AddMinutes(1));
        task.Start(At.AddMinutes(2));
        task.Complete(At.AddMinutes(3));
        task.ReopenForFurtherWork(At.AddMinutes(5));
        var assignmentA = new MaintenanceAssignment(task.Id, staffA.UserId, Guid.NewGuid(), At.AddMinutes(1));
        assignmentA.Reassign(At.AddMinutes(4));
        var assignmentB = new MaintenanceAssignment(task.Id, staffB.UserId, Guid.NewGuid(), At.AddMinutes(5));
        var result = new MaintenanceResult(task.Id, 1, staffA.UserId, "Đã xử lý lần đầu", At.AddMinutes(3));
        result.RequestRevision(Guid.NewGuid(), At.AddMinutes(5), "Cần xử lý thêm");
        db.AddRange(assignmentA, assignmentB, result,
            new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.ASSIGNED, At.AddMinutes(1), assignmentA.Id, MaintenanceTaskStatus.OPEN, MaintenanceTaskStatus.ASSIGNED),
            new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.COMPLETED, At.AddMinutes(3), assignmentA.Id, MaintenanceTaskStatus.IN_PROGRESS, MaintenanceTaskStatus.COMPLETED),
            new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.MANAGER_REVIEWED, At.AddMinutes(5), fromStatus: MaintenanceTaskStatus.COMPLETED, toStatus: MaintenanceTaskStatus.ASSIGNED, detail: "Cần xử lý thêm"),
            new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.REASSIGNED, At.AddMinutes(6), assignmentB.Id, MaintenanceTaskStatus.ASSIGNED, MaintenanceTaskStatus.ASSIGNED));
        await db.SaveChangesAsync();

        var detail = await Service(db, staffA, staffB).HistoryDetailAsync(task.Id, default);

        Assert.Equal(2, detail.Assignments.Count);
        Assert.Single(detail.Results);
        Assert.Equal(MaintenanceResultStatus.REVISION_REQUIRED, detail.Results[0].ResultStatus);
        Assert.Contains(detail.Timeline, entry => entry.ActivityType == MaintenanceActivityType.MANAGER_REVIEWED && entry.ToStatus == MaintenanceTaskStatus.ASSIGNED);
        Assert.All(detail.Timeline.Zip(detail.Timeline.Skip(1)), pair => Assert.True(pair.First.OccurredAt <= pair.Second.OccurredAt));
    }

    private static MaintenanceTask AddTask(MaintenanceDbContext db, string number, string title, Guid? facilityId, Guid? equipmentId)
    {
        var task = new MaintenanceTask(number, title, Guid.NewGuid(), At, facilityId: facilityId, equipmentId: equipmentId);
        db.MaintenanceTasks.Add(task);
        return task;
    }

    private static MaintenanceService Service(MaintenanceDbContext db, params MaintenanceStaffRecord[] staff) => new(db, new AssetSource(), new StaffDirectory(staff));
    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>().UseInMemoryDatabase($"maintenance-history-{Guid.NewGuid()}").Options);
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
