using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.AiChatbot.Infrastructure;
using PropFlow.Modules.AiChatbot.Infrastructure.Persistence;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AiAuthorizationHttpTests(AiAuthorizationFixture fixture) : IClassFixture<AiAuthorizationFixture>
{
    private AuthDatabaseFixture Database => fixture.Database;
    private AiAuthorizationFixture.Actor Manager => fixture["MANAGER"];
    private AiAuthorizationFixture.Actor Admin => fixture["ADMIN"];
    private AiAuthorizationFixture.Actor Staff => fixture["STAFF"];
    private AiAuthorizationFixture.Actor Resident => fixture["RESIDENT"];
    private string PiiEmail() => $"b7-{Guid.NewGuid():N}@example.invalid";

    private async Task<T> Call<T>(AiAuthorizationFixture.Actor actor, HttpMethod method, string path, object? input = null)
    {
        using var response = await Send(actor.Client, method, path, input);
        await Expect(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? input)
    {
        using var request = new HttpRequestMessage(method, "/api/v1/" + path);
        if (input is not null) request.Content = JsonContent.Create(input);
        return await client.SendAsync(request);
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.True(response.StatusCode == status,
            $"{response.RequestMessage!.RequestUri!.AbsolutePath}: expected {(int)status}, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private async Task Reject(HttpMethod method, string path, object? input, Func<Task<string>> state,
        params (HttpClient Client, HttpStatusCode Status)[] actors)
    {
        var before = await state();
        var calls = fixture.Gemini.Calls.Count;
        var notices = fixture.Realtime.Requests.Count;
        foreach (var (client, status) in actors)
        {
            using var response = await Send(client, method, path, input);
            await Expect(response, status);
        }
        Assert.Equal(before, await state());
        Assert.Equal(calls, fixture.Gemini.Calls.Count);
        Assert.Equal(notices, fixture.Realtime.Requests.Count);
    }

    private static async Task<T[]> Rows<T>(IQueryable<T> records) where T : class
    {
        var rows = await records.AsNoTracking().ToArrayAsync();
        return rows.OrderBy(row => JsonSerializer.Serialize(row), StringComparer.Ordinal).ToArray();
    }
}

public sealed class AiAuthorizationFixture : IAsyncLifetime
{
    private readonly List<HttpClient> clients = [];
    private readonly Dictionary<string, Actor> actors = [];
    public sealed record Actor(Guid Id, HttpClient Client);
    internal Actor this[string role] => actors[role];
    internal AuthDatabaseFixture Database { get; }
    internal GeminiTransport Gemini { get; } = new();
    internal RequestUpdates Realtime { get; } = new();
    internal HttpClient Anonymous { get; private set; } = null!;
    internal IReadOnlyList<(HttpClient Client, HttpStatusCode Status)> ManagerDenials { get; private set; } = [];
    internal IReadOnlyList<(HttpClient Client, HttpStatusCode Status)> KnowledgeDenials { get; private set; } = [];

    public AiAuthorizationFixture()
    {
        Database = new() { ConfigureFixtureServices = services =>
        {
            foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) &&
                service.ImplementationType is { } type && typeof(BackgroundService).IsAssignableFrom(type)).ToArray())
                services.Remove(worker);
            services.PostConfigure<ChatAiOptions>(o =>
            {
                o.Enabled = true;
                o.ApiKey = "synthetic-b7-provider";
                o.BaseUrl = "https://gemini.example.invalid/v1beta/";
                o.RequestsPerHour = 1000;
            });
            services.AddHttpClient("ResidentChatGemini").ConfigurePrimaryHttpMessageHandler(() => Gemini);
            services.AddHttpClient("ServiceRequestGemini").ConfigurePrimaryHttpMessageHandler(() => Gemini);
            services.RemoveAll<IServiceRequestRealtimeNotifier>();
            services.AddSingleton<IServiceRequestRealtimeNotifier>(Realtime);
        } };
    }

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        using var scope = Database.Factory.Services.CreateScope();
        var s = scope.ServiceProvider;
        await s.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        await s.GetRequiredService<AiClassificationDbContext>().Database.MigrateAsync();
        await s.GetRequiredService<AiRecommendationDbContext>().Database.MigrateAsync();
        await s.GetRequiredService<AiChatbotDbContext>().Database.MigrateAsync();
        await s.GetRequiredService<MaintenanceDbContext>().Database.MigrateAsync();
        await s.GetRequiredService<BillingDbContext>().Database.MigrateAsync();
        await s.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
        await s.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        foreach (var role in new[] { "MANAGER", "ADMIN", "STAFF", "ACCOUNTANT", "RESIDENT" })
            actors.Add(role, await Login(role));
        Anonymous = Client();
        var revokedManager = await Login("MANAGER", revoke: true);
        var revokedAdmin = await Login("ADMIN", revoke: true);
        KnowledgeDenials = [(Anonymous, HttpStatusCode.Unauthorized), (revokedManager.Client, HttpStatusCode.Unauthorized),
            (revokedAdmin.Client, HttpStatusCode.Unauthorized), (this["STAFF"].Client, HttpStatusCode.Forbidden),
            (this["ACCOUNTANT"].Client, HttpStatusCode.Forbidden), (this["RESIDENT"].Client, HttpStatusCode.Forbidden)];
        ManagerDenials = [.. KnowledgeDenials, (this["ADMIN"].Client, HttpStatusCode.Forbidden)];
    }

    public async Task DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await Database.DisposeAsync();
    }

    private HttpClient Client()
    {
        var client = Database.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        clients.Add(client);
        return client;
    }

    internal async Task<Actor> Login(string role, bool revoke = false, Guid? residentId = null)
    {
        const string password = "Synthetic B7 integration password!";
        using var scope = Database.Factory.Services.CreateScope();
        var s = scope.ServiceProvider;
        var auth = s.GetRequiredService<AuthenticationDbContext>();
        var access = s.GetRequiredService<AdministrationDbContext>();
        var name = "b7_" + Guid.NewGuid().ToString("N");
        var now = Database.Factory.Clock.GetUtcNow();
        var user = new UserAccount(name, "pending", "Tài khoản thử B7", now, name + "@example.invalid",
            status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(s.GetRequiredService<IAuthSecrets>().HashPassword(user, password), null, now);
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        var roleId = await access.Roles.Where(x => x.Code == role).Select(x => x.Id).SingleAsync();
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, now));
        await access.SaveChangesAsync();
        if (residentId is { } id)
        {
            var residents = s.GetRequiredService<ResidentsDbContext>();
            var resident = await residents.Residents.SingleAsync(x => x.Id == id);
            resident.LinkUserAccount(user.Id, user.Id, now);
            await residents.SaveChangesAsync();
        }
        var client = Client();
        using var login = await AuthPost(client, "login", new LoginRequest(name, password));
        var session = (await login.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(role, session.User.Role);
        if (revoke)
        {
            using var logout = await AuthPost(client, "logout", new { });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return new(user.Id, client);
    }

    private static async Task<HttpResponseMessage> AuthPost(HttpClient client, string action, object input)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + action) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }

    internal sealed class RequestUpdates : IServiceRequestRealtimeNotifier
    {
        internal ConcurrentQueue<Guid> Requests { get; } = new();
        public Task NotifyChangedAsync(Guid serviceRequestId, Guid? residentUserId = null, CancellationToken ct = default)
        {
            Requests.Enqueue(serviceRequestId);
            return Task.CompletedTask;
        }
    }

    internal sealed class GeminiTransport : HttpMessageHandler
    {
        internal sealed record Invocation(string Kind, string Payload);
        internal ConcurrentQueue<Invocation> Calls { get; } = new();
        internal Func<Invocation, CancellationToken, Task<HttpResponseMessage>>? Override { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var raw = await request.Content!.ReadAsStringAsync(ct);
            using var body = JsonDocument.Parse(raw);
            string kind;
            var root = body.RootElement;
            if (root.TryGetProperty("requests", out _)) kind = "embedding";
            else
            {
                var properties = root.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("properties");
                kind = properties.TryGetProperty("priorityCode", out _) ? "recommendation"
                    : properties.TryGetProperty("categoryCode", out _) ? "classification" : "chat";
            }
            var payload = kind == "embedding" ? raw
                : root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
            var call = new Invocation(kind, payload);
            Calls.Enqueue(call);
            return Override is { } respond ? await respond(call, ct) : Reply(call);
        }

        internal static HttpResponseMessage Reply(Invocation call, string? response = null)
        {
            if (call.Kind == "embedding") return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embeddings = new[] { new { values = Enumerable.Repeat(1f, 768) } } })
            };
            var text = response ?? (call.Kind switch
            {
                "recommendation" => """{"priorityCode":"URGENT","maintenanceRecommended":true,"action":"Kiểm tra máy bơm","resources":"Kỹ thuật viên","reason":"Áp suất nước thấp."}""",
                "classification" => """{"categoryCode":"REPAIR","reason":"Máy bơm hoạt động không ổn định."}""",
                _ => """{"supported":true,"answer":"Xem hướng dẫn trong tài liệu [1].","citations":[1]}"""
            });
            return new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { candidates = new[] { new { finishReason = "STOP", content = new
                    { parts = new[] { new { text } } } } }, usageMetadata = new { promptTokenCount = 5, candidatesTokenCount = 3, totalTokenCount = 8 } })
            };
        }
    }
}
