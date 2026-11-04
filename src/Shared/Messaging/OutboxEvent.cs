namespace PropFlow.Shared.Messaging;

// Technical delivery envelope. Each owning module maps its own outbox table.
public sealed class OutboxEvent
{
    private OutboxEvent() { }
    public OutboxEvent(string name, Guid sourceId, string payload, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); CorrelationId = Id; Name = name; SourceId = sourceId;
        Payload = payload; CreatedAt = NextAttemptAt = now;
    }
    public Guid Id { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid SourceId { get; private set; }
    public string Name { get; private set; } = "";
    public int Version { get; private set; } = 1;
    public string Payload { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int Attempts { get; private set; }
    public string Status { get; private set; } = "Pending";
    public string? LastErrorType { get; private set; }
    public void Delivered(DateTimeOffset now) { Status = "Delivered"; DeliveredAt = now; LastErrorType = null; }
    public void Failed(string errorType, DateTimeOffset now) {
        Attempts++; LastErrorType = errorType; Status = Attempts >= 8 ? "Failed" : "Pending";
        NextAttemptAt = now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, Attempts)));
    }
}
