namespace PropFlow.Modules.PropertyAssets.Contracts;

public interface ICurrentBuildingFloorLimit
{
    Task<int?> GetMaximumFloorAsync(CancellationToken ct);
}
