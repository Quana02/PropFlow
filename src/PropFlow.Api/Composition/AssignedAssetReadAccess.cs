using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.Roles;
using PropFlow.Modules.PropertyAssets.Application;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Api.Composition;

public sealed class AssignedAssetReadAccess(IHttpContextAccessor http, IEnumerable<IAssignedAssetSource> sources) : IAssetReadAccess
{
    public async Task<AssetReadScope> GetScopeAsync(CancellationToken ct)
    {
        var user = http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return AssetReadScope.None;
        if (user.IsInRole(SystemRoleCodes.Manager) && user.HasClaim("permission", SystemPermissionCodes.ManageOperations))
            return AssetReadScope.Manager;
        if (!user.IsInRole(SystemRoleCodes.Staff) || !user.HasClaim("permission", SystemPermissionCodes.PerformAssignedOperations) ||
            !Guid.TryParse(user.FindFirst("sub")?.Value, out var staffId))
            return AssetReadScope.None;

        var references = new List<AssignedAssetReference>();
        foreach (var source in sources)
            references.AddRange(await source.GetActiveAssignmentsAsync(staffId, ct));

        return new AssetReadScope(false, references.Count > 0,
            references.Where(r => r.FacilityId.HasValue).Select(r => r.FacilityId!.Value).Distinct().ToArray(),
            references.Where(r => r.EquipmentId.HasValue).Select(r => r.EquipmentId!.Value).Distinct().ToArray());
    }
}
