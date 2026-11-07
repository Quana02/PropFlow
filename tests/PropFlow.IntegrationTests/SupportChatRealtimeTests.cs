using System.Collections.Concurrent;
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
using Microsoft.Extensions.Options;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Presentation;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-13-ScopeExtension-SupportChat")]
public sealed class SupportChatRealtimeTests(SupportChatDatabaseFixture database) : IClassFixture<SupportChatDatabaseFixture>
{
    [Theory]
    [InlineData("RESIDENT", HttpStatusCode.OK)] [InlineData("MANAGER", HttpStatusCode.OK)]
    [InlineData("STAFF", HttpStatusCode.Forbidden)] [InlineData("ADMIN", HttpStatusCode.Forbidden)]
    [InlineData("ACCOUNTANT", HttpStatusCode.Forbidden)] [InlineData("", HttpStatusCode.Unauthorized)]
    public async Task HttpChatOnlyAllowsResidentsAndManagers(string role, HttpStatusCode expected)
    {
        var directory = new SupportChatTestDirectory(); var actor = role == "MANAGER" ? directory.Manager() : directory.Resident();
        using var server = Server(directory); using var client = Client(server, role, actor);
        using var list = await client.GetAsync("/api/v1/support-chat/conversations"); Assert.Equal(expected, list.StatusCode);
        using var availability = await client.GetAsync("/api/v1/support-chat/availability");
        Assert.Equal(role == "MANAGER" ? HttpStatusCode.OK : role.Length == 0 ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, availability.StatusCode);
    }

