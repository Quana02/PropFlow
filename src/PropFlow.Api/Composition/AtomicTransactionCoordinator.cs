using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

/// <summary>Enlists the FE-02 and FE-03 DbContexts in one explicit local PostgreSQL transaction.</summary>
public sealed class AtomicTransactionCoordinator(ResidentsDbContext residents, ApartmentsDbContext apartments) : IAtomicTransactionCoordinator
{
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await residents.Database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await residents.Database.BeginTransactionAsync(cancellationToken);
        await apartments.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
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
            await apartments.Database.UseTransactionAsync(null, cancellationToken);
        }
    }
}
