namespace PropFlow.Modules.Residents.Contracts;

/// <summary>Technical local-transaction boundary for a single PostgreSQL connection.</summary>
public interface IAtomicTransactionCoordinator
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
