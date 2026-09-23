using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Features.Authentication.State;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Features.Administration.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var configuredUrl = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(configuredUrl)) throw new InvalidOperationException("Thiếu cấu hình Api:BaseUrl.");
var apiBase = new Uri(new Uri(builder.HostEnvironment.BaseAddress), configuredUrl);
if (apiBase.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("API authentication yêu cầu HTTPS.");
builder.Services.AddScoped(sp => new ApiClient(new HttpClient { BaseAddress = apiBase }, sp.GetRequiredService<ILogger<ApiClient>>()));
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthSession>());
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped(sp =>
{
    var handler = new AuthHttpHandler(sp.GetRequiredService<AuthSession>(), sp.GetRequiredService<NavigationManager>()) { InnerHandler = new HttpClientHandler() };
    return new AuthenticatedApiClient(new HttpClient(handler) { BaseAddress = apiBase }, sp.GetRequiredService<ILogger<ApiClient>>());
});
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<OnboardingState>();

builder.Services.AddScoped<IBuildingApiClient, BuildingApiClient>();
builder.Services.AddScoped<IFacilityApiClient, FacilityApiClient>();
builder.Services.AddScoped<IEquipmentApiClient, EquipmentApiClient>();
builder.Services.AddScoped<IAdministrationApiClient, AdministrationApiClient>();
await builder.Build().RunAsync();
