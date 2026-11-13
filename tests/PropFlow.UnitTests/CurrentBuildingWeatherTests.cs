using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using PropFlow.Api.Composition;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class CurrentBuildingWeatherTests
{
    [Theory]
    [InlineData("12 Nguyễn Văn Linh, Quận Hải Châu, Thành phố Đà Nẵng, Việt Nam", "Đà Nẵng")]
    [InlineData("1 Lê Duẩn, TP. Pleiku, Gia Lai", "Pleiku")]
    [InlineData("Tòa A, Hà Nội", "Hà Nội")]
    [InlineData("Tòa A, TPHCM", "Hồ Chí Minh")]
    [InlineData("", null)]
    public void City_comes_from_configured_address(string address, string? city) =>
        Assert.Equal(city, CurrentBuildingWeatherService.CityFromAddress(address));

    [Fact]
    public async Task Missing_building_never_uses_a_fake_city_or_temperature()
    {
        var location = new Location(null);
        var provider = new Provider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await new CurrentBuildingWeatherService(location, provider, cache, TimeProvider.System).GetAsync(default);
        Assert.Null(result.City); Assert.Null(result.TemperatureC); Assert.Equal(0, provider.Requests);
        Assert.Contains("Chưa cấu hình", result.Message);
    }

    [Fact]
    public async Task Weather_uses_building_city_coordinates_and_caches_success()
    {
        var provider = new Provider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new CurrentBuildingWeatherService(new Location(new("12 Lê Duẩn, Đà Nẵng", "Asia/Ho_Chi_Minh")), provider, cache, TimeProvider.System);
        var result = await service.GetAsync(default);
        Assert.Equal("Đà Nẵng", result.City); Assert.Equal(29.5m, result.TemperatureC);
        Assert.Equal("Có mưa", result.Condition); Assert.Equal("rainy", result.Icon);
        Assert.Equal(2, provider.Requests);
        Assert.Equal(result, await service.GetAsync(default)); Assert.Equal(2, provider.Requests);
        Assert.Contains("latitude=16.06778", provider.ForecastPath);
        Assert.Contains("longitude=108.22083", provider.ForecastPath);
    }

    [Fact]
    public async Task Provider_failure_keeps_correct_city_without_inventing_temperature()
    {
        var provider = new Provider { Fail = true };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await new CurrentBuildingWeatherService(new Location(new("Tòa A, Hà Nội", "Asia/Ho_Chi_Minh")), provider, cache, TimeProvider.System).GetAsync(default);
        Assert.Equal("Hà Nội", result.City); Assert.Null(result.TemperatureC);
        Assert.NotNull(result.Message); Assert.Null(result.UpdatedAt);
    }

    [Fact]
    public async Task Geocoding_mismatch_does_not_show_weather_for_another_city()
    {
        var provider = new Provider();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await new CurrentBuildingWeatherService(new Location(new("Tòa A, Hà Nội", "Asia/Ho_Chi_Minh")), provider, cache, TimeProvider.System).GetAsync(default);
        Assert.Equal("Hà Nội", result.City); Assert.Null(result.TemperatureC); Assert.Equal(1, provider.Requests);
    }
    private sealed class Location(CurrentBuildingLocation? value) : ICurrentBuildingLocation
    { public Task<CurrentBuildingLocation?> GetAsync(CancellationToken ct) => Task.FromResult(value); }
    private sealed class Provider : HttpMessageHandler, IHttpClientFactory
    {
        public bool Fail;
        public int Requests;
        public string? ForecastPath;
        public HttpClient CreateClient(string name) => new(this, false) { BaseAddress = new(name == "BuildingWeatherForecast" ? "https://api.open-meteo.com/" : "https://geocoding-api.open-meteo.com/") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (Fail) throw new HttpRequestException("Simulated outage");
            if (request.RequestUri!.AbsolutePath.Contains("forecast"))
            {
                ForecastPath = request.RequestUri.Query;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { current = new { temperature_2m = 29.5m, weather_code = 61, is_day = 1 } }) });
            }
            Assert.Contains("countryCode=VN", request.RequestUri.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            { results = new[] { new { name = "Đà Nẵng", admin1 = "Đà Nẵng", country_code = "VN", latitude = 16.06778m, longitude = 108.22083m } } }) });
        }
    }
}
