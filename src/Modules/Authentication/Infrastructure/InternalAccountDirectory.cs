using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.Modules.Authentication.Infrastructure;

public sealed class InternalAccountDirectory(AuthenticationDbContext db, IAuthSecrets secrets) : IInternalAccountDirectory
{
    public async Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        await db.UserAccounts.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .Select(x => new InternalAccountRecord(x.Id, x.Username, x.DisplayName, x.Email, x.Status.ToString())).ToArrayAsync(ct);

    public async Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToUpperInvariant();
        if (await db.UserAccounts.AnyAsync(x => x.Username == username || x.Email == email, ct))
            throw new InvalidOperationException("Tên đăng nhập hoặc email đã được sử dụng.");
        var account = new UserAccount(username, "pending", request.DisplayName, DateTimeOffset.UtcNow, email, request.PhoneNumber,
            AccountStatus.ACTIVE, emailVerified: true, createdBy: actorUserId);
        account.ChangePasswordHash(secrets.HashPassword(account, request.Password), actorUserId, DateTimeOffset.UtcNow);
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return new(account.Id, account.Username, account.DisplayName, account.Email, account.Status.ToString());
    }

    public async Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct)
    {
        var account = await db.UserAccounts.SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (account is null) return null;
        var now = DateTimeOffset.UtcNow;
        switch (status.Trim().ToUpperInvariant())
        {
            case "ACTIVE": account.Activate(actorUserId, now); break;
            case "LOCKED": account.Lock(actorUserId, now); break;
            case "DISABLED": account.Disable(actorUserId, now); break;
            default: throw new ArgumentException("Trạng thái tài khoản không được hỗ trợ.", nameof(status));
        }
        await db.SaveChangesAsync(ct);
        return new(account.Id, account.Username, account.DisplayName, account.Email, account.Status.ToString());
    }
}
