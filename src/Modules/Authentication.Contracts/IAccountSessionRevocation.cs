namespace PropFlow.Modules.Authentication.Contracts;

public interface IAccountSessionRevocation
{
    Task<T> WithAccountLockAsync<T>(Guid userId, Func<Task<T>> operation, CancellationToken ct);
    Task RevokeAsync(Guid userId, string reason, CancellationToken ct);
}
