using System.Security.Claims;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Infrastructure;

namespace PropFlow.UnitTests;

public sealed class AccessTokenStateValidatorTests
{
    [Theory]
    [InlineData("ACTIVE", "ADMIN", "ADMIN", true)]
    [InlineData("DISABLED", "ADMIN", "ADMIN", false)]
    [InlineData("LOCKED", "ADMIN", "ADMIN", false)]
    [InlineData("ACTIVE", "MANAGER", "ADMIN", false)]
    public async Task IsCurrentAsync_checks_account_status_and_current_role(
        string status, string currentRole, string tokenRole, bool expected)
    {
        var userId = Guid.NewGuid();
        var permissions = new[] { "administration.view-system-overview" };
        var validator = new AccessTokenStateValidator(
            new Sessions(status == "ACTIVE" ? new("admin@example.com", SessionAccess.Fingerprint(currentRole, permissions)) : null),
            new Access(new AccountAccess(currentRole, permissions)));

        var principal = Principal(userId, tokenRole, permissions);

        Assert.Equal(expected, await validator.IsCurrentAsync(principal, CancellationToken.None));
    }

    [Fact]
    public async Task IsCurrentAsync_rejects_token_when_effective_permissions_changed()
    {
        var userId = Guid.NewGuid();
        var validator = new AccessTokenStateValidator(
            new Sessions(new("admin@example.com", SessionAccess.Fingerprint("ADMIN", ["administration.manage-internal-accounts"]))),
            new Access(new AccountAccess("ADMIN", ["administration.view-system-overview"])));

        var principal = Principal(userId, "ADMIN", ["administration.manage-internal-accounts"]);

        Assert.False(await validator.IsCurrentAsync(principal, CancellationToken.None));
    }

    [Fact]
    public async Task IsCurrentAsync_rejects_token_issued_before_email_changed()
    {
        var id = Guid.NewGuid();
        var validator = new AccessTokenStateValidator(
            new Sessions(new("new@example.com", SessionAccess.Fingerprint("ADMIN", []))),
            new Access(new AccountAccess("ADMIN", [])));
        Assert.False(await validator.IsCurrentAsync(Principal(id, "ADMIN", []), CancellationToken.None));
    }

    private static ClaimsPrincipal Principal(Guid userId, string role, IEnumerable<string> permissions)
    {
        var claims = new List<Claim> { new("sub", userId.ToString()), new("sid", Guid.NewGuid().ToString()), new("role", role), new("email", "admin@example.com") };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed class Sessions(AccessSessionState? session) : IAccessSessionReader
    {
        public Task<AccessSessionState?> FindAsync(Guid userId, Guid sessionId, CancellationToken ct) => Task.FromResult(session);
    }

    private sealed class Access(AccountAccess access) : IAccountAccess
    {
        public Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult<AccountAccess?>(access);
        public Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<Guid[]> GetResidentUserIdsAsync(Guid[] userIds, CancellationToken ct) => throw new NotSupportedException();
    }
}
