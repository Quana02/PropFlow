using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Authentication.Infrastructure;

internal sealed class AccountSessionRevocation(IAuthStore store, TimeProvider clock) : IAccountSessionRevocation
{
    public Task<T> WithAccountLockAsync<T>(Guid userId, Func<Task<T>> operation, CancellationToken ct) =>
        store.SerializedAsync("user:" + userId, operation, ct);

    public async Task RevokeAsync(Guid userId, string reason, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        foreach (var token in await store.SessionsAsync(userId, ct))
            token.Revoke(reason, now);
    }
}
