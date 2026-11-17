using Microsoft.Extensions.Configuration;

namespace PropFlow.IntegrationTests;

internal static class TestDatabaseConfiguration
{
    private const string ApiUserSecretsId = "adbac45b-651b-498f-a797-12585846625f";
    private const string TestDatabase = TestDatabaseSafety.DedicatedName;

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

        var builder = TestDatabaseSafety.Dedicated(configuration.GetConnectionString("PropFlowTestDatabase"));
        TestDatabaseSafety.RequireName(builder.Database, GetExpectedDatabaseName());
        TestDatabaseSafety.RejectApplicationDatabase(builder, configuration.GetConnectionString("PropFlowDatabase"));
        return builder.ConnectionString;
    }
}
