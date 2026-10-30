namespace PropFlow.Modules.Maintenance.Application;

/// <summary>
/// Coordinates the synchronous FE-07 task creation and FE-04 asset-status invariant
/// on one local PostgreSQL transaction.
/// </summary>
public interface IMaintenanceAssetTransactionCoordinator
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}
