namespace PropFlow.Modules.Administration.Domain.AuditLogs;

public class AuditLog
{
    private AuditLog()
    {
    }

    public AuditLog(
        string action,
        string entityType,
        DateTimeOffset now,
        Guid? actorUserId = null,
        Guid? entityId = null,
        string? oldValues = null,
        string? newValues = null,
        string? ipAddress = null,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Audit action is required.", nameof(action));

        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Entity type is required.", nameof(entityType));

        Id = Guid.NewGuid();
        Action = action.Trim();
        EntityType = entityType.Trim();
        ActorUserId = actorUserId;
        EntityId = entityId;
        OldValues = oldValues;
        NewValues = newValues;
        IpAddress = ipAddress?.Trim();
        CorrelationId = correlationId?.Trim();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid? EntityId { get; private set; }
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
