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
            var refresh = new RefreshToken(user.Id, secrets.HashToken(token), expires, Now, ipAddress: ip,
                accessFingerprint: SessionAccess.Fingerprint(response.Role, response.Permissions));
            store.Add(refresh);
            return new SessionIssue(secrets.Issue(response, Now, refresh.SessionId), token, expires);
        }, ct);
        return session ?? throw InvalidCredentials();
    }

    public async Task<SessionIssue> RefreshAsync(string token, CancellationToken ct, Guid? expectedSessionId = null)
    {
        var hash = secrets.HashToken(token);
        var candidate = await store.RefreshAsync(hash, ct);
        if (candidate == null) throw InvalidCredentials();
        var result = await store.SerializedAsync("user:" + candidate.UserId, async () =>
        {
            var old = (await store.RefreshAsync(hash, ct))!;
            var user = (await store.UserAsync(old.UserId, ct))!;
            if (old.SessionId is not Guid sessionId || (expectedSessionId is Guid expected && sessionId != expected)
                || !old.IsActiveAt(Now)
                || user.Status != AccountStatus.ACTIVE || user.IsLockedOutAt(Now)) return null;
            AccountResponse account;
            try { account = await AccountAsync(user, ct); }
            catch (AuthFailure failure) when (failure.Code == "access_unavailable")
            {
                old.Revoke("access_changed", Now);
                return null;
            }
            var fingerprint = SessionAccess.Fingerprint(account.Role, account.Permissions);
            if (old.AccessFingerprint != fingerprint)
            {
                old.Revoke("access_changed", Now);
                return null;
            }
            var raw = secrets.NewToken();
            var replacement = new RefreshToken(user.Id, secrets.HashToken(raw), old.ExpiresAt, Now,
                sessionId: sessionId, accessFingerprint: fingerprint);
            store.Add(replacement);
            old.Revoke("rotation", Now, replacement.Id);
            return new SessionIssue(secrets.Issue(account, Now, sessionId), raw, old.ExpiresAt);
        }, ct);
        return result ?? throw InvalidCredentials();
    }

    // FE-01.7: revoke the whole current rotation chain, never unrelated devices.
    public async Task<bool> LogoutAsync(string? token, CancellationToken ct, Guid? expectedSessionId = null)
    {
        if (string.IsNullOrEmpty(token)) return true;
        var candidate = await store.RefreshAsync(secrets.HashToken(token), ct);
        if (candidate?.SessionId is not Guid sessionId) return true;
        if (expectedSessionId is Guid expected && sessionId != expected) return false;
        await store.SerializedAsync("user:" + candidate.UserId, async () =>
        {
            // Find the live leaf after acquiring the same lock as rotation, including when the cookie is stale.
            var current = await store.CurrentRefreshAsync(candidate.UserId, sessionId, ct);
            current?.Revoke("logout", Now);
            return true;
        }, ct);
        invalidation?.Invalidate(candidate.UserId, sessionId);
        return true;
    }
}
