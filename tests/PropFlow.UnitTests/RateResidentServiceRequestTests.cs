using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application.RateResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.UnitTests;

public sealed class RateResidentServiceRequestTests
{
    [Fact]
    public async Task HandleAsync_SavesFeedbackForAuthenticatedResident()
    {
        var requestId = Guid.NewGuid();
        var residentId = Guid.NewGuid();
        var store = new FakeStore(SaveResidentServiceRequestFeedbackOutcome.Saved);
        var handler = Handler(store, new ActiveResidentResidence(residentId, Guid.NewGuid(), Guid.NewGuid()));

        var result = await handler.HandleAsync(Guid.NewGuid(), requestId, new(5, "Dịch vụ rất tốt."), default);

        Assert.Equal(RateResidentServiceRequestOutcome.Saved, result.Outcome);
        Assert.Equal(residentId, store.ResidentId);
        Assert.Equal(requestId, store.RequestId);
        Assert.Equal(5, result.Response!.Rating);
    }

    [Fact]
    public async Task HandleAsync_RejectsInvalidRatingBeforePersistence()
    {
        var store = new FakeStore(SaveResidentServiceRequestFeedbackOutcome.Saved);
        var handler = Handler(store, new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), new(0, "Ổn"), default);

        Assert.Equal(RateResidentServiceRequestOutcome.InvalidInput, result.Outcome);
        Assert.Null(store.RequestId);
    }

    [Fact]
    public async Task HandleAsync_RejectsRequestThatIsNotCompleted()
    {
        var store = new FakeStore(SaveResidentServiceRequestFeedbackOutcome.RequestNotCompleted);
        var handler = Handler(store, new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), new(4, "Phục vụ khá tốt."), default);

        Assert.Equal(RateResidentServiceRequestOutcome.RequestNotCompleted, result.Outcome);
        Assert.Equal("request_not_completed", result.ErrorCode);
    }

    private static RateResidentServiceRequestHandler Handler(FakeStore store, ActiveResidentResidence? residence) =>
        new(store, new FakeResidenceSource(residence), new FakeTimeZone(), new FixedClock());

    private sealed class FakeStore(SaveResidentServiceRequestFeedbackOutcome outcome) : IResidentServiceRequestFeedbackStore
    {
        public Guid? ResidentId { get; private set; }
        public Guid? RequestId { get; private set; }

        public Task<(SaveResidentServiceRequestFeedbackOutcome Outcome, ResidentServiceRequestFeedbackResponse? Response)> SaveAsync(
            Guid residentId, Guid serviceRequestId, int rating, string comment, DateTimeOffset now, CancellationToken ct)
        {
            ResidentId = residentId;
            RequestId = serviceRequestId;
            ResidentServiceRequestFeedbackResponse? response = outcome == SaveResidentServiceRequestFeedbackOutcome.Saved
                ? new(serviceRequestId, rating, comment, now) : null;
            return Task.FromResult((outcome, response));
        }
    }

    private sealed class FakeResidenceSource(ActiveResidentResidence? residence) : IResidentResidenceSource
    {
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid userId, DateOnly date, CancellationToken ct) => Task.FromResult(residence);
    }

    private sealed class FakeTimeZone : ICurrentBuildingTimeZone
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 8, 8, 0, 0, TimeSpan.Zero);
    }
}
