namespace PropFlow.Modules.PropertyAssets.Contracts;

public sealed record CurrentBuildingLocation(string Address, string TimeZoneId);
public interface ICurrentBuildingLocation
{
    Task<CurrentBuildingLocation?> GetAsync(CancellationToken ct);
}

public sealed record CurrentBuildingWeatherResponse(string? City, string TimeZoneId,
    decimal? TemperatureC, string Condition, string Icon, DateTimeOffset? UpdatedAt, string? Message = null);
