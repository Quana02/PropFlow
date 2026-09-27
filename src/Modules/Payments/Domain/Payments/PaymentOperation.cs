namespace PropFlow.Modules.Payments.Domain.Payments;

public sealed class PaymentOperation
{
    private PaymentOperation() { }
    public PaymentOperation(string key, Guid paymentId, string action, string payloadHash, DateTimeOffset now)
    { Id = Guid.NewGuid(); Key = key; PaymentId = paymentId; Action = action; PayloadHash = payloadHash; CreatedAt = now; }
    public Guid Id { get; private set; }
    public string Key { get; private set; } = null!;
    public Guid PaymentId { get; private set; }
    public string Action { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
