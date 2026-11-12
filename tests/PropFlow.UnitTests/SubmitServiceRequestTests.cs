using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Application.SubmitServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.1")]
public sealed class SubmitServiceRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Description_only_request_is_saved_unclassified_for_manager_review()
    {
        var residence = new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var store = new FakeStore(null);
        var command = new CreateResidentServiceRequestRequest(null, null,
            "  Vòi nước trong bếp bị rò.\nNhờ kiểm tra giúp tôi.  ", null, null, null, null);

        var result = await Handler(store, residence).HandleAsync(Guid.NewGuid(), command, default);

        Assert.Equal(SubmitServiceRequestOutcome.Created, result.Outcome);
        var request = Assert.IsType<ServiceRequest>(store.Request);
        Assert.Equal("Vòi nước trong bếp bị rò. Nhờ kiểm tra giúp tôi.", request.Title);
        Assert.Equal(command.Description.Trim(), request.Description);
        Assert.Equal(residence.ApartmentUnitId, request.ApartmentUnitId);
        Assert.Null(request.CategoryId);
        Assert.Null(request.FinalPriorityCode);
        Assert.Null(request.ServiceAreaCode);
        Assert.Null(request.FacilityId);
        Assert.Equal(0, store.CategoryLookups);
    }

    [Fact]
    public async Task Long_description_keeps_full_content_and_produces_short_title()
    {
        var store = new FakeStore(null);
        var description = string.Join(" ", Enumerable.Repeat("Nước bị rò trong bếp.", 30));
        var command = new CreateResidentServiceRequestRequest(null, null, description, null, null, null, null);

        var result = await Handler(store, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
            .HandleAsync(Guid.NewGuid(), command, default);

        Assert.Equal(SubmitServiceRequestOutcome.Created, result.Outcome);
        Assert.Equal(description, store.Request!.Description);
        Assert.InRange(store.Request.Title.Length, 1, 120);
        Assert.EndsWith("…", store.Request.Title);
    }

    [Theory]
    [InlineData("", null, null, "invalid_description")]
    [InlineData("Quá ngắn", null, null, "invalid_description")]
    [InlineData("Vòi nước bị rò cần kiểm tra.", "UNKNOWN", null, "invalid_priority")]
    [InlineData("Vòi nước bị rò cần kiểm tra.", null, "UNKNOWN", "invalid_service_area")]
    public async Task Simplified_input_still_enforces_server_validation(string description, string? priority, string? area, string code)
    {
        var store = new FakeStore(null);
        var result = await Handler(store, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).HandleAsync(
            Guid.NewGuid(), new(null, null, description, area, priority, null, null), default);

        Assert.Equal(SubmitServiceRequestOutcome.InvalidInput, result.Outcome);
        Assert.Equal(code, result.ErrorCode);
        Assert.Null(store.Request);
    }

    [Fact]
    public async Task Public_requests_use_the_active_facility_catalog_and_reject_an_inactive_facility()
    {
        var active = new MaintenanceAssetReference(Guid.NewGuid(), "F-1", "Tiện ích từ danh mục", "FACILITY", "ACTIVE", null);
        var inactive = new MaintenanceAssetReference(Guid.NewGuid(), "F-2", "Tiện ích ngừng dùng", "FACILITY", "INACTIVE", null);
        var equipment = new MaintenanceAssetReference(Guid.NewGuid(), "E-1", "Thiết bị", "EQUIPMENT", "ACTIVE", null);
        var store = new FakeStore(Guid.NewGuid());
        var residence = new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var handler = Handler(store, residence, assets: new FacilityCatalog(active, inactive, equipment));
        var option = Assert.Single(await handler.GetFacilitiesAsync(default));
        Assert.Equal(new ResidentServiceRequestFacility(active.Id, active.Name), option);

        var request = ValidRequest(null) with { ServiceAreaCode = ServiceRequestAreaCodes.CommonArea, FacilityId = inactive.Id };
        Assert.Equal("invalid_facility", (await handler.HandleAsync(Guid.NewGuid(), request, default)).ErrorCode);
        Assert.Null(store.Request);
        Assert.Equal(SubmitServiceRequestOutcome.Created,
            (await handler.HandleAsync(Guid.NewGuid(), request with { FacilityId = option.Id }, default)).Outcome);
        Assert.Equal(active.Id, store.Request!.FacilityId);
    }

    private sealed class FacilityCatalog(params MaintenanceAssetReference[] rows) : IMaintenanceAssetSource
    {
        public Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceAssetReference>>(rows);
        public Task<MaintenanceAssetReference?> GetFacilityAsync(Guid id, CancellationToken ct) => Task.FromResult(rows.SingleOrDefault(item => item.Id == id && item.AssetType == "FACILITY"));
        public Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact]
    public async Task HandleAsync_CreatesRequestForAuthenticatedResidentsActiveApartment()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var residence = new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var store = new FakeStore(categoryId);
        var handler = Handler(store, residence);

        var result = await handler.HandleAsync(
            userId,
            ValidRequest(new DateOnly(2026, 10, 3)),
            CancellationToken.None);

        Assert.Equal(SubmitServiceRequestOutcome.Created, result.Outcome);
        Assert.NotNull(result.Response);
        Assert.Equal("SUBMITTED", result.Response.Status);
        Assert.StartsWith("YC-20261002-", result.Response.RequestNumber, StringComparison.Ordinal);
        Assert.NotNull(store.Request);
        Assert.Equal(residence.ResidentId, store.Request.ResidentId);
        Assert.Equal(residence.ResidentApartmentId, store.Request.ResidentApartmentId);
        Assert.Equal(residence.ApartmentUnitId, store.Request.ApartmentUnitId);
        Assert.Equal(categoryId, store.Request.CategoryId);
        Assert.Equal("KITCHEN", store.Request.ServiceAreaCode);
        Assert.Equal(new DateOnly(2026, 10, 3), store.Request.PreferredDate);
        Assert.Equal("MORNING", store.Request.PreferredTimeCode);
        Assert.Equal("Nước đang bị rò rỉ bên dưới bồn rửa trong bếp.", store.Request.Description);
        Assert.NotNull(store.Activity);
        Assert.Equal(ServiceActivityType.CREATED, store.Activity.ActivityType);
        Assert.Equal(userId, store.Activity.PerformedBy);
    }

    [Fact]
    public async Task HandleAsync_PublishesRealtimeInvalidationAfterRequestIsStored()
    {
        var userId = Guid.NewGuid();
        var store = new FakeStore(Guid.NewGuid());
        var realtime = new RecordingRealtimeNotifier();
        var handler = Handler(
            store,
            new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            realtime: realtime);

        var result = await handler.HandleAsync(
            userId,
            ValidRequest(new DateOnly(2026, 10, 3)),
            CancellationToken.None);

        Assert.Equal(SubmitServiceRequestOutcome.Created, result.Outcome);
        Assert.Equal(result.Response!.Id, realtime.ServiceRequestId);
        Assert.Equal(userId, realtime.ResidentUserId);
        Assert.True(realtime.StoreWasCommitted);
    }

    [Fact]
    public async Task HandleAsync_RejectsPastPreferredDateUsingBuildingLocalDate()
    {
        var store = new FakeStore(Guid.NewGuid());
        var handler = Handler(store, new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        var result = await handler.HandleAsync(
            Guid.NewGuid(),
            ValidRequest(new DateOnly(2026, 10, 1)),
            CancellationToken.None);

        Assert.Equal(SubmitServiceRequestOutcome.InvalidInput, result.Outcome);
        Assert.Equal("preferred_date_in_past", result.ErrorCode);
        Assert.Null(store.Request);
    }

    [Fact]
    public async Task HandleAsync_RejectsAccountWithoutActiveResidence()
    {
        var store = new FakeStore(Guid.NewGuid());
        var handler = Handler(store, null);

        var result = await handler.HandleAsync(
            Guid.NewGuid(),
            ValidRequest(new DateOnly(2026, 10, 3)),
            CancellationToken.None);

        Assert.Equal(SubmitServiceRequestOutcome.ResidentResidenceNotFound, result.Outcome);
        Assert.Equal("active_residence_not_found", result.ErrorCode);
        Assert.Null(store.Request);
    }

    [Fact]
    public async Task HandleAsync_AcceptsAnytimeAndUsesBuildingLocalDateInRequestNumber()
    {
        var store = new FakeStore(Guid.NewGuid());
        var residence = new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var localNextDay = new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
        var handler = Handler(store, residence, localNextDay);
        var request = ValidRequest(new DateOnly(2026, 10, 2)) with { PreferredTimeCode = "ANYTIME" };

        var result = await handler.HandleAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.Equal(SubmitServiceRequestOutcome.Created, result.Outcome);
        Assert.StartsWith("YC-20261002-", result.Response!.RequestNumber, StringComparison.Ordinal);
        Assert.Equal("ANYTIME", store.Request!.PreferredTimeCode);
    }

    private static SubmitServiceRequestHandler Handler(
        FakeStore store,
        ActiveResidentResidence? residence,
        DateTimeOffset? now = null,
        RecordingRealtimeNotifier? realtime = null,
        IMaintenanceAssetSource? assets = null) =>
        new(
            store,
            new FakeResidenceSource(residence),
            new FakeBuildingTimeZone(),
            new FixedTimeProvider(now ?? Now),
            realtime?.Track(store),
            assets);

    private static CreateResidentServiceRequestRequest ValidRequest(DateOnly? preferredDate) =>
        new(
            "REPAIR",
            "Kiểm tra đường ống nước",
            "Nước đang bị rò rỉ bên dưới bồn rửa trong bếp.",
            "KITCHEN",
            "URGENT",
            preferredDate,
            "MORNING");

    private sealed class FakeStore(Guid? categoryId) : IServiceRequestSubmissionStore
    {
        public int CategoryLookups { get; private set; }
        public ServiceRequest? Request { get; private set; }
        public ServiceRequestActivity? Activity { get; private set; }

        public Task<Guid?> FindActiveCategoryIdAsync(string categoryCode, CancellationToken ct)
        {
            CategoryLookups++;
            return Task.FromResult(categoryId);
        }

        public Task AddAsync(
            ServiceRequest request,
            ServiceRequestActivity initialActivity,
            CancellationToken ct)
        {
            Request = request;
            Activity = initialActivity;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeResidenceSource(ActiveResidentResidence? residence) : IResidentResidenceSource
    {
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid userId, DateOnly date, CancellationToken ct) =>
            Task.FromResult(residence);
    }

    private sealed class FakeBuildingTimeZone : ICurrentBuildingTimeZone
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
    }

    private sealed class RecordingRealtimeNotifier : IServiceRequestRealtimeNotifier
    {
        private FakeStore? _store;
        public Guid? ServiceRequestId { get; private set; }
        public Guid? ResidentUserId { get; private set; }
        public bool StoreWasCommitted { get; private set; }

        public RecordingRealtimeNotifier Track(FakeStore store)
        {
            _store = store;
            return this;
        }

        public Task NotifyChangedAsync(Guid serviceRequestId, Guid? residentUserId = null, CancellationToken ct = default)
        {
            ServiceRequestId = serviceRequestId;
            ResidentUserId = residentUserId;
            StoreWasCommitted = _store?.Request is not null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
