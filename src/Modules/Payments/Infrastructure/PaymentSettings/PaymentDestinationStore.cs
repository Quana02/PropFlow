using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed class PaymentDestinationStore(PaymentsDbContext db) : IPaymentDestinationStore
{
    public Task<PaymentDestination?> ActiveAsync(CancellationToken ct) =>
        db.PaymentDestinations.AsNoTracking().SingleOrDefaultAsync(item => item.IsActive, ct);

    public async Task<IReadOnlyList<PaymentDestination>> HistoryAsync(CancellationToken ct) =>
        await db.PaymentDestinations.AsNoTracking().OrderByDescending(item => item.Version).Take(50).ToArrayAsync(ct);

    public async Task<PaymentDestination> AddReplacementAsync(
        PaymentDestinationWrite write,
        int expectedVersion,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('payment-destination'))", ct);
        var active = await db.PaymentDestinations.SingleOrDefaultAsync(item => item.IsActive, ct);
        if ((active?.Version ?? 0) != expectedVersion) throw Conflict();
        if (active is not null) active.Deactivate(actorUserId, now);
        var lastVersion = await db.PaymentDestinations.MaxAsync(item => (int?)item.Version, ct) ?? 0;
        var destination = new PaymentDestination(
            lastVersion + 1,
            write.BankBin,
            write.BankCode,
            write.BankName,
            write.ProtectedAccountNumber,
            write.AccountNumberLast4,
            write.AccountName,
            actorUserId,
            now,
            write.SePayAccountId);
        db.PaymentDestinations.Add(destination);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return destination;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw Conflict();
        }
    }

    public async Task DeactivateAsync(int expectedVersion, Guid actorUserId, DateTimeOffset now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('payment-destination'))", ct);
        var active = await db.PaymentDestinations.SingleOrDefaultAsync(item => item.IsActive, ct);
        if (active is null || active.Version != expectedVersion) throw Conflict();
        active.Deactivate(actorUserId, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static PaymentSettingsFailure Conflict() => new(409, "payment_destination_changed", "Cấu hình đã được thay đổi bởi phiên làm việc khác. Vui lòng tải lại.");
}
