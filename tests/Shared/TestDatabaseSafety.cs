#nullable enable
using System.Text.RegularExpressions;
using Npgsql;

namespace PropFlow.Testing;

internal static class TestDatabaseSafety
{
    public const string DedicatedName = "propflow_test";
    public static string ProcessOwner { get; } = Guid.NewGuid().ToString("N");

    public static NpgsqlConnectionStringBuilder Dedicated(string? configured = null)
    {
        var target = Parse(configured ?? Environment.GetEnvironmentVariable("PROPFLOW_TEST_DATABASE"));
        RequireName(target.Database, DedicatedName);
        RejectApplicationDatabase(target, Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase"));
        return target;
    }

    public static NpgsqlConnectionStringBuilder Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Cần cấu hình kết nối PostgreSQL test riêng.");
        NpgsqlConnectionStringBuilder target;
        try { target = new(value); }
        catch (ArgumentException) { throw new InvalidOperationException("Cấu hình PostgreSQL test không hợp lệ."); }
        if (target.Host is not ("localhost" or "127.0.0.1" or "::1") ||
            target.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(target.Username))
            throw new InvalidOperationException("PostgreSQL test phải dùng endpoint loopback và tài khoản được chỉ định.");
        return target;
    }

    public static async Task VerifyOpenAsync(NpgsqlConnection connection, string expected)
    {
        RequireName(Parse(connection.ConnectionString).Database, expected);
        await using var command = new NpgsqlCommand("SELECT current_database()", connection);
        RequireName((string?)await command.ExecuteScalarAsync(), expected);
    }

    public static void RequireName(string? actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidOperationException("Database PostgreSQL thực tế không khớp đích test đã xác minh.");
    }

    public static async Task CreateAsync(NpgsqlConnection admin, string name, string? owner = null)
    {
        var marker = Marker(name, owner ?? ProcessOwner);
        await VerifyOpenAsync(admin, "postgres");
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin))
            await create.ExecuteNonQueryAsync();
        await using (var register = new NpgsqlCommand($"COMMENT ON DATABASE \"{name}\" IS '{marker}'", admin))
            await register.ExecuteNonQueryAsync();
        await VerifyOwnedAsync(admin, name, owner ?? ProcessOwner);
    }

    public static async Task DropAsync(NpgsqlConnection admin, string name, string? owner = null)
    {
        await VerifyOpenAsync(admin, "postgres");
        await VerifyOwnedAsync(admin, name, owner ?? ProcessOwner);
        await using var drop = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    public static async Task VerifyFixtureAsync(string? configured, string name, string owner, string? dedicated = null)
    {
        RequireFixtureName(name, owner);
        var source = Dedicated(dedicated);
        var target = Parse(configured);
        RequireName(target.Database, name);
        RejectApplicationDatabase(target, Environment.GetEnvironmentVariable("ConnectionStrings__PropFlowDatabase"));
        if (source.Host != target.Host || source.Port != target.Port || source.Username != target.Username)
            throw new InvalidOperationException("Endpoint đích khác PostgreSQL test đã cấu hình.");
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await VerifyOpenAsync(connection, name);
        await VerifyOwnedAsync(connection, name, owner);
    }

    public static async Task VerifyTargetAsync(string configured, string expected)
    {
        var target = Parse(configured);
        RequireName(target.Database, expected);
        await using var connection = new NpgsqlConnection(target.ConnectionString);
        await connection.OpenAsync();
        await VerifyOpenAsync(connection, expected);
    }

    public static bool SameTarget(string? configured, string expectedConnection)
    {
        var actual = Parse(configured);
        var expected = Parse(expectedConnection);
        return actual.Database == expected.Database && actual.Host == expected.Host &&
            actual.Port == expected.Port && actual.Username == expected.Username;
    }

    public static void RejectApplicationDatabase(NpgsqlConnectionStringBuilder test, string? application)
    {
        if (string.IsNullOrWhiteSpace(application)) return;
        var configured = new NpgsqlConnectionStringBuilder(application);
        if (configured.Database == test.Database && configured.Port == test.Port &&
            configured.Host is "localhost" or "127.0.0.1" or "::1")
            throw new InvalidOperationException("Database test trùng database ứng dụng.");
    }

    private static async Task VerifyOwnedAsync(NpgsqlConnection connection, string name, string owner)
    {
        var marker = Marker(name, owner);
        await using var ownership = new NpgsqlCommand(
            "SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = @name", connection);
        ownership.Parameters.AddWithValue("name", name);
        if (!string.Equals((string?)await ownership.ExecuteScalarAsync(), marker, StringComparison.Ordinal))
            throw new InvalidOperationException("Database không thuộc fixture của lượt chạy này.");
    }

    public static void RequireFixtureName(string name, string owner) => Marker(name, owner);

    private static string Marker(string name, string owner)
    {
        if (!Regex.IsMatch(name, @"\Apropflow_[a-z0-9_]+_[a-f0-9]{32}\z") ||
            !Regex.IsMatch(owner, @"\A[a-f0-9]{32}\z"))
            throw new InvalidOperationException("Tên hoặc mã sở hữu database fixture không hợp lệ.");
        return "PropFlow test fixture " + owner;
    }
}
