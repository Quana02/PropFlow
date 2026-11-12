using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Communication.Application.EmergencyCalls;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Presentation;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-13-Emergency-Voice")]
public sealed class EmergencyCallRealtimeTests(SupportChatDatabaseFixture database) : IClassFixture<SupportChatDatabaseFixture>
{
    [Fact]
    public async Task Real_signaling_routes_audio_to_one_manager_and_only_both_media_confirmations_mark_connected()
    {
        var directory = new SupportChatTestDirectory();
        var resident = directory.Resident(); var manager = directory.Manager(); var stranger = directory.Resident();
        using var server = Server(directory);
        using var residentApi = Client(server, "RESIDENT", resident);
        using var managerApi = Client(server, "MANAGER", manager);
        await using var residentHub = Hub(server, "RESIDENT", resident);
        await using var managerHub = Hub(server, "MANAGER", manager);
        await using var strangerHub = Hub(server, "RESIDENT", stranger);
        var offerReceived = new TaskCompletionSource<EmergencySignal>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answerReceived = new TaskCompletionSource<EmergencySignal>(TaskCreationOptions.RunContinuationsAsynchronously);
        managerHub.On<EmergencySignal>("EmergencySignal", s => offerReceived.TrySetResult(s));
        residentHub.On<EmergencySignal>("EmergencySignal", s => answerReceived.TrySetResult(s));
        await residentHub.StartAsync(); await managerHub.StartAsync(); await strangerHub.StartAsync();
        var request = new StartEmergencyCall(Guid.NewGuid(), residentHub.ConnectionId);
        using var started = await residentApi.PostAsJsonAsync("/api/v1/emergency-calls", request);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var call = (await started.Content.ReadFromJsonAsync<EmergencyCallView>())!;
        Assert.Equal("CONNECTING", call.Status);
        using var replay = await residentApi.PostAsJsonAsync("/api/v1/emergency-calls", request);
        Assert.Equal(call.Id, (await replay.Content.ReadFromJsonAsync<EmergencyCallView>())!.Id);
        var offer = new EmergencySignal(call.Id, call.Revision, "offer", """{"type":"offer","sdp":"v=0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111"}""");
        await residentHub.InvokeAsync("Signal", offer);
        Assert.Equal(offer, await offerReceived.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAsync<HubException>(() => strangerHub.InvokeAsync("Signal", offer));
        var answer = new EmergencySignal(call.Id, call.Revision, "answer", """{"type":"answer","sdp":"v=0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111"}""");
        await managerHub.InvokeAsync("Signal", answer);
        Assert.Equal(answer, await answerReceived.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await residentHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
        Assert.Equal("CONNECTING", (await Active(residentApi, residentHub.ConnectionId!))!.Status);
        await managerHub.InvokeAsync("MediaConnected", call.Id, call.Revision);
        Assert.Equal("CONNECTED", (await Active(managerApi, managerHub.ConnectionId!))!.Status);
        using var forbidden = await managerApi.PostAsJsonAsync("/api/v1/emergency-calls", new StartEmergencyCall(Guid.NewGuid(), managerHub.ConnectionId));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var ended = await residentApi.PostAsync($"/api/v1/emergency-calls/{call.Id}/end", null);
        ended.EnsureSuccessStatusCode();
        Assert.Null(await Active(managerApi, managerHub.ConnectionId!));
        await using var verification = database.CreateDb();
        Assert.Equal("ENDED", (await verification.EmergencyCalls.SingleAsync(x => x.Id == call.Id)).Status);
        Assert.False(verification.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Concurrent_starts_and_manager_connections_create_one_persistent_call_and_one_assignment()
    {
        var directory = new SupportChatTestDirectory(); var resident = directory.Resident(); var one = directory.Manager(); var two = directory.Manager();
        async Task Connect(Guid user, string connection)
        { await using var db = database.CreateDb(); await Service(db, directory).ConnectedAsync(user, connection, default); }
        await Connect(resident, "concurrent-resident");
        var request = new StartEmergencyCall(Guid.NewGuid(), "concurrent-resident");
        async Task<EmergencyCallView> Start()
        { await using var db = database.CreateDb(); return await Service(db, directory).StartAsync(resident, request, default); }
        var calls = await Task.WhenAll(Start(), Start()); Assert.Equal(calls[0].Id, calls[1].Id);
        await Task.WhenAll(Connect(one, "concurrent-manager-one"), Connect(two, "concurrent-manager-two"));
        await using var verify = database.CreateDb();
        var saved = await verify.EmergencyCalls.SingleAsync(x => x.ResidentUserId == resident);
        Assert.Equal("CONNECTING", saved.Status); Assert.Contains(saved.ManagerUserId!.Value, new[] { one, two });
        await Service(verify, directory).EndAsync(resident, saved.Id, default);
    }

    private EmergencyCallService Service(PropFlow.Modules.Communication.Infrastructure.Persistence.CommunicationDbContext db, ISupportChatActors directory) =>
        new(db, directory, new QuietPush(), TimeProvider.System, NullLogger<EmergencyCallService>.Instance);
    private static async Task<EmergencyCallView?> Active(HttpClient client, string connection)
    {
        var view = await client.GetFromJsonAsync<EmergencyActiveCall>("/api/v1/emergency-calls/active?connectionId=" + Uri.EscapeDataString(connection));
        return view!.Call;
    }
    private TestServer Server(SupportChatTestDirectory directory) => new(new WebHostBuilder().ConfigureLogging(l => l.ClearProviders()).ConfigureServices(services =>
    {
        services.AddAuthentication("EmergencyTest").AddScheme<AuthenticationSchemeOptions, TestAuth>("EmergencyTest", _ => { });
        services.AddAuthorization(options =>
        {
            options.AddPolicy(SupportChatPolicies.Use, p => p.RequireAssertion(c => SupportChatPolicies.CanUse(c.User)));
            options.AddPolicy(SupportChatPolicies.Resident, p => p.RequireAssertion(c => SupportChatPolicies.IsResident(c.User)));
            options.AddPolicy(SupportChatPolicies.Manager, p => p.RequireAssertion(c => SupportChatPolicies.IsManager(c.User)));
        });
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("emergency-call-start", l => { l.PermitLimit = 100; l.Window = TimeSpan.FromMinutes(1); });
            options.AddFixedWindowLimiter(SupportChatPolicies.RateLimit, l => { l.PermitLimit = 100; l.Window = TimeSpan.FromMinutes(1); });
        });
        services.AddSignalR(); services.AddSingleton<IUserIdProvider, UserId>(); services.AddSingleton<EmergencyFrameGate>();
        services.AddControllers().ConfigureApplicationPartManager(m => m.ApplicationParts.Clear()).AddApplicationPart(typeof(EmergencyCallController).Assembly);
        services.AddScoped(_ => database.CreateDb()); services.AddSingleton<ISupportChatActors>(directory); services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEmergencyCallRealtime, EmergencyCallRealtime>(); services.AddScoped<IEmergencyCalls, EmergencyCallService>();
        services.AddOptions<EmergencyRtcOptions>(); services.AddScoped<IEmergencyRtcConfiguration, EmergencyRtcConfigurationProvider>();
    }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.UseEndpoints(e => { e.MapControllers(); e.MapHub<EmergencyCallHub>("/hubs/emergency-calls"); }); }));
    private static HttpClient Client(TestServer server, string role, Guid user)
    { var client = server.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Role", role); client.DefaultRequestHeaders.Add("X-Test-Actor", user.ToString()); return client; }
    private static HubConnection Hub(TestServer server, string role, Guid user) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/emergency-calls", options =>
    {
        options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => server.CreateHandler();
        options.Headers["X-Test-Role"] = role; options.Headers["X-Test-Actor"] = user.ToString();
    }).Build();
    private sealed class UserId : IUserIdProvider { public string? GetUserId(HubConnectionContext c) => c.User?.FindFirstValue("sub"); }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault(); var actor = Request.Headers["X-Test-Actor"].FirstOrDefault();
            if (!Guid.TryParse(actor, out _) || role is null) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity([new("sub", actor!), new(ClaimTypes.Role, role), new("permission", role == "MANAGER" ? "MANAGE_OPERATIONS" : "USE_RESIDENT_SERVICES")], Scheme.Name)), Scheme.Name)));
        }
    }
    private sealed class QuietPush : IEmergencyCallRealtime
    {
        public Task ChangedAsync(IReadOnlyCollection<Guid> users, CancellationToken ct) => Task.CompletedTask;
        public Task SignalAsync(string connection, EmergencySignal signal, CancellationToken ct) => Task.CompletedTask;
    }
}
