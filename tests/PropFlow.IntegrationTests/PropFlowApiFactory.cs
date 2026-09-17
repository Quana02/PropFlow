using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace PropFlow.IntegrationTests;

public class PropFlowApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Ensures appsettings.Testing.json is loaded and takes precedence
            config.AddJsonFile("appsettings.Testing.json", optional: false, reloadOnChange: false);
            using var rsa = RSA.Create(2048);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "None",
                ["Logging:LogLevel:Microsoft"] = "None",
                ["Authentication:Issuer"] = "propflow-tests", ["Authentication:Audience"] = "propflow-tests",
                ["Authentication:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
                ["Authentication:OtpHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ["Authentication:Mail:Host"] = "invalid.test", ["Authentication:Mail:From"] = "tests@example.invalid"
                , ["Cors:AllowedOrigins:0"] = "https://localhost:7201"
            });
        });
    }

    public string GetConnectionString()
    {
        var config = Services.GetRequiredService<IConfiguration>();
        var connStr = config.GetConnectionString("PropFlowDatabase");

        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new InvalidOperationException("Connection string 'PropFlowDatabase' is not configured in Testing environment.");
        }

        return connStr;
    }
}

