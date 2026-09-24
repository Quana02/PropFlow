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

    public async Task<PagedInternalAccountRecords> SearchAsync(
        IReadOnlyCollection<Guid> userIds,
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (userIds.Count == 0) return new(0, []);

        var query = db.UserAccounts.AsNoTracking().Where(x => userIds.Contains(x.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToUpperInvariant();
            query = query.Where(x => x.Username.ToUpper().Contains(normalizedSearch)
                || x.DisplayName.ToUpper().Contains(normalizedSearch)
                || (x.Email != null && x.Email.ToUpper().Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AccountStatus>(status.Trim(), true, out var parsedStatus))
                throw new ArgumentException("Trạng thái tài khoản không được hỗ trợ.", nameof(status));
            query = query.Where(x => x.Status == parsedStatus);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.DisplayName).ThenBy(x => x.Username)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new InternalAccountRecord(x.Id, x.Username, x.DisplayName, x.Email, x.Status.ToString()))
            .ToArrayAsync(ct);
        return new(total, items);
    }

    public async Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToUpperInvariant();
        if (await db.UserAccounts.AnyAsync(x => x.Username == username, ct))
            throw new InternalAccountConflictException("Tên đăng nhập đã được sử dụng.");
        if (await db.UserAccounts.AnyAsync(x => x.Email == email, ct))
            throw new InternalAccountConflictException("Email đã được sử dụng.");
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
