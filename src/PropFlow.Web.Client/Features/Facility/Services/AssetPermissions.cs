using System.Security.Claims;

namespace PropFlow.Web.Client.Features.Facility.Services;

public static class AssetPermissions
{
    public const string ViewPolicy = "property-assets.view";
    public static bool CanManage(ClaimsPrincipal user) => user.IsInRole("MANAGER") && user.HasClaim("permission", "MANAGE_OPERATIONS");
    public static bool CanView(ClaimsPrincipal user) => CanManage(user) ||
        (user.IsInRole("STAFF") && user.HasClaim("permission", "PERFORM_ASSIGNED_OPERATIONS"));
}
