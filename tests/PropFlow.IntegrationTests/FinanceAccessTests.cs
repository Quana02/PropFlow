using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using PropFlow.Web.Client.Layout;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.IntegrationTests;

public sealed class FinanceAccessTests
{
    [Fact]
    public void Resident_simulation_endpoint_requires_resident_role()
    {
        var authorization = typeof(PropFlow.Modules.Payments.Presentation.ResidentServiceRequestPaymentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("RESIDENT", authorization.Roles);
    }
    [Fact]
    public void Sepay_oauth_requires_admin_and_payment_destination_permission()
    {
        var authorization = typeof(PropFlow.Modules.Payments.Presentation.SePayOAuthController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("ADMIN", authorization.Roles);
        Assert.Equal("payment-settings.manage", authorization.Policy);
        var callback = typeof(AuthSession).Assembly.GetTypes().Single(type => type.GetCustomAttributes(typeof(RouteAttribute), false)
            .Cast<RouteAttribute>().Any(route => route.Template == "/admin/sepay/callback"));
        Assert.Equal("ADMIN", callback.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles);
    }

    [Fact]
    public void Sepay_sandbox_setup_requires_admin_and_payment_destination_permission()
    {
        var authorization = typeof(PropFlow.Modules.Payments.Presentation.SePaySandboxController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("ADMIN", authorization.Roles);
        Assert.Equal("payment-settings.manage", authorization.Policy);
    }
    [Fact]
    public void Bank_transfer_review_api_requires_accountant_and_finance_permission()
    {
        var authorization = typeof(PropFlow.Modules.Payments.Presentation.BankTransfersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        Assert.Equal("ACCOUNTANT", authorization.Roles);
        Assert.Equal("finance.manage", authorization.Policy);
    }
    [Fact]
    public void Finance_pages_require_accountant_role()
    {
        var pages = typeof(AuthSession).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(RouteAttribute), false)
                .Cast<RouteAttribute>().Any(route => route.Template.StartsWith("/finance/", StringComparison.Ordinal)))
            .ToArray();

        Assert.Contains(pages, page => page.Name == "BankTransferReview");
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

        Assert.Contains(pages, page => page.Name == "BankTransferReview");
        Assert.All(pages, page =>
            Assert.Equal(typeof(AccountantLayout), page.GetCustomAttributes(typeof(LayoutAttribute), false)
                .Cast<LayoutAttribute>().SingleOrDefault()?.LayoutType));
    }
}
