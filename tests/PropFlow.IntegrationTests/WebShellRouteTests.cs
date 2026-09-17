using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc.Testing;
using PropFlow.Web;
using PropFlow.Web.Client.Features.Authentication.Components;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-01")]
public sealed class WebShellRouteTests : IClassFixture<WebApplicationFactory<WebAssemblyShellHost>>
{
    private readonly WebApplicationFactory<WebAssemblyShellHost> factory;
    public WebShellRouteTests(WebApplicationFactory<WebAssemblyShellHost> factory) => this.factory = factory;

    [Theory]
    [InlineData("/login")]
    [InlineData("/register")]
    [InlineData("/registration/resume")]
    [InlineData("/forgot-password")]
    [InlineData("/otp")]
    [InlineData("/reset-password")]
    [InlineData("/account")]
    [InlineData("/account/change-password")]
    [InlineData("/resident")]
    [InlineData("/forbidden")]
    public async Task Direct_navigation_serves_only_the_non_prerendered_shell(string route)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost"), AllowAutoRedirect = false });
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("_framework/blazor.web.js", html);
        Assert.DoesNotContain("id=\"profile-name\"", html);
        Assert.DoesNotContain("id=\"username\"", html);
        Assert.DoesNotContain("data-purpose=\"user-profile-menu\"", html);
        Assert.DoesNotContain("authorization metadata", html);
    }

    [Fact]
    public void Authentication_routes_declare_their_layout_for_client_navigation()
    {
        Assert.False(Attribute.IsDefined(typeof(AuthLayout), typeof(LayoutAttribute)));
        var routePages = typeof(AuthSession).Assembly.GetTypes()
            .Where(type => Attribute.IsDefined(type, typeof(RouteAttribute)))
            .ToArray();
        Assert.DoesNotContain(routePages, page => page.Namespace == "PropFlow.Web.Client.Pages.Auth");
        var pages = routePages.Where(type => type.Namespace == "PropFlow.Web.Client.Features.Authentication.Pages").ToArray();

        Assert.NotEmpty(pages);
        foreach (var route in new[] { "/login", "/register", "/forgot-password", "/otp", "/reset-password" })
            Assert.Single(pages.Where(page => page.GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Any(attribute => attribute.Template == route)));
        Assert.All(pages, page =>
            Assert.Equal(typeof(AuthLayout), page.GetCustomAttributes(typeof(LayoutAttribute), false)
                .Cast<LayoutAttribute>().SingleOrDefault()?.LayoutType));
    }
}
