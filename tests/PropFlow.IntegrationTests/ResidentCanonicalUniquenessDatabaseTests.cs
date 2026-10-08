using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class ResidentCanonicalUniquenessDatabaseTests : IClassFixture<PropFlowApiFactory>, IAsyncLifetime
{
    private const string IdentityIndexName = "ux_residents_identity_canonical";
    private const string EmailIndexName = "ux_residents_email_canonical";
    private readonly string _connectionString;
    private readonly PropFlowApiFactory _factory;

    public ResidentCanonicalUniquenessDatabaseTests(PropFlowApiFactory factory)
    {
        _factory = factory;
        _connectionString = factory.GetConnectionString();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ResidentsDbContext>().Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Residents_ShouldHaveCanonicalIdentityAndEmailUniqueIndexes()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'residents'
              AND indexname IN ('ux_residents_identity_canonical', 'ux_residents_email_canonical')
            ORDER BY indexname;
            """,
            connection);

        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0), reader.GetString(1));
        }

        Assert.Equal(2, indexes.Count);
        Assert.Contains("UNIQUE INDEX", indexes[IdentityIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upper", indexes[IdentityIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("btrim", indexes[IdentityIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("identity_type", indexes[IdentityIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("regexp_replace", indexes[IdentityIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", indexes[IdentityIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNIQUE INDEX", indexes[EmailIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lower", indexes[EmailIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("btrim", indexes[EmailIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("email", indexes[EmailIndexName], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WHERE", indexes[EmailIndexName], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DirectInsert_ShouldRejectDuplicateCanonicalIdentity()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var identity = CreateNumericIdentity();
        await InsertResidentAsync(connection, transaction, "CCCD", FormatIdentity(identity), $"first-{Guid.NewGuid():N}@example.test", "0900000001");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertResidentAsync(connection, transaction, " cccd ", identity, $"second-{Guid.NewGuid():N}@example.test", "0900000002"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(IdentityIndexName, exception.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DirectInsert_ShouldRejectDuplicateNormalizedEmail()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var emailToken = Guid.NewGuid().ToString("N");
        var email = $"resident-{emailToken}@example.test";
        await InsertResidentAsync(connection, transaction, "CCCD", CreateNumericIdentity(), email.ToUpperInvariant(), "0900000003");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertResidentAsync(connection, transaction, "CCCD", CreateNumericIdentity(), $"  {email}  ", "0900000004"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(EmailIndexName, exception.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DirectInsert_ShouldAllowMultipleNullEmails()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await InsertResidentAsync(connection, transaction, "CCCD", CreateNumericIdentity(), null, "0900000005");
        await InsertResidentAsync(connection, transaction, "CCCD", CreateNumericIdentity(), null, "0900000006");

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DirectInsert_ShouldAllowSamePhoneForDistinctIdentityAndEmail()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        const string sharedPhone = "0900000007";

        await InsertResidentAsync(connection, transaction, "CCCD", CreateNumericIdentity(), $"first-{Guid.NewGuid():N}@example.test", sharedPhone);
        await InsertResidentAsync(connection, transaction, "CCCD", CreateNumericIdentity(), $"second-{Guid.NewGuid():N}@example.test", sharedPhone);

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DirectInsert_ShouldAllowSameNumericIdentityForDifferentIdentityTypes()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var identity = CreateNumericIdentity();

        await InsertResidentAsync(connection, transaction, "CCCD", identity, $"cccd-{Guid.NewGuid():N}@example.test", "0900000008");
        await InsertResidentAsync(connection, transaction, "CMND", identity, $"cmnd-{Guid.NewGuid():N}@example.test", "0900000009");

        await transaction.RollbackAsync();
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task InsertResidentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string identityType,
        string identityNumber,
        string? email,
        string phoneNumber)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO residents.residents
                (id, resident_code, full_name, identity_type, identity_number, phone_number, email, status, created_at, updated_at)
            VALUES
                (@id, @resident_code, @full_name, @identity_type, @identity_number, @phone_number, @email, 'ACTIVE', @now, @now);
            """,
            connection,
            transaction);
        var id = Guid.NewGuid();
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("resident_code", $"TST-{id:N}"[..24]);
        command.Parameters.AddWithValue("full_name", "Canonical uniqueness test");
        command.Parameters.AddWithValue("identity_type", identityType);
        command.Parameters.AddWithValue("identity_number", identityNumber);
        command.Parameters.AddWithValue("phone_number", phoneNumber);
        command.Parameters.AddWithValue("email", email is null ? DBNull.Value : email);
        command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateNumericIdentity()
    {
        return string.Concat(Guid.NewGuid().ToByteArray().Take(12).Select(value => (value % 10).ToString()));
    }

    private static string FormatIdentity(string identity)
    {
        return $"{identity[..3]} {identity[3..6]}-{identity[6..9]}.{identity[9..]}";
    }
}
