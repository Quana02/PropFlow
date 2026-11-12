using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Authentication.Infrastructure;

namespace PropFlow.UnitTests;

public sealed class ServiceRequestRealtimeAuthenticationTests
{
    [Fact]
    public async Task ServiceRequestHub_AcceptsAccessTokenFromSignalRQueryString()
    {
        using var rsa = RSA.Create(2048);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = "propflow-tests",
            ["Authentication:Audience"] = "propflow-tests",
            ["Authentication:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
            ["Authentication:OtpHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Authentication:Mail:Host"] = "invalid.test",
            ["Authentication:Mail:From"] = "tests@example.invalid",
            ["Authentication:Mail:Port"] = "587"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPropFlowAuthentication(configuration);
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/hubs/service-requests";
        httpContext.Request.QueryString = new QueryString("?access_token=staff-token");
        var context = new MessageReceivedContext(
            httpContext,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)),
            options);

        await options.Events.OnMessageReceived(context);

        Assert.Equal("staff-token", context.Token);
    }
}
