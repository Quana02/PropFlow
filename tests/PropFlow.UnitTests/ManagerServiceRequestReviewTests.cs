using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
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

    [Fact]
    public async Task ConfirmPropertyContext_SavesContext_ReviewsRequest_AndWritesAuditActivities()
    {
        await using var db = CreateDb();
        var submittedAt = new DateTimeOffset(2026, 10, 11, 2, 0, 0, TimeSpan.Zero);
        var request = NewRequest(submittedAt);
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();
        var apartmentId = Guid.NewGuid();
        var facilityId = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var outcome = await new ManagerServiceRequestStore(db).ConfirmPropertyContextAsync(
            request.Id, managerId, apartmentId, facilityId, equipmentId,
            "bathroom-master", "Đủ thông tin để tiếp nhận", submittedAt.AddMinutes(5), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Success, outcome);
        Assert.Equal(ServiceRequestStatus.UNDER_REVIEW, request.Status);
        Assert.Equal(apartmentId, request.ApartmentUnitId);
        Assert.Equal(facilityId, request.FacilityId);
        Assert.Equal(equipmentId, request.EquipmentId);
        Assert.Equal("BATHROOM-MASTER", request.ServiceAreaCode);
        var activities = await db.ServiceRequestActivities.OrderBy(x => x.CreatedAt).ToListAsync();
        Assert.Equal(2, activities.Count);
        Assert.Contains(activities, x => x.ActivityType == ServiceActivityType.PROPERTY_CONTEXT_ASSOCIATED && x.PerformedBy == managerId);
        Assert.Contains(activities, x => x.ActivityType == ServiceActivityType.MANAGER_REVIEWED && x.ToStatus == ServiceRequestStatus.UNDER_REVIEW);
    }

    [Fact]
    public async Task ConfirmPropertyContext_ReturnsConflict_WithoutChangingExistingReviewedRequest()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var request = NewRequest(now);
        request.MarkUnderReview(now.AddMinutes(1));
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();

        var outcome = await new ManagerServiceRequestStore(db).ConfirmPropertyContextAsync(
            request.Id, Guid.NewGuid(), Guid.NewGuid(), null, null, null, null,
            now.AddMinutes(2), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Conflict, outcome);
        Assert.Null(request.ApartmentUnitId);
        Assert.Empty(await db.ServiceRequestActivities.ToListAsync());
    }

    [Fact]
    public async Task Assign_CreatesActiveAssignment_ChangesStatus_AndWritesActivity()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 12, 2, 0, 0, TimeSpan.Zero);
        var request = NewRequest(now);
        request.MarkUnderReview(now.AddMinutes(1));
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();

        var outcome = await new ManagerServiceRequestStore(db).AssignAsync(
            request.Id, managerId, staffId, "Kiểm tra hệ thống cấp nước", now.AddMinutes(2), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Success, outcome);
        Assert.Equal(ServiceRequestStatus.ASSIGNED, request.Status);
        var assignment = Assert.Single(await db.ServiceRequestAssignments.ToListAsync());
        Assert.Equal(staffId, assignment.StaffUserId);
        Assert.Equal(AssignmentStatus.ASSIGNED, assignment.Status);
        var activity = Assert.Single(await db.ServiceRequestActivities.ToListAsync());
        Assert.Equal(ServiceActivityType.ASSIGNED, activity.ActivityType);
        Assert.Equal(assignment.Id, activity.AssignmentId);
        Assert.Equal(managerId, activity.PerformedBy);
    }

    [Fact]
    public async Task Assign_ReassignsExistingStaff_AndPreservesAssignmentHistory()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 12, 2, 0, 0, TimeSpan.Zero);
        var request = NewRequest(now);
        request.MarkUnderReview(now.AddMinutes(1));
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();
        var store = new ManagerServiceRequestStore(db);
        var firstStaffId = Guid.NewGuid();
        var secondStaffId = Guid.NewGuid();

        Assert.Equal(ManagerServiceRequestOutcome.Success, await store.AssignAsync(
            request.Id, Guid.NewGuid(), firstStaffId, null, now.AddMinutes(2), CancellationToken.None));
        Assert.Equal(ManagerServiceRequestOutcome.Success, await store.AssignAsync(
            request.Id, Guid.NewGuid(), secondStaffId, "Điều phối lại", now.AddMinutes(3), CancellationToken.None));

        var assignments = await db.ServiceRequestAssignments.OrderBy(x => x.AssignedAt).ToListAsync();
        Assert.Equal(2, assignments.Count);
        Assert.Equal(AssignmentStatus.REASSIGNED, assignments[0].Status);
        Assert.NotNull(assignments[0].EndedAt);
        Assert.Equal(AssignmentStatus.ASSIGNED, assignments[1].Status);
        Assert.Equal(secondStaffId, assignments[1].StaffUserId);
        Assert.Contains(await db.ServiceRequestActivities.ToListAsync(), x => x.ActivityType == ServiceActivityType.REASSIGNED);
    }

    [Fact]
    public async Task List_FiltersRequestsSubmittedWithinRequestedNumberOfDays()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 12, 3, 0, 0, TimeSpan.Zero);
        var recent = NewRequest(now.AddDays(-29));
        var old = NewRequest(now.AddDays(-31));
        db.ServiceRequests.AddRange(recent, old);
        await db.SaveChangesAsync();

        var page = await new ManagerServiceRequestStore(db, new FixedTimeProvider(now)).ListAsync(
            new ManagerServiceRequestListQuery(SubmittedWithinDays: 30), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(recent.Id, item.Id);
        Assert.Equal(1, page.TotalCount);
    }

    private static ServiceRequestsDbContext CreateDb() => new(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ServiceRequest NewRequest(DateTimeOffset now) => new(
        $"SR-{Guid.NewGuid():N}"[..20], Guid.NewGuid(), Guid.NewGuid(),
        "Kiểm tra rò rỉ nước", "Mô tả yêu cầu đủ dài để Manager xem xét.", now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
