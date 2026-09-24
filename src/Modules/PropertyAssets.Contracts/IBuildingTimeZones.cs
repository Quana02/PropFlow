namespace PropFlow.Modules.PropertyAssets.Contracts;

public interface IBuildingTimeZones
{
    Task<string> GetSystemTimeZoneAsync(CancellationToken ct);
}
