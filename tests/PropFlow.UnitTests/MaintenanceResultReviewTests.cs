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

public sealed class MaintenanceResultReviewTests
{
    [Fact]
    public async Task ReviewResultAsync_Approve_ClosesCompletedTaskAndRecordsServerReviewer()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var manager = Guid.NewGuid();
        var (task, result, _) = await AddCompletedTaskAsync(db, staff, manager);

        var reviewed = await Service(db, staff).ReviewResultAsync(task.Id, result.Id, new("approve", "Đạt yêu cầu"), manager, default);

        Assert.Equal(MaintenanceTaskStatus.CLOSED, reviewed.Status);
        Assert.Equal(MaintenanceResultStatus.APPROVED, reviewed.LatestResult!.ResultStatus);
        var persistedTask = await db.MaintenanceTasks.SingleAsync();
        var persistedResult = await db.MaintenanceResults.SingleAsync();
        Assert.Equal(manager, persistedTask.ClosedBy);
        Assert.Equal(manager, persistedResult.ReviewedBy);
        Assert.Equal("Đạt yêu cầu", persistedResult.ReviewNote);
        Assert.Contains(await db.MaintenanceTaskActivities.ToListAsync(), activity => activity.ActivityType == MaintenanceActivityType.CLOSED && activity.PerformedBy == manager);
    }

    [Fact]
    public async Task ReviewResultAsync_RequestRevision_ReopensWithoutDeletingResultActivityOrAssignment()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var manager = Guid.NewGuid();
        var (task, result, assignment) = await AddCompletedTaskAsync(db, staff, manager);
        var originalActivity = new MaintenanceTaskActivity(task.Id, MaintenanceActivityType.COMPLETED, DateTimeOffset.UtcNow.AddMinutes(-1), assignment.Id, MaintenanceTaskStatus.IN_PROGRESS, MaintenanceTaskStatus.COMPLETED, performedBy: staff.UserId);
        db.MaintenanceTaskActivities.Add(originalActivity);
        await db.SaveChangesAsync();

        var reviewed = await Service(db, staff).ReviewResultAsync(task.Id, result.Id, new("request-revision", "Cần kiểm tra lại áp lực."), manager, default);
        db.ChangeTracker.Clear();

        Assert.Equal(MaintenanceTaskStatus.ASSIGNED, reviewed.Status);
        Assert.Equal(MaintenanceResultStatus.REVISION_REQUIRED, reviewed.LatestResult!.ResultStatus);
        Assert.Single(await db.MaintenanceResults.Where(item => item.Id == result.Id).ToListAsync());
        Assert.Contains(await db.MaintenanceTaskActivities.ToListAsync(), item => item.Id == originalActivity.Id);
        var assignments = await db.MaintenanceAssignments.ToListAsync();
        Assert.Single(assignments);
        Assert.Equal(assignment.Id, assignments[0].Id);
        Assert.Equal(AssignmentStatus.ASSIGNED, assignments[0].Status);
        Assert.Equal(staff.UserId, reviewed.AssignedStaffUserId);
    }

    [Fact]
    public async Task ReviewResultAsync_RequestRevision_RejectsTaskThatIsNotCompletedOrResultThatDoesNotExist()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var service = Service(db, staff);
        var openTask = new MaintenanceTask("MT-OPEN", "Kiểm tra", Guid.NewGuid(), DateTimeOffset.UtcNow);
        var submitted = new MaintenanceResult(openTask.Id, 1, staff.UserId, "Đã hoàn thành", DateTimeOffset.UtcNow.AddMinutes(-1));
        db.AddRange(openTask, submitted);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewResultAsync(openTask.Id, submitted.Id, new("request-revision"), Guid.NewGuid(), default));
        var (completedTask, _, _) = await AddCompletedTaskAsync(db, staff, Guid.NewGuid());
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReviewResultAsync(completedTask.Id, Guid.NewGuid(), new("approve"), Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ReviewResultAsync_RequestRevisionThenReassign_PreservesTaskAndAssignmentHistory()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var manager = Guid.NewGuid();
        var (task, result, _) = await AddCompletedTaskAsync(db, staffA, manager);
        var service = Service(db, staffA, staffB);

        await service.ReviewResultAsync(task.Id, result.Id, new("request-revision", "Xử lý thêm"), manager, default);
        var reassigned = await service.AssignTaskAsync(task.Id, new(staffB.UserId), manager, default);
        db.ChangeTracker.Clear();

        Assert.Equal(task.Id, reassigned.Id);
        Assert.Equal(staffB.UserId, reassigned.AssignedStaffUserId);
        Assert.Equal(MaintenanceTaskStatus.ASSIGNED, reassigned.Status);
        var assignments = await db.MaintenanceAssignments.OrderBy(item => item.AssignedAt).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Equal(AssignmentStatus.REASSIGNED, assignments[0].Status);
        Assert.Equal(staffB.UserId, assignments[1].StaffUserId);
        Assert.Equal(AssignmentStatus.ASSIGNED, assignments[1].Status);
        Assert.Single(await db.MaintenanceTasks.ToListAsync());
        Assert.Single(await db.MaintenanceResults.ToListAsync());
    }

    [Fact]
    public async Task ReviewResultAsync_ClosedTaskCannotBeReopenedAndAssignmentCannotChange()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var manager = Guid.NewGuid();
        var (task, result, _) = await AddCompletedTaskAsync(db, staffA, manager);
        var service = Service(db, staffA, staffB);

        await service.ReviewResultAsync(task.Id, result.Id, new("approve"), manager, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReviewResultAsync(task.Id, result.Id, new("request-revision"), manager, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignTaskAsync(task.Id, new(staffB.UserId), manager, default));
    }

    private static async Task<(MaintenanceTask Task, MaintenanceResult Result, MaintenanceAssignment Assignment)> AddCompletedTaskAsync(
        MaintenanceDbContext db, MaintenanceStaffRecord staff, Guid manager)
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var task = new MaintenanceTask($"MT-{Guid.NewGuid():N}", "Bảo trì máy bơm", manager, startedAt);
        task.MarkAssigned(startedAt.AddMinutes(1));
        task.Start(startedAt.AddMinutes(2));
        task.Complete(startedAt.AddMinutes(4));
        var assignment = new MaintenanceAssignment(task.Id, staff.UserId, manager, startedAt.AddMinutes(1));
        var result = new MaintenanceResult(task.Id, 1, staff.UserId, "Đã kiểm tra máy bơm", startedAt.AddMinutes(5), "Đã thay phớt", "Còn rung nhẹ", recommendation: "Theo dõi thêm");
        db.AddRange(task, assignment, result);
        await db.SaveChangesAsync();
        return (task, result, assignment);
    }

    private static MaintenanceService Service(MaintenanceDbContext db, params MaintenanceStaffRecord[] staff) =>
        new(db, new AssetSource(), new StaffDirectory(staff));

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-review-{Guid.NewGuid()}").Options);

    private static MaintenanceStaffRecord Staff(string displayName) => new(Guid.NewGuid(), displayName.Replace(" ", ".").ToLowerInvariant(), displayName);

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
