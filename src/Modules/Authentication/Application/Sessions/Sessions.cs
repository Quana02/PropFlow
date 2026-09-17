using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Domain.Tokens;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    // FE-01.2
    public async Task<SessionIssue> LoginAsync(LoginRequest request, string? ip, CancellationToken ct)
    {
        var candidate = await store.UsernameAsync(request.Username.Trim(), ct);
        if (candidate == null) { secrets.VerifyPassword(null, request.Password); throw InvalidCredentials(); }
        var session = await store.SerializedAsync("user:" + candidate.Id, async () =>
        {
            var user = (await store.UserAsync(candidate.Id, ct))!;
            var valid = secrets.VerifyPassword(user, request.Password);
            if (!valid || user.Status != AccountStatus.ACTIVE || user.IsLockedOutAt(Now))
            {
                if (!valid && !user.IsLockedOutAt(Now)) user.RecordLoginFailure(policy.LockoutFailures, TimeSpan.FromMinutes(policy.LockoutMinutes), Now);
                return null;
            }
            var response = await AccountAsync(user, ct);
            user.RecordLoginSuccess(ip, Now);
            var token = secrets.NewToken();
            var expires = Now.AddDays(policy.RefreshDays);
            store.Add(new RefreshToken(user.Id, secrets.HashToken(token), expires, Now, ipAddress: ip));
            return new SessionIssue(secrets.Issue(response, Now), token, expires);
        }, ct);
        return session ?? throw InvalidCredentials();
    }

    public async Task<SessionIssue> RefreshAsync(string token, CancellationToken ct)
    {
        var hash = secrets.HashToken(token);
        var candidate = await store.RefreshAsync(hash, ct);
        if (candidate == null) throw InvalidCredentials();
        var result = await store.SerializedAsync("user:" + candidate.UserId, async () =>
        {
            var old = (await store.RefreshAsync(hash, ct))!;
            var user = (await store.UserAsync(old.UserId, ct))!;
            if (!old.IsActiveAt(Now) || user.Status != AccountStatus.ACTIVE || user.IsLockedOutAt(Now)) return null;
            var account = await AccountAsync(user, ct);
            var raw = secrets.NewToken();
            var replacement = new RefreshToken(user.Id, secrets.HashToken(raw), old.ExpiresAt, Now);
            store.Add(replacement);
            old.Revoke("rotation", Now, replacement.Id);
            return new SessionIssue(secrets.Issue(account, Now), raw, old.ExpiresAt);
        }, ct);
        return result ?? throw InvalidCredentials();
    }

    // FE-01.7: revoke the whole current rotation chain, never unrelated devices.
    public async Task LogoutAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token)) return;
        var candidate = await store.RefreshAsync(secrets.HashToken(token), ct);
        if (candidate == null) return;
        await store.SerializedAsync("user:" + candidate.UserId, async () =>
        {
            var current = await store.RefreshAsync(secrets.HashToken(token), ct);
            // Follow only this session's rotation chain; also handles a concurrent refresh in another tab.
            while (current?.ReplacedByTokenId is Guid next)
                current = await store.RefreshByIdAsync(next, ct);
            if (current is { IsRevoked: false }) current.Revoke("logout", Now);
            return true;
        }, ct);
    }
}
