namespace PropFlow.Modules.Residents.Contracts;

public sealed record ResidentAccountScopeItem(Guid ResidentId, Guid? UserId, string ResidentCode, string FullName,
    string? ContactEmail, bool IsActive, IReadOnlyList<string> Apartments);

public sealed record ResidentAccountScopePage(IReadOnlyList<ResidentAccountScopeItem> Items, int TotalCount, int PageIndex);

public interface IResidentAccountScope
{
    Task<ResidentAccountScopePage> SearchAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<Guid?> FindActiveAccountAsync(Guid residentId, CancellationToken ct);

    // Called inside the account mutation transaction so the link and status cannot change before commit.
    Task<bool> LockActiveLinkAsync(Guid residentId, Guid userId, CancellationToken ct);
}
