using Microsoft.Extensions.Configuration;
using Npgsql;

namespace PropFlow.IntegrationTests;

internal static class TestDatabaseConfiguration
{
    private const string ApiUserSecretsId = "adbac45b-651b-498f-a797-12585846625f";

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

        var builder = new NpgsqlConnectionStringBuilder(sourceConnection);
        if (string.IsNullOrWhiteSpace(dedicatedTestConnection)) builder.Database = "propflow_test";
        if (!string.Equals(builder.Database, "propflow_test", StringComparison.Ordinal))
            throw new InvalidOperationException("Integration tests chỉ được phép kết nối database 'propflow_test'.");

        return builder.ConnectionString;
    }
}
