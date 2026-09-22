namespace PropFlow.Modules.ServiceRequests.Contracts;

public interface IServiceRequestOverviewSource
{
    Task<int> CountOpenAsync(CancellationToken ct);
}
