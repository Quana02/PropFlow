using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Features.Authentication.State;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Features.Administration.Services;
using PropFlow.Web.Client.Features.Maintenance.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Features.Resident.Shared.Services;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.ServiceRequests.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

var configuredUrl = builder.Configuration["Api:BaseUrl"];
if (string.IsNullOrWhiteSpace(configuredUrl)) throw new InvalidOperationException("Thiếu cấu hình Api:BaseUrl.");
var apiBase = new Uri(new Uri(builder.HostEnvironment.BaseAddress), configuredUrl);
if (apiBase.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("API authentication yêu cầu HTTPS.");
builder.Services.AddScoped(sp => new ApiClient(new HttpClient { BaseAddress = apiBase }, sp.GetRequiredService<ILogger<ApiClient>>()));
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthSession>());
builder.Services.AddAuthorizationCore(options => options.AddPolicy(AssetPermissions.ViewPolicy,
    policy => policy.RequireAssertion(context => AssetPermissions.CanView(context.User))));
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("finance.manage",
        policy => policy.RequireRole("ACCOUNTANT").RequireClaim("permission", "MANAGE_FINANCE"));
    options.AddPolicy(ResidentAccountPolicies.Read,
        policy => policy.RequireRole("MANAGER").RequireClaim("permission", ResidentAccountPolicies.Read));
    options.AddPolicy(ResidentAccountPolicies.RequestEmailChange,
        policy => policy.RequireRole("MANAGER").RequireClaim("permission", ResidentAccountPolicies.RequestEmailChange));
    options.AddPolicy(PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.Read,
        policy => policy.RequireAssertion(context => PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.CanRead(context.User)));
    options.AddPolicy(PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.Manage,
        policy => policy.RequireAssertion(context => PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.CanManage(context.User)));
    options.AddPolicy(PropFlow.Modules.Communication.Contracts.SupportChatPolicies.Manager,
        policy => policy.RequireAssertion(context => PropFlow.Modules.Communication.Contracts.SupportChatPolicies.IsManager(context.User)));
    options.AddPolicy(PropFlow.Modules.Communication.Contracts.SupportChatPolicies.Resident,
        policy => policy.RequireAssertion(context => PropFlow.Modules.Communication.Contracts.SupportChatPolicies.IsResident(context.User)));
});
builder.Services.AddScoped<PropFlow.Web.Client.Features.AiChatbot.Services.KnowledgeApiClient>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.AiChatbot.Services.ResidentChatApiClient>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Communication.SupportChat.SupportChatApi>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Communication.EmergencyCalls.EmergencyCallApi>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Communication.EmergencyCalls.EmergencyCallClient>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Communication.SupportChat.SupportChatConnection>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Communication.SupportChat.ISupportChatConnection>(sp => sp.GetRequiredService<PropFlow.Web.Client.Features.Communication.SupportChat.SupportChatConnection>());
builder.Services.AddScoped(sp =>
{
    var handler = new AuthHttpHandler(sp.GetRequiredService<AuthSession>(), sp.GetRequiredService<NavigationManager>()) { InnerHandler = new HttpClientHandler() };
    return new AuthenticatedApiClient(new HttpClient(handler) { BaseAddress = apiBase }, sp.GetRequiredService<ILogger<ApiClient>>());
});
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<PropFlow.Web.Client.Shared.Feedback.ToastService>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Complaints.Services.ComplaintApiClient>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Communication.Services.AnnouncementApiClient>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Communication.Services.AnnouncementRealtimeClient>();
builder.Services.AddScoped<OnboardingState>();

builder.Services.AddScoped<IBuildingApiClient, BuildingApiClient>();
builder.Services.AddScoped<IFacilityApiClient, FacilityApiClient>();
builder.Services.AddScoped<IEquipmentApiClient, EquipmentApiClient>();
builder.Services.AddScoped<IAdministrationApiClient, AdministrationApiClient>();
builder.Services.AddScoped<IMaintenanceApiClient, MaintenanceApiClient>();
builder.Services.AddScoped<MaintenanceHubConnectionFactory>();
builder.Services.AddScoped<IResidentServiceRequestApiClient, ResidentServiceRequestApiClient>();
builder.Services.AddScoped<IManagerServiceRequestApiClient, ManagerServiceRequestApiClient>();
builder.Services.AddScoped<IStaffServiceRequestApiClient, StaffServiceRequestApiClient>();
builder.Services.AddScoped<ServiceRequestRealtimeClient>();
builder.Services.AddScoped<IFinanceApiClient, FinanceApiClient>();
builder.Services.AddScoped<FinanceCycleApi>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Resident.Portal.Services.ResidentFinanceNoticeState>();
builder.Services.AddScoped<ResidentNotificationConnectionFactory>();
builder.Services.AddScoped<BillingWorkspaceApi>();
builder.Services.AddScoped<IResidentApiClient, ResidentApiClient>();
builder.Services.AddScoped<PropFlow.Web.Client.Features.Apartment.Services.IApartmentApiClient, PropFlow.Web.Client.Features.Apartment.Services.ApartmentApiClient>();
await builder.Build().RunAsync();
