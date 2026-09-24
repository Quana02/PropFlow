using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.Api.Composition;

public sealed class AdministrationTransaction(
    AuthenticationDbContext authentication,
    AdministrationDbContext administration) : IAdministrationTransaction
{
    public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await authentication.Database.OpenConnectionAsync(ct);
        await using var transaction = await authentication.Database.BeginTransactionAsync(ct);
        await administration.Database.UseTransactionAsync(transaction.GetDbTransaction(), ct);

        try
        {
            await operation(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
        finally
        {
            await administration.Database.UseTransactionAsync(null, ct);
        }
    }
}
