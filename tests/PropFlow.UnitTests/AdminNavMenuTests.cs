using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Web.Client.Layout.Navigation.Admin;

namespace PropFlow.UnitTests;

[Trait("UseCase", "FE-15 Navigation")]
public sealed class AdminNavMenuTests
{
    [Theory]
    [InlineData("/admin/dashboard?tab=payment", "payment")]
    [InlineData("/admin/dashboard?other=1&tab=%70ayment#settings", "payment")]
    [InlineData("/admin/dashboard/?tab=activity", "activity")]
    [InlineData("/admin/dashboard?tab=accounts", "accounts")]
    [InlineData("/admin/dashboard", "overview")]
    [InlineData("/admin/dashboard?tab=unknown", "overview")]
    public void Query_match_selects_exactly_one_supported_tab(string location, string expected)
    {
        var matches = new[] { "overview", "accounts", "activity", "payment" }.Where(tab => AdminTabLink.MatchesUri("https://example.test" + location, tab)).ToArray();
        Assert.Equal([expected], matches);
    }

    [Fact]
    public void Other_pages_do_not_keep_a_dashboard_tab_highlighted()
    {
        foreach (var tab in new[] { "overview", "accounts", "activity", "payment" })
            Assert.False(AdminTabLink.MatchesUri("https://example.test/account", tab));
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://example.test/", "https://example.test/admin/dashboard?tab=activity");
        protected override void NavigateToCore(string uri, bool forceLoad) { Uri = ToAbsoluteUri(uri).AbsoluteUri; NotifyLocationChanged(false); }
    }
    private sealed class AdminAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Test admin"), new Claim(ClaimTypes.Role, "ADMIN")], "test"))));
    }

    [Fact]
    public async Task Query_navigation_updates_exactly_one_active_link_without_recreating_the_menu()
    {
        var navigation = new TestNavigation();
        using var services = new ServiceCollection().AddLogging().AddAuthorizationCore()
            .AddSingleton<NavigationManager>(navigation).AddSingleton<AuthenticationStateProvider, AdminAuthentication>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment menu = builder => { builder.OpenComponent<AdminNavMenu>(0); builder.CloseComponent(); };
            var root = await renderer.RenderComponentAsync<CascadingAuthenticationState>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CascadingAuthenticationState.ChildContent)] = menu }));
            AssertActive(root.ToHtmlString(), "activity");
            foreach (var tab in new[] { "payment", "activity", "accounts", "overview", "payment", "accounts" })
            {
                navigation.NavigateTo($"/admin/dashboard?tab={tab}");
                await root.QuiescenceTask;
                AssertActive(root.ToHtmlString(), tab);
            }
        });
    }

    private static void AssertActive(string html, string tab)
    {
        var links = Regex.Matches(html, "<a\\b[^>]*>").Select(x => x.Value).ToArray();
        Assert.Equal(4, links.Length);
        var current = Assert.Single(links.Where(x => x.Contains("aria-current=\"page\"", StringComparison.Ordinal)));
        Assert.Contains($"tab={tab}", current);
        Assert.Single(links.Where(x => x.Contains("is-selected", StringComparison.Ordinal)));
    }
}
