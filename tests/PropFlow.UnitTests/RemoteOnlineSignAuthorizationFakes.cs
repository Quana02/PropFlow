using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.RemoteOnlineSign.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.UnitTests;

internal sealed class RemoteTestContractCodes : IRemoteContractCodeGenerator
{
    private int next = 1;
    public Task<string> NextAsync(DateTimeOffset createdAt, CancellationToken ct) => Task.FromResult($"HD-TEST-{next++:D6}");
}

internal sealed class RemoteTestAccountAccess(Func<Guid, bool>? resident = null) : IAccountAccess
{
    private readonly Func<Guid, bool> resident = resident ?? (_ => true);
    public Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult<AccountAccess?>(resident(userId) ? new("RESIDENT", []) : null);
    public Task<Guid[]> GetResidentUserIdsAsync(Guid[] userIds, CancellationToken ct) => Task.FromResult(userIds.Where(resident).ToArray());
    public Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class RemoteTestAccountDirectory(Func<Guid, string?>? status = null) : IInternalAccountDirectory
{
    private readonly Func<Guid, string?> status = status ?? (_ => "ACTIVE");
    public Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InternalAccountRecord>>(userIds.Where(id => status(id) is not null).Select(id => new InternalAccountRecord(id, "test", "Test", "test@example.invalid", status(id)!)).ToArray());
    public Task<PagedInternalAccountRecords> SearchAsync(IReadOnlyCollection<Guid> userIds, string? search, string? status, int page, int pageSize, CancellationToken ct) => Task.FromResult(new PagedInternalAccountRecords(0, []));
    public Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
    public Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class RemoteTestResidentScope(Func<Guid, bool>? active = null, IReadOnlyList<ResidentAccountScopeItem>? items = null) : IResidentAccountScope
{
    private readonly Func<Guid, bool> active = active ?? (_ => true);
    private readonly IReadOnlyList<ResidentAccountScopeItem> items = items ?? [];
    public Task<bool> IsActiveAccountAsync(Guid userId, CancellationToken ct) => Task.FromResult(active(userId));
    public Task<IReadOnlyDictionary<Guid, ResidentAccountScopeItem>> GetByAccountIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ResidentAccountScopeItem>>(userIds.Where(active).Distinct().ToDictionary(id => id, id => new ResidentAccountScopeItem(Guid.NewGuid(), id, "TEST", "Test resident", null, true, [])));
    public Task<ResidentAccountScopePage> SearchAsync(string? search, int pageIndex, int pageSize, CancellationToken ct) =>
        Task.FromResult(new ResidentAccountScopePage(items.Where(x => string.IsNullOrWhiteSpace(search) || x.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) || x.ResidentCode.Contains(search, StringComparison.OrdinalIgnoreCase) || x.ContactEmail?.Contains(search, StringComparison.OrdinalIgnoreCase) == true).Take(pageSize).ToArray(), items.Count, pageIndex));
    public Task<Guid?> FindActiveAccountAsync(Guid residentId, CancellationToken ct) => Task.FromResult<Guid?>(null);
    public Task<bool> LockActiveLinkAsync(Guid residentId, Guid userId, CancellationToken ct) => Task.FromResult(false);
}
