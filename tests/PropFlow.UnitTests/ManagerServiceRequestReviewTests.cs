using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.ServiceRequests.Application.ReviewManagerServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestEvidence;
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

    [Fact]
    public async Task ApproveResolution_CompletesAssignment_ResolvesRequest_AndWritesActivity()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);
        var (request, assignment) = await AddInProgressRequestWithResultAsync(db, now);
        var managerId = Guid.NewGuid();

        var outcome = await new ManagerServiceRequestStore(db).ApproveResolutionAsync(
            request.Id, managerId, "Kết quả đạt yêu cầu", now.AddMinutes(6), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Success, outcome);
        Assert.Equal(ServiceRequestStatus.RESOLVED, request.Status);
        Assert.Equal(AssignmentStatus.COMPLETED, assignment.Status);
        Assert.NotNull(request.ResolvedAt);
        Assert.Contains(await db.ServiceRequestActivities.ToListAsync(), x =>
            x.ActivityType == ServiceActivityType.RESOLVED &&
            x.FromStatus == ServiceRequestStatus.IN_PROGRESS &&
            x.ToStatus == ServiceRequestStatus.RESOLVED &&
            x.PerformedBy == managerId);
    }

    [Fact]
    public async Task RequestRework_KeepsRequestInProgress_AndRequiresANewerStaffResult()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);
        var (request, assignment) = await AddInProgressRequestWithResultAsync(db, now);
        var store = new ManagerServiceRequestStore(db);
        var managerId = Guid.NewGuid();

        var reworkOutcome = await store.RequestReworkAsync(
            request.Id, managerId, "Ảnh chưa chứng minh đã xử lý xong", now.AddMinutes(6), CancellationToken.None);
        var prematureApproval = await store.ApproveResolutionAsync(
            request.Id, managerId, null, now.AddMinutes(7), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Success, reworkOutcome);
        Assert.Equal(ManagerServiceRequestOutcome.Conflict, prematureApproval);
        Assert.Equal(ServiceRequestStatus.IN_PROGRESS, request.Status);
        Assert.Equal(AssignmentStatus.IN_PROGRESS, assignment.Status);
        Assert.Contains(await db.ServiceRequestActivities.ToListAsync(), x =>
            x.ActivityType == ServiceActivityType.REWORK_REQUESTED &&
            x.Detail == "Ảnh chưa chứng minh đã xử lý xong");
    }

    [Fact]
    public async Task ApproveResolution_ReturnsConflict_WhenLatestStaffResultHasNoEvidence()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 13, 4, 0, 0, TimeSpan.Zero);
        var request = NewRequest(now);
        request.MarkUnderReview(now.AddMinutes(1));
        request.MarkAssigned(now.AddMinutes(2));
        request.MarkInProgress(now.AddMinutes(3));
        var assignment = new ServiceRequestAssignment(request.Id, Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(2));
        assignment.Start(now.AddMinutes(3));
        var result = new ServiceRequestActivity(request.Id, ServiceActivityType.WORK_RESULT_ADDED,
            now.AddMinutes(4), assignment.Id, workResult: "Đã xử lý nhưng chưa tải ảnh.", performedBy: assignment.StaffUserId);
        db.AddRange(request, assignment, result);
        await db.SaveChangesAsync();

        var outcome = await new ManagerServiceRequestStore(db).ApproveResolutionAsync(
            request.Id, Guid.NewGuid(), null, now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Conflict, outcome);
        Assert.Equal(ServiceRequestStatus.IN_PROGRESS, request.Status);
        Assert.Equal(AssignmentStatus.IN_PROGRESS, assignment.Status);
    }

    [Fact]
    public async Task Close_ClosesResolvedRequest_AndWritesManagerAuditActivity()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);
        var request = NewRequest(now);
        request.MarkUnderReview(now.AddMinutes(1));
        request.MarkAssigned(now.AddMinutes(2));
        request.MarkInProgress(now.AddMinutes(3));
        request.Resolve(now.AddMinutes(4));
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();
        var managerId = Guid.NewGuid();

        var outcome = await new ManagerServiceRequestStore(db).CloseAsync(
            request.Id, managerId, "Đã nghiệm thu và bàn giao", now.AddMinutes(5), CancellationToken.None);

        Assert.Equal(ManagerServiceRequestOutcome.Success, outcome);
        Assert.Equal(ServiceRequestStatus.CLOSED, request.Status);
        Assert.Equal(managerId, request.ClosedBy);
        Assert.NotNull(request.ClosedAt);
        Assert.Contains(await db.ServiceRequestActivities.ToListAsync(), x =>
            x.ActivityType == ServiceActivityType.CLOSED &&
            x.FromStatus == ServiceRequestStatus.RESOLVED &&
            x.ToStatus == ServiceRequestStatus.CLOSED &&
            x.PerformedBy == managerId);
    }

    private static ServiceRequestsDbContext CreateDb() => new(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ServiceRequest NewRequest(DateTimeOffset now) => new(
        $"SR-{Guid.NewGuid():N}"[..20], Guid.NewGuid(), Guid.NewGuid(),
        "Kiểm tra rò rỉ nước", "Mô tả yêu cầu đủ dài để Manager xem xét.", now);

    private static async Task<(ServiceRequest Request, ServiceRequestAssignment Assignment)> AddInProgressRequestWithResultAsync(
        ServiceRequestsDbContext db,
        DateTimeOffset now)
    {
        var request = NewRequest(now);
        request.MarkUnderReview(now.AddMinutes(1));
        request.MarkAssigned(now.AddMinutes(2));
        request.MarkInProgress(now.AddMinutes(3));
        var assignment = new ServiceRequestAssignment(
            request.Id, Guid.NewGuid(), Guid.NewGuid(), now.AddMinutes(2));
        assignment.Start(now.AddMinutes(3));
        var result = new ServiceRequestActivity(
            request.Id,
            ServiceActivityType.WORK_RESULT_ADDED,
            now.AddMinutes(5),
            assignment.Id,
            workResult: "Đã xử lý hoàn tất và kiểm tra vận hành.",
            performedBy: assignment.StaffUserId);
        var evidence = new ServiceRequestEvidenceFile(
            request.Id, result.Id, "ket-qua.jpg", "image/jpeg", 1024,
            $"{request.Id:N}/ket-qua.jpg", assignment.StaffUserId, now.AddMinutes(5));
        db.AddRange(request, assignment, result, evidence);
        await db.SaveChangesAsync();
        return (request, assignment);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
