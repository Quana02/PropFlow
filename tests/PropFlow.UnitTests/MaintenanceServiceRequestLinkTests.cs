using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTaskActivities;
using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-07.4")]
public sealed class MaintenanceServiceRequestLinkTests
{
    [Fact]
    public async Task CreateTaskAsync_PersistsValidatedServiceRequestWithoutChangingTaskLifecycle()
    {
        await using var db = CreateDb();
        var reference = Request(linkable: true);
        var service = Service(db, reference);

        var created = await service.CreateTaskAsync(
            new("CVBT-MAYBOM-01-111026", "Kiểm tra máy bơm", Description: "Kiểm tra", ServiceRequestId: reference.Id),
            Guid.NewGuid(), CancellationToken.None);

        var persisted = await db.MaintenanceTasks.SingleAsync();
        Assert.Equal(reference.Id, persisted.SourceServiceRequestId);
        Assert.Equal(MaintenanceTaskStatus.OPEN, persisted.Status);
        Assert.Equal(reference.RequestNumber, created.RelatedServiceRequest!.RequestNumber);
        Assert.Contains(await db.MaintenanceTaskActivities.ToListAsync(), item => item.ActivityType == MaintenanceActivityType.SERVICE_REQUEST_LINKED);
    }

    [Fact]
    public async Task CreateTaskAsync_RejectsMissingOrIneligibleServiceRequest()
    {
        await using var db = CreateDb();
        var missing = Service(db, null);
        await Assert.ThrowsAsync<ArgumentException>(() => missing.CreateTaskAsync(
            new("CVBT-MAYBOM-02-111026", "Kiểm tra", Description: "Kiểm tra", ServiceRequestId: Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None));

        var closedRequest = Request(linkable: false);
        var ineligible = Service(db, closedRequest);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ineligible.CreateTaskAsync(
            new("CVBT-MAYBOM-03-111026", "Kiểm tra", Description: "Kiểm tra", ServiceRequestId: closedRequest.Id), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task LinkTaskServiceRequestAsync_PersistsFirstLinkAndPreservesAssignmentsAndStatus()
    {
        await using var db = CreateDb();
        var task = new MaintenanceTask("CVBT-MAYBOM-04-111026", "Kiểm tra", Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.MaintenanceTasks.Add(task);
        await db.SaveChangesAsync();
        var reference = Request(linkable: true);
        var service = Service(db, reference);

        var linked = await service.LinkTaskServiceRequestAsync(task.Id, new(reference.Id), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(reference.Id, linked.SourceServiceRequestId);
        Assert.Equal(MaintenanceTaskStatus.OPEN, (await db.MaintenanceTasks.SingleAsync()).Status);
        await Assert.ThrowsAsync<ArgumentException>(() => service.LinkTaskServiceRequestAsync(task.Id, new(Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task TaskDetail_ExposesOnlyRelatedRequestReadModelAndMatchesTheSameEquipment()
    {
        await using var db = CreateDb();
        var facilityId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var request = Request(linkable: true) with { FacilityId = facilityId, EquipmentId = equipmentId };
        var service = Service(db, request, new Assets(facilityId, equipmentId));

        var created = await service.CreateTaskAsync(
            new("CVBT-MAYBOM-05-111026", "Kiểm tra máy bơm", Description: "Kiểm tra", FacilityId: facilityId, EquipmentId: equipmentId, ServiceRequestId: request.Id),
            Guid.NewGuid(), CancellationToken.None);

        var detail = await service.TaskAsync(created.Id, CancellationToken.None);

        Assert.NotNull(detail.RelatedServiceRequest);
        Assert.Equal("MATCHED", detail.RelatedServiceRequest!.ContextMatch);
        Assert.Contains("Máy bơm", detail.RelatedServiceRequest.LocationDescription);
        Assert.DoesNotContain("resident", detail.RelatedServiceRequest.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TaskDetail_DisplaysTheRelatedApartmentUnitAndFloorFromTheServiceRequest()
    {
        await using var db = CreateDb();
        var apartmentId = Guid.NewGuid();
        var request = Request(linkable: true) with
        {
            ApartmentUnitId = apartmentId,
            ApartmentUnitNumber = "dư",
            ApartmentFloorNumber = 2
        };
        var service = Service(db, request);

        var created = await service.CreateTaskAsync(
            new("CVBT-CANHO-07-111026", "Kiểm tra căn hộ", Description: "Kiểm tra", ServiceRequestId: request.Id),
            Guid.NewGuid(), CancellationToken.None);

        var detail = await service.TaskAsync(created.Id, CancellationToken.None);

        Assert.Equal("Căn hộ thụ hưởng: dư · Tầng 2", detail.RelatedServiceRequest!.LocationDescription);
    }

    [Fact]
    public async Task TaskDetail_UsesAnExplicitFallbackWhenTheRelatedApartmentCannotBeDescribed()
    {
        await using var db = CreateDb();
        var request = Request(linkable: true) with { ApartmentUnitId = Guid.NewGuid() };
        var service = Service(db, request);

        var created = await service.CreateTaskAsync(
            new("CVBT-CANHO-08-111026", "Kiểm tra căn hộ", Description: "Kiểm tra", ServiceRequestId: request.Id),
            Guid.NewGuid(), CancellationToken.None);

        var detail = await service.TaskAsync(created.Id, CancellationToken.None);

        Assert.Equal("Căn hộ thụ hưởng: Chưa xác định", detail.RelatedServiceRequest!.LocationDescription);
    }

    [Fact]
    public async Task TaskDetail_MarksDifferentEquipmentAsWarningWithoutBlockingTheLink()
    {
        await using var db = CreateDb();
        var facilityId = Guid.NewGuid();
        var taskEquipmentId = Guid.NewGuid();
        var requestEquipmentId = Guid.NewGuid();
        var request = Request(linkable: true) with { FacilityId = facilityId, EquipmentId = requestEquipmentId };
        var service = Service(db, request, new Assets(facilityId, taskEquipmentId, requestEquipmentId));

        var created = await service.CreateTaskAsync(
            new("CVBT-MAYBOM-06-111026", "Kiểm tra máy bơm", Description: "Kiểm tra", FacilityId: facilityId, EquipmentId: taskEquipmentId, ServiceRequestId: request.Id),
            Guid.NewGuid(), CancellationToken.None);

        Assert.Equal("DIFFERENT_CONTEXT", created.RelatedServiceRequest!.ContextMatch);
        Assert.Contains("khác", created.RelatedServiceRequest.ContextWarning!, StringComparison.OrdinalIgnoreCase);
    }

    private static MaintenanceDbContext CreateDb() => new(new DbContextOptionsBuilder<MaintenanceDbContext>()
        .UseInMemoryDatabase($"maintenance-service-request-{Guid.NewGuid()}").Options);

    private static MaintenanceService Service(MaintenanceDbContext db, ServiceRequestMaintenanceReference? reference, Assets? assets = null) =>
        new(db, assets ?? new Assets(), new Staff(), serviceRequestSource: new Requests(reference));

    private static ServiceRequestMaintenanceReference Request(bool linkable) => new(
        Guid.NewGuid(), "YC-20261011-001", "Rò nước máy bơm", linkable ? "SUBMITTED" : "CLOSED",
        null, null, null, "HIGH", DateTimeOffset.UtcNow, linkable,
        linkable ? null : "service_request_not_linkable");

    private sealed class Requests(ServiceRequestMaintenanceReference? item) : IServiceRequestMaintenanceSource
    {
        public Task<ServiceRequestMaintenanceReference?> GetAsync(Guid serviceRequestId, CancellationToken ct) =>
            Task.FromResult(item is { Id: var id } && id == serviceRequestId ? item : null);
        public Task<IReadOnlyList<ServiceRequestMaintenanceReference>> SearchAsync(string? search, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ServiceRequestMaintenanceReference>>(item is null ? [] : [item]);
    }

    private sealed class Assets(params Guid[] equipmentIds) : IMaintenanceAssetSource
    {
        private readonly Guid? facilityId = equipmentIds.Length > 0 ? equipmentIds[0] : null;
        public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>([]);
        public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(this.facilityId == facilityId ? new(facilityId, "CSVC-T06", "Khu kỹ thuật T06", "FACILITY", "UNDER_MAINTENANCE", null) : null);
        public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct) => Task.FromResult<MaintenanceAssetReference?>(equipmentIds.Skip(1).Contains(equipmentId) ? new(equipmentId, "TB-MAYBOM", "Máy bơm", "EQUIPMENT", "UNDER_MAINTENANCE", null, facilityId) : null);
        public Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Staff : IMaintenanceStaffDirectory
    {
        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>([]);
        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId, CancellationToken ct) => Task.FromResult<MaintenanceStaffRecord?>(null);
    }
}
