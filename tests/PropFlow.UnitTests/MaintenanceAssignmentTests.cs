using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceAssignmentTests
{
    [Fact]
    public async Task AssignTaskAsync_AssignsValidatedStaffAndRecordsServerActor()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var actor = Guid.NewGuid();
        var task = AddTask(db);
        var service = Service(db, staff);

        var result = await service.AssignTaskAsync(task.Id, new(staff.UserId), actor, default);

        Assert.Equal(MaintenanceTaskStatus.ASSIGNED, result.Status);
        Assert.Equal(staff.UserId, result.AssignedStaffUserId);
        Assert.Equal(staff.DisplayName, result.AssignedStaffDisplayName);
        var persisted = await db.MaintenanceAssignments.SingleAsync();
        Assert.Equal(staff.UserId, persisted.StaffUserId);
        Assert.Equal(actor, persisted.AssignedBy);
        Assert.Equal(AssignmentStatus.ASSIGNED, persisted.Status);
    }

    [Fact]
    public async Task AssignTaskAsync_ReassignsWithoutRemovingPriorAssignment()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var task = AddTask(db);
        var service = Service(db, staffA, staffB);

        await service.AssignTaskAsync(task.Id, new(staffA.UserId), Guid.NewGuid(), default);
        var result = await service.AssignTaskAsync(task.Id, new(staffB.UserId), Guid.NewGuid(), default);
        db.ChangeTracker.Clear();

        var assignments = await db.MaintenanceAssignments.OrderBy(x => x.AssignedAt).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Equal(AssignmentStatus.REASSIGNED, assignments[0].Status);
        Assert.NotNull(assignments[0].EndedAt);
        Assert.Equal(AssignmentStatus.ASSIGNED, assignments[1].Status);
        Assert.Equal(staffB.UserId, result.AssignedStaffUserId);
        var detail = await service.TaskAsync(task.Id, default);
        Assert.Equal(staffB.UserId, detail.AssignedStaffUserId);
    }

    [Fact]
    public async Task AssignTaskAsync_RejectsSameStaffAndInvalidStaff()
    {
        await using var db = CreateDb();
        var staff = Staff("Nhân viên A");
        var task = AddTask(db);
        var service = Service(db, staff);

        await service.AssignTaskAsync(task.Id, new(staff.UserId), Guid.NewGuid(), default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AssignTaskAsync(task.Id, new(staff.UserId), Guid.NewGuid(), default));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AssignTaskAsync(task.Id, new(Guid.NewGuid()), Guid.NewGuid(), default));
        Assert.Single(await db.MaintenanceAssignments.ToListAsync());
    }

    [Fact]
    public async Task TasksAsync_ReturnsCurrentAssignmentOnly()
    {
        await using var db = CreateDb();
        var staffA = Staff("Nhân viên A");
        var staffB = Staff("Nhân viên B");
        var task = AddTask(db);
        var service = Service(db, staffA, staffB);

        await service.AssignTaskAsync(task.Id, new(staffA.UserId), Guid.NewGuid(), default);
        await service.AssignTaskAsync(task.Id, new(staffB.UserId), Guid.NewGuid(), default);

        var page = await service.TasksAsync(new(), default);
        var item = Assert.Single(page.Items);
        Assert.Equal(staffB.UserId, item.AssignedStaffUserId);
        Assert.Equal(staffB.DisplayName, item.AssignedStaffDisplayName);
    }

    private static MaintenanceService Service(MaintenanceDbContext db, params MaintenanceStaffRecord[] staff) =>
        new(db, new AssetSource(), new StaffDirectory(staff));

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-assignment-{Guid.NewGuid()}").Options);

    private static MaintenanceTask AddTask(MaintenanceDbContext db)
    {
        var task = new MaintenanceTask($"MT-{Guid.NewGuid():N}", "Kiểm tra", Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.MaintenanceTasks.Add(task);
        db.SaveChanges();
        return task;
    }

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
