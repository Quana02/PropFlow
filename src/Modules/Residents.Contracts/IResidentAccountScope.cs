namespace PropFlow.Modules.Residents.Contracts;

public sealed record ResidentAccountScopeItem(Guid ResidentId, Guid? UserId, string ResidentCode, string FullName,
    string? ContactEmail, bool IsActive, IReadOnlyList<string> Apartments);

public sealed record ResidentAccountScopePage(IReadOnlyList<ResidentAccountScopeItem> Items, int TotalCount, int PageIndex);

public interface IResidentAccountScope
{
    Task<ResidentAccountScopePage> SearchAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    // Batch read boundary for callers that must display an already-linked
    // resident or filter a page of candidates without per-row database calls.
    Task<IReadOnlyDictionary<Guid, ResidentAccountScopeItem>> GetByAccountIdsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
    Task<Guid?> FindActiveAccountAsync(Guid residentId, CancellationToken ct);

    // Cross-module callers that receive an authenticated user id must not infer
    // resident eligibility from a role assignment alone. Residents owns the link
    // between that account and an active resident profile.
    Task<bool> IsActiveAccountAsync(Guid userId, CancellationToken ct);

    // Called inside the account mutation transaction so the link and status cannot change before commit.
    Task<bool> LockActiveLinkAsync(Guid residentId, Guid userId, CancellationToken ct);
}
