using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Payments.Presentation;

namespace PropFlow.IntegrationTests;

public sealed class SePayRateLimitTests
{
    [Fact]
    public async Task SwitchingBetweenSandboxAndOAuthDoesNotBlockStatusAfterFiveRequests()
    {
        using var factory = new RateLimitFactory();
        using var server = Server(factory);
        using var client = server.CreateClient();
        for (var i = 0; i < 12; i++)
        {
            using var response = await client.GetAsync(i % 2 == 0 ? "/sandbox/status" : "/oauth/status");
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Status request {i + 1} returned {(int)response.StatusCode}; browsing SePay setup must not spend the authentication/OTP allowance.");
        }
    }

    [Fact]
    public async Task ExhaustedAuthenticationAllowanceDoesNotBlockSePayStatus()
    {
        using var factory = new RateLimitFactory();
        using var server = Server(factory);
        using var client = server.CreateClient();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/auth-entry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/auth-entry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sandbox/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/oauth/status")).StatusCode);
    }

    [Fact]
    public async Task StatusReadsDoNotConsumeOtpAllowanceAndOtpRemainsLimitedPerAdmin()
    {
        using var factory = new RateLimitFactory();
        using var server = Server(factory);
        using var client = server.CreateClient();
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sandbox/status")).StatusCode);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/sandbox/challenge", null)).StatusCode);
        using var rejected = await client.PostAsync("/oauth/challenge", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/oauth/status")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Actor", "admin-two");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/sandbox/challenge", null)).StatusCode);
    }

    [Fact]
    public async Task ProviderChecksRemainLimitedWithoutBlockingStatusOrOtp()
    {
        using var factory = new RateLimitFactory();
        using var server = Server(factory);
        using var client = server.CreateClient();
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/sandbox/preview", null)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/sandbox/preview", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/sandbox/status")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/sandbox/challenge", null)).StatusCode);
    }

    private sealed class RateLimitFactory : PropFlowApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:RateLimits:auth-entry:PermitLimit"] = "5",
                ["Authentication:RateLimits:auth-entry:WindowMinutes"] = "15",
                [$"Payments:RateLimits:{PaymentSettingsRateLimitPolicies.Read}:PermitLimit"] = "20",
                [$"Payments:RateLimits:{PaymentSettingsRateLimitPolicies.Provider}:PermitLimit"] = "3",
                [$"Payments:RateLimits:{PaymentSettingsRateLimitPolicies.Otp}:PermitLimit"] = "2"
            }));
        }
    }

    // Exercise Program's actual limiter registrations and controller/action metadata, without
    // contacting SePay, loading a user's credentials, or reading/writing business data.
    private static TestServer Server(RateLimitFactory factory)
    {
        var policies = factory.Services.GetServices<IConfigureOptions<RateLimiterOptions>>().ToArray();
        return new TestServer(new WebHostBuilder().ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddRouting(); services.AddProblemDetails(); services.AddRateLimiter(_ => { });
                foreach (var policy in policies) services.AddSingleton(policy);
            })
            .Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    var actor = context.Request.Headers["X-Test-Actor"].FirstOrDefault() ?? "admin-one";
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", actor)], "Test"));
                    await next();
                });
                app.UseRouting(); app.UseRateLimiter();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/sandbox/status", _ => Task.CompletedTask).WithMetadata(Metadata<SePaySandboxController>("State"));
                    endpoints.MapGet("/oauth/status", _ => Task.CompletedTask).WithMetadata(Metadata<SePayOAuthController>("State"));
                    endpoints.MapPost("/sandbox/preview", _ => Task.CompletedTask).WithMetadata(Metadata<SePaySandboxController>("Preview"));
                    endpoints.MapPost("/sandbox/challenge", _ => Task.CompletedTask).WithMetadata(Metadata<SePaySandboxController>("Challenge"));
                    endpoints.MapPost("/oauth/challenge", _ => Task.CompletedTask).WithMetadata(Metadata<SePayOAuthController>("Challenge"));
                    endpoints.MapPost("/auth-entry", _ => Task.CompletedTask).RequireRateLimiting("auth-entry");
                });
            }));
    }
    private static object[] Metadata<T>(string method) => typeof(T).GetCustomAttributes<EnableRateLimitingAttribute>(true)
        .Concat(typeof(T).GetMethod(method, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .GetCustomAttributes<EnableRateLimitingAttribute>(true)).Cast<object>().ToArray();
}
