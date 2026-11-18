using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.UtilityConsumption;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Domain.ElectricityUsages;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "UtilityConsumption-Task1")]
public sealed class UtilityConsumptionHttpTests : IAsyncLifetime
{
    private readonly Guid apartment = Guid.NewGuid(), resident = Guid.NewGuid(), manager = Guid.NewGuid();
    private static readonly DateOnly Month = new(2026, 10, 1);
    private string databaseName = "", adminConnection = "";
    private Factory factory = null!;
    public async Task InitializeAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString()) { Pooling = false };
        Assert.Equal("propflow_test", connection.Database);
        connection.Database = "postgres"; adminConnection = connection.ConnectionString;
        databaseName = "propflow_utility_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnection))
        { await admin.OpenAsync(); await TestDatabaseSafety.CreateAsync(admin, databaseName); }
        connection.Database = databaseName;
        await TestDatabaseSafety.VerifyFixtureAsync(connection.ConnectionString, databaseName, TestDatabaseSafety.ProcessOwner,
            TestDatabaseConfiguration.GetConnectionString());
        factory = new(connection.ConnectionString, new Directory(apartment));
        using var scope = factory.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var insights = scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>();
        Assert.Equal(databaseName, billing.Database.GetDbConnection().Database);
        Assert.Equal(databaseName, insights.Database.GetDbConnection().Database);
        await billing.Database.ExecuteSqlRawAsync(billing.Database.GenerateCreateScript());
        var audit = scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Administration.Infrastructure.Persistence.AdministrationDbContext>();
        await audit.Database.ExecuteSqlRawAsync(audit.Database.GenerateCreateScript());
        await insights.Database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        for (var i = -8; i <= 0; i++)
        {
            var month = Month.AddMonths(i); var days = DateTime.DaysInMonth(month.Year, month.Month);
            var electricity = (i == 0 ? 30m : 10m) * days;
            billing.ElectricityUsages.Add(new ElectricityUsage(apartment, month, electricity, .4m * days, manager, now));
            var period = month.AddMonths(1);
            var invoice = new Invoice($"UTILITY-{month:yyyyMM}", apartment, period, period.AddMonths(1).AddDays(-1), manager, now);
            invoice.AddItem("ELECTRICITY_KWH_MONTHLY", "Điện", "PER_KWH", electricity, 1000, manager, now);
            invoice.Issue(manager, period, now);
            billing.Invoices.Add(invoice); billing.InvoiceRecipients.Add(new(invoice.Id, resident, "Cư dân kiểm thử", null, "A-101", 80));
        }
        await billing.SaveChangesAsync();
    }
    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        if (string.IsNullOrEmpty(databaseName)) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await TestDatabaseSafety.DropAsync(admin, databaseName);
    }
    [Fact]
    public async Task Http_scope_concurrent_review_and_notification_outbox_work_on_postgres()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/utility-insights?month=2026-10-01")).StatusCode);
        using var staff = Client("STAFF", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/utility-insights?month=2026-10-01")).StatusCode);
        using var owner = Client("RESIDENT", resident);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/v1/utility-insights/apartments/{apartment}?month=2026-10-01")).StatusCode);
        var own = await owner.GetFromJsonAsync<UtilityInsightView[]>("/api/v1/resident/utility-insights");
        Assert.NotEmpty(own!);
        Assert.All(own!, x => Assert.Equal(apartment, x.ApartmentId));
        using var stranger = Client("RESIDENT", Guid.NewGuid());
        Assert.Empty((await stranger.GetFromJsonAsync<UtilityInsightView[]>("/api/v1/resident/utility-insights"))!);
        using var managerClient = Client("MANAGER", manager);
        var path = $"/api/v1/utility-insights/apartments/{apartment}?month=2026-10-01";
        var view = await managerClient.GetFromJsonAsync<UtilityInsightView>(path);
        Assert.Equal("HIGH", view!.Analysis.Electricity.Severity);
        using var accountant = Client("ACCOUNTANT", Guid.NewGuid());
        var reviewPath = $"/api/v1/utility-insights/apartments/{apartment}/review?month=2026-10-01";
        var request = new UtilityReviewInput(view.Fingerprint, "CONFIRMED", "Đã đối chiếu công tơ.");
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PostAsJsonAsync(reviewPath, request)).StatusCode);
        var duplicate = await Task.WhenAll(managerClient.PostAsJsonAsync(reviewPath, request), managerClient.PostAsJsonAsync(reviewPath, request));
        Assert.All(duplicate, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>();
            Assert.Equal(1, await db.UtilityInsightReviews.CountAsync()); Assert.Equal(1, await db.UtilityNotices.CountAsync());
            var service = scope.ServiceProvider.GetRequiredService<UtilityInsights>();
            await service.DispatchAsync(factory.Publisher, default); await service.DispatchAsync(factory.Publisher, default);
            Assert.Single(factory.Publisher.Notices); Assert.Equal(resident, factory.Publisher.Notices.Single().UserId);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await managerClient.GetAsync($"/api/v1/utility-insights/apartments/{apartment}?month=2099-01-01")).StatusCode);
        var body = await (await owner.GetAsync("/api/v1/resident/utility-insights")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("recipients", body, StringComparison.OrdinalIgnoreCase);
    }
    private HttpClient Client(string role, Guid user)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Role", role); client.DefaultRequestHeaders.Add("X-Test-User", user.ToString()); return client;
    }
    private sealed class Factory(string connection, Directory directory) : PropFlowApiFactory(connection)
    {
        public Publisher Publisher { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = o.DefaultForbidScheme = "UtilityTest"; })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("UtilityTest", _ => { });
                services.RemoveAll<IBillingHouseholdDirectory>(); services.AddSingleton<IBillingHouseholdDirectory>(directory);
                services.RemoveAll<ICurrentBuildingTimeZone>(); services.AddSingleton<ICurrentBuildingTimeZone, Zone>();
                services.RemoveAll<IFinanceNoticePublisher>(); services.AddSingleton<IFinanceNoticePublisher>(Publisher);
            });
        }
    }
    private sealed class Directory(Guid apartment) : IBillingHouseholdDirectory
    {
        public Task<IReadOnlyList<BillingApartment>> ManagedApartmentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingApartment>>([new(apartment, "A-101")]);
        public Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingHousehold>>([]);
    }
    private sealed class Zone : ICurrentBuildingTimeZone { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh"); }
    private sealed class Publisher : IFinanceNoticePublisher
    {
        public List<FinanceNotice> Notices = [];
        public Task PublishAsync(FinanceNotice notice, CancellationToken ct) { Notices.Add(notice); return Task.CompletedTask; }
    }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.Role, role.ToString()), new("sub", Request.Headers["X-Test-User"].ToString()) };
            if (role == "MANAGER") claims.Add(new("permission", "MANAGE_OPERATIONS"));
            if (role == "ACCOUNTANT") claims.Add(new("permission", "MANAGE_FINANCE"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
