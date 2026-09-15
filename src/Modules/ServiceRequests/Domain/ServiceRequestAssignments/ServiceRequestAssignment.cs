using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;

public class ServiceRequestAssignment
{
    private ServiceRequestAssignment()
    {
    }

    public ServiceRequestAssignment(Guid serviceRequestId, Guid staffUserId, Guid assignedBy, DateTimeOffset now, string? assignmentNote = null)
    {
        ThrowIfEmpty(serviceRequestId, nameof(serviceRequestId));
        ThrowIfEmpty(staffUserId, nameof(staffUserId));
        ThrowIfEmpty(assignedBy, nameof(assignedBy));

        Id = Guid.NewGuid();
        ServiceRequestId = serviceRequestId;
        StaffUserId = staffUserId;
        AssignedBy = assignedBy;
        Status = AssignmentStatus.ASSIGNED;
        AssignmentNote = string.IsNullOrWhiteSpace(assignmentNote) ? null : assignmentNote.Trim();
        AssignedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ServiceRequestId { get; private set; }
    public Guid StaffUserId { get; private set; }
    public Guid AssignedBy { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public string? AssignmentNote { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public ServiceRequest ServiceRequest { get; private set; } = null!;

    public bool IsActive => Status is AssignmentStatus.ASSIGNED or AssignmentStatus.IN_PROGRESS;

    public void Start(DateTimeOffset now)
    {
        EnsureActive();
        Status = AssignmentStatus.IN_PROGRESS;
        StartedAt ??= now;
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureActive();
        Status = AssignmentStatus.COMPLETED;
        CompletedAt = now;
        EndedAt = now;
    }

    public void EndAsReassigned(DateTimeOffset now)
    {
        EnsureActive();
        Status = AssignmentStatus.REASSIGNED;
        EndedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureActive();
        Status = AssignmentStatus.CANCELLED;
        EndedAt = now;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Only active assignments can change state.");
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
