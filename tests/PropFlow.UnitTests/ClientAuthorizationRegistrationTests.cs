using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Web.Client.Features.UtilityInsights.Pages;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

public sealed class ClientAuthorizationRegistrationTests
{
    [Theory]
    [InlineData("MANAGER", "MANAGE_OPERATIONS", true)]
    [InlineData("ACCOUNTANT", "MANAGE_FINANCE", true)]
    [InlineData("MANAGER", "", false)]
    [InlineData("ACCOUNTANT", "", false)]
    [InlineData("MANAGER", "MANAGE_FINANCE", false)]
    [InlineData("ACCOUNTANT", "MANAGE_OPERATIONS", false)]
    [InlineData("RESIDENT", "MANAGE_OPERATIONS", false)]
    [InlineData("STAFF", "MANAGE_FINANCE", false)]
    [InlineData("ADMIN", "MANAGE_OPERATIONS", false)]
    [InlineData(null, null, false)]
    public async Task Utility_route_resolves_client_policy_and_enforces_role_with_matching_permission(string? role, string? permission, bool allowed)
    {
        await using var services = new ServiceCollection().AddLogging().AddClientAuthorization().BuildServiceProvider();
        var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        // AuthorizeRouteView uses the same policy combination before rendering a page.
        var policy = await AuthorizationPolicy.CombineAsync(provider, typeof(UtilityInsightsManagement).GetCustomAttributes<AuthorizeAttribute>());
        Assert.NotNull(policy);
        var claims = new List<Claim>();
        if (role is not null) claims.Add(new(ClaimTypes.Role, role));
        if (!string.IsNullOrEmpty(permission)) claims.Add(new("permission", permission));
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, role is null ? null : "test"));
        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, null, policy!);
        Assert.Equal(allowed, result.Succeeded);
    }

    [Fact]
    public async Task Every_named_policy_on_client_routes_is_registered_at_startup()
    {
        await using var services = new ServiceCollection().AddLogging().AddClientAuthorization().BuildServiceProvider();
        var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var names = typeof(UtilityInsightsManagement).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes<RouteAttribute>().Any())
            .SelectMany(type => type.GetCustomAttributes<AuthorizeAttribute>())
            .Select(attribute => attribute.Policy).Where(name => !string.IsNullOrEmpty(name)).Distinct();
        foreach (var name in names) Assert.True(await provider.GetPolicyAsync(name!) is not null, $"Client route policy is not registered: {name}");
    }
}
