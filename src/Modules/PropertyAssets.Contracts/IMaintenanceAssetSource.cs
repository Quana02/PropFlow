namespace PropFlow.Modules.PropertyAssets.Contracts;

public sealed record MaintenanceAssetReference(
    Guid Id,
    string Code,
    string Name,
    string AssetType,
    string Status,
    string? LocationDescription,
    Guid? FacilityId,
    string? FacilityType)
{
    public MaintenanceAssetReference(
        Guid id,
        string code,
        string name,
        string assetType,
        string status,
        string? locationDescription)
        : this(id, code, name, assetType, status, locationDescription, null, null) { }

    public MaintenanceAssetReference(
        Guid id,
        string code,
        string name,
        string assetType,
        string status,
        string? locationDescription,
        Guid? facilityId)
        : this(id, code, name, assetType, status, locationDescription, facilityId, null) { }
}

public interface IMaintenanceAssetSource
{
    Task<IReadOnlyList<MaintenanceAssetReference>> GetAvailableAsync(CancellationToken ct);
    Task<MaintenanceAssetReference?> GetFacilityAsync(Guid facilityId, CancellationToken ct);
    Task<MaintenanceAssetReference?> GetEquipmentAsync(Guid equipmentId, CancellationToken ct);
    Task<bool> ExistsAsync(Guid? facilityId, Guid? equipmentId, CancellationToken ct);
    Task MarkUnderMaintenanceAsync(Guid facilityId, Guid? equipmentId, Guid actor, CancellationToken ct) => Task.CompletedTask;
    Task MarkActiveAsync(Guid facilityId, Guid? equipmentId, Guid actor, CancellationToken ct) => Task.CompletedTask;
    Task MarkUnderMaintenanceAsync(Guid? facilityId, Guid? equipmentId, Guid actor, CancellationToken ct) =>
        facilityId is { } value ? MarkUnderMaintenanceAsync(value, equipmentId, actor, ct) : Task.CompletedTask;
    Task MarkActiveAsync(Guid? facilityId, Guid? equipmentId, Guid actor, CancellationToken ct) =>
        facilityId is { } value ? MarkActiveAsync(value, equipmentId, actor, ct) : Task.CompletedTask;
}
