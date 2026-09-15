namespace PropFlow.Modules.Administration.Domain.UserBuildingAccesses;

public class UserBuildingAccess
{
    private UserBuildingAccess()
    {
    }

    public UserBuildingAccess(Guid userId, Guid buildingId, DateTimeOffset now, Guid? grantedBy = null, string? reason = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be empty.", nameof(userId));

        if (buildingId == Guid.Empty)
            throw new ArgumentException("BuildingId cannot be empty.", nameof(buildingId));

        Id = Guid.NewGuid();
        UserId = userId;
        BuildingId = buildingId;
        GrantedBy = grantedBy;
        GrantedAt = now;
        Reason = reason?.Trim();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid BuildingId { get; private set; }
    public Guid? GrantedBy { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
    public Guid? RevokedBy { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Revoke(Guid revokedBy, DateTimeOffset now, string? reason = null)
    {
        if (revokedBy == Guid.Empty)
            throw new ArgumentException("RevokedBy cannot be empty.", nameof(revokedBy));

        RevokedBy = revokedBy;
        RevokedAt = now;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            Reason = reason.Trim();
        }
    }
}
