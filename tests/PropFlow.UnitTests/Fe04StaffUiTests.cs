using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Features.Facility.Pages;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Features.Maintenance.Models;
using PropFlow.Web.Client.Features.Maintenance.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

public sealed class Fe04StaffUiTests
{
    [Fact]
    public async Task Building_background_response_does_not_replace_profile_when_manager_opens_editor()
    {
        await using var services = Services("MANAGER", "MANAGE_OPERATIONS");
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<CurrentBuildingDetails>());
        var page = services.GetRequiredService<TrackingActivator>().Instances.OfType<CurrentBuildingDetails>().Single();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var transport = services.GetRequiredService<Transport>();
        transport.Updated = true;
        transport.BeforeBuildingResponse = () => typeof(CurrentBuildingDetails).GetField("IsEditDialogOpen", flags)!.SetValue(page, true);
        await renderer.Dispatcher.InvokeAsync(() => (Task)typeof(CurrentBuildingDetails).GetMethod("ReloadAsync", flags)!
            .Invoke(page, [false, CancellationToken.None])!);
        var profile = (CurrentBuildingOverviewResponse)typeof(CurrentBuildingDetails).GetField("Building", flags)!.GetValue(page)!;
        Assert.Equal("Current property", profile.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_removes_old_data_when_assignment_is_revoked(bool building)
    {
        await using var services = Services("STAFF", "PERFORM_ASSIGNED_OPERATIONS");
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(async () => building
            ? await renderer.RenderComponentAsync<CurrentBuildingDetails>()
            : await renderer.RenderComponentAsync<EquipmentDeepLink>());
        services.GetRequiredService<Transport>().Revoked = true;
        var type = building ? typeof(CurrentBuildingDetails) : typeof(EquipmentList);
        var component = services.GetRequiredService<TrackingActivator>().Instances.Single(type.IsInstanceOfType);
        var method = type.GetMethod(building ? "ReloadAsync" : "RefreshDetailAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await renderer.Dispatcher.InvokeAsync(() => (Task)method.Invoke(component,
            building ? [false, CancellationToken.None] : [CancellationToken.None])!);
        await renderer.Dispatcher.InvokeAsync(() => typeof(ComponentBase).GetMethod("StateHasChanged",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(component, null));
        var html = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Assert.DoesNotContain(building ? "Current property" : "Serial-from-detail", html);
    }

    [Theory]
    [InlineData("equipment")]
    [InlineData("facility")]
    [InlineData("building")]
    public async Task Staff_open_view_refreshes_when_manager_updates_shared_asset(string page)
    {
        await using var services = Services("STAFF", "PERFORM_ASSIGNED_OPERATIONS");
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(async () => page switch
        {
            "equipment" => await renderer.RenderComponentAsync<EquipmentDeepLink>(),
            "facility" => await renderer.RenderComponentAsync<FacilityList>(),
            _ => await renderer.RenderComponentAsync<CurrentBuildingDetails>()
        });
        if (page == "facility")
        {
            var component = services.GetRequiredService<TrackingActivator>().Instances.OfType<FacilityList>().Single();
            await renderer.Dispatcher.InvokeAsync(() => (Task)typeof(FacilityList).GetMethod("OpenDetailDialog",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(component, [new FacilityModel { Id = FacilityId }])!);
            await renderer.Dispatcher.InvokeAsync(() => typeof(ComponentBase).GetMethod("StateHasChanged",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(component, null));
            var initial = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
            Assert.Contains("Chi Tiết Cơ Sở Vật Chất", initial);
        }
        services.GetRequiredService<Transport>().Updated = true;
        if (page == "building")
        {
            await Task.Delay(TimeSpan.FromSeconds(11));
        }
        else
        {
            var type = page == "facility" ? typeof(FacilityList) : typeof(EquipmentList);
            var component = services.GetRequiredService<TrackingActivator>().Instances.Single(type.IsInstanceOfType);
            var refresh = type.GetMethod("RefreshAssetStatusForRealtimeAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await renderer.Dispatcher.InvokeAsync(() => (Task)refresh.Invoke(component, null)!);
        }
        var html = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Assert.Contains("Manager-updated", html);
        if (page == "equipment") Assert.Contains("Updated bay", html);
    }

    [Theory]
    [InlineData("STAFF", "PERFORM_ASSIGNED_OPERATIONS", false)]
    [InlineData("MANAGER", "MANAGE_OPERATIONS", true)]
    public async Task Asset_pages_render_details_for_staff_and_only_manager_write_controls(string role, string permission, bool canManage)
    {
        await using var services = Services(role, permission);
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var equipment = await Render<EquipmentList>(renderer);
        Assert.Contains("Xem chi tiết thiết bị", equipment);
        Assert.Equal(canManage, equipment.Contains("Chỉnh sửa hồ sơ"));
        Assert.Equal(canManage, equipment.Contains("Đổi trạng thái vận hành"));
        Assert.Contains("GEN-01", equipment);
        var facility = await Render<FacilityList>(renderer);
        Assert.Contains("Xem chi tiết", facility);
        Assert.Contains("FAC-01", facility);
        var building = await Render<CurrentBuildingDetails>(renderer);
        Assert.Contains("Current property", building);
        Assert.Equal(canManage, building.Contains("Chỉnh sửa"));
        Assert.Equal(canManage, building.Contains("Tổng số căn hộ"));
        Assert.Equal(canManage, building.Contains("Thông tin cập nhật"));
    }

    [Fact]
    public async Task Equipment_deep_link_loads_read_api_detail_and_operational_location()
    {
        await using var services = Services("STAFF", "PERFORM_ASSIGNED_OPERATIONS");
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await Render<EquipmentDeepLink>(renderer);
        Assert.Contains("Serial-from-detail", html);
        Assert.Contains("Roof bay 1", html);
        Assert.DoesNotContain("Chỉnh sửa hồ sơ", html);
    }

    [Theory]
    [InlineData("STAFF", "PERFORM_ASSIGNED_OPERATIONS", true, false)]
    [InlineData("STAFF", "MANAGE_OPERATIONS", false, false)]
    [InlineData("STAFF", "", false, false)]
    [InlineData("MANAGER", "MANAGE_OPERATIONS", true, true)]
    [InlineData("MANAGER", "", false, false)]
    [InlineData("ADMIN", "MANAGE_OPERATIONS", false, false)]
    public async Task Client_permissions_match_server_role_and_permission_requirements(string role, string permission, bool view, bool manage)
    {
        await using var services = Services(role, permission);
        var user = (await services.GetRequiredService<AuthSession>().GetAuthenticationStateAsync()).User;
        Assert.Equal(view, AssetPermissions.CanView(user));
        Assert.Equal(manage, AssetPermissions.CanManage(user));
    }

    private sealed class EquipmentDeepLink : EquipmentList
    {
        public EquipmentDeepLink() => EquipmentId = AssetId;
    }

    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly Guid FacilityId = Guid.NewGuid();
    private static Task<string> Render<T>(HtmlRenderer renderer, Dictionary<string, object?>? parameters = null) where T : IComponent =>
        renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<T>(
            parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters))).ToHtmlString()));

