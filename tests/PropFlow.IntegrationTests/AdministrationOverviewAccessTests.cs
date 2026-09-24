using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Permissions;
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
        private readonly AccountResponse _account;

        public OverviewFactory(AccountResponse account) => _account = account;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApartmentOverviewSource>();
                services.RemoveAll<ICurrentBuildingTimeZone>();
                services.RemoveAll<IResidentOverviewSource>();
                services.RemoveAll<IServiceRequestOverviewSource>();
                services.RemoveAll<IInternalAccountDirectory>();
                services.RemoveAll<IAccountAccess>();
                services.AddScoped<IApartmentOverviewSource, Apartments>();
                services.AddScoped<ICurrentBuildingTimeZone, Buildings>();
                services.AddScoped<IResidentOverviewSource, Residents>();
                services.AddScoped<IServiceRequestOverviewSource, Requests>();
                services.AddSingleton<IInternalAccountDirectory>(new Accounts(_account));
                services.AddSingleton<IAccountAccess>(new Access(_account));
            });
        }
    }

    private sealed class Accounts(AccountResponse account) : IInternalAccountDirectory
    {
        private readonly InternalAccountRecord _account = new(account.Id, account.Username, account.DisplayName, account.Email, account.Status);
        public Task<IReadOnlyList<InternalAccountRecord>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<InternalAccountRecord>>(userIds.Contains(_account.Id) ? [_account] : []);
        public Task<PagedInternalAccountRecords> SearchAsync(IReadOnlyCollection<Guid> userIds, string? search, string? status, int page, int pageSize, CancellationToken ct) => throw new NotSupportedException();
        public Task<InternalAccountRecord> CreateAsync(CreateInternalAccount request, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<InternalAccountRecord?> SetStatusAsync(Guid userId, string status, Guid actorUserId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Access(AccountResponse account) : IAccountAccess
    {
        public Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult<AccountAccess?>(new AccountAccess(account.Role, account.Permissions));
        public Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Apartments : IApartmentOverviewSource
    {
        public Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
    private sealed class Buildings : ICurrentBuildingTimeZone
    {
        public Task<string> GetAsync(CancellationToken ct) =>
            Task.FromResult("UTC");
    }
    private sealed class Residents : IResidentOverviewSource
    {
        public Task<ResidentOverviewCounts> GetCountsAsync(DateOnly date, IReadOnlyCollection<Guid> activeApartmentIds, CancellationToken ct) =>
            Task.FromResult(new ResidentOverviewCounts(0, 0));
    }
    private sealed class Requests : IServiceRequestOverviewSource
    {
        public Task<ServiceRequestOverviewData> GetOverviewAsync(DateTimeOffset instant, int trendDays,
            string timeZoneId, CancellationToken ct) =>
            Task.FromResult(new ServiceRequestOverviewData(0, [], []));
    }

    [Theory]
    [InlineData("ADMIN", HttpStatusCode.OK)]
    [InlineData("MANAGER", HttpStatusCode.Forbidden)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)]
    public async Task Overview_is_readable_only_by_admin(string role, HttpStatusCode expected)
    {
        var permissions = role == "ADMIN" ? new[] { SystemPermissionCodes.ViewSystemOverview } : [];
        var account = new AccountResponse(Guid.NewGuid(), "test", "Test", null, null, "ACTIVE", true, role, permissions);
        await using var factory = new OverviewFactory(account);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
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

    [Fact]
    public async Task Overview_rejects_admin_without_reporting_permission()
    {
        var account = new AccountResponse(Guid.NewGuid(), "admin", "Admin", null, null, "ACTIVE", true, "ADMIN", []);
        await using var factory = new OverviewFactory(account);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var token = factory.Services.GetRequiredService<IAuthSecrets>().Issue(account, DateTimeOffset.UtcNow).AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.GetAsync("/api/v1/reporting/administration-overview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
