namespace PropFlow.Modules.Billing.Application.Finance;

public interface IPricingChangeDelivery
{
    // A delivery failure must not turn a committed price change into a failed save.
    Task TryDispatchAsync(CancellationToken ct);
}
