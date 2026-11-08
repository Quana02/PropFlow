using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Layout.Navigation.Shared;

namespace PropFlow.UnitTests;

public sealed class CompactNavigationTests
{
    private sealed class Authentication(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation(string route) => Initialize("https://test.invalid/", "https://test.invalid" + route);
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new NotSupportedException("Static HTML rendering must not execute JS.");
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args) => InvokeAsync<T>(id, args);
    }
    [Theory]
    [InlineData("ADMIN", "MANAGE_INTERNAL_ACCOUNTS", "/admin/dashboard?tab=activity", 5)]
    [InlineData("MANAGER", "MANAGE_OPERATIONS", "/manager/service-requests/123", 12)]
    [InlineData("STAFF", "PERFORM_ASSIGNED_OPERATIONS", "/staff/service-requests/123", 9)]
    [InlineData("ADMIN", "MANAGE_INTERNAL_ACCOUNTS", "/knowledge/documents", 5)]
    [InlineData("MANAGER", "MANAGE_OPERATIONS", "/knowledge/documents", 12)]
    [InlineData("STAFF", "PERFORM_ASSIGNED_OPERATIONS", "/knowledge/documents", 9)]
    [InlineData("ACCOUNTANT", "", "/finance/invoices", 8)]
    [InlineData("RESIDENT", "", "/resident", 0)]
    [InlineData("ADMIN", "", "/admin/dashboard", 4)]
    [InlineData("MANAGER", "", "/buildings", 0)]
    [InlineData("STAFF", "", "/buildings", 0)]
    public async Task Actual_shared_menu_preserves_role_routes_order_permissions_and_active_state(string role, string permission, string route, int count)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (permission.Length > 0) claims.Add(new("permission", permission));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        using var services = new ServiceCollection().AddLogging().AddAuthorizationCore(options =>
        {
            options.AddPolicy(AssetPermissions.ViewPolicy, policy => policy.RequireAssertion(context => AssetPermissions.CanView(context.User)));
            options.AddPolicy(KnowledgePolicies.Read, policy => policy.RequireAssertion(context => KnowledgePolicies.CanRead(context.User)));
        }).AddSingleton<AuthenticationStateProvider>(new Authentication(user)).AddSingleton<NavigationManager>(new Navigation(route))
          .AddSingleton<IJSRuntime, NoJavaScript>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment menu = builder => { builder.OpenComponent<AppNavMenu>(0); builder.CloseComponent(); };
            var view = await renderer.RenderComponentAsync<CascadingAuthenticationState>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(CascadingAuthenticationState.ChildContent)] = menu }));
            var html = view.ToHtmlString();
            var links = Regex.Matches(html, "<a\\b[^>]*>").Select(match => match.Value)
                .Where(link => !link.Contains("operations-brand", StringComparison.Ordinal)).ToArray();
            Assert.Equal(count, links.Length);
            var routes = links.Select(link => System.Net.WebUtility.HtmlDecode(Regex.Match(link, "href=\"([^\"]+)\"").Groups[1].Value)).ToArray();
            var expected = role switch
            {
                "ADMIN" => new[] { "/admin/dashboard?tab=overview", "/admin/dashboard?tab=accounts", "/admin/dashboard?tab=payment", "/admin/dashboard?tab=activity" },
                "ACCOUNTANT" => new[] { "/finance/payments/bank-transfers", "/finance/dashboard", "/finance/payments/pending", "/finance/invoices/unpaid", "/finance/payments/overdue", "/finance/payments/confirmed", "/finance/invoices", "/account" },
                "MANAGER" when count > 0 => new[] { "/buildings", "/apartments", "/residents", "/facilities", "/equipment", "/manager/support-chat", "/manager/complaints", "/manager/announcements", "/manager/service-requests", "/maintenance", "/operations/service-fee" },
                "STAFF" when count > 0 => new[] { "/buildings", "/apartments", "/residents", "/facilities", "/equipment", "/staff/complaints", "/staff/service-requests", "/my-maintenance" },
                _ => Array.Empty<string>()
            };
            if (KnowledgePolicies.CanRead(user)) expected = [..expected, "/knowledge/documents"];
            Assert.Equal(expected, routes);
            Assert.All(links, link => { Assert.Contains("title=", link); Assert.Contains("aria-label=", link); });
            if (count > 0) Assert.Single(links.Where(link => link.Contains("aria-current=\"page\"", StringComparison.Ordinal)));
            else Assert.DoesNotContain("/knowledge/documents", html);
            Assert.Contains("class=\"app-nav-scroll\"", html);
        });
    }
}
