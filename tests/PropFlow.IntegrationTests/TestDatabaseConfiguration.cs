using Microsoft.Extensions.Configuration;
using Npgsql;

namespace PropFlow.IntegrationTests;

internal static class TestDatabaseConfiguration
{
    private const string ApiUserSecretsId = "adbac45b-651b-498f-a797-12585846625f";
    private const string TestDatabase = "propflow_test";

    public static string GetExpectedDatabaseName()
    {
        var configured = Environment.GetEnvironmentVariable("PROPFLOW_INTEGRATION_DATABASE");
        if (!string.IsNullOrWhiteSpace(configured) && !string.Equals(configured.Trim(), TestDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException($"PROPFLOW_INTEGRATION_DATABASE phải là '{TestDatabase}'.");

        return TestDatabase;
    }

    public static string GetConnectionString()
    {
        var secretsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", ApiUserSecretsId, "secrets.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(secretsPath, optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var dedicatedTestConnection = configuration.GetConnectionString("PropFlowTestDatabase");
        var sourceConnection = dedicatedTestConnection ?? configuration.GetConnectionString("PropFlowDatabase")
            ?? throw new InvalidOperationException(
                "Configure ConnectionStrings:PropFlowTestDatabase hoặc ConnectionStrings:PropFlowDatabase trong API User Secrets/environment.");

        var builder = new NpgsqlConnectionStringBuilder(sourceConnection)
        {
            Database = GetExpectedDatabaseName()
        };

        return builder.ConnectionString;
    }
}
