using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace PropFlow.IntegrationTests;

public class PropFlowApiFactory : WebApplicationFactory<Program>
{
    private readonly string? fixtureConnection;

    public PropFlowApiFactory() { }

    protected PropFlowApiFactory(string connection) => fixtureConnection = connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection()
                .UseEphemeralDataProtectionProvider()
                .SetApplicationName("PropFlow.IntegrationTests");
            services.AddOptions<DatabaseConnectionOptions>()
                .Validate(options => TestDatabaseSafety.SameTarget(options.PropFlowDatabase, ExpectedConnection()),
                    "API test dùng database khác đích đã xác minh.")
                .ValidateOnStart();
        });

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var expected = ExpectedConnection();
            TestDatabaseSafety.VerifyTargetAsync(expected, TestDatabaseSafety.Parse(expected).Database!).GetAwaiter().GetResult();
            // Ensures appsettings.Testing.json is loaded and takes precedence
            config.AddJsonFile("appsettings.Testing.json", optional: false, reloadOnChange: false);
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PropFlowDatabase"] = expected
            });
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

    private string ExpectedConnection() => fixtureConnection ?? TestDatabaseConfiguration.GetConnectionString();

    public string GetConnectionString()
    {
        var config = Services.GetRequiredService<IConfiguration>();
        var connStr = config.GetConnectionString("PropFlowDatabase");

        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new InvalidOperationException("Connection string 'PropFlowDatabase' is not configured in Testing environment.");
        }

        var expected = ExpectedConnection();
        if (!TestDatabaseSafety.SameTarget(connStr, expected))
            throw new InvalidOperationException("API test dùng database khác đích đã xác minh.");
        TestDatabaseSafety.VerifyTargetAsync(connStr, TestDatabaseSafety.Parse(expected).Database!).GetAwaiter().GetResult();
        return connStr;
    }
}

