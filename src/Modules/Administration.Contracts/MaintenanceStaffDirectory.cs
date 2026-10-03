namespace PropFlow.Modules.Administration.Contracts;

// Narrow read boundary for operational modules that need to assign work to
// active STAFF accounts. Role and account-status ownership stays in Administration.
public sealed record MaintenanceStaffRecord(Guid UserId, string Username, string DisplayName);

public interface IMaintenanceStaffDirectory
{
    Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct);
    Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<MaintenanceStaffRecord>> GetHistoricalStaffAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) => GetActiveStaffAsync(ct);
}
