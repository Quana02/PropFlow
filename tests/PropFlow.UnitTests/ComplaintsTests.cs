using PropFlow.Modules.Complaints.Domain.ComplaintActivities;
using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;
using PropFlow.Modules.Complaints.Domain.Complaints;

namespace PropFlow.UnitTests;

public class ComplaintsTests
{
    private readonly DateTimeOffset _now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Complaint_Constructor_CreatesSubmittedComplaintWithScalarServiceRequestReference()
    {
        var relatedServiceRequestId = Guid.NewGuid();
        var complaint = NewComplaint(relatedServiceRequestId);

        Assert.NotEqual(Guid.Empty, complaint.Id);
        Assert.Equal(ComplaintStatus.SUBMITTED, complaint.Status);
        Assert.Equal(relatedServiceRequestId, complaint.RelatedServiceRequestId);
        Assert.Null(complaint.OfficialResponse);
        Assert.Throws<ArgumentException>(() => new Complaint("CP-2", Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "Subject", "Description", _now));
    }

    [Fact]
    public void Complaint_Lifecycle_RecordsOfficialResponseResolvedAndClosed()
    {
        var complaint = NewComplaint();
        var reviewedAt = _now.AddMinutes(30);
        var resolvedAt = _now.AddHours(2);
        var closedAt = _now.AddHours(3);
        var closedBy = Guid.NewGuid();

        complaint.MarkUnderReview(reviewedAt);
        complaint.AddOfficialResponse("We investigated and resolved the issue.", resolvedAt);
        complaint.Resolve(resolvedAt);
        complaint.Close(closedBy, closedAt);

        Assert.Equal(ComplaintStatus.CLOSED, complaint.Status);
        Assert.Equal("We investigated and resolved the issue.", complaint.OfficialResponse);
        Assert.Equal(resolvedAt, complaint.ResolvedAt);
        Assert.Equal(closedAt, complaint.ClosedAt);
        Assert.Equal(closedBy, complaint.ClosedBy);
        Assert.Throws<InvalidOperationException>(() => complaint.Cancel(closedAt.AddMinutes(1)));
    }

    [Fact]
    public void Complaint_Close_RequiresResolvedStatusAndOfficialResponse()
    {
        var complaint = NewComplaint();
        complaint.MarkUnderReview(_now.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(() => complaint.Close(Guid.NewGuid(), _now.AddMinutes(20)));

        complaint.Resolve(_now.AddMinutes(30));

        Assert.Throws<InvalidOperationException>(() => complaint.Close(Guid.NewGuid(), _now.AddMinutes(40)));
    }

    [Fact]
    public void ComplaintFollowup_Lifecycle_AllowsOnlyActiveTransitions()
    {
        var followup = new ComplaintFollowup(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Check lobby camera", _now, dueAt: _now.AddDays(1));
        var startedAt = _now.AddMinutes(20);
        var completedAt = _now.AddHours(4);

        followup.Start(startedAt);
        followup.Complete("Camera checked", completedAt);

        Assert.Equal(ComplaintFollowupStatus.COMPLETED, followup.Status);
        Assert.Equal(startedAt, followup.StartedAt);
        Assert.Equal(completedAt, followup.CompletedAt);
        Assert.Equal("Camera checked", followup.Result);
        Assert.False(followup.IsActive);
        Assert.Throws<InvalidOperationException>(() => followup.Cancel(completedAt.AddMinutes(1)));
    }

    [Fact]
    public void ComplaintActivity_IsAppendOnlySnapshot()
    {
        var complaintId = Guid.NewGuid();
        var followupId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        var activity = new ComplaintActivity(
            complaintId,
            ComplaintActivityType.STATUS_CHANGED,
            _now,
            followupId,
            ComplaintStatus.SUBMITTED,
            ComplaintStatus.UNDER_REVIEW,
            "Manager reviewed",
            actor);

        Assert.NotEqual(Guid.Empty, activity.Id);
        Assert.Equal(complaintId, activity.ComplaintId);
        Assert.Equal(followupId, activity.FollowupId);
        Assert.Equal(ComplaintActivityType.STATUS_CHANGED, activity.ActivityType);
        Assert.Equal(ComplaintStatus.SUBMITTED, activity.FromStatus);
        Assert.Equal(ComplaintStatus.UNDER_REVIEW, activity.ToStatus);
        Assert.Equal(actor, activity.PerformedBy);
    }

    private Complaint NewComplaint(Guid? relatedServiceRequestId = null)
    {
        return new Complaint(
            "CP-2026-0001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Noise complaint",
            "Persistent noise after quiet hours",
            _now,
            apartmentUnitId: Guid.NewGuid(),
            relatedServiceRequestId: relatedServiceRequestId,
            facilityId: Guid.NewGuid(),
            equipmentId: Guid.NewGuid());
    }
}
