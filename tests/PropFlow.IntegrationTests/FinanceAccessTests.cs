using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using PropFlow.Web.Client.Layout;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.IntegrationTests;

public sealed class FinanceAccessTests
{
    [Fact]
    public void Finance_pages_require_accountant_role()
    {
        var pages = typeof(AuthSession).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Any(route => route.Template.StartsWith("/finance/", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(5, pages.Length);
        Assert.All(pages, page =>
        {
            var authorization = page.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().SingleOrDefault();
            Assert.NotNull(authorization);
            Assert.Equal("ACCOUNTANT", authorization.Roles);
        });
    }

    [Fact]
    public void Finance_pages_use_accountant_layout()
    {
        var pages = typeof(AuthSession).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Any(route => route.Template.StartsWith("/finance/", StringComparison.Ordinal)))
            .ToArray();

        Assert.Equal(5, pages.Length);
        Assert.All(pages, page =>
            Assert.Equal(typeof(AccountantLayout), page.GetCustomAttributes(typeof(LayoutAttribute), false)
                .Cast<LayoutAttribute>().SingleOrDefault()?.LayoutType));
    }
}
