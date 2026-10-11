using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.4")]
public sealed class ListResidentServiceRequestsTests
{
    [Fact]
    public async Task HandleAsync_ReturnsOnlyRequestsForAuthenticatedResident()
    {
        var userId = Guid.NewGuid();
        var residentId = Guid.NewGuid();
        var expected = new ResidentServiceRequestListItem(
            Guid.NewGuid(), "YC-20261008-ABC", "Sửa đường ống nước", "REPAIR",
            "SUBMITTED", "URGENT", "KITCHEN", new DateOnly(2026, 10, 9),
            "MORNING", new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 8, 0, 0, TimeSpan.Zero), null, null, null);
        var store = new FakeStore([expected]);
        var handler = new ListResidentServiceRequestsHandler(
            store,
            new FakeResidenceSource(new ActiveResidentResidence(residentId, Guid.NewGuid(), Guid.NewGuid())),
            new FakeBuildingTimeZone(),
            new FixedTimeProvider());

        var result = await handler.HandleAsync(userId, CancellationToken.None);

        Assert.Equal(ListResidentServiceRequestsOutcome.Success, result.Outcome);
        Assert.Equal(residentId, store.RequestedResidentId);
        Assert.Equal([expected], result.Items);
    }

    [Fact]
    public async Task HandleAsync_RejectsAccountWithoutActiveResidence()
    {
        var store = new FakeStore([]);
        var handler = new ListResidentServiceRequestsHandler(
            store,
            new FakeResidenceSource(null),
            new FakeBuildingTimeZone(),
            new FixedTimeProvider());

        var result = await handler.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ListResidentServiceRequestsOutcome.ResidentResidenceNotFound, result.Outcome);
        Assert.Empty(result.Items);
        Assert.Null(store.RequestedResidentId);
    }

    [Fact]
    public async Task HandleAsync_AppliesSearchStatusAndInclusiveBuildingDateRange()
    {
        var residentId = Guid.NewGuid();
        var store = new FakeStore([]);
        var handler = new ListResidentServiceRequestsHandler(
            store,
            new FakeResidenceSource(new ActiveResidentResidence(residentId, Guid.NewGuid(), Guid.NewGuid())),
            new FakeBuildingTimeZone(),
            new FixedTimeProvider());

        var result = await handler.HandleAsync(
            Guid.NewGuid(),
            new ResidentServiceRequestListQuery(
                "  rò nước  ",
                "in_progress",
                new DateOnly(2026, 10, 8),
                new DateOnly(2026, 10, 9)),
            CancellationToken.None);

        Assert.Equal(ListResidentServiceRequestsOutcome.Success, result.Outcome);
        Assert.NotNull(store.RequestedFilter);
        Assert.Equal("rò nước", store.RequestedFilter.Search);
        Assert.Equal(ServiceRequestStatus.IN_PROGRESS, store.RequestedFilter.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 17, 0, 0, TimeSpan.Zero), store.RequestedFilter.SubmittedFromInclusive);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 17, 0, 0, TimeSpan.Zero), store.RequestedFilter.SubmittedToExclusive);
    }

    [Theory]
    [InlineData("unknown", null, null, "invalid_status")]
    [InlineData(null, "2026-10-10", "2026-10-09", "invalid_date_range")]
    public async Task HandleAsync_RejectsInvalidFilters(
        string? status,
        string? fromDate,
        string? toDate,
        string expectedErrorCode)
    {
        var store = new FakeStore([]);
        var handler = new ListResidentServiceRequestsHandler(
            store,
            new FakeResidenceSource(new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())),
            new FakeBuildingTimeZone(),
            new FixedTimeProvider());

        var result = await handler.HandleAsync(
            Guid.NewGuid(),
            new ResidentServiceRequestListQuery(
                Status: status,
                FromDate: fromDate is null ? null : DateOnly.Parse(fromDate),
                ToDate: toDate is null ? null : DateOnly.Parse(toDate)),
            CancellationToken.None);

        Assert.Equal(ListResidentServiceRequestsOutcome.InvalidInput, result.Outcome);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        Assert.Null(store.RequestedResidentId);
    }

    [Fact]
    public async Task ReadStore_FiltersOnlyOwnedRequestsBySearchStatusAndSubmittedRange()
    {
        var residentId = Guid.NewGuid();
        var submittedAt = new DateTimeOffset(2026, 10, 8, 3, 0, 0, TimeSpan.Zero);
        await using var db = new ServiceRequestsDbContext(
            new DbContextOptionsBuilder<ServiceRequestsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var expected = Request("YC-20261008-MATCH", residentId, "Rò nước phòng bếp", submittedAt);
        expected.MarkUnderReview(submittedAt.AddMinutes(1));
        expected.MarkAssigned(submittedAt.AddMinutes(2));
        expected.MarkInProgress(submittedAt.AddMinutes(3));
        var wrongStatus = Request("YC-20261008-WAIT", residentId, "Rò nước phòng tắm", submittedAt);
        var anotherResident = Request("YC-20261008-OTHER", Guid.NewGuid(), "Rò nước phòng bếp", submittedAt);
        db.AddRange(expected, wrongStatus, anotherResident);
        await db.SaveChangesAsync();
        var store = new ResidentServiceRequestReadStore(db);

        var items = await store.ListAsync(
            residentId,
            new ResidentServiceRequestFilter(
                "rò nước",
                ServiceRequestStatus.IN_PROGRESS,
                new DateTimeOffset(2026, 10, 7, 17, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 8, 17, 0, 0, TimeSpan.Zero)),
            CancellationToken.None);

        var item = Assert.Single(items);
        Assert.Equal(expected.Id, item.Id);
    }

    private static ServiceRequest Request(
        string number,
        Guid residentId,
        string title,
        DateTimeOffset submittedAt) =>
        new(
            number,
            residentId,
            Guid.NewGuid(),
            title,
            "Mô tả yêu cầu hợp lệ để phục vụ kiểm thử.",
            submittedAt,
            apartmentUnitId: Guid.NewGuid(),
            finalPriorityCode: "NORMAL",
            serviceAreaCode: "KITCHEN");

    private sealed class FakeStore(IReadOnlyList<ResidentServiceRequestListItem> items)
        : IResidentServiceRequestReadStore
    {
        public Guid? RequestedResidentId { get; private set; }
        public ResidentServiceRequestFilter? RequestedFilter { get; private set; }

        public Task<IReadOnlyList<ResidentServiceRequestListItem>> ListAsync(
            Guid residentId,
            ResidentServiceRequestFilter filter,
            CancellationToken ct)
        {
            RequestedResidentId = residentId;
            RequestedFilter = filter;
            return Task.FromResult(items);
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
