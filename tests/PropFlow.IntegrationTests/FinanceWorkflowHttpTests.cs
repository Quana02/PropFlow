using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Billing.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class FinanceWorkflowHttpTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string database = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                using var rsa = RSA.Create(2048);
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PropFlowDatabase"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                    ["Authentication:Issuer"] = "finance-tests",
                    ["Authentication:Audience"] = "finance-tests",
                    ["Authentication:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
                    ["Authentication:OtpHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Authentication:Mail:Host"] = "invalid.test",
                    ["Authentication:Mail:From"] = "test@example.invalid",
                    ["Logging:LogLevel:Default"] = "None"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = options.DefaultChallengeScheme = options.DefaultForbidScheme = "FinanceTest";
                }).AddScheme<AuthenticationSchemeOptions, Auth>("FinanceTest", _ => { });
                services.RemoveAll<BillingDbContext>();
                services.AddScoped(_ => new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
                    .UseInMemoryDatabase(database).Options));
            });
        }

        public HttpClient Client(string? role, string? permission)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
            if (permission is not null) client.DefaultRequestHeaders.Add("X-Test-Permission", permission);
            return client;
        }
    }

    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new("sub", "20000000-0000-0000-0000-000000000001"), new(ClaimTypes.Role, role.ToString()) };
            if (Request.Headers.TryGetValue("X-Test-Permission", out var permission)) claims.Add(new("permission", permission.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }

    [Theory]
    [InlineData("STAFF", "MANAGE_FINANCE")]
    [InlineData("RESIDENT", "USE_RESIDENT_SERVICES")]
    [InlineData("ADMIN", "MANAGE_FINANCE")]
    [InlineData("ACCOUNTANT", null)]
    [InlineData("MANAGER", null)]
    public async Task Unauthorized_roles_and_missing_permissions_cannot_read_billing_or_collect_payments(string role, string? permission)
    {
        await using var factory = new Factory();
        using var client = factory.Client(role, permission);
        foreach (var path in new[] { "invoices", "rates", $"invoices/{Guid.NewGuid()}/print" })
        {
            using var response = await client.GetAsync("/api/v1/billing/cycle/" + path);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var collect = await client.PostAsJsonAsync("/api/v1/payments/collect", new { });
        Assert.Equal(HttpStatusCode.Forbidden, collect.StatusCode);
    }

    [Fact]
    public async Task Manager_can_read_current_rates_but_cannot_collect_or_confirm_payments()
    {
        await using var factory = new Factory();
        using var manager = factory.Client("MANAGER", "MANAGE_OPERATIONS");
        using var rates = await manager.GetAsync("/api/v1/billing/cycle/rates");
        Assert.Equal(HttpStatusCode.OK, rates.StatusCode);

        using var collect = await manager.PostAsJsonAsync("/api/v1/payments/collect", new { });
        using var confirm = await manager.PostAsync($"/api/v1/payments/{Guid.NewGuid()}/confirm", null);
        Assert.Equal(HttpStatusCode.Forbidden, collect.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, confirm.StatusCode);

        using var anonymous = factory.Client(null, null);
        using var anonymousRates = await anonymous.GetAsync("/api/v1/billing/cycle/rates");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRates.StatusCode);
    }
}
