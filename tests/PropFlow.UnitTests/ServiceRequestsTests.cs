using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestCategories;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.UnitTests;

public class ServiceRequestsTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ServiceRequestCategory_Constructor_NormalizesCodeAndSetsAudit()
    {
        var category = new ServiceRequestCategory(" plumbing ", "Plumbing", _now, "Water issues", displayOrder: 2);

        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal("PLUMBING", category.Code);
        Assert.Equal("Plumbing", category.Name);
        Assert.True(category.IsActive);
        Assert.Equal(2, category.DisplayOrder);
        Assert.Equal(_now, category.CreatedAt);
        Assert.Throws<ArgumentException>(() => new ServiceRequestCategory("", "Name", _now));
    }

    [Fact]
    public void ServiceRequest_Constructor_CreatesSubmittedRequestWithScalarReferences()
    {
        var request = NewServiceRequest();

        Assert.NotEqual(Guid.Empty, request.Id);
        Assert.Equal(ServiceRequestStatus.SUBMITTED, request.Status);
        Assert.Equal(_now, request.SubmittedAt);
        Assert.Null(request.ResolvedAt);
        Assert.Null(request.ClosedAt);
        Assert.Throws<ArgumentException>(() => new ServiceRequest("SR-2", Guid.Empty, Guid.NewGuid(), "Title", "Description", _now));
    }

    [Fact]
    public void ServiceRequest_Lifecycle_RecordsResolvedAndClosedTimestamps()
    {
        var request = NewServiceRequest();
        var resolvedAt = _now.AddHours(2);
        var closedAt = _now.AddHours(3);
        var closedBy = Guid.NewGuid();

        request.MarkUnderReview(_now.AddMinutes(10));
        request.MarkAssigned(_now.AddMinutes(20));
        request.MarkInProgress(_now.AddMinutes(30));
        request.Resolve(resolvedAt);
        request.Close(closedBy, closedAt);

        Assert.Equal(ServiceRequestStatus.CLOSED, request.Status);
        Assert.Equal(resolvedAt, request.ResolvedAt);
        Assert.Equal(closedAt, request.ClosedAt);
        Assert.Equal(closedBy, request.ClosedBy);
        Assert.Throws<InvalidOperationException>(() => request.UpdateDetails("New", "New desc", null, null, closedAt.AddMinutes(1)));
    }

    [Fact]
    public void ServiceRequest_Close_RequiresResolvedStatus()
    {
        var request = NewServiceRequest();

        request.MarkUnderReview(_now.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(() => request.Close(Guid.NewGuid(), _now.AddMinutes(20)));
    }

    [Fact]
    public void ServiceRequestAssignment_Lifecycle_AllowsOnlyActiveTransitions()
    {
        var assignment = new ServiceRequestAssignment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _now, "Initial");
        var startedAt = _now.AddMinutes(10);
        var completedAt = _now.AddHours(1);

        assignment.Start(startedAt);
        assignment.Complete(completedAt);

        Assert.Equal(AssignmentStatus.COMPLETED, assignment.Status);
        Assert.Equal(startedAt, assignment.StartedAt);
        Assert.Equal(completedAt, assignment.CompletedAt);
        Assert.Equal(completedAt, assignment.EndedAt);
        Assert.False(assignment.IsActive);
        Assert.Throws<InvalidOperationException>(() => assignment.Cancel(completedAt.AddMinutes(1)));
    }

    [Fact]
    public void ServiceRequestActivity_IsAppendOnlySnapshot()
    {
        var requestId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        var activity = new ServiceRequestActivity(
            requestId,
            ServiceActivityType.STATUS_CHANGED,
            _now,
            assignmentId,
            ServiceRequestStatus.SUBMITTED,
            ServiceRequestStatus.UNDER_REVIEW,
            "Review",
            "Manager reviewed",
            performedBy: actor);

        Assert.NotEqual(Guid.Empty, activity.Id);
        Assert.Equal(requestId, activity.ServiceRequestId);
        Assert.Equal(assignmentId, activity.AssignmentId);
        Assert.Equal(ServiceActivityType.STATUS_CHANGED, activity.ActivityType);
        Assert.Equal(ServiceRequestStatus.SUBMITTED, activity.FromStatus);
        Assert.Equal(ServiceRequestStatus.UNDER_REVIEW, activity.ToStatus);
        Assert.Equal(actor, activity.PerformedBy);
    }

    private ServiceRequest NewServiceRequest()
    {
        return new ServiceRequest(
            "SR-2026-0001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            
            "Leaking pipe",
            "Water is leaking near the kitchen",
            _now,
            apartmentUnitId: Guid.NewGuid(),
            categoryId: Guid.NewGuid(),
            facilityId: Guid.NewGuid(),
            equipmentId: Guid.NewGuid(),
            finalPriorityCode: "high");
    }
}
