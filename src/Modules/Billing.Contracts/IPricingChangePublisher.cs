namespace PropFlow.Modules.Billing.Contracts;

public sealed record PricingChangeNotice(Guid EventId, string Title, string Message, DateTimeOffset CreatedAt);
public interface IPricingChangePublisher
{
    Task PublishAsync(PricingChangeNotice notice, CancellationToken ct);
}
