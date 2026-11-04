using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

/// <summary>Enlists FE-07 and FE-04 writes in one local PostgreSQL transaction.</summary>
public sealed class MaintenanceAssetTransactionCoordinator(
    MaintenanceDbContext maintenance,
    PropertyAssetsDbContext assets) : IMaintenanceAssetTransactionCoordinator
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await maintenance.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await maintenance.Database.BeginTransactionAsync(cancellationToken);
        await assets.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        try
        {
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            await assets.Database.UseTransactionAsync(null, cancellationToken);
        }
    }
}
