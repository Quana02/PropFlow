using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application.EmergencyCalls;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-13")]
public sealed partial class CommunicationAuthorizationHttpTests : IAsyncLifetime
{
    private readonly AuthDatabaseFixture database;
    private readonly List<HttpClient> clients = [];
    private readonly List<HubConnection> hubs = [];
    private readonly Recorder realtime = new();
    private int rtcInvocations;
    private readonly List<(HttpClient Client, HttpStatusCode Status)> denied = [];
    private Actor owner = null!, other = null!, manager = null!, unassigned = null!, unlinked = null!;
    private AuthTestClock Clock => database.Factory.Clock;
    private const string Chat = "/api/v1/support-chat";
    private const string Emergency = "/api/v1/emergency-calls";
    private EmergencyRtcOptions rtc = null!;

    public CommunicationAuthorizationHttpTests()
    {
        database = new AuthDatabaseFixture { ConfigureFixtureServices = services =>
        {
            foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) &&
                service.ImplementationType is { } type && typeof(BackgroundService).IsAssignableFrom(type)).ToArray())
                services.Remove(worker);
            services.RemoveAll<ISupportChatRealtime>();
            services.AddSingleton<ISupportChatRealtime>(realtime);
            services.RemoveAll<IEmergencyCallRealtime>();
            services.AddSingleton<IEmergencyCallRealtime>(realtime);
            services.RemoveAll<IEmergencyRtcConfiguration>();
            services.AddScoped<EmergencyRtcConfigurationProvider>();
            services.AddScoped<IEmergencyRtcConfiguration>(provider => new CountingRtc(
                provider.GetRequiredService<EmergencyRtcConfigurationProvider>(), () => Interlocked.Increment(ref rtcInvocations)));
            services.Configure<EmergencyRtcOptions>(options =>
            {
                options.StunUrls = ["stun:127.0.0.1:9"];
                options.TurnUrls = [];
                options.TurnSharedSecret = "";
            });
        } };
    }

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        using var scope = database.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        rtc = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmergencyRtcOptions>>().Value;
        owner = await Login("RESIDENT", resident: true);
        other = await Login("RESIDENT", resident: true);
        unlinked = await Login("RESIDENT");
        manager = await Login("MANAGER");
        unassigned = await Login("MANAGER");
        denied.Add((Client(), HttpStatusCode.Unauthorized));
        foreach (var role in new[] { "ADMIN", "ACCOUNTANT", "STAFF" })
            denied.Add(((await Login(role)).Client, HttpStatusCode.Forbidden));
        foreach (var role in new[] { "MANAGER", "RESIDENT" })
            denied.Add(((await Login(role, revoked: true)).Client, HttpStatusCode.Unauthorized));
    }

    public async Task DisposeAsync()
    {
        try
        {
            foreach (var hub in hubs) await hub.DisposeAsync();
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            await database.DisposeAsync();
        }
    }

    private HttpClient Client()
    {
        var client = database.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        clients.Add(client);
        return client;
    }

    private sealed record Actor(Guid Id, HttpClient Client, string Token);

    private async Task<Actor> Login(string role, bool resident = false, bool revoked = false)
    {
        const string password = "Synthetic B5 communication password!";
        using var scope = database.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var auth = services.GetRequiredService<AuthenticationDbContext>();
        var access = services.GetRequiredService<AdministrationDbContext>();
        var name = "b5_" + Guid.NewGuid().ToString("N");
        var now = Clock.GetUtcNow();
        var user = new UserAccount(name, "pending", "Tài khoản thử B5", now, name + "@example.invalid",
            status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(services.GetRequiredService<IAuthSecrets>().HashPassword(user, password), null, now);
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        var roleId = await access.Roles.Where(row => row.Code == role).Select(row => row.Id).SingleAsync();
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, now));
        await access.SaveChangesAsync();
        if (resident)
        {
            var home = await database.SeedResidentAsync(name + "-contact@example.invalid");
            var residents = services.GetRequiredService<ResidentsDbContext>();
            var row = await residents.Residents.SingleAsync(x => x.Id == home.ResidentId);
            row.LinkUserAccount(user.Id, user.Id, now);
            await residents.SaveChangesAsync();
        }
        var client = Client();
        using var login = await AuthPost(client, "login", new LoginRequest(name, password));
        var session = (await login.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(role, session.User.Role);
        if (revoked)
        {
            using var logout = await AuthPost(client, "logout", new { });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return new(user.Id, client, session.AccessToken);
    }

    private static async Task<HttpResponseMessage> AuthPost(HttpClient client, string action, object input)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + action) { Content = JsonContent.Create(input) };
        req.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(req);
    }

    private async Task<HubConnection> Connect(Actor actor, string name)
    {
        var hub = AuthenticationTestSessions.ConnectWebSocket(database.Factory, name, actor.Token, negotiate: true);
        hubs.Add(hub);
        await hub.StartAsync();
        await hub.InvokeAsync("Heartbeat");
        return hub;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string route, object? input = null)
    {
        using var req = new HttpRequestMessage(method, route);
        if (input is not null) req.Content = JsonContent.Create(input);
        return await client.SendAsync(req);
    }

    private static async Task<T> Call<T>(Actor actor, HttpMethod method, string route, object? input = null)
    {
        using var response = await Send(actor.Client, method, route, input);
        AssertStatus(HttpStatusCode.OK, response);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static void AssertStatus(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: expected {(int)expected}, got {(int)response.StatusCode}");

    private async Task Deny(HttpMethod method, string route, object? input = null)
    {
        var before = await Snapshot();
        foreach (var (client, status) in denied)
        {
            using var response = await Send(client, method, route, input);
            AssertStatus(status, response);
        }
        Assert.Equal(before, await Snapshot());
    }

    private async Task Reject(Actor actor, HttpMethod method, string route, HttpStatusCode status, object? input = null)
    {
        var before = await Snapshot();
        using var response = await Send(actor.Client, method, route, input);
        AssertStatus(status, response);
        Assert.Equal(before, await Snapshot());
    }

    private async Task<string> Snapshot()
    {
        using var scope = database.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        return JsonSerializer.Serialize(new
        {
            conversations = await db.SupportConversations.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            messages = await db.SupportMessages.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            presence = await db.SupportManagerPresence.AsNoTracking().OrderBy(x => x.UserId).ToArrayAsync(),
            chatConnections = await db.SupportManagerConnections.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            calls = await db.EmergencyCalls.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            connections = await db.EmergencyConnections.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            notices = await db.Notifications.CountAsync(),
            emails = await db.FinanceEmails.CountAsync(),
            events = realtime.Events.ToArray()
        });
    }

    private sealed class Recorder : ISupportChatRealtime, IEmergencyCallRealtime
    {
        public sealed record Event(string Kind, Guid? Conversation, Guid[] Users);
        public readonly ConcurrentQueue<Event> Events = new();

        public Task ChangedAsync(Guid conversationId, IReadOnlyCollection<Guid> users, CancellationToken ct) =>
            Record(new("chat", conversationId, users.Order().ToArray()));
        public Task ChangedAsync(IReadOnlyCollection<Guid> users, CancellationToken ct) =>
            Record(new("call", null, users.Order().ToArray()));
        public Task DraftAsync(Guid recipient, SupportDraft draft, CancellationToken ct) =>
            Record(new("draft", draft.ConversationId, [recipient]));
        public Task TypingAsync(Guid recipient, SupportTyping typing, CancellationToken ct) =>
            Record(new("typing", typing.ConversationId, [recipient]));
        public Task SignalAsync(string connection, EmergencySignal signal, CancellationToken ct) =>
            Record(new("signal", signal.CallId, []));
        private Task Record(Event item)
        {
            Events.Enqueue(item);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingRtc(IEmergencyRtcConfiguration provider, Action count) : IEmergencyRtcConfiguration
    {
        public Task<EmergencyRtcConfiguration> GetAsync(Guid actor, CancellationToken ct)
        {
            count();
            return provider.GetAsync(actor, ct);
        }
    }
}
