using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

namespace PropFlow.Modules.Maintenance.Domain.MaintenanceAssignments;

public class MaintenanceAssignment
{
    private MaintenanceAssignment()
    {
    }

    public MaintenanceAssignment(
        Guid maintenanceTaskId,
        Guid staffUserId,
        Guid assignedBy,
        DateTimeOffset now,
        string? assignmentNote = null)
    {
        ThrowIfEmpty(maintenanceTaskId, nameof(maintenanceTaskId));
        ThrowIfEmpty(staffUserId, nameof(staffUserId));
        ThrowIfEmpty(assignedBy, nameof(assignedBy));

        Id = Guid.NewGuid();
        MaintenanceTaskId = maintenanceTaskId;
        StaffUserId = staffUserId;
        AssignedBy = assignedBy;
        Status = AssignmentStatus.ASSIGNED;
        AssignmentNote = string.IsNullOrWhiteSpace(assignmentNote) ? null : assignmentNote.Trim();
        AssignedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid MaintenanceTaskId { get; private set; }
    public Guid StaffUserId { get; private set; }
    public Guid AssignedBy { get; private set; }
    public AssignmentStatus Status { get; private set; }
    public string? AssignmentNote { get; private set; }
    public DateTimeOffset AssignedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    public bool IsActive => Status is AssignmentStatus.ASSIGNED or AssignmentStatus.IN_PROGRESS;

    public MaintenanceTask? MaintenanceTask { get; private set; }

    public void Start(DateTimeOffset now)
    {
        EnsureActive();
        if (now < AssignedAt)
            throw new ArgumentException("Started time cannot be before assigned time.", nameof(now));

        Status = AssignmentStatus.IN_PROGRESS;
        StartedAt ??= now;
    }

    public void Complete(DateTimeOffset now)
    {
        EnsureActive();
        if (StartedAt is not null && now < StartedAt)
            throw new ArgumentException("Completed time cannot be before started time.", nameof(now));

        Status = AssignmentStatus.COMPLETED;
        CompletedAt = now;
        EndedAt = now;
    }

    public void Reassign(DateTimeOffset now)
    {
        EnsureActive();
        if (now < AssignedAt)
            throw new ArgumentException("Ended time cannot be before assigned time.", nameof(now));

        Status = AssignmentStatus.REASSIGNED;
        EndedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureActive();
        if (now < AssignedAt)
            throw new ArgumentException("Ended time cannot be before assigned time.", nameof(now));

        Status = AssignmentStatus.CANCELLED;
        EndedAt = now;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Only active maintenance assignments can be modified.");
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
