namespace PropFlow.Modules.PropertyAssets.Contracts;

public sealed record MaintenanceAssetReference(
    Guid Id,
    string Code,
    string Name,
    string AssetType,
    string Status,
    string? LocationDescription,
    Guid? FacilityId = null);

public interface IMaintenanceAssetSource
{
    Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct);
    Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct);
    Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct);
    Task MarkUnderMaintenanceAsync(Guid facilityId, Guid? equipmentId, Guid actor, CancellationToken ct) => Task.CompletedTask;
    Task MarkActiveAsync(Guid facilityId, Guid? equipmentId, Guid actor, CancellationToken ct) => Task.CompletedTask;
}
