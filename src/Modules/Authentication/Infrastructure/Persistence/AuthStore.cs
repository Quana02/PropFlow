using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Domain.Tokens;
using PropFlow.Modules.Authentication.Domain.ResidentVerifications;

namespace PropFlow.Modules.Authentication.Infrastructure.Persistence;

public sealed class AuthStore(AuthenticationDbContext db) : IAuthStore
{
    public async Task<T> SerializedAsync<T>(string key, Func<Task<T>> action, CancellationToken ct)
    {
        // All participating contexts use the same database and sequential operations.
        // Npgsql reuses the ambient enlisted connection between contexts.
        using var transaction = new TransactionScope(TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted }, TransactionScopeAsyncFlowOption.Enabled);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        db.ChangeTracker.Clear();
        try
        {
            var result = await action();
            await db.SaveChangesAsync(ct);
            transaction.Complete();
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new AuthFailure(409, "account_conflict", "Thông tin tài khoản không thể sử dụng. Vui lòng kiểm tra hoặc liên hệ ban quản lý.");
        }
    }
    public Task<UserAccount?> UserAsync(Guid id, CancellationToken ct) => db.UserAccounts.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<UserAccount?> UsernameAsync(string username, CancellationToken ct) => db.UserAccounts.SingleOrDefaultAsync(x => x.Username == username, ct);
    public Task<UserAccount?> EmailAsync(string email, CancellationToken ct) => db.UserAccounts.SingleOrDefaultAsync(x => x.Email == email, ct);
    public Task<bool> PhoneInUseAsync(string phone, Guid? except, CancellationToken ct) => db.UserAccounts.AnyAsync(x => x.PhoneNumber == phone && x.Id != except, ct);
    public Task<ResidentVerification?> VerificationAsync(Guid id, CancellationToken ct) => db.ResidentVerifications.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<List<ResidentVerification>> VerificationsAsync(Guid id, CancellationToken ct) => db.ResidentVerifications.Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public Task<PasswordResetToken?> RecoveryAsync(Guid id, CancellationToken ct) => db.PasswordResetTokens.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<List<PasswordResetToken>> RecoveriesAsync(Guid id, CancellationToken ct) => db.PasswordResetTokens.Where(x => x.UserId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    public Task<RefreshToken?> RefreshAsync(string hash, CancellationToken ct) => db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    public Task<RefreshToken?> RefreshByIdAsync(Guid id, CancellationToken ct) => db.RefreshTokens.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<List<RefreshToken>> SessionsAsync(Guid id, CancellationToken ct) => db.RefreshTokens.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct);
    public void Add(UserAccount value) => db.Add(value);
    public void Add(ResidentVerification value) => db.Add(value);
    public void Add(PasswordResetToken value) => db.Add(value);
    public void Add(RefreshToken value) => db.Add(value);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
