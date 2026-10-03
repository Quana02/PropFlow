using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Residents.Application;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.UnitTests;

public sealed class ResidentDuplicatePolicyTests
{
    [Theory]
    [InlineData("ux_residents_identity_canonical", "RESIDENT_IDENTITY_ALREADY_EXISTS", "identityNumber")]
    [InlineData("ux_residents_email_canonical", "RESIDENT_EMAIL_ALREADY_EXISTS", "email")]
    public void TranslateDatabaseException_MapsCanonicalIndexesToSafeBusinessErrors(
        string constraintName,
        string expectedCode,
        string expectedField)
    {
        using var db = CreateContext();
        var policy = new ResidentDuplicatePolicy(db);
        var postgres = new PostgresException(
            "duplicate key",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName);

        var translated = policy.TranslateDatabaseException(new DbUpdateException("database write failed", postgres));

        Assert.NotNull(translated);
        Assert.Equal(expectedCode, translated.Code);
        Assert.Equal(expectedField, translated.Field);
        Assert.Equal(409, translated.StatusCode);
        Assert.DoesNotContain(constraintName, translated.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TranslateDatabaseException_DoesNotHideUnrelatedUniqueConstraint()
    {
        using var db = CreateContext();
        var policy = new ResidentDuplicatePolicy(db);
        var postgres = new PostgresException(
            "duplicate key",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: "IX_residents_resident_code");

        Assert.Null(policy.TranslateDatabaseException(new DbUpdateException("database write failed", postgres)));
    }

    private static ResidentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ResidentsDbContext>()
            .UseInMemoryDatabase($"resident-duplicate-policy-{Guid.NewGuid():N}")
            .Options;
        return new ResidentsDbContext(options);
    }
}
