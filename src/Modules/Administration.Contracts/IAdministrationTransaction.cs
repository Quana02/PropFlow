namespace PropFlow.Modules.Administration.Contracts;

public interface IAdministrationTransaction
{
    Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken ct);
}
