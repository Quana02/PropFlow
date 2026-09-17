using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PropFlow.Web.Client.Features.Facility.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Api:BaseUrl"] ?? "https://localhost:7101";
    return new HttpClient { BaseAddress = new Uri(baseUrl) };
});

builder.Services.AddScoped<IBuildingApiClient, BuildingApiClient>();
builder.Services.AddScoped<IFacilityApiClient, FacilityApiClient>();
builder.Services.AddScoped<IEquipmentApiClient, EquipmentApiClient>();
await builder.Build().RunAsync();
