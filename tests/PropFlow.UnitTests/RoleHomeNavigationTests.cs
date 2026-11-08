using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

public sealed class RoleHomeNavigationTests
{
    [Theory]
    [InlineData("ACCOUNTANT", "/finance/dashboard")]
    [InlineData("RESIDENT", "/resident")]
    [InlineData("MANAGER", "/buildings")]
    [InlineData("STAFF", "/buildings")]
    [InlineData("ADMIN", "/admin/dashboard")]
    public void Restored_session_opens_role_home_only_from_entry_routes(string role, string home)
    {
        Assert.Equal(home, RoleHomeNavigation.EntryRedirect(role, ""));
        Assert.Equal(home, RoleHomeNavigation.EntryRedirect(role, "login?returnUrl=%2F"));
        Assert.Null(RoleHomeNavigation.EntryRedirect(role, home));
    }

    [Theory]
    [InlineData("resident/billing")]
    [InlineData("finance/payments/overdue?page=2")]
    [InlineData("apartments")]
    [InlineData("reset-password")]
    public void Existing_page_is_preserved(string path)
        => Assert.Null(RoleHomeNavigation.EntryRedirect("RESIDENT", path));

    [Theory]
    [InlineData("")]
    [InlineData("login")]
    public void Anonymous_visitor_stays_on_public_entry(string path)
        => Assert.Null(RoleHomeNavigation.EntryRedirect(null, path));
}
