using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application.GetResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.2-FE-05.3")]
public sealed class GetResidentServiceRequestTests
{
    [Fact]
    public async Task HandleAsync_ReturnsDetailAndHistoryForAuthenticatedResident()
    {
        var residentId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var expected = Detail(requestId);
        var store = new FakeStore(expected);
        var handler = Handler(store, new ActiveResidentResidence(residentId, Guid.NewGuid(), Guid.NewGuid()));

        var result = await handler.HandleAsync(Guid.NewGuid(), requestId, CancellationToken.None);

        Assert.Equal(GetResidentServiceRequestOutcome.Success, result.Outcome);
        Assert.Equal(expected, result.Detail);
        Assert.Equal(residentId, store.RequestedResidentId);
        Assert.Equal(requestId, store.RequestedServiceRequestId);
        Assert.Single(result.Detail!.Activities);
    }

    [Fact]
    public async Task HandleAsync_RejectsAccountWithoutActiveResidence()
    {
        var store = new FakeStore(Detail(Guid.NewGuid()));
        var handler = Handler(store, null);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(GetResidentServiceRequestOutcome.ResidentResidenceNotFound, result.Outcome);
        Assert.Null(result.Detail);
        Assert.Null(store.RequestedResidentId);
    }

    [Fact]
    public async Task HandleAsync_ReturnsNotFoundWhenRequestDoesNotBelongToResident()
    {
        var residentId = Guid.NewGuid();
        var store = new FakeStore(null);
        var handler = Handler(store, new ActiveResidentResidence(residentId, Guid.NewGuid(), Guid.NewGuid()));

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(GetResidentServiceRequestOutcome.RequestNotFound, result.Outcome);
        Assert.Null(result.Detail);
        Assert.Equal(residentId, store.RequestedResidentId);
    }

    [Fact]
    public async Task DetailStore_ScopesByResidentAndDoesNotExposeInternalActivityText()
    {
        var residentId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
        await using var db = new ServiceRequestsDbContext(
            new DbContextOptionsBuilder<ServiceRequestsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var request = new ServiceRequest(
            "YC-20261008-STORE",
            residentId,
            Guid.NewGuid(),
            "Sửa đường ống nước",
            "Đường ống dưới bồn rửa bị rò rỉ.",
            now,
            apartmentUnitId: Guid.NewGuid(),
            finalPriorityCode: "URGENT",
            serviceAreaCode: "KITCHEN");
        var activity = new ServiceRequestActivity(
            request.Id,
            ServiceActivityType.CREATED,
            now,
            toStatus: ServiceRequestStatus.SUBMITTED,
            title: "Ghi chú nội bộ không được lộ",
            detail: "SECRET_INTERNAL_DETAIL",
            workResult: "SECRET_INTERNAL_RESULT",
            performedBy: Guid.NewGuid());
        db.AddRange(request, activity);
        await db.SaveChangesAsync();
        var store = new ResidentServiceRequestDetailStore(db);

        var ownDetail = await store.GetAsync(residentId, request.Id, CancellationToken.None);
        var anotherResidentDetail = await store.GetAsync(Guid.NewGuid(), request.Id, CancellationToken.None);

        Assert.NotNull(ownDetail);
        var residentActivity = Assert.Single(ownDetail!.Activities);
        Assert.Equal("Đã gửi yêu cầu", residentActivity.Title);
        Assert.DoesNotContain("SECRET", residentActivity.Description, StringComparison.Ordinal);
        Assert.Null(anotherResidentDetail);
    }

    private static GetResidentServiceRequestHandler Handler(
        FakeStore store,
        ActiveResidentResidence? residence) =>
        new(store, new FakeResidenceSource(residence), new FakeBuildingTimeZone(), new FixedTimeProvider());

    private static ResidentServiceRequestDetailResponse Detail(Guid id) =>
        new(
            id,
            "YC-20261008-ABC",
            "Sửa đường ống nước",
            "Đường ống dưới bồn rửa bị rò rỉ.",
            "REPAIR",
            "Sửa chữa kỹ thuật",
            "SUBMITTED",
            "URGENT",
            "KITCHEN",
            new DateOnly(2026, 10, 9),
            "MORNING",
            new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero),
            null,
            null,
            null,
            null,
            null,
            [new(Guid.NewGuid(), "CREATED", null, "SUBMITTED", "Đã gửi yêu cầu", "Yêu cầu đã được ghi nhận.", new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero))]);

    private sealed class FakeStore(ResidentServiceRequestDetailResponse? detail)
        : IResidentServiceRequestDetailStore
    {
        public Guid? RequestedResidentId { get; private set; }
        public Guid? RequestedServiceRequestId { get; private set; }

        public Task<ResidentServiceRequestDetailResponse?> GetAsync(Guid residentId, Guid serviceRequestId, CancellationToken ct)
        {
            RequestedResidentId = residentId;
            RequestedServiceRequestId = serviceRequestId;
            return Task.FromResult(detail);
        }
    }

    private sealed class FakeResidenceSource(ActiveResidentResidence? residence) : IResidentResidenceSource
    {
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid userId, DateOnly date, CancellationToken ct) =>
            Task.FromResult(residence);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeBuildingTimeZone : ICurrentBuildingTimeZone
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
    }
}
