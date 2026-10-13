using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.1")]
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

    private sealed class FakeStore(IReadOnlyList<ResidentServiceRequestListItem> items)
        : IResidentServiceRequestReadStore
    {
        public Guid? RequestedResidentId { get; private set; }

        public Task<IReadOnlyList<ResidentServiceRequestListItem>> ListAsync(Guid residentId, CancellationToken ct)
        {
            RequestedResidentId = residentId;
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
