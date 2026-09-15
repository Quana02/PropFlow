using PropFlow.Modules.Complaints.Domain.ComplaintFollowups;
using PropFlow.Modules.Complaints.Domain.Complaints;

namespace PropFlow.Modules.Complaints.Domain.ComplaintActivities;

public class ComplaintActivity
{
    private ComplaintActivity()
    {
    }

    public ComplaintActivity(
        Guid complaintId,
        ComplaintActivityType activityType,
        DateTimeOffset now,
        Guid? followupId = null,
        ComplaintStatus? fromStatus = null,
        ComplaintStatus? toStatus = null,
        string? detail = null,
        Guid? performedBy = null)
    {
        if (complaintId == Guid.Empty)
            throw new ArgumentException("ComplaintId cannot be empty.", nameof(complaintId));

        Id = Guid.NewGuid();
        ComplaintId = complaintId;
        FollowupId = followupId;
        ActivityType = activityType;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        PerformedBy = performedBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ComplaintId { get; private set; }
    public Guid? FollowupId { get; private set; }
    public ComplaintActivityType ActivityType { get; private set; }
    public ComplaintStatus? FromStatus { get; private set; }
    public ComplaintStatus? ToStatus { get; private set; }
    public string? Detail { get; private set; }
    public Guid? PerformedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public Complaint Complaint { get; private set; } = null!;
    public ComplaintFollowup? Followup { get; private set; }
}
