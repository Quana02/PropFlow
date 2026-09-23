using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Reporting.Application.AdministrationOverview;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.IntegrationTests;

public sealed class AdministrationOverviewAccessTests
{
    private sealed class OverviewFactory : PropFlowApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApartmentOverviewSource>();
                services.RemoveAll<IBuildingTimeZones>();
                services.RemoveAll<IResidentOverviewSource>();
                services.RemoveAll<IServiceRequestOverviewSource>();
                services.AddScoped<IApartmentOverviewSource, Apartments>();
                services.AddScoped<IBuildingTimeZones, Buildings>();
                services.AddScoped<IResidentOverviewSource, Residents>();
                services.AddScoped<IServiceRequestOverviewSource, Requests>();
            });
        }
    }

    private sealed class Apartments : IApartmentOverviewSource
    {
        public Task<IReadOnlyList<ActiveApartmentIds>> GetActiveApartmentsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActiveApartmentIds>>([]);
    }
    private sealed class Buildings : IBuildingTimeZones
    {
        public Task<IReadOnlyList<BuildingTimeZone>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BuildingTimeZone>>([]);
    }
    private sealed class Residents : IResidentOverviewSource
    {
        public Task<ResidentOverviewCounts> GetCountsAsync(IReadOnlyList<ApartmentOccupancyAtDate> apartments, CancellationToken ct) =>
            Task.FromResult(new ResidentOverviewCounts(0, 0));
    }
    private sealed class Requests : IServiceRequestOverviewSource
    {
        public Task<ServiceRequestOverviewData> GetOverviewAsync(DateTimeOffset instant, int trendDays,
            IReadOnlyList<ServiceRequestBuildingTimeZone> buildingTimeZones, CancellationToken ct) =>
            Task.FromResult(new ServiceRequestOverviewData(0, [], []));
    }

    [Theory]
    [InlineData("ADMIN", HttpStatusCode.OK)]
    [InlineData("MANAGER", HttpStatusCode.Forbidden)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    public async Task Overview_is_readable_only_by_admin(string role, HttpStatusCode expected)
    {
        await using var factory = new OverviewFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var account = new AccountResponse(Guid.NewGuid(), "test", "Test", null, null, "ACTIVE", true, role, []);
        var token = factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/v1/reporting/administration-overview");
        Assert.Equal(expected, response.StatusCode);
        if (role == "ADMIN")
        {
            var overview = await response.Content.ReadFromJsonAsync<AdministrationOverviewDto>();
            Assert.NotNull(overview);
            Assert.Equal(0, overview.TotalApartments);
            Assert.Equal(0, overview.VacantApartments);
        }
    }
}
