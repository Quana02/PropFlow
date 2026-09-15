using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;

public class ServiceRequestActivity
{
    private ServiceRequestActivity()
    {
    }

    public ServiceRequestActivity(
        Guid serviceRequestId,
        ServiceActivityType activityType,
        DateTimeOffset now,
        Guid? assignmentId = null,
        ServiceRequestStatus? fromStatus = null,
        ServiceRequestStatus? toStatus = null,
        string? title = null,
        string? detail = null,
        string? workResult = null,
        Guid? performedBy = null)
    {
        if (serviceRequestId == Guid.Empty)
            throw new ArgumentException("ServiceRequestId cannot be empty.", nameof(serviceRequestId));

        Id = Guid.NewGuid();
        ServiceRequestId = serviceRequestId;
        AssignmentId = assignmentId;
        ActivityType = activityType;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        WorkResult = string.IsNullOrWhiteSpace(workResult) ? null : workResult.Trim();
        PerformedBy = performedBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ServiceRequestId { get; private set; }
    public Guid? AssignmentId { get; private set; }
    public ServiceActivityType ActivityType { get; private set; }
    public ServiceRequestStatus? FromStatus { get; private set; }
    public ServiceRequestStatus? ToStatus { get; private set; }
    public string? Title { get; private set; }
    public string? Detail { get; private set; }
    public string? WorkResult { get; private set; }
    public Guid? PerformedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public ServiceRequest ServiceRequest { get; private set; } = null!;
    public ServiceRequestAssignment? Assignment { get; private set; }
}
