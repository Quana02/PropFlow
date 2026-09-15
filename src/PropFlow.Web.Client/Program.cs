using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var baseUrl = config["Api:BaseUrl"] ?? "https://localhost:7101";
    return new HttpClient { BaseAddress = new Uri(baseUrl) };
});

builder.Services.AddScoped<PropFlow.Web.Client.Features.Facility.Services.IBuildingApiClient, PropFlow.Web.Client.Features.Facility.Services.BuildingApiClient>();

await builder.Build().RunAsync();
