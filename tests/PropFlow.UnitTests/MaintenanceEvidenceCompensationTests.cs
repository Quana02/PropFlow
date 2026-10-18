using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class MaintenanceEvidenceCompensationTests
{
    [Fact]
    public async Task SubmitEvidence_WhenDatabaseWriteFails_DeletesUploadedCloudObject()
    {
        var storage = new EvidenceStorage();
        await using var db = CreateDb(throwOnSave: true);
        var staff = new MaintenanceStaffRecord(Guid.NewGuid(), "staff", "Nhân viên");
        var task = AddTask(db, staff.UserId);
        await db.Database.EnsureCreatedAsync();
        await db.SaveChangesAsync();
        var service = Service(db, staff, storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitMyTaskResultWithEvidenceAsync(
            task.Id, new("Đã hoàn thành"), staff.UserId, [Upload()], default));

        Assert.Single(storage.StoredKeys);
        Assert.Equal(storage.StoredKeys, storage.DeletedKeys);
    }

    [Fact]
    public async Task SubmitEvidence_WhenCleanupFails_PreservesDatabaseFailure()
    {
        var storage = new EvidenceStorage { ThrowOnDelete = true };
        await using var db = CreateDb(throwOnSave: true);
        var staff = new MaintenanceStaffRecord(Guid.NewGuid(), "staff", "Nhân viên");
        var task = AddTask(db, staff.UserId);
        await db.Database.EnsureCreatedAsync();
        await db.SaveChangesAsync();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, staff, storage)
            .SubmitMyTaskResultWithEvidenceAsync(task.Id, new("Đã hoàn thành"), staff.UserId, [Upload()], default));

        Assert.Equal("Simulated database failure.", failure.Message);
        Assert.Single(storage.DeletedKeys);
    }

    private static MaintenanceEvidenceUpload Upload() => new("evidence.png", "image/png", 8,
        _ => Task.FromResult<Stream>(new MemoryStream([137, 80, 78, 71, 13, 10, 26, 10])));

    private static MaintenanceService Service(MaintenanceDbContext db, MaintenanceStaffRecord staff, IMaintenanceEvidenceStorage storage) =>
        new(db, new Assets(), new Staff(staff), new Notifier(), evidenceStorage: storage);

    private static MaintenanceTask AddTask(MaintenanceDbContext db, Guid staffId)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new MaintenanceTask("MT-COMPENSATE", "Kiểm tra", Guid.NewGuid(), now);
        var assignment = new MaintenanceAssignment(task.Id, staffId, Guid.NewGuid(), now);
        task.MarkAssigned(now); task.Start(now); assignment.Start(now);
        db.AddRange(task, assignment);
        return task;
    }

    private static MaintenanceDbContext CreateDb(bool throwOnSave)
    {
        var options = new DbContextOptionsBuilder<MaintenanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        if (throwOnSave) options.AddInterceptors(new ThrowOnResultSaveInterceptor());
        return new(options.Options);
    }

    private sealed class ThrowOnResultSaveInterceptor : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
            eventData.Context!.ChangeTracker.Entries().Any(entry => entry.Entity.GetType().Name == "MaintenanceResult")
                ? throw new InvalidOperationException("Simulated database failure.") : result;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default) =>
            eventData.Context!.ChangeTracker.Entries().Any(entry => entry.Entity.GetType().Name == "MaintenanceResult")
                ? ValueTask.FromException<InterceptionResult<int>>(new InvalidOperationException("Simulated database failure.")) : ValueTask.FromResult(result);
    }
    private sealed class EvidenceStorage : IMaintenanceEvidenceStorage
    {
        public bool ThrowOnDelete { get; init; }
        public List<string> StoredKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public Task<StoredMaintenanceEvidence> StoreAsync(Guid resultId, MaintenanceEvidenceUpload upload, CancellationToken ct)
        {
            var key = $"propflow/maintenance/evidence/{resultId:N}/{Guid.NewGuid():N}"; StoredKeys.Add(key);
            return Task.FromResult(new StoredMaintenanceEvidence(key, upload.OriginalFileName, "image/png", upload.Length));
        }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken ct) { DeletedKeys.Add(storageKey); if (ThrowOnDelete) throw new InvalidOperationException("delete"); return Task.CompletedTask; }
    }
    private sealed class Assets : IMaintenanceAssetSource
    {
        public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>([]);
        public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid id, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(null);
        public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid id, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(null);
        public Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct) => Task.FromResult(false);
    }
    private sealed class Staff(MaintenanceStaffRecord value) : IMaintenanceStaffDirectory
    {
        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>([value]);
        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid id, CancellationToken ct) => Task.FromResult<MaintenanceStaffRecord?>(id == value.UserId ? value : null);
    }
    private sealed class Notifier : IMaintenanceNotifier
    {
        public Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientUserIds, CancellationToken ct) => Task.CompletedTask;
    }
}
