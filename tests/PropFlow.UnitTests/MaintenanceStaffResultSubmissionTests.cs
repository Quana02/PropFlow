using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceStaffResultSubmissionTests
{
    [Fact]
    public async Task SubmitMyTaskResultAsync_CurrentAssignee_PersistsResultActivityAndCompletesTask()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var now = DateTimeOffset.Parse("2026-10-04T14:30:00+00:00");
        var (task, assignment) = AddInProgressTask(db, staff.UserId, now.AddMinutes(-2));
        await db.SaveChangesAsync();
        var service = Service(db, now, staff);

        var dto = await service.SubmitMyTaskResultAsync(task.Id,
            new("Đã kiểm tra áp suất máy bơm.", "Đã thay van", "Không còn rò rỉ"), staff.UserId, default);

        Assert.Equal(MaintenanceTaskStatus.COMPLETED, dto.Status);
        Assert.Equal("Đã kiểm tra áp suất máy bơm.", dto.LatestResult!.Summary);
        Assert.Equal(1, dto.LatestResult.AttemptNo);
        Assert.Equal(staff.UserId, dto.LatestResult.SubmittedBy);
        Assert.Equal(now, dto.LatestResult.SubmittedAt);
        Assert.Equal(AssignmentStatus.COMPLETED, (await db.MaintenanceAssignments.SingleAsync()).Status);
        Assert.Contains(await db.MaintenanceTaskActivities.ToListAsync(), activity =>
            activity.ActivityType == MaintenanceActivityType.RESULT_SUBMITTED &&
            activity.FromStatus == MaintenanceTaskStatus.IN_PROGRESS &&
            activity.ToStatus == MaintenanceTaskStatus.COMPLETED && activity.PerformedBy == staff.UserId);
    }

    [Fact]
    public async Task SubmitMyTaskResultAsync_RejectsWhitespaceSummaryAndKeepsTaskInProgress()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var (task, _) = AddInProgressTask(db, staff.UserId);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db, DateTimeOffset.UtcNow, staff)
            .SubmitMyTaskResultAsync(task.Id, new("   "), staff.UserId, default));

        Assert.Empty(await db.MaintenanceResults.ToListAsync());
        Assert.Equal(MaintenanceTaskStatus.IN_PROGRESS, (await db.MaintenanceTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task SubmitMyTaskResultAsync_RejectsNonCurrentAndReassignedStaff()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var (task, oldAssignment) = AddInProgressTask(db, staffA.UserId);
        oldAssignment.Reassign(DateTimeOffset.UtcNow);
        var newAssignment = new MaintenanceAssignment(task.Id, staffB.UserId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        newAssignment.Start(DateTimeOffset.UtcNow);
        db.MaintenanceAssignments.Add(newAssignment);
        await db.SaveChangesAsync();
        var service = Service(db, DateTimeOffset.UtcNow, staffA, staffB);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SubmitMyTaskResultAsync(task.Id, new("Xong"), staffA.UserId, default));
        var result = await service.SubmitMyTaskResultAsync(task.Id, new("Xong"), staffB.UserId, default);

        Assert.Equal(MaintenanceTaskStatus.COMPLETED, result.Status);
        Assert.Equal(staffB.UserId, result.LatestResult!.SubmittedBy);
    }

    [Theory]
    [InlineData(MaintenanceTaskStatus.ASSIGNED)]
    [InlineData(MaintenanceTaskStatus.COMPLETED)]
    [InlineData(MaintenanceTaskStatus.CLOSED)]
    [InlineData(MaintenanceTaskStatus.CANCELLED)]
    public async Task SubmitMyTaskResultAsync_RejectsStatusesOtherThanInProgress(MaintenanceTaskStatus targetStatus)
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var (task, assignment) = AddInProgressTask(db, staff.UserId);
        var now = DateTimeOffset.UtcNow;
        if (targetStatus == MaintenanceTaskStatus.ASSIGNED)
        {
            task = new MaintenanceTask("MT-ASSIGNED", "Kiểm tra", Guid.NewGuid(), now);
            assignment = new MaintenanceAssignment(task.Id, staff.UserId, Guid.NewGuid(), now);
            task.MarkAssigned(now);
            db.AddRange(task, assignment);
        }
        else if (targetStatus == MaintenanceTaskStatus.COMPLETED)
        {
            task.Complete(now);
        }
        else if (targetStatus == MaintenanceTaskStatus.CLOSED)
        {
            task.Complete(now);
            task.Close(Guid.NewGuid(), now);
        }
        else
        {
            task.Cancel(now);
        }
        await db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => Service(db, now, staff)
            .SubmitMyTaskResultAsync(task.Id, new("Xong"), staff.UserId, default));
        Assert.Empty(await db.MaintenanceResults.ToListAsync());
    }

    private static (MaintenanceTask Task, MaintenanceAssignment Assignment) AddInProgressTask(MaintenanceDbContext db, Guid staffId, DateTimeOffset? createdAt = null)
    {
        var at = createdAt ?? DateTimeOffset.UtcNow.AddMinutes(-10);
        var task = new MaintenanceTask($"MT-{Guid.NewGuid():N}", "Kiểm tra máy bơm", Guid.NewGuid(), at);
        var assignment = new MaintenanceAssignment(task.Id, staffId, Guid.NewGuid(), at);
        task.MarkAssigned(at); task.Start(at.AddMinutes(1)); assignment.Start(at.AddMinutes(1));
        db.AddRange(task, assignment);
        return (task, assignment);
    }

    private static MaintenanceService Service(MaintenanceDbContext db, DateTimeOffset now, params MaintenanceStaffRecord[] staff) =>
        new(db, new AssetSource(), new StaffDirectory(staff), new FixedTimeProvider(now));

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-staff-result-{Guid.NewGuid()}").Options);
    private static MaintenanceStaffRecord Staff(string name) => new(Guid.NewGuid(), name.Replace(" ", ".").ToLowerInvariant(), name);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