    [Fact]
    public async Task RealSignalRDeliversUnsentDraftOnlyToAssignedManagerAndStopsAfterClosing()
    {
        var directory = new SupportChatTestDirectory();
        var manager = directory.Manager(); var otherManager = directory.Manager(); var resident = directory.Resident(); var stranger = directory.Resident();
        using var server = Server(directory);
        using var managerApi = Client(server, "MANAGER", manager); using var residentApi = Client(server, "RESIDENT", resident); using var strangerApi = Client(server, "RESIDENT", stranger);
        await using var managerHub = Hub(server, "MANAGER", manager); await using var otherHub = Hub(server, "MANAGER", otherManager); await using var residentHub = Hub(server, "RESIDENT", resident);
        var received = new TaskCompletionSource<SupportDraft>(TaskCreationOptions.RunContinuationsAsynchronously);
        var managerFrames = new ConcurrentQueue<SupportDraft>(); var otherFrames = new ConcurrentQueue<SupportDraft>();
        managerHub.On<SupportDraft>("SupportChatDraft", draft => { managerFrames.Enqueue(draft); received.TrySetResult(draft); });
        otherHub.On<SupportDraft>("SupportChatDraft", draft => otherFrames.Enqueue(draft));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await managerHub.StartAsync(timeout.Token); await otherHub.StartAsync(timeout.Token); await residentHub.StartAsync(timeout.Token);
        for (var i = 0; i < 30; i++)
        {
            var state = await managerApi.GetFromJsonAsync<SupportAvailabilityView>("/api/v1/support-chat/availability", timeout.Token);
            if (state?.Connected == true) break;
            await Task.Delay(50, timeout.Token);
        }
        using var ready = await managerApi.PutAsJsonAsync("/api/v1/support-chat/availability", new ChatAvailabilityRequest(true), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        using var opened = await residentApi.PostAsJsonAsync("/api/v1/support-chat/conversations", new ChatSendRequest(Guid.NewGuid(), "Cần hỗ trợ sửa chữa"), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var chat = (await opened.Content.ReadFromJsonAsync<SupportConversationView>(timeout.Token))!; Assert.Equal("ACTIVE", chat.Status);
        using var blocked = await strangerApi.GetAsync($"/api/v1/support-chat/conversations/{chat.Id}/messages", timeout.Token); Assert.Equal(HttpStatusCode.NotFound, blocked.StatusCode);
        await residentHub.InvokeAsync("ResidentDraft", chat.Id, 1L, "BẢN NHÁP CHƯA GỬI", timeout.Token);
        var draft = await received.Task.WaitAsync(timeout.Token);
        Assert.Equal("BẢN NHÁP CHƯA GỬI", draft.Text); Assert.Equal(chat.Id, draft.ConversationId); Assert.Empty(otherFrames);
        await using (var db = database.CreateDb())
        { Assert.DoesNotContain(await db.SupportMessages.Where(x => x.ConversationId == chat.Id).ToArrayAsync(timeout.Token), x => x.Text.Contains("BẢN NHÁP")); }
        using var closed = await managerApi.PostAsync($"/api/v1/support-chat/conversations/{chat.Id}/close", null, timeout.Token); Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        await residentHub.InvokeAsync("ResidentDraft", chat.Id, 2L, "KHÔNG ĐƯỢC TRUYỀN SAU KHI KẾT THÚC", timeout.Token);
        Assert.Single(managerFrames); Assert.Empty(otherFrames);
    }

    private TestServer Server(SupportChatTestDirectory directory) => new(new WebHostBuilder().ConfigureLogging(l => l.ClearProviders()).ConfigureServices(services =>
    {
        services.AddAuthentication("SupportTest").AddScheme<AuthenticationSchemeOptions, TestAuth>("SupportTest", _ => { });
        services.AddAuthorization(options => {
            options.AddPolicy(SupportChatPolicies.Use, p => p.RequireAssertion(c => SupportChatPolicies.CanUse(c.User)));
            options.AddPolicy(SupportChatPolicies.Manager, p => p.RequireAssertion(c => SupportChatPolicies.IsManager(c.User)));
            options.AddPolicy(SupportChatPolicies.Resident, p => p.RequireAssertion(c => SupportChatPolicies.IsResident(c.User)));
        });
        services.AddRateLimiter(options => options.AddFixedWindowLimiter(SupportChatPolicies.RateLimit, l => { l.PermitLimit = 100; l.Window = TimeSpan.FromMinutes(1); }));
        services.AddSignalR(); services.AddSingleton<IUserIdProvider, TestUserId>(); services.AddSingleton<SupportChatFrameGate>();
        services.AddControllers().ConfigureApplicationPartManager(m => m.ApplicationParts.Clear()).AddApplicationPart(typeof(SupportChatController).Assembly);
        services.AddScoped(_ => database.CreateDb()); services.AddSingleton<ISupportChatActors>(directory);
        services.AddSingleton(TimeProvider.System); services.AddSingleton<ISupportChatRealtime, SupportChatRealtime>(); services.AddScoped<ISupportChat, SupportChatService>();
    }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
        app.UseEndpoints(e => { e.MapControllers(); e.MapHub<SupportChatHub>("/hubs/support-chat"); }); }));
    private static HttpClient Client(TestServer server, string role, Guid actor)
    { var client = server.CreateClient(); if (role.Length > 0) { client.DefaultRequestHeaders.Add("X-Test-Role", role); client.DefaultRequestHeaders.Add("X-Test-Actor", actor.ToString()); } return client; }
    private static HubConnection Hub(TestServer server, string role, Guid actor) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/support-chat", options => {
        options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => server.CreateHandler();
        options.Headers["X-Test-Role"] = role; options.Headers["X-Test-Actor"] = actor.ToString();
    }).Build();
    private sealed class TestUserId : IUserIdProvider { public string? GetUserId(HubConnectionContext connection) => connection.User?.FindFirstValue("sub"); }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault(); var actor = Request.Headers["X-Test-Actor"].FirstOrDefault();
            if (role is null || !Guid.TryParse(actor, out _)) return Task.FromResult(AuthenticateResult.NoResult());
            var permission = role == "MANAGER" ? "MANAGE_OPERATIONS" : "USE_RESIDENT_SERVICES";
            var user = new ClaimsPrincipal(new ClaimsIdentity([new("sub", actor), new(ClaimTypes.Role, role), new("permission", permission)], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new(user, Scheme.Name)));
        }
    }
}
