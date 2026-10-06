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

public sealed class MaintenanceStaffHistoryTests
{
    [Fact]
    public async Task MyHistoryDetailAsync_PreservesTwoWorkPassResultsAndReviews()
    {
        await using var db = Db(); var staff = Staff("A"); var manager = Guid.NewGuid(); var task = MakeTask("MT-PASS", "Bơm", DateTimeOffset.UtcNow.AddMinutes(-20)); db.Add(task); await db.SaveChangesAsync(); var service = Service(db, staff);
        await service.AssignTaskAsync(task.Id, new(staff.UserId), manager, default);
        await service.StartMyTaskAsync(task.Id, staff.UserId, default); await service.UpdateMyTaskProgressAsync(task.Id, new("Tiến độ 1"), staff.UserId, default);
        var first = await service.SubmitMyTaskResultAsync(task.Id, new("Hoàn thành kiểm tra lần 1"), staff.UserId, default);
        await service.ReviewResultAsync(task.Id, first.LatestResult!.Id, new("request-revision", "Cần xử lý thêm"), manager, default);
        await service.StartMyTaskAsync(task.Id, staff.UserId, default); await service.UpdateMyTaskProgressAsync(task.Id, new("Tiến độ 2"), staff.UserId, default);
        var second = await service.SubmitMyTaskResultAsync(task.Id, new("Hoàn thành xử lý lần 2"), staff.UserId, default);
        await service.ReviewResultAsync(task.Id, second.LatestResult!.Id, new("approve", "Đạt"), manager, default);
        var history = await service.MyHistoryDetailAsync(task.Id, staff.UserId, default);
        Assert.Equal(MaintenanceTaskStatus.CLOSED, history.Task.Status); Assert.Collection(history.Results, r => { Assert.Equal(1, r.AttemptNo); Assert.Equal("Hoàn thành kiểm tra lần 1", r.Summary); Assert.Equal(MaintenanceResultStatus.REVISION_REQUIRED, r.ResultStatus); Assert.Equal("Cần xử lý thêm", r.ReviewNote); }, r => { Assert.Equal(2, r.AttemptNo); Assert.Equal("Hoàn thành xử lý lần 2", r.Summary); Assert.Equal(MaintenanceResultStatus.APPROVED, r.ResultStatus); });
        Assert.Contains(history.Timeline, e => e.ActivityType == MaintenanceActivityType.RESULT_SUBMITTED); Assert.Contains(history.Timeline, e => e.ActivityType == MaintenanceActivityType.CLOSED); Assert.Single(history.Assignments);
    }
    [Fact]
    public async Task MyHistoryAsync_FormerAndCurrentAssigneesCanRead_ButUnrelatedStaffCannot()
    {
        await using var db = Db(); var a = Staff("A"); var b = Staff("B"); var c = Staff("C");
        var task = MakeTask("MT-01", "Máy bơm", DateTimeOffset.UtcNow); var old = new MaintenanceAssignment(task.Id, a.UserId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-2)); old.Reassign(DateTimeOffset.UtcNow.AddMinutes(-1)); var current = new MaintenanceAssignment(task.Id, b.UserId, Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.AddRange(task, old, current); await db.SaveChangesAsync(); var service = Service(db, a, b, c);
        Assert.Single((await service.MyHistoryAsync(new(), a.UserId, default)).Page.Items);
        Assert.Single((await service.MyHistoryAsync(new(), b.UserId, default)).Page.Items);
        Assert.Empty((await service.MyHistoryAsync(new(), c.UserId, default)).Page.Items);
        await service.MyHistoryDetailAsync(task.Id, a.UserId, default);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MyHistoryDetailAsync(task.Id, c.UserId, default));
    }

    [Fact]
    public async Task MyHistoryAsync_FiltersBeforePaginationAndHonorsSort()
    {
        await using var db = Db(); var staff = Staff("A");
        for (var i = 0; i < 5; i++) { var task = MakeTask($"MT-{i}", i == 4 ? "Khác" : "Bơm", DateTimeOffset.UtcNow.AddMinutes(i), facilityId: i == 4 ? Guid.NewGuid() : null); db.Add(task); db.Add(new MaintenanceAssignment(task.Id, staff.UserId, Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(i))); }
        await db.SaveChangesAsync(); var service = Service(db, staff);
        var page = await service.MyHistoryAsync(new(SearchKeyword: "Bơm", Sort: MaintenanceHistorySort.OLDEST, PageSize: 2, PageIndex: 2), staff.UserId, default);
        Assert.Equal(4, page.Page.TotalCount); Assert.Equal(2, page.Page.Items.Count); Assert.Equal("MT-2", page.Page.Items[0].TaskNumber);
    }

    static MaintenanceTask MakeTask(string code, string title, DateTimeOffset at, Guid? facilityId = null) => new(code, title, Guid.NewGuid(), at, facilityId: facilityId);
    static MaintenanceDbContext Db() => new(new DbContextOptionsBuilder<MaintenanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    static MaintenanceStaffRecord Staff(string name) => new(Guid.NewGuid(), name, name);
    static MaintenanceService Service(MaintenanceDbContext db, params MaintenanceStaffRecord[] staff) => new(db, new Assets(), new Staffs(staff));
    sealed class Assets : IMaintenanceAssetSource { public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken c)=>Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>([]); public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid id,CancellationToken c)=>Task.FromResult<MaintenanceAssetReference?>(null); public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid id,CancellationToken c)=>Task.FromResult<MaintenanceAssetReference?>(null); public Task<bool> ExistsAsync(Guid? a,Guid? b,CancellationToken c)=>Task.FromResult(false); }
    sealed class Staffs(params MaintenanceStaffRecord[] all) : IMaintenanceStaffDirectory { public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken c)=>Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>(all); public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid id,CancellationToken c)=>Task.FromResult<MaintenanceStaffRecord?>(all.SingleOrDefault(x=>x.UserId==id)); }
}