    private static ServiceProvider Services(string role, string permission)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPolicy(AssetPermissions.ViewPolicy, policy => policy.RequireAssertion(c => AssetPermissions.CanView(c.User))));
        services.AddSingleton<NavigationManager, Navigation>();
        services.AddSingleton<IJSRuntime, NoJs>();
        services.AddSingleton<TrackingActivator>();
        services.AddSingleton<IComponentActivator>(sp => sp.GetRequiredService<TrackingActivator>());
        var transport = new Transport();
        services.AddSingleton(transport);
        var api = new AuthenticatedApiClient(new HttpClient(transport) { BaseAddress = new Uri("https://test.invalid") }, NullLogger<ApiClient>.Instance);
        services.AddSingleton<ApiClient>(api);
        services.AddSingleton(api);
        var session = new AuthSession(api);
        session.Accept(new SessionResponse("test", DateTimeOffset.UtcNow.AddHours(1), new AccountResponse(Guid.NewGuid(), "user", "User", null, null, "ACTIVE", true, role, [permission])));
        services.AddSingleton(session);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationManager { ["Api:BaseUrl"] = "https://test.invalid" });
        services.AddSingleton<MaintenanceHubConnectionFactory>();
        services.AddSingleton<IMaintenanceApiClient, MaintenanceApiClient>();
        services.AddSingleton<AuthenticationStateProvider>(session);
        services.AddSingleton<IFacilityApiClient, FacilityApiClient>();
        services.AddSingleton<IEquipmentApiClient, EquipmentApiClient>();
        services.AddSingleton<IBuildingApiClient, BuildingApiClient>();
        return services.BuildServiceProvider();
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://test.invalid/", "https://test.invalid/equipment");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class Transport : HttpMessageHandler
    {
        public bool Updated { get; set; }
        public bool Revoked { get; set; }
        public Action? BeforeBuildingResponse { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/buildings/current") BeforeBuildingResponse?.Invoke();
            if (Revoked)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            object result = path switch
            {
                "/api/v1/buildings/current" => new CurrentBuildingOverviewResponse { Name = Updated ? "Manager-updated" : "Current property", Address = "Main street", TotalApartments = 42 },
                "/api/v1/maintenance/overview" => new MaintenanceOverviewModel(0, 0, null, []),
                "/api/v1/facility-types" or "/api/v1/equipment-types" => Array.Empty<object>(),
                "/api/v1/facilities" => new PagedResult<FacilityModel> { Items = [new() { Id = FacilityId, Code = "FAC-01", Name = "Lobby" }], TotalCount = 1 },
                _ when path == $"/api/v1/facilities/{FacilityId}" => new FacilityDetailModel { Id = FacilityId, Code = "FAC-01", Name = Updated ? "Manager-updated" : "Lobby" },
                "/api/v1/equipments" => new PagedResult<EquipmentModel> { Items = [new() { Id = AssetId, Code = "GEN-01", Name = "Generator" }], TotalCount = 1 },
                _ when path == $"/api/v1/equipments/{AssetId}" => new EquipmentDetailModel { Id = AssetId, Code = "GEN-01", Name = Updated ? "Manager-updated" : "Generator", SerialNumber = "Serial-from-detail", LocationDescription = Updated ? "Updated bay" : "Roof bay 1" },
                _ => throw new InvalidOperationException(path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result, result.GetType()) });
        }
    }

    private sealed class TrackingActivator : IComponentActivator
    {
        public List<IComponent> Instances { get; } = [];
        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            Instances.Add(component);
            return component;
        }
    }
}
