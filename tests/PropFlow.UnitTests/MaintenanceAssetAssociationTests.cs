using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceSchedules;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceAssetAssociationTests
{
    [Fact]
    public async Task AssociateTaskAssetAsync_PersistsFacilityOrEquipmentAndAcceptsBuildingLevelEquipment()
    {
        await using var db = CreateDb();
        var facility = Asset("FACILITY");
        var buildingEquipment = Asset("EQUIPMENT");
        var service = new MaintenanceService(db, new AssetSource(facility, buildingEquipment), new StaffDirectory());
        var task = AddTask(db);

        var facilityResult = await service.AssociateTaskAssetAsync(task.Id, new(facility.Id, null), CancellationToken.None);
        Assert.Equal(facility.Id, facilityResult.FacilityId);
        Assert.Null(facilityResult.EquipmentId);

        var equipmentResult = await service.AssociateTaskAssetAsync(task.Id, new(null, buildingEquipment.Id), CancellationToken.None);
        Assert.Null(equipmentResult.FacilityId);
        Assert.Equal(buildingEquipment.Id, equipmentResult.EquipmentId);

        var persisted = await db.MaintenanceTasks.SingleAsync();
        Assert.Null(persisted.FacilityId);
        Assert.Equal(buildingEquipment.Id, persisted.EquipmentId);
        Assert.Empty(persisted.Assignments);
    }

    [Fact]
    public async Task AssociateTaskAssetAsync_RejectsMissingAssetsAndMismatchedFacilityEquipment()
    {
        await using var db = CreateDb();
        var facility = Asset("FACILITY");
        var otherFacilityId = Guid.NewGuid();
        var equipment = Asset("EQUIPMENT", otherFacilityId);
        var service = new MaintenanceService(db, new AssetSource(facility, equipment), new StaffDirectory());
        var task = AddTask(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AssociateTaskAssetAsync(task.Id, new(Guid.NewGuid(), null), CancellationToken.None));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AssociateTaskAssetAsync(task.Id, new(null, Guid.NewGuid()), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AssociateTaskAssetAsync(task.Id, new(facility.Id, equipment.Id), CancellationToken.None));
    }

    [Fact]
    public async Task CreateTaskAsync_RemainsValidWithoutAssetAssociation()
    {
        await using var db = CreateDb();
        var service = new MaintenanceService(db, new AssetSource(), new StaffDirectory());

        var result = await service.CreateTaskAsync(new("MT-ASSOC-001", "Kiểm tra bơm"), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result.FacilityId);
        Assert.Null(result.EquipmentId);
        Assert.Equal(MaintenanceTaskStatus.OPEN, result.Status);
        Assert.Empty((await db.MaintenanceAssignments.ToListAsync()));
    }

    [Fact]
    public async Task CreateTaskAsync_FromSchedule_InheritsAssetAndTimeAndIgnoresClientOverrides()
    {
        await using var db = CreateDb();
        var facility = Asset("FACILITY");
        var equipment = Asset("EQUIPMENT", facility.Id);
        var start = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        var end = start.AddHours(2);
        var schedule = AddSchedule(db, facility.Id, equipment.Id, start, end);
        var service = new MaintenanceService(db, new AssetSource(facility, equipment), new StaffDirectory());

        var result = await service.CreateTaskAsync(new("MT-INHERIT-001", "Bảo trì theo lịch", schedule.Id, PlannedStartAt: start.AddDays(3), DueAt: end.AddDays(3), FacilityId: Guid.NewGuid(), EquipmentId: Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(schedule.Id, result.ScheduleId);
        Assert.Equal(facility.Id, result.FacilityId);
        Assert.Equal(equipment.Id, result.EquipmentId);
        Assert.Equal(start, result.PlannedStartAt);
        Assert.Equal(end, result.DueAt);
        Assert.Equal(MaintenanceTaskStatus.OPEN, result.Status);
    }

    [Fact]
    public async Task CreateTaskAsync_FromFacilityOnlySchedule_InheritsOnlyFacility()
    {
        await using var db = CreateDb();
        var facility = Asset("FACILITY");
        var schedule = AddSchedule(db, facility.Id, null);
        var service = new MaintenanceService(db, new AssetSource(facility), new StaffDirectory());

        var result = await service.CreateTaskAsync(new("MT-INHERIT-002", "Bảo trì khu vực", schedule.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(facility.Id, result.FacilityId);
        Assert.Null(result.EquipmentId);
    }

    [Fact]
    public async Task CreateTaskAsync_FromAssetlessSchedule_RemainsAssetlessUntilFe073Association()
    {
        await using var db = CreateDb();
        var schedule = AddSchedule(db, null, null);
        var service = new MaintenanceService(db, new AssetSource(), new StaffDirectory());

        var result = await service.CreateTaskAsync(new("MT-INHERIT-EMPTY", "Bảo trì", schedule.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(schedule.Id, result.ScheduleId);
        Assert.Null(result.FacilityId);
        Assert.Null(result.EquipmentId);
    }

    [Fact]
    public async Task CreateTaskAsync_ManualTaskUsesCommandTimeAndScheduleChangesDoNotSyncExistingTask()
    {
        await using var db = CreateDb();
        var service = new MaintenanceService(db, new AssetSource(), new StaffDirectory());
        var manualStart = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.Zero);
        var manualDue = manualStart.AddHours(3);

        var manual = await service.CreateTaskAsync(new("MT-MANUAL-TIME", "Kiểm tra", PlannedStartAt: manualStart, DueAt: manualDue), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(manualStart, manual.PlannedStartAt);
        Assert.Equal(manualDue, manual.DueAt);

        var scheduleStart = manualStart.AddDays(1);
        var schedule = AddSchedule(db, null, null, scheduleStart, scheduleStart.AddHours(1));
        var scheduled = await service.CreateTaskAsync(new("MT-SNAPSHOT-TIME", "Theo lịch", schedule.Id), Guid.NewGuid(), CancellationToken.None);
        schedule.UpdatePlan("Lịch đã đổi", scheduleStart.AddHours(2), Guid.NewGuid(), DateTimeOffset.UtcNow, plannedEndAt: scheduleStart.AddHours(4));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var persisted = await db.MaintenanceTasks.SingleAsync(task => task.Id == scheduled.Id);
        Assert.Equal(scheduleStart, persisted.PlannedStartAt);
        Assert.Equal(scheduleStart.AddHours(1), persisted.DueAt);
    }

    [Fact]
    public async Task CreateTaskAsync_RejectsScheduleWithInconsistentAssetContext()
    {
        await using var db = CreateDb();
        var facility = Asset("FACILITY");
        var equipment = Asset("EQUIPMENT", Guid.NewGuid());
        var schedule = AddSchedule(db, facility.Id, equipment.Id);
        var service = new MaintenanceService(db, new AssetSource(facility, equipment), new StaffDirectory());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateTaskAsync(new("MT-INHERIT-003", "Bảo trì", schedule.Id), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ActivateDueSchedulesAsync_SkipsInconsistentScheduleAndContinuesWithValidSchedule()
    {
        await using var db = CreateDb();
        var facility = Asset("FACILITY");
        var mismatchedEquipment = Asset("EQUIPMENT", Guid.NewGuid());
        var validEquipment = Asset("EQUIPMENT", facility.Id);
        AddSchedule(db, facility.Id, mismatchedEquipment.Id, DateTimeOffset.UtcNow.AddMinutes(-2));
        AddSchedule(db, facility.Id, validEquipment.Id, DateTimeOffset.UtcNow.AddMinutes(-1));
        var source = new ActivatingAssetSource(facility, mismatchedEquipment, validEquipment);
        var service = new MaintenanceService(db, source, new StaffDirectory());

        await service.ActivateDueSchedulesAsync(CancellationToken.None);

        Assert.Contains(validEquipment.Id, source.ActivatedScheduleEquipmentIds);
    }

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-association-{Guid.NewGuid()}")
        .Options);

    private static MaintenanceTask AddTask(MaintenanceDbContext db)
    {
        var task = new MaintenanceTask($"MT-{Guid.NewGuid():N}", "Kiểm tra", Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.MaintenanceTasks.Add(task);
        db.SaveChanges();
        return task;
    }

    private static MaintenanceSchedule AddSchedule(MaintenanceDbContext db, Guid? facilityId, Guid? equipmentId, DateTimeOffset? plannedStartAt = null, DateTimeOffset? plannedEndAt = null)
    {
        var start = plannedStartAt ?? DateTimeOffset.UtcNow.AddDays(1);
        var schedule = new MaintenanceSchedule($"MS-{Guid.NewGuid():N}", "Lịch kiểm thử", start, Guid.NewGuid(), DateTimeOffset.UtcNow, facilityId, equipmentId, plannedEndAt: plannedEndAt);
        db.MaintenanceSchedules.Add(schedule);
        db.SaveChanges();
        return schedule;
    }

    private static MaintenanceAssetReference Asset(string type, Guid? facilityId = null) =>
        new(Guid.NewGuid(), $"{type}-001", "Tài sản kiểm thử", type, "ACTIVE", null, facilityId);

    private sealed class AssetSource(params MaintenanceAssetReference[] assets) : IMaintenanceAssetSource
    {
        public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>(assets);
        public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct) => Task.FromResult(assets.SingleOrDefault(x => x.AssetType == "FACILITY" && x.Id == facilityId));
        public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct) => Task.FromResult(assets.SingleOrDefault(x => x.AssetType == "EQUIPMENT" && x.Id == equipmentId));
        public Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class StaffDirectory : IMaintenanceStaffDirectory
    {
        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>([]);
        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId, CancellationToken ct) => Task.FromResult<MaintenanceStaffRecord?>(null);
    }

    private sealed class ActivatingAssetSource(params MaintenanceAssetReference[] assets) : IMaintenanceAssetSource
    {
        public List<Guid> ActivatedScheduleEquipmentIds { get; } = [];
        public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>(assets);
        public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct) => Task.FromResult(assets.SingleOrDefault(x => x.AssetType == "FACILITY" && x.Id == facilityId));
        public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct) => Task.FromResult(assets.SingleOrDefault(x => x.AssetType == "EQUIPMENT" && x.Id == equipmentId));
        public Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct) => Task.FromResult(true);
        public Task MarkUnderMaintenanceAsync(Guid facilityId, Guid? equipmentId, Guid actor, CancellationToken ct)
        {
            var equipment = assets.SingleOrDefault(x => x.Id == equipmentId);
            if (equipment?.FacilityId is not null && equipment.FacilityId != facilityId)
                throw new ArgumentException("Thiết bị không thuộc cơ sở vật chất đã chọn.");
            if (equipmentId is { } id) ActivatedScheduleEquipmentIds.Add(id);
            return Task.CompletedTask;
        }
    }
}
