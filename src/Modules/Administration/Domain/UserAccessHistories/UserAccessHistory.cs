using PropFlow.Modules.Administration.Domain.Roles;

namespace PropFlow.Modules.Administration.Domain.UserAccessHistories;

public class UserAccessHistory
{
    private UserAccessHistory()
    {
    }

    public UserAccessHistory(
        Guid targetUserId,
        AccessActionType action,
        DateTimeOffset now,
        Guid? oldRoleId = null,
        Guid? newRoleId = null,
        AccountStatus? oldStatus = null,
        AccountStatus? newStatus = null,
        string? reason = null,
        Guid? performedBy = null,
        string? metadata = null)
    {
        if (targetUserId == Guid.Empty)
            throw new ArgumentException("TargetUserId cannot be empty.", nameof(targetUserId));

        Id = Guid.NewGuid();
        TargetUserId = targetUserId;
        Action = action;
        OldRoleId = oldRoleId;
        NewRoleId = newRoleId;
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Reason = reason?.Trim();
        PerformedBy = performedBy;
        Metadata = metadata;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid TargetUserId { get; private set; }
    public AccessActionType Action { get; private set; }
    public Guid? OldRoleId { get; private set; }
    public Guid? NewRoleId { get; private set; }
    public AccountStatus? OldStatus { get; private set; }
    public AccountStatus? NewStatus { get; private set; }
    public string? Reason { get; private set; }
    public Guid? PerformedBy { get; private set; }
    public string? Metadata { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Within-module navigations
    public Role? OldRole { get; private set; }
    public Role? NewRole { get; private set; }
}
