using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.Modules.Residents.Application;

public sealed class ResidentDuplicateException(string code, string field, string message) : Exception(message)
{
    public string Code { get; } = code;
    public string Field { get; } = field;
    public int StatusCode => 409;
}

public sealed class ResidentDuplicatePolicy(ResidentsDbContext db)
{
    private const string IdentityIndexName = "ux_residents_identity_canonical";
    private const string EmailIndexName = "ux_residents_email_canonical";

    public async Task EnsureUniqueAsync(string? identityType, string? identityNumber, string? email, Guid? excludedResidentId, CancellationToken ct)
    {
        var typeKey = ResidentProfileNormalization.IdentityType(identityType);
        var numberKey = ResidentProfileNormalization.IdentityNumber(identityNumber);
        var emailKey = ResidentProfileNormalization.Email(email);
        var lockKeys = new List<string>();
        if (typeKey is not null && numberKey is not null) lockKeys.Add($"resident:identity:{typeKey}:{numberKey}");
        if (emailKey is not null) lockKeys.Add($"resident:email:{emailKey}");

        foreach (var lockKey in lockKeys.Order(StringComparer.Ordinal))
        {
            var lockId = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(lockKey)));
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
        }

        if (typeKey is not null && numberKey is not null && await IdentityExistsAsync(typeKey, numberKey, excludedResidentId, ct))
            throw new ResidentDuplicateException("RESIDENT_IDENTITY_ALREADY_EXISTS", "identityNumber", "Số giấy tờ đã được sử dụng bởi một cư dân khác.");

        if (emailKey is not null && await EmailExistsAsync(emailKey, excludedResidentId, ct))
            throw new ResidentDuplicateException("RESIDENT_EMAIL_ALREADY_EXISTS", "email", "Email đã được sử dụng bởi một cư dân khác.");
    }

    private Task<bool> IdentityExistsAsync(string typeKey, string numberKey, Guid? excludedResidentId, CancellationToken ct) =>
        excludedResidentId.HasValue
            ? db.Residents.FromSqlInterpolated($"SELECT * FROM residents.residents WHERE id <> {excludedResidentId.Value} AND upper(btrim(identity_type)) = {typeKey} AND regexp_replace(identity_number, '[^0-9]', '', 'g') = {numberKey}").AsNoTracking().AnyAsync(ct)
            : db.Residents.FromSqlInterpolated($"SELECT * FROM residents.residents WHERE upper(btrim(identity_type)) = {typeKey} AND regexp_replace(identity_number, '[^0-9]', '', 'g') = {numberKey}").AsNoTracking().AnyAsync(ct);

    private Task<bool> EmailExistsAsync(string emailKey, Guid? excludedResidentId, CancellationToken ct) =>
        excludedResidentId.HasValue
            ? db.Residents.FromSqlInterpolated($"SELECT * FROM residents.residents WHERE id <> {excludedResidentId.Value} AND lower(btrim(email)) = {emailKey}").AsNoTracking().AnyAsync(ct)
            : db.Residents.FromSqlInterpolated($"SELECT * FROM residents.residents WHERE lower(btrim(email)) = {emailKey}").AsNoTracking().AnyAsync(ct);

    public ResidentDuplicateException? TranslateDatabaseException(DbUpdateException exception)
    {
        if (exception.InnerException is not PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
            return null;

        return postgres.ConstraintName switch
        {
            IdentityIndexName => new ResidentDuplicateException(
                "RESIDENT_IDENTITY_ALREADY_EXISTS",
                "identityNumber",
                "Số giấy tờ đã được sử dụng bởi một cư dân khác."),
            EmailIndexName => new ResidentDuplicateException(
                "RESIDENT_EMAIL_ALREADY_EXISTS",
                "email",
                "Email đã được sử dụng bởi một cư dân khác."),
            _ => null
        };
    }
}

public sealed class ResidentProfileService(
    ResidentsDbContext db,
    ResidentDuplicatePolicy duplicates,
    IAtomicTransactionCoordinator transaction,
    TimeProvider clock)
{
    public async Task<bool> UpdateAsync(Guid residentId, ResidentProfileUpdate command, Guid? actorId, CancellationToken ct)
    {
        try
        {
            return await transaction.ExecuteAsync(async token =>
            {
                var resident = await db.Residents.SingleOrDefaultAsync(x => x.Id == residentId, token);
                if (resident is null) return false;
                await duplicates.EnsureUniqueAsync(command.IdentityType, command.IdentityNumber, command.Email, residentId, token);
                resident.UpdateProfile(command.FullName, command.DateOfBirth, command.Gender, command.Nationality,
                    command.IdentityType, command.IdentityNumber, command.IdentityIssuedDate, command.IdentityExpiryDate,
                    command.PhoneNumber, command.Email, command.Note, actorId, clock.GetUtcNow());
                await db.SaveChangesAsync(token);
                return true;
            }, ct);
        }
        catch (DbUpdateException exception)
        {
            var translated = duplicates.TranslateDatabaseException(exception);
            if (translated is not null) throw translated;
            throw;
        }
    }
}

public sealed record ResidentProfileUpdate(string FullName, DateOnly? DateOfBirth, string? Gender, string? Nationality,
    string? IdentityType, string? IdentityNumber, DateOnly? IdentityIssuedDate, DateOnly? IdentityExpiryDate,
    string? PhoneNumber, string? Email, string? Note);
