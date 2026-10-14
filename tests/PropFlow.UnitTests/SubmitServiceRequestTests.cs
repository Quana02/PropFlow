using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
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
        DateTimeOffset? now = null) =>
        new(
            store,
            new FakeResidenceSource(residence),
            new FakeBuildingTimeZone(),
            new FixedTimeProvider(now ?? Now));

    private static CreateResidentServiceRequestRequest ValidRequest(DateOnly preferredDate) =>
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
        public ServiceRequest? Request { get; private set; }
        public ServiceRequestActivity? Activity { get; private set; }

        public Task<Guid?> FindActiveCategoryIdAsync(string categoryCode, CancellationToken ct) =>
            Task.FromResult(categoryId);

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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
