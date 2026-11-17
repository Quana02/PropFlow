using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;

namespace PropFlow.IntegrationTests;

public sealed class TestDatabaseSafetyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Host=localhost;Username=fixture;Database=propflow")]
    [InlineData("Host=localhost;Username=fixture;Database=postgres")]
    [InlineData("Host=remote.invalid;Username=fixture;Database=propflow_test")]
    [InlineData("Host=localhost;Username=fixture;Database=propflow_phase11_11111111111111111111111111111111")]
    public void A_dedicated_source_is_explicit_and_never_accepts_application_or_arbitrary_databases(string configured)
        => Assert.Throws<InvalidOperationException>(() => TestDatabaseSafety.Dedicated(configured));

    [Fact]
    public void Incorrect_database_after_an_override_or_server_probe_is_rejected()
    {
        const string configured = "Host=localhost;Username=fixture;Database=propflow_test";
        var source = TestDatabaseSafety.Parse(configured);
        Assert.Throws<InvalidOperationException>(() => TestDatabaseSafety.RejectApplicationDatabase(source,
            "Host=127.0.0.1;Username=app;Database=propflow_test"));
        Assert.Throws<InvalidOperationException>(() => TestDatabaseSafety.RequireName("propflow", "propflow_test"));
        Assert.False(TestDatabaseSafety.SameTarget(configured.Replace("propflow_test", "propflow"), configured));
        Assert.False(TestDatabaseSafety.SameTarget(configured + ";Port=7878", configured));
    }

    [Fact]
    public async Task A_matching_prefix_cannot_migrate_or_delete_a_fixture_owned_by_another_run()
    {
        var dedicated = TestDatabaseConfiguration.GetConnectionString();
        var target = new NpgsqlConnectionStringBuilder(dedicated) { Database = "postgres", Pooling = false };
        await using var admin = new NpgsqlConnection(target.ConnectionString);
        await admin.OpenAsync();
        var name = "propflow_safety_test_" + Guid.NewGuid().ToString("N");
        var owner = Guid.NewGuid().ToString("N");
        await TestDatabaseSafety.CreateAsync(admin, name, owner);
        try
        {
            target.Database = name;
            await TestDatabaseSafety.VerifyFixtureAsync(target.ConnectionString, name, owner, dedicated);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TestDatabaseSafety.VerifyFixtureAsync(dedicated, name, owner, dedicated));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TestDatabaseSafety.VerifyFixtureAsync(target.ConnectionString, name, TestDatabaseSafety.ProcessOwner, dedicated));
            await Assert.ThrowsAsync<InvalidOperationException>(() => TestDatabaseSafety.DropAsync(admin, name, TestDatabaseSafety.ProcessOwner));
            await TestDatabaseSafety.VerifyFixtureAsync(target.ConnectionString, name, owner, dedicated);
        }
        finally
        {
            await TestDatabaseSafety.DropAsync(admin, name, owner);
        }
        await using var exists = new NpgsqlCommand("SELECT EXISTS (SELECT FROM pg_database WHERE datname = @name)", admin);
        exists.Parameters.AddWithValue("name", name);
        Assert.False((bool)(await exists.ExecuteScalarAsync())!);
    }

    [Fact]
    public void Api_host_rejects_an_effective_configuration_override_before_serving_requests()
    {
        using var factory = new PropFlowApiFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:PropFlowDatabase"] = "Host=localhost;Database=propflow;Username=synthetic" })));

        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("API test dùng database khác đích đã xác minh.", error.Failures);
    }
}
