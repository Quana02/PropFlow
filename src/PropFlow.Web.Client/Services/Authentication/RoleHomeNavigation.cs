namespace PropFlow.Web.Client.Services.Authentication;

public static class RoleHomeNavigation
{
    public static string Home(string? role) => role switch
    {
        "ADMIN" => "/admin/dashboard",
        "MANAGER" or "STAFF" => "/buildings",
        "ACCOUNTANT" => "/finance/dashboard",
        "RESIDENT" => "/resident",
        _ => "/account"
    };

    public static bool IsEntryRoute(string relativePath)
        => relativePath.Split('?', '#')[0].Trim('/') is "" or "login";

    public static string? EntryRedirect(string? role, string relativePath)
        => role is not null && IsEntryRoute(relativePath) ? Home(role) : null;
}
