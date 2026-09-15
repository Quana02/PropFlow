using PropFlow.Modules.Complaints.Domain.Complaints;

namespace PropFlow.Modules.Complaints.Domain.ComplaintFollowups;

public class ComplaintFollowup
{
    private ComplaintFollowup()
    {
    }

    public ComplaintFollowup(
        Guid complaintId,
        Guid staffUserId,
        Guid assignedBy,
        string title,
        DateTimeOffset now,
        string? instruction = null,
        DateTimeOffset? dueAt = null)
    {
        ThrowIfEmpty(complaintId, nameof(complaintId));
        ThrowIfEmpty(staffUserId, nameof(staffUserId));
        ThrowIfEmpty(assignedBy, nameof(assignedBy));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = Guid.NewGuid();
        ComplaintId = complaintId;
        StaffUserId = staffUserId;
        AssignedBy = assignedBy;
        Title = title.Trim();
        Instruction = string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim();
        Status = ComplaintFollowupStatus.ASSIGNED;
        DueAt = dueAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ComplaintId { get; private set; }
    public Guid StaffUserId { get; private set; }
    public Guid AssignedBy { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Instruction { get; private set; }
    public ComplaintFollowupStatus Status { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? Result { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Complaint Complaint { get; private set; } = null!;

    public bool IsActive => Status is ComplaintFollowupStatus.ASSIGNED or ComplaintFollowupStatus.IN_PROGRESS;

    public void Start(DateTimeOffset now)
    {
        EnsureActive();
        Status = ComplaintFollowupStatus.IN_PROGRESS;
        StartedAt ??= now;
        UpdatedAt = now;
    }

    public void Complete(string? result, DateTimeOffset now)
    {
        EnsureActive();
        Status = ComplaintFollowupStatus.COMPLETED;
        Result = string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        EnsureActive();
        Status = ComplaintFollowupStatus.CANCELLED;
        UpdatedAt = now;
    }

    private void EnsureActive()
    {
        if (!IsActive)
            throw new InvalidOperationException("Only active follow-ups can change state.");
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
