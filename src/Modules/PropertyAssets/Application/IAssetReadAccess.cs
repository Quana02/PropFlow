namespace PropFlow.Modules.PropertyAssets.Application;

public interface IAssetReadAccess
{
    Task<AssetReadScope> GetScopeAsync(CancellationToken ct);
}

public sealed record AssetReadScope(bool Unrestricted, bool HasAssignedWork, Guid[] FacilityIds, Guid[] EquipmentIds)
{
    public static AssetReadScope Manager { get; } = new(true, true, [], []);
    public static AssetReadScope None { get; } = new(false, false, [], []);
}
