using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05.5")]
public sealed class ManagerServiceRequestReviewTests
{
    [Fact]
    public async Task Review_MovesSubmittedRequestToUnderReview_AndWritesAuditActivity()
    {
        await using var db = CreateDb();
        var submittedAt = new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero);
        var request = NewRequest(submittedAt);
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();
        var managerId = Guid.NewGuid();
        var reviewedAt = submittedAt.AddMinutes(10);

        var outcome = await new ManagerServiceRequestStore(db).ReviewAsync(
            request.Id, managerId, "Đã kiểm tra nội dung", reviewedAt, CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Success, outcome);
        Assert.Equal(ServiceRequestStatus.UNDER_REVIEW, request.Status);
        var activity = Assert.Single(await db.ServiceRequestActivities.ToListAsync());
        Assert.Equal(ServiceActivityType.MANAGER_REVIEWED, activity.ActivityType);
        Assert.Equal(ServiceRequestStatus.SUBMITTED, activity.FromStatus);
        Assert.Equal(ServiceRequestStatus.UNDER_REVIEW, activity.ToStatus);
        Assert.Equal(managerId, activity.PerformedBy);
        Assert.Equal("Đã kiểm tra nội dung", activity.Detail);
    }

    [Fact]
    public async Task Review_ReturnsConflict_WhenRequestWasAlreadyReviewed()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var request = NewRequest(now);
        request.MarkUnderReview(now.AddMinutes(1));
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();

        var outcome = await new ManagerServiceRequestStore(db).ReviewAsync(
            request.Id, Guid.NewGuid(), null, now.AddMinutes(2), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Conflict, outcome);
        Assert.Empty(await db.ServiceRequestActivities.ToListAsync());
    }

    private static ServiceRequestsDbContext CreateDb() => new(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ServiceRequest NewRequest(DateTimeOffset now) => new(
        $"SR-{Guid.NewGuid():N}"[..20], Guid.NewGuid(), Guid.NewGuid(),
        "Kiểm tra rò rỉ nước", "Mô tả yêu cầu đủ dài để Manager xem xét.", now);
}
