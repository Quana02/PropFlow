using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Api.Composition;

// Resident shell enrichment: uses the configured condominium address, never the user's IP/GPS.
public sealed class CurrentBuildingWeatherService(ICurrentBuildingLocation buildings, IHttpClientFactory clients,
    IMemoryCache cache, TimeProvider clock)
{
    public async Task<CurrentBuildingWeatherResponse> GetAsync(CancellationToken ct)
    {
        var building = await buildings.GetAsync(ct);
        var zone = building?.TimeZoneId ?? "Asia/Ho_Chi_Minh";
        var city = CityFromAddress(building?.Address);
        if (city is null) return Unavailable(null, zone, "Chưa cấu hình địa chỉ chung cư.");
        var key = ("resident-building-weather", building!.Address, zone);
        if (cache.TryGetValue(key, out CurrentBuildingWeatherResponse? cached) && cached is not null) return cached;
        CurrentBuildingWeatherResponse result;
        try
        {
            var query = Uri.EscapeDataString(city);
            var geo = await clients.CreateClient("BuildingWeatherGeocoding").GetFromJsonAsync<Geocoding>(
                $"v1/search?name={query}&count=10&language=vi&countryCode=VN", ct);
            var normalized = Normalize(city);
            var location = geo?.Results?.Where(x => x.CountryCode == "VN" && x.Latitude is >= -90 and <= 90 && x.Longitude is >= -180 and <= 180)
                .Where(x => Normalize(x.Name) == normalized || Normalize(x.Admin1) == normalized)
                .OrderByDescending(x => Normalize(x.Name) == normalized).FirstOrDefault();
            if (location is null) result = Unavailable(city, zone, "Chưa xác định được vị trí từ địa chỉ chung cư.");
            else
            {
                var latitude = location.Latitude!.Value.ToString(CultureInfo.InvariantCulture);
                var longitude = location.Longitude!.Value.ToString(CultureInfo.InvariantCulture);
                var forecast = await clients.CreateClient("BuildingWeatherForecast").GetFromJsonAsync<Forecast>(
                    $"v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code,is_day&temperature_unit=celsius&timezone=UTC&forecast_days=1", ct);
                var current = forecast?.Current;
                if (current?.Temperature is not { } temperature || current.Code is not { } code || temperature is < -100 or > 70)
                    result = Unavailable(city, zone, "Dữ liệu thời tiết chưa khả dụng.");
                else
                {
                    var (condition, icon) = Describe(code, current.IsDay == 1);
                    result = new(city, zone, temperature, condition, icon, clock.GetUtcNow());
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { result = Unavailable(city, zone, "Chưa tải được thời tiết. Vui lòng thử lại sau."); }
        cache.Set(key, result, result.TemperatureC.HasValue ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(1));
        return result;
    }

    private static CurrentBuildingWeatherResponse Unavailable(string? city, string zone, string message) =>
        new(city, zone, null, "Chưa có dữ liệu", "cloud_off", null, message);

    public static string? CityFromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        var parts = address.Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(p => Normalize(p) is not ("vietnam" or "vn") && !Regex.IsMatch(p, @"^\d+$")).ToArray();
        var city = parts.LastOrDefault(p => Regex.IsMatch(p, @"^(thành phố|tp\.?|thị xã)\s+", RegexOptions.IgnoreCase))
            ?? parts.LastOrDefault();
        if (city is null) return null;
        city = Regex.Replace(city, @"^(thành phố|tp\.?|tỉnh|thị xã)\s+", "", RegexOptions.IgnoreCase).Trim();
        return Normalize(city) switch { "hcm" or "tphcm" => "Hồ Chí Minh", "hn" => "Hà Nội", _ => city };
    }

    private static string Normalize(string? text)
    {
        var value = (text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        value = new string(value.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)).ToArray()).Replace('đ', 'd');
        return Regex.Replace(value, "^(thanhpho|tinh)|city$", "");
    }
    private static (string, string) Describe(int code, bool day) => code switch
    {
        0 => (day ? "Trời quang" : "Đêm quang", day ? "light_mode" : "nightlight"),
        1 or 2 => ("Ít mây", day ? "partly_cloudy_day" : "partly_cloudy_night"),
        3 => ("Nhiều mây", "cloud"), 45 or 48 => ("Sương mù", "foggy"),
        51 or 53 or 55 or 56 or 57 => ("Mưa phùn", "rainy"),
        61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => ("Có mưa", "rainy"),
        71 or 73 or 75 or 77 or 85 or 86 => ("Có tuyết", "weather_snowy"),
        95 or 96 or 99 => ("Dông", "thunderstorm"), _ => ("Thời tiết hiện tại", "cloud")
    };
    private sealed record Geocoding(Location[]? Results);
    private sealed record Location(string? Name, decimal? Latitude, decimal? Longitude, string? Admin1,
        [property: JsonPropertyName("country_code")] string? CountryCode);
    private sealed record Forecast(Current? Current);
    private sealed record Current([property: JsonPropertyName("temperature_2m")] decimal? Temperature,
        [property: JsonPropertyName("weather_code")] int? Code, [property: JsonPropertyName("is_day")] int? IsDay);
}
