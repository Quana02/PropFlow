using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Application.Equipment.Dtos;
using PropFlow.Modules.PropertyAssets.Application.Facilities.Dtos;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Buildings;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class PropertyOperationsAuthorizationHttpTests
{
    [Fact]
    [Trait("Feature", "FE-04")]
    public async Task Current_building_update_requires_manager_and_preserves_singleton_identity()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        var before = await assets.Buildings.AsNoTracking().SingleAsync();
        var input = new
        {
            name = "Chung cư B6", address = "12 Kiểm thử, TP. Hồ Chí Minh", numberOfFloors = 100,
            timeZoneId = "Asia/Ho_Chi_Minh", id = Guid.NewGuid(), updatedBy = fixture.Actor("STAFF").Id
        };
        const string route = "buildings/current";
        await Denied(HttpMethod.Put, route, input, AssetSnapshot);
        await Denied(HttpMethod.Put, route, new { name = "Sai", address = "Thử", numberOfFloors = 0 }, AssetSnapshot,
            [(Manager.Client, HttpStatusCode.BadRequest)]);
        await Check(Manager.Client, HttpMethod.Put, route, input, HttpStatusCode.OK);
        var after = await assets.Buildings.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(input.name, after.Name);
        Assert.Equal(input.address, after.Address);
        Assert.Equal(Manager.Id, after.UpdatedBy);
    }

    [Fact]
    [Trait("Feature", "FE-04")]
    public async Task Manager_asset_mutations_preserve_relationships_and_cannot_override_emergency_maintenance()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceDbContext>();
        await maintenance.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        var staffId = fixture.Actor("STAFF").Id;
        var facilityTypeInput = new { name = "Phòng kỹ thuật B6", createdBy = staffId, isActive = false };
        var equipmentTypeInput = new { name = "Máy bơm B6", createdBy = staffId, isActive = false };
        await Denied(HttpMethod.Post, "facility-types", facilityTypeInput, AssetSnapshot);
        await Denied(HttpMethod.Post, "equipment-types", equipmentTypeInput, AssetSnapshot);
        await OneTransition(HttpMethod.Post, "facility-types", facilityTypeInput, "facility-types", facilityTypeInput, HttpStatusCode.Created);
        await OneTransition(HttpMethod.Post, "equipment-types", equipmentTypeInput, "equipment-types", equipmentTypeInput, HttpStatusCode.OK);
        var facilityType = await assets.FacilityTypes.AsNoTracking().SingleAsync(x => x.Name == facilityTypeInput.name);
        var equipmentType = await assets.EquipmentTypes.AsNoTracking().SingleAsync(x => x.Name == equipmentTypeInput.name);
        Assert.Equal(Manager.Id, facilityType.CreatedBy);
        Assert.Equal(Manager.Id, equipmentType.CreatedBy);
        Assert.True(facilityType.IsActive);
        Assert.True(equipmentType.IsActive);

        var createFacility = new { name = "Phòng máy", facilityTypeId = facilityType.Id, locationDescription = "Tầng trệt", createdBy = staffId, status = 1 };
        await Denied(HttpMethod.Post, "Facilities", createFacility, AssetSnapshot);
        using var createdFacility = await Send(Manager.Client, HttpMethod.Post, "Facilities", createFacility);
        await Expect(createdFacility, HttpStatusCode.Created);
        var facility = (await createdFacility.Content.ReadFromJsonAsync<FacilityDto>())!;
        await Denied(HttpMethod.Post, "Facilities", createFacility, AssetSnapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        var createEquipment = new { name = "Bơm nước", facilityId = facility.Id, equipmentTypeId = equipmentType.Id, createdBy = staffId, status = 1 };
        await Denied(HttpMethod.Post, "Equipments", createEquipment, AssetSnapshot);
        using var createdEquipment = await Send(Manager.Client, HttpMethod.Post, "Equipments", createEquipment);
        await Expect(createdEquipment, HttpStatusCode.Created);
        var equipment = (await createdEquipment.Content.ReadFromJsonAsync<EquipmentDto>())!;
        Assert.Equal(MasterDataStatus.ACTIVE, facility.Status);
        Assert.Equal(EquipmentStatus.ACTIVE, equipment.Status);
        Assert.Equal(Manager.Id, facility.CreatedBy);
        Assert.Equal(Manager.Id, equipment.CreatedBy);

        var facilityRoute = $"Facilities/{facility.Id}";
        var equipmentRoute = $"Equipments/{equipment.Id}";
        var updateFacility = new { name = "Phòng máy chính", facilityTypeId = facilityType.Id, updatedBy = staffId, status = 1 };
        var updateEquipment = new { name = "Bơm nước chính", facilityId = facility.Id, equipmentTypeId = equipmentType.Id, updatedBy = staffId, status = 1 };
        await Denied(HttpMethod.Put, facilityRoute, updateFacility, AssetSnapshot);
        await Denied(HttpMethod.Put, equipmentRoute, updateEquipment, AssetSnapshot);
        await Denied(HttpMethod.Put, equipmentRoute, new { name = "Sai loại", equipmentTypeId = facilityType.Id, facilityId = facility.Id }, AssetSnapshot,
            [(Manager.Client, HttpStatusCode.BadRequest)]);
        await Check(Manager.Client, HttpMethod.Put, facilityRoute, updateFacility, HttpStatusCode.OK);
        await Check(Manager.Client, HttpMethod.Put, equipmentRoute, updateEquipment, HttpStatusCode.OK);
        var updatedFacility = await assets.Facilities.AsNoTracking().SingleAsync(x => x.Id == facility.Id);
        var updatedEquipment = await assets.Equipment.AsNoTracking().SingleAsync(x => x.Id == equipment.Id);
        Assert.Equal(updateFacility.name, updatedFacility.Name);
        Assert.Equal(updateEquipment.name, updatedEquipment.Name);
        Assert.Equal(facility.Id, updatedEquipment.FacilityId);
        Assert.Equal(equipmentType.Id, updatedEquipment.EquipmentTypeId);
        Assert.Equal(facilityType.Id, updatedFacility.FacilityTypeId);
        Assert.Equal(Manager.Id, updatedFacility.UpdatedBy);
        Assert.Equal(Manager.Id, updatedEquipment.UpdatedBy);
        Assert.Equal(MasterDataStatus.ACTIVE, updatedFacility.Status);
        Assert.Equal(EquipmentStatus.ACTIVE, updatedEquipment.Status);

        var facilityStatus = facilityRoute + "/status";
        var equipmentStatus = equipmentRoute + "/status";
        var inactive = new { status = 1, updatedBy = staffId };
        var active = new { status = 0, updatedBy = staffId };
        await Denied(HttpMethod.Patch, facilityStatus, inactive, AssetSnapshot);
        await Denied(HttpMethod.Patch, equipmentStatus, inactive, AssetSnapshot);
        await Denied(HttpMethod.Patch, facilityStatus, new { status = (int)MasterDataStatus.UNDER_MAINTENANCE }, AssetSnapshot,
            [(Manager.Client, HttpStatusCode.Conflict)]);
        await Denied(HttpMethod.Patch, equipmentStatus, new { status = (int)EquipmentStatus.UNDER_MAINTENANCE }, AssetSnapshot,
            [(Manager.Client, HttpStatusCode.Conflict)]);
        await Check(Manager.Client, HttpMethod.Patch, facilityStatus, inactive, HttpStatusCode.OK);
        await Check(Manager.Client, HttpMethod.Patch, equipmentStatus, inactive, HttpStatusCode.OK);
        Assert.Equal(MasterDataStatus.INACTIVE, await assets.Facilities.Where(x => x.Id == facility.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(EquipmentStatus.INACTIVE, await assets.Equipment.Where(x => x.Id == equipment.Id).Select(x => x.Status).SingleAsync());
        var statusEvents = await assets.ResidentEvents.Where(x => x.SourceId == facility.Id || x.SourceId == equipment.Id)
            .Select(x => x.Payload).ToArrayAsync();
        Assert.Equal(2, statusEvents.Count(x => x.Contains("INACTIVE", StringComparison.Ordinal)));
        await Check(Manager.Client, HttpMethod.Patch, facilityStatus, active, HttpStatusCode.OK);
        await Check(Manager.Client, HttpMethod.Patch, equipmentStatus, active, HttpStatusCode.OK);

        var task = new CreateMaintenanceTaskCommand(null, "Bảo trì khẩn cấp B6", Description: "Kiểm tra phòng máy",
            PriorityCode: "HIGH", PlannedStartAt: Now.AddMinutes(1), DueAt: Now.AddHours(1), FacilityId: facility.Id, IsEmergency: true);
        await Check(Manager.Client, HttpMethod.Post, "maintenance/tasks", task, HttpStatusCode.Created);
        Assert.Equal(MasterDataStatus.UNDER_MAINTENANCE, await assets.Facilities.Where(x => x.Id == facility.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(EquipmentStatus.UNDER_MAINTENANCE, await assets.Equipment.Where(x => x.Id == equipment.Id).Select(x => x.Status).SingleAsync());
        await Denied(HttpMethod.Patch, facilityStatus, active, AssetSnapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        await Denied(HttpMethod.Patch, equipmentStatus, active, AssetSnapshot, [(Manager.Client, HttpStatusCode.Conflict)]);
        Assert.Single(await maintenance.MaintenanceTasks.AsNoTracking().Where(x => x.FacilityId == facility.Id && x.IsEmergency).ToArrayAsync());
    }

    [Fact]
    [Trait("Feature", "FE-04")]
    public async Task Resident_weather_uses_current_building_and_never_calls_provider_for_denied_requests()
    {
        var residence = await Database.SeedResidentAsync(ContactEmail());
        var resident = await fixture.Login("RESIDENT", residentId: residence.ResidentId);
        var revoked = await fixture.Login("RESIDENT", revoked: true, residentId: (await Database.SeedResidentAsync(ContactEmail())).ResidentId);
        var actors = new List<(HttpClient, HttpStatusCode)>
        {
            (fixture.Client(), HttpStatusCode.Unauthorized), (revoked.Client, HttpStatusCode.Unauthorized)
        };
        foreach (var role in new[] { "MANAGER", "STAFF", "ACCOUNTANT", "ADMIN" })
            actors.Add((fixture.Actor(role).Client, HttpStatusCode.Forbidden));
        const string path = "resident/weather?city=Da-Nang&residentId=outside";
        await SetWeatherBuilding("TP. Hồ Chí Minh");
        var start = fixture.WeatherCalls.Count;
        await Denied(HttpMethod.Get, path, null, AssetSnapshot, actors);
        Assert.Equal(start, fixture.WeatherCalls.Count);
        var response = (await resident.Client.GetFromJsonAsync<CurrentBuildingWeatherResponse>("/api/v1/" + path))!;
        Assert.Equal("Hồ Chí Minh", response.City);
        Assert.Equal(29, response.TemperatureC);
        Assert.Equal(response, await resident.Client.GetFromJsonAsync<CurrentBuildingWeatherResponse>("/api/v1/" + path));
        var calls = fixture.WeatherCalls.Skip(start).ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Contains("Hồ Chí Minh", Uri.UnescapeDataString(calls[0]));
        Assert.DoesNotContain("outside", string.Join(' ', calls));
        await Denied(HttpMethod.Get, path, null, AssetSnapshot, actors);
        Assert.Equal(start + 2, fixture.WeatherCalls.Count);
        await SetWeatherBuilding("TP. Hà Nội");
        var unavailable = (await resident.Client.GetFromJsonAsync<CurrentBuildingWeatherResponse>("/api/v1/" + path))!;
        Assert.Equal("Hà Nội", unavailable.City);
        Assert.Null(unavailable.TemperatureC);
        Assert.Equal(start + 3, fixture.WeatherCalls.Count);
    }

    private Task SetWeatherBuilding(string city) => Check(Manager.Client, HttpMethod.Put, "buildings/current",
        new { name = "Chung cư thử", address = $"12 Kiểm thử, {city}", numberOfFloors = 100 }, HttpStatusCode.OK);

    private async Task<string> AssetSnapshot()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var assets = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        return JsonSerializer.Serialize(new
        {
            Building = await assets.Buildings.AsNoTracking().SingleAsync(),
            Facilities = await assets.Facilities.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Equipment = await assets.Equipment.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            FacilityTypes = await assets.FacilityTypes.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            EquipmentTypes = await assets.EquipmentTypes.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Outbox = await assets.ResidentEvents.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }
}

public sealed partial class PropertyOperationsAuthorizationFixture
{
    internal List<string> WeatherCalls { get; } = [];

    partial void ConfigureProviders(IServiceCollection services)
    {
        services.RemoveAll<IMaintenanceEvidenceStorage>();
        services.AddSingleton<IMaintenanceEvidenceStorage, UnexpectedEvidenceStorage>();
        services.AddHttpClient("BuildingWeatherGeocoding").ConfigurePrimaryHttpMessageHandler(() => new WeatherHandler(WeatherCalls));
        services.AddHttpClient("BuildingWeatherForecast").ConfigurePrimaryHttpMessageHandler(() => new WeatherHandler(WeatherCalls));
    }

    private sealed class UnexpectedEvidenceStorage : IMaintenanceEvidenceStorage
    {
        public Task<StoredMaintenanceEvidence> StoreAsync(Guid resultId, MaintenanceEvidenceUpload upload, CancellationToken ct) =>
            throw new InvalidOperationException("Không được gọi kho ảnh khi tạo nhiệm vụ hoặc đổi trạng thái tài sản.");
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) =>
            throw new InvalidOperationException("Không được đọc kho ảnh trong ca B6.");
        public Task DeleteAsync(string storageKey, CancellationToken ct) =>
            throw new InvalidOperationException("Không được xóa ảnh trong ca B6.");
    }

    private sealed class WeatherHandler(List<string> calls) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!;
            calls.Add(url.PathAndQuery);
            if (Uri.UnescapeDataString(url.Query).Contains("Hà Nội", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var payload = url.AbsolutePath == "/v1/search"
                ? """{"results":[{"name":"Hồ Chí Minh","country_code":"VN","latitude":10.82,"longitude":106.63}]}"""
                : """{"current":{"temperature_2m":29,"weather_code":2,"is_day":1}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
                });
        }
    }
}
