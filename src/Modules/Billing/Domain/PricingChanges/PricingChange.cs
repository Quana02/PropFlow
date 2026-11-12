namespace PropFlow.Modules.Billing.Domain.PricingChanges;

public sealed class PricingChange
{
    private PricingChange() { }
    public PricingChange(string title, string message, Guid actor, DateTimeOffset now)
    {
        Id = Guid.NewGuid(); CorrelationId = Id; Title = title; Message = message;
        ActorId = actor; CreatedAt = NextAttemptAt = now;
    }
    public Guid Id { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid ActorId { get; private set; }
    public string EventName { get; private set; } = "PricingChanged";
    public int Version { get; private set; } = 1;
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public int Attempts { get; private set; }
    public string Status { get; private set; } = "Pending";
    public string? LastErrorType { get; private set; }
    public void Delivered(DateTimeOffset now) { DeliveredAt = now; Status = "Delivered"; LastErrorType = null; }
    public void Failed(string errorType, DateTimeOffset now)
    {
        Attempts++; LastErrorType = errorType; Status = Attempts >= 8 ? "Failed" : "Pending";
        NextAttemptAt = now.AddSeconds(Math.Min(3600, 10 * Math.Pow(2, Attempts)));
    }
}
