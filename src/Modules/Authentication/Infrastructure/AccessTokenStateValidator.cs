using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.Modules.Authentication.Infrastructure;

public interface IAccessSessionReader
{
    Task<AccessSessionState?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct);
    async Task<IReadOnlyDictionary<AccessSessionKey, AccessSessionState>> FindManyAsync(
        IReadOnlyCollection<AccessSessionKey> keys, CancellationToken ct)
    {
        var result = new Dictionary<AccessSessionKey, AccessSessionState>();
        foreach (var key in keys)
            if (await FindAsync(key.UserId, key.SessionId, ct) is { } value)
                result[key] = value;
        return result;
    }
}

public readonly record struct AccessSessionKey(Guid UserId, Guid SessionId);
public sealed record AccessSessionState(string? Email, string Fingerprint);

internal sealed class AccessSessionReader(AuthenticationDbContext db, TimeProvider clock) : IAccessSessionReader
{
    public async Task<AccessSessionState?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct)
    {
        var key = new AccessSessionKey(userId, sessionId);
        return (await FindManyAsync([key], ct)).GetValueOrDefault(key);
    }

    public async Task<IReadOnlyDictionary<AccessSessionKey, AccessSessionState>> FindManyAsync(
        IReadOnlyCollection<AccessSessionKey> keys, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var users = keys.Select(key => key.UserId).Distinct().ToArray();
        var sessions = keys.Select(key => (Guid?)key.SessionId).Distinct().ToArray();
        return await db.RefreshTokens.AsNoTracking()
            .Where(token => users.Contains(token.UserId) && token.SessionId != null && sessions.Contains(token.SessionId)
                && token.RevokedAt == null && token.ExpiresAt > now && token.AccessFingerprint != null
                && token.User!.Status == AccountStatus.ACTIVE)
            .Select(token => new { token.UserId, token.SessionId, token.User!.Email, token.AccessFingerprint })
            .ToDictionaryAsync(row => new AccessSessionKey(row.UserId, row.SessionId!.Value),
                row => new AccessSessionState(row.Email, row.AccessFingerprint!), ct);
    }
}

public sealed class AccessTokenStateValidator(IAccessSessionReader sessions, IAccountAccess access)
{
    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (GetKey(principal) is not { } key) return false;
        var currentAccess = await access.GetAsync(key.UserId, ct);
        var session = await sessions.FindAsync(key.UserId, key.SessionId, ct);
        return Matches(principal, currentAccess, session);
    }

    public async Task<HashSet<ClaimsPrincipal>> CurrentAsync(IReadOnlyCollection<ClaimsPrincipal> principals, CancellationToken ct)
    {
        var candidates = principals.Select(principal => (Principal: principal, Key: GetKey(principal)))
            .Where(candidate => candidate.Key.HasValue)
            .Select(candidate => (candidate.Principal, Key: candidate.Key!.Value))
            .ToArray();
        if (candidates.Length == 0) return [];
        var keys = candidates.Select(candidate => candidate.Key).Distinct().ToArray();
        var currentAccess = await access.GetManyAsync(keys.Select(key => key.UserId).Distinct().ToArray(), ct);
        var activeSessions = await sessions.FindManyAsync(keys, ct);
        return candidates.Where(candidate => Matches(candidate.Principal,
                currentAccess.GetValueOrDefault(candidate.Key.UserId), activeSessions.GetValueOrDefault(candidate.Key)))
            .Select(candidate => candidate.Principal).ToHashSet();
    }

    public static AccessSessionKey? GetKey(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId) && userId != Guid.Empty
        && Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessionId) && sessionId != Guid.Empty
            ? new(userId, sessionId)
            : null;

    private static bool Matches(ClaimsPrincipal principal, AccountAccess? currentAccess, AccessSessionState? session)
    {
        if (session is null) return false;
        var tokenRole = principal.FindFirst("role")?.Value;
        if (currentAccess is null || !string.Equals(currentAccess.Role, tokenRole, StringComparison.Ordinal)) return false;
        var tokenPermissions = principal.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        if (!tokenPermissions.SetEquals(currentAccess.Permissions)) return false;

        return string.Equals(session.Email, principal.FindFirst("email")?.Value, StringComparison.OrdinalIgnoreCase)
            && session.Fingerprint == SessionAccess.Fingerprint(currentAccess.Role, currentAccess.Permissions);
    }
}
