using System.Security.Claims;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Authentication.Infrastructure;

public sealed class AccessTokenStateValidator(
    IInternalAccountDirectory accounts,
    IAccountAccess access)
{
    public async Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var userId)) return false;

        var account = (await accounts.GetAsync([userId], ct)).SingleOrDefault();
        if (account is null || account.Status != "ACTIVE") return false;

        var currentAccess = await access.GetAsync(userId, ct);
        var tokenRole = principal.FindFirst("role")?.Value;
        if (currentAccess is null || !string.Equals(currentAccess.Role, tokenRole, StringComparison.Ordinal)) return false;

        var tokenPermissions = principal.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        return tokenPermissions.SetEquals(currentAccess.Permissions);
    }
}
