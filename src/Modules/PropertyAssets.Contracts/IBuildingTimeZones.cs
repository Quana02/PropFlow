namespace PropFlow.Modules.PropertyAssets.Contracts;

public sealed record BuildingTimeZone(Guid BuildingId, string TimeZoneId);

public interface IBuildingTimeZones
{
    Task<IReadOnlyList<BuildingTimeZone>> GetAllAsync(CancellationToken ct);
}
