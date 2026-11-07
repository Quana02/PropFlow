using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.IntegrationTests;
public sealed class FinanceWorkflowHttpTests
{
    internal sealed class Directory : IApartmentOverviewSource, IApartmentResidentRelationshipSource, ICurrentBuildingTimeZone
    {
        public static Guid Apartment = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
        public Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([Apartment]);
        public Task<IReadOnlyList<ActiveApartmentOption>> GetActiveApartmentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ActiveApartmentOption>>([new(Apartment,"A-101",1)]);
        public Task<IReadOnlyList<ApartmentReference>> GetApartmentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApartmentReference>>([new(Apartment,"A-101",1)]);
        public Task<IReadOnlyList<ResidentApartmentOwnership>> GetOwnershipsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ResidentApartmentOwnership>>([]);
        public Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(Guid id, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string database = Guid.NewGuid().ToString();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => {
                using var rsa=RSA.Create(2048);
                config.AddInMemoryCollection(new Dictionary<string,string?> {
                    ["ConnectionStrings:PropFlowDatabase"]="Host=localhost;Database=unused;Username=unused;Password=unused",
                    ["Authentication:Issuer"]="finance-tests",["Authentication:Audience"]="finance-tests",
                    ["Authentication:PrivateKeyPem"]=rsa.ExportRSAPrivateKeyPem(),
                    ["Authentication:OtpHashKey"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Authentication:Mail:Host"]="invalid.test",["Authentication:Mail:From"]="test@example.invalid",
                    ["Logging:LogLevel:Default"]="None"
                });
            });
            builder.ConfigureTestServices(services => {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(o=>{o.DefaultAuthenticateScheme=o.DefaultChallengeScheme=o.DefaultForbidScheme="FinanceTest";})
                    .AddScheme<AuthenticationSchemeOptions,Auth>("FinanceTest",_=>{});
                services.RemoveAll<BillingDbContext>(); services.RemoveAll<PaymentsDbContext>();
                services.AddScoped(_=>new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase(database+"billing").Options));
                services.AddScoped(_=>new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(database+"payments").Options));
                services.RemoveAll<IApartmentOverviewSource>(); services.RemoveAll<IApartmentResidentRelationshipSource>(); services.RemoveAll<ICurrentBuildingTimeZone>();
                services.AddSingleton<IApartmentOverviewSource,Directory>(); services.AddSingleton<IApartmentResidentRelationshipSource,Directory>(); services.AddSingleton<ICurrentBuildingTimeZone,Directory>();
            });
        }
        public HttpClient Client(string? role="ACCOUNTANT",string? permission="MANAGE_FINANCE") {
            var c=CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new Uri("https://localhost")});
            if(role is not null) c.DefaultRequestHeaders.Add("X-Test-Role",role);
            if(permission is not null) c.DefaultRequestHeaders.Add("X-Test-Permission",permission);
            return c;
        }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if(!Request.Headers.TryGetValue("X-Test-Role",out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new List<Claim>{new("sub","20000000-0000-0000-0000-000000000001"),new(ClaimTypes.Role,role.ToString())};
            if(Request.Headers.TryGetValue("X-Test-Permission",out var permission)) claims.Add(new("permission",permission.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }
    [Theory]
    [InlineData("STAFF","MANAGE_FINANCE")]
    [InlineData("RESIDENT","USE_RESIDENT_SERVICES")]
    [InlineData("ADMIN","MANAGE_FINANCE")]
    [InlineData("ACCOUNTANT",null)]
    [InlineData("MANAGER",null)]
    public async Task Unauthorized_roles_and_missing_permissions_cannot_read_or_export(string role,string? permission)
    {
        await using var f=new Factory(); using var c=f.Client(role,permission);
        foreach(var path in new[]{"invoices","invoices/export","payments/export?status=CONFIRMED","fees","rates",$"invoices/{Guid.NewGuid()}/print"})
            Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/v1/billing/workspace/"+path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync("/api/v1/billing/workspace/invoices",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync("/api/v1/payments",new{})).StatusCode);
    }
    [Fact]
    public async Task Accountant_creates_issues_and_exports_same_invoice_manager_reads_but_cannot_write()
    {
        await using var f=new Factory(); using var c=f.Client();
        const string root="/api/v1/billing/workspace/";
        var fee=await Id(await c.PostAsJsonAsync(root+"fees",new{code="MANAGEMENT",name="Phí quản lý",calculationMethod="AREA",billingFrequency="MONTHLY"}));
        var rate=await Id(await c.PostAsJsonAsync(root+"rates",new{feeTypeId=fee,name="Đơn giá gốc",unitRate=1000,unitName="m²",effectiveFrom="2020-01-01"}));
        var body=new{invoiceNumber="INV-TEST",apartmentUnitId=Directory.Apartment,periodStart="2026-01-01",periodEnd="2026-01-31",dueDate="2026-02-10",lines=new[]{new{rateId=rate,quantity=12.3456,description="Diện tích thực nhập"}}};
        var id=await Id(await c.PostAsJsonAsync(root+"invoices",body));
        Assert.Equal(HttpStatusCode.NoContent,(await c.PostAsJsonAsync(root+$"invoices/{id}/issue",new{issueDate="2026-02-01",dueDate="2026-02-10"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PutAsJsonAsync(root+$"invoices/{id}",body)).StatusCode);
        using var manager=f.Client("MANAGER","MANAGE_OPERATIONS");
        var detail=await manager.GetFromJsonAsync<JsonElement>(root+$"invoices/{id}");
        Assert.Equal("A-101",detail.GetProperty("apartmentNumber").GetString()); Assert.Equal(12346m,detail.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1000m,detail.GetProperty("lines")[0].GetProperty("unitRate").GetDecimal());
        foreach(var path in new[]{"fees","rates","invoices",$"invoices/{id}/issue",$"invoices/{id}/cancel"})
            Assert.Equal(HttpStatusCode.Forbidden,(await manager.PostAsJsonAsync(root+path,new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await manager.PostAsJsonAsync($"/api/v1/payments/{Guid.NewGuid()}/confirm",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await manager.PostAsJsonAsync("/api/v1/payments",new{})).StatusCode);
        var excel=await manager.GetAsync(root+"invoices/export"); Assert.Equal(HttpStatusCode.OK,excel.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",excel.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[]{80,75},(await excel.Content.ReadAsByteArrayAsync()).Take(2));
        Assert.Equal(HttpStatusCode.OK,(await manager.GetAsync(root+"payments/export?status=CONFIRMED")).StatusCode);
        var print=await manager.GetStringAsync(root+$"invoices/{id}/print"); Assert.Contains("INV-TEST",print);Assert.Contains("A-101",print);
        if (Environment.GetEnvironmentVariable("PROPFLOW_FINANCE_QA_OUTPUT") is { Length: > 0 } output)
        {
            System.IO.Directory.CreateDirectory(output);
            await File.WriteAllBytesAsync(Path.Combine(output,"invoice-report.xlsx"),await excel.Content.ReadAsByteArrayAsync());
            await File.WriteAllTextAsync(Path.Combine(output,"invoice-print.html"),print);
        }
        using var anon=f.Client(null,null); Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(root+"invoices/export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync(root+$"invoices/{Guid.NewGuid()}/print")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(root+"invoices?Status=INVALID")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await c.DeleteAsync(root+$"invoices/{id}")).StatusCode);
    }
    private static async Task<Guid> Id(HttpResponseMessage r) { Assert.Equal(HttpStatusCode.OK,r.StatusCode);return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); }
}
