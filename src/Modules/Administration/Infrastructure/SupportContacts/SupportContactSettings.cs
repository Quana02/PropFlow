using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.SystemConfigurations;
using PropFlow.Modules.Administration.Infrastructure.Persistence;

namespace PropFlow.Modules.Administration.Infrastructure.SupportContacts;

public sealed class SupportContactsConflictException : Exception;
public sealed class SupportContactsUnavailableException : Exception;

// Only these public contact values are supported. No configuration/provider or credential editing.
public sealed class SupportContactSettings(AdministrationDbContext db, TimeProvider clock)
{
    private Task<List<SystemConfiguration>> ReadRows(CancellationToken ct) => db.SystemConfigurations.AsNoTracking()
        .Where(x => x.ConfigKey == SupportContactRules.EmailKey || x.ConfigKey == SupportContactRules.PhoneKey).ToListAsync(ct);

    private static SupportContactsState State(IReadOnlyList<SystemConfiguration> rows)
    {
        string? Value(string key) => rows.SingleOrDefault(x => x.ConfigKey == key && x.IsActive)?.ConfigValue;
        var email = SupportContactRules.NormalizeEmail(Value(SupportContactRules.EmailKey));
        var phone = SupportContactRules.NormalizePhone(Value(SupportContactRules.PhoneKey));
        if (SupportContactRules.Validate(email, phone).Any()) throw new SupportContactsUnavailableException();
        // The two rows and their versions are read in one DB snapshot; includes inactive/empty rows to prevent ABA overwrites.
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|",
            rows.OrderBy(x => x.ConfigKey, StringComparer.Ordinal).Select(x => $"{x.ConfigKey}:{x.Id:N}:{x.Version:N}")))));
        return new(email, phone, version);
    }

    public async Task<SupportContactsState> ReadAsync(CancellationToken ct) => State(await ReadRows(ct));
    public async Task<PublicSupportContacts> PublicAsync(CancellationToken ct)
    {
        var state = await ReadAsync(ct);
        return new(state.Email, state.Phone);
    }

    public async Task<SupportContactsState> UpdateAsync(UpdateSupportContacts input, Guid actor, CancellationToken ct)
    {
        if (actor == Guid.Empty) throw new ValidationException("Phiên đăng nhập không hợp lệ.");
        Validator.ValidateObject(input, new ValidationContext(input), true);
        var email = SupportContactRules.NormalizeEmail(input.Email);
        var phone = SupportContactRules.NormalizePhone(input.Phone);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Also serializes first creation when no settings rows exist yet.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('administration-support-contacts', 8))", ct);
        var rows = await db.SystemConfigurations.Where(x => x.ConfigKey == SupportContactRules.EmailKey || x.ConfigKey == SupportContactRules.PhoneKey).ToListAsync(ct);
        foreach (var row in rows) await db.Entry(row).ReloadAsync(ct);
        if (!string.Equals(State(rows).Version, input.ExpectedVersion, StringComparison.OrdinalIgnoreCase)) throw new SupportContactsConflictException();
        void Set(string key, string? value)
        {
            var row = rows.SingleOrDefault(x => x.ConfigKey == key);
            if (row is null)
            {
                if (value is null) return;
                row = new(key, value, clock.GetUtcNow(), updatedBy: actor);
                rows.Add(row); db.SystemConfigurations.Add(row);
            }
            else if (row.ConfigValue != (value ?? "") || !row.IsActive)
            {
                row.UpdateValue(value ?? "", actor, clock.GetUtcNow());
                if (!row.IsActive) row.ToggleActive(true, actor, clock.GetUtcNow());
            }
        }
        Set(SupportContactRules.EmailKey, email); Set(SupportContactRules.PhoneKey, phone);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new SupportContactsConflictException(); }
        return State(rows);
    }
}
