using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Presentation;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FacilityDiscovery-Task2-LiveVoice")]
public sealed class FacilityLiveHttpTests
{
    [Fact]
    public async Task Live_endpoint_enforces_role_residency_no_store_and_connection_budget()
    {
        var resident = Guid.NewGuid(); var provider = new Provider(); var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(FacilityLiveController).Assembly);
        builder.Services.AddAuthentication("LiveTest").AddScheme<AuthenticationSchemeOptions, Authentication>("LiveTest", _ => { });
        builder.Services.AddAuthorization();
        var database = Guid.NewGuid().ToString();
        builder.Services.AddDbContext<AiRecommendationDbContext>(o => o.UseInMemoryDatabase(database));
        builder.Services.AddSingleton<IFacilityResidentAccess>(new Access(resident));
        builder.Services.AddSingleton<IFacilityLiveProvider>(provider);
        builder.Services.AddSingleton<IFacilityGuideSource, Source>();
        builder.Services.AddSingleton<IFacilityNeedAdvisor, Advisor>();
        builder.Services.AddScoped<FacilityRecommendations>();
        builder.Services.AddSingleton<ICurrentBuildingTimeZone, Zone>(); builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(new FacilityRecommendationOptions()); builder.Services.AddSingleton(new FacilityLiveOptions());
        builder.Services.AddScoped<FacilityLiveSessions>();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        await app.StartAsync(); using var client = app.GetTestClient();
        const string route = "/api/v1/resident/facility-recommendations/live";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(route, null)).StatusCode);
        foreach (var role in new[] { "ADMIN", "MANAGER", "STAFF", "ACCOUNTANT" })
        {
            client.DefaultRequestHeaders.Remove("X-Role"); client.DefaultRequestHeaders.Add("X-Role", role);
            client.DefaultRequestHeaders.Remove("X-Actor"); client.DefaultRequestHeaders.Add("X-Actor", resident.ToString());
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(route, null)).StatusCode);
        }
        client.DefaultRequestHeaders.Remove("X-Role"); client.DefaultRequestHeaders.Add("X-Role", "RESIDENT");
        client.DefaultRequestHeaders.Remove("X-Actor"); client.DefaultRequestHeaders.Add("X-Actor", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(route, null)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Actor"); client.DefaultRequestHeaders.Add("X-Actor", resident.ToString());
        var response = await client.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var session = await response.Content.ReadFromJsonAsync<FacilityLiveSession>();
        Assert.Equal("auth_tokens/test-only", session!.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(1, provider.Calls);
        var lookup=await client.PostAsJsonAsync(route+"/lookup",new FacilityRecommendationRequest("Tìm chỗ đọc sách hôm nay"));
        Assert.Equal(HttpStatusCode.OK,lookup.StatusCode);
        Assert.Equal("NO_MATCH",(await lookup.Content.ReadFromJsonAsync<FacilityRecommendationView>())!.Mode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(route+"/lookup",new { need="" })).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Role");client.DefaultRequestHeaders.Add("X-Role","STAFF");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync(route+"/lookup",new {need="Đọc sách"})).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Role");client.DefaultRequestHeaders.Add("X-Role","RESIDENT");
        provider.Ready = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsync(route, null)).StatusCode);
        await app.StopAsync();
    }
    private sealed class Source : IFacilityGuideSource { public Task<FacilityGuideWindow> ReadAsync(CancellationToken ct)=>Task.FromResult(new FacilityGuideWindow([],"test")); }
    private sealed class Advisor : IFacilityNeedAdvisor
    { public bool Ready=>true; public Task<FacilityAdvisorResult?> RecommendAsync(string need,FacilityAdvisorInput[] choices,CancellationToken ct)=>throw new InvalidOperationException("Live lookup must not invoke a second model."); }
    private sealed class Provider : IFacilityLiveProvider
    {
        public bool Ready { get; set; } = true; public int Calls;
        public Task<FacilityLiveSession?> CreateAsync(DateTime now, DateTimeOffset expires, CancellationToken ct)
        { Calls++; return Task.FromResult<FacilityLiveSession?>(new("auth_tokens/test-only", "wss://generativelanguage.googleapis.com/ws/test", JsonSerializer.SerializeToElement(new { model = "models/test" }), expires)); }
    }
    private sealed class Access(Guid allowed) : IFacilityResidentAccess { public Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => Task.FromResult(actor == allowed); }
    private sealed class Zone : ICurrentBuildingTimeZone { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh"); }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim(ClaimTypes.Role, role!), new Claim("sub", Request.Headers["X-Actor"]!) };
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
