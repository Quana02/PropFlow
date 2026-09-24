using System.Security.Claims;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Contracts;
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
            new Accounts(new InternalAccountRecord(userId, "admin", "Admin", "admin@example.com", status)),
            new Access(new AccountAccess(currentRole, permissions)));

        var principal = Principal(userId, tokenRole, permissions);

        Assert.Equal(expected, await validator.IsCurrentAsync(principal, CancellationToken.None));
    }

    [Fact]
    public async Task IsCurrentAsync_rejects_token_when_effective_permissions_changed()
    {
        var userId = Guid.NewGuid();
        var validator = new AccessTokenStateValidator(
            new Accounts(new InternalAccountRecord(userId, "admin", "Admin", "admin@example.com", "ACTIVE")),
            new Access(new AccountAccess("ADMIN", ["administration.view-system-overview"])));

        var principal = Principal(userId, "ADMIN", ["administration.manage-internal-accounts"]);

        Assert.False(await validator.IsCurrentAsync(principal, CancellationToken.None));
    }

    private static ClaimsPrincipal Principal(Guid userId, string role, IEnumerable<string> permissions)
    {
        var claims = new List<Claim> { new("sub", userId.ToString()), new("role", role) };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private sealed class Accounts(InternalAccountRecord account) : IInternalAccountDirectory
    {
        public Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InternalAccountRecord>>(userIds.Contains(account.Id) ? [account] : []);

        public Task<PagedInternalAccountRecords> SearchAsync(IReadOnlyCollection<Guid> userIds, string? search, string? status, int page, int pageSize, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class Access(AccountAccess access) : IAccountAccess
    {
        public Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult<AccountAccess?>(access);
        public Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }
}
