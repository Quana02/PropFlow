using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Api.Controllers;
using PropFlow.Modules.PropertyAssets.Presentation.Controllers;
using PropFlow.Web.Client.Layout;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.IntegrationTests;

public sealed class Fe04AccessTests
{
    [Fact]
    public void Get_current_building_allows_only_manager_and_staff()
    {
        var authorization = typeof(CurrentBuildingController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER", "STAFF" }, SplitRoles(authorization.Roles));
        Assert.Equal("api/v1/buildings/current", typeof(CurrentBuildingController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), true).Cast<Microsoft.AspNetCore.Mvc.RouteAttribute>().Single().Template);
    }

    [Fact]
    public void Put_current_building_allows_only_manager()
    {
        var update = typeof(CurrentBuildingController).GetMethod(nameof(CurrentBuildingController.UpdateCurrent));
        var authorization = update!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER" }, SplitRoles(authorization.Roles));
        Assert.NotNull(update);
        // UpdateCurrent is at class route level "api/v1/buildings/current", so method-level HttpPut has no template
        var httpPut = update.GetCustomAttributes(typeof(HttpPutAttribute), true).Cast<HttpPutAttribute>().SingleOrDefault();
        Assert.NotNull(httpPut);
    }

    [Fact]
    public void Building_details_page_allows_manager_and_staff_and_uses_main_layout()
    {
        var page = typeof(AuthSession).Assembly.GetTypes()
            .Single(type => type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.RouteAttribute), false)
                .Cast<Microsoft.AspNetCore.Components.RouteAttribute>().Any(route => route.Template == "/buildings"));
        var authorization = page.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single();
        var layout = page.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.LayoutAttribute), false).Cast<Microsoft.AspNetCore.Components.LayoutAttribute>().Single();

        Assert.Equal(new[] { "MANAGER", "STAFF" }, SplitRoles(authorization.Roles));
        Assert.Equal(typeof(MainLayout), layout.LayoutType);
    }

    // FE-04.3: Facility Management Tests
    [Fact]
    public void Get_facilities_allows_manager_and_staff()
    {
        var getMethod = typeof(FacilitiesController).GetMethod(nameof(FacilitiesController.GetFacilities));
        var authorization = getMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER", "STAFF" }, SplitRoles(authorization.Roles));
    }

    [Fact]
    public void Get_facility_by_id_allows_manager_and_staff()
    {
        var getMethod = typeof(FacilitiesController).GetMethod(nameof(FacilitiesController.GetFacilityById));
        var authorization = getMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER", "STAFF" }, SplitRoles(authorization.Roles));
    }

    [Fact]
    public void Post_facility_allows_only_manager()
    {
        var postMethod = typeof(FacilitiesController).GetMethod(nameof(FacilitiesController.CreateFacility));
        var authorization = postMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER" }, SplitRoles(authorization.Roles));
    }

    [Fact]
    public void Put_facility_allows_only_manager()
    {
        var putMethod = typeof(FacilitiesController).GetMethod(nameof(FacilitiesController.UpdateFacility));
        var authorization = putMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER" }, SplitRoles(authorization.Roles));
    }

    // FE-04.4: Facility Status Management Test
    [Fact]
    public void Patch_facility_status_allows_only_manager()
    {
        var patchMethod = typeof(FacilitiesController).GetMethod(nameof(FacilitiesController.SetFacilityStatus));
        var authorization = patchMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER" }, SplitRoles(authorization.Roles));
    }

    // FE-04.5: Equipment Management Tests
    [Fact]
    public void Get_equipments_allows_manager_and_staff()
    {
        var getMethod = typeof(EquipmentsController).GetMethod(nameof(EquipmentsController.GetEquipments));
        var authorization = getMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER", "STAFF" }, SplitRoles(authorization.Roles));
    }

    [Fact]
    public void Get_equipment_by_id_allows_manager_and_staff()
    {
        var getMethod = typeof(EquipmentsController).GetMethod(nameof(EquipmentsController.GetEquipmentById));
        var authorization = getMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER", "STAFF" }, SplitRoles(authorization.Roles));
    }

    [Fact]
    public void Post_equipment_allows_only_manager()
    {
        var postMethod = typeof(EquipmentsController).GetMethod(nameof(EquipmentsController.CreateEquipment));
        var authorization = postMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER" }, SplitRoles(authorization.Roles));
    }

    [Fact]
    public void Put_equipment_allows_only_manager()
    {
        var putMethod = typeof(EquipmentsController).GetMethod(nameof(EquipmentsController.UpdateEquipment));
        var authorization = putMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER" }, SplitRoles(authorization.Roles));
    }

    // FE-04.6: Equipment Status Management Test
    [Fact]
    public void Patch_equipment_status_allows_only_manager()
    {
        var patchMethod = typeof(EquipmentsController).GetMethod(nameof(EquipmentsController.SetEquipmentStatus));
        var authorization = patchMethod!.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(new[] { "MANAGER" }, SplitRoles(authorization.Roles));
    }

    private static string[] SplitRoles(string? roles) => roles!
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .OrderBy(role => role, StringComparer.Ordinal)
        .ToArray();
}
