namespace PropFlow.Modules.PropertyAssets.Contracts;

public interface ICurrentBuildingTimeZone
{
    Task<string> GetAsync(CancellationToken ct);
}
