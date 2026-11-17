using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-09")]
public sealed partial class PaymentAuthorizationHttpTests : IAsyncLifetime
{
    private const string Password = "Synthetic payment test password!";
    private const string Settings = "/api/v1/payment-settings/";
    private readonly AuthDatabaseFixture database = new();
    private readonly FakeSePay provider = new();
    private readonly List<HttpClient> clients = [];
    private WebApplicationFactory<Program> host = null!;
    private AuthTestClock Clock => database.Factory.Clock;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        host = database.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:SePay:Enabled"] = "true",
                ["Payments:SePay:OAuth:Enabled"] = "true",
                ["Payments:SePay:OAuth:ClientId"] = "fixture-client",
                ["Payments:SePay:OAuth:ClientSecret"] = "fixture-client-secret",
                ["Payments:SePay:OAuth:RedirectUri"] = "https://app.example.invalid/admin/sepay/callback",
                ["Payments:SePay:AuthenticationMode"] = "HMAC",
                ["Payments:SePay:WebhookSecret"] = new string('k', 40),
                ["Payments:SePay:WebhookPublicBaseUrl"] = "https://api.example.invalid",
                ["Payments:RateLimits:payment-settings-otp:PermitLimit"] = "1000",
                ["Payments:RateLimits:payment-settings-provider:PermitLimit"] = "1000"
            }));
            builder.ConfigureTestServices(services =>
            {
                foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) &&
                    service.ImplementationType is { } type && typeof(BackgroundService).IsAssignableFrom(type)).ToArray())
                    services.Remove(worker);
                services.AddHttpClient<SePayOAuth>().ConfigurePrimaryHttpMessageHandler(() => provider);
                services.AddHttpClient<ISePayConnectionProbe, SePayConnectionProbe>().ConfigurePrimaryHttpMessageHandler(() => provider);
                services.AddHttpClient<SePayBankAccounts>().ConfigurePrimaryHttpMessageHandler(() => provider);
                services.RemoveAll<IPaymentBankDirectory>();
                services.AddSingleton<IPaymentBankDirectory, FixtureBanks>();
            });
        });
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        try { if (host is not null) await host.DisposeAsync(); }
        finally { await database.DisposeAsync(); }
    }

    private HttpClient Client()
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
            { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        clients.Add(client);
        return client;
    }

    private sealed record Actor(Guid Id, string Email, HttpClient Client);

    private async Task<Actor> Login(string role, bool revoked = false, bool verified = true)
    {
        using var scope = host.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var name = "b2_" + Guid.NewGuid().ToString("N");
        var email = name + "@example.invalid";
        var now = Clock.GetUtcNow();
        var user = new UserAccount(name, "pending", "Tài khoản fixture", now, email,
            status: AccountStatus.ACTIVE, emailVerified: verified);
        user.ChangePasswordHash(scope.ServiceProvider.GetRequiredService<IAuthSecrets>().HashPassword(user, Password), null, now);
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id,
            await access.Roles.Where(row => row.Code == role).Select(row => row.Id).SingleAsync(), now));
        await access.SaveChangesAsync();
        var client = Client();
        using var response = await CookiePost(client, "login", new LoginRequest(name, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(role, session.User.Role);
        if (revoked)
        {
            using var logout = await CookiePost(client, "logout", new { });
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return new(user.Id, email, client);
    }

    private static async Task<HttpResponseMessage> CookiePost(HttpClient client, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }

    private async Task<(HttpClient Client, HttpStatusCode Status)[]> DeniedActors(string expectedRole)
    {
        var denied = new List<(HttpClient, HttpStatusCode)> { (Client(), HttpStatusCode.Unauthorized) };
        foreach (var role in new[] { "ADMIN", "MANAGER", "STAFF", "ACCOUNTANT", "RESIDENT" }.Where(role => role != expectedRole))
            denied.Add(((await Login(role)).Client, HttpStatusCode.Forbidden));
        denied.Add(((await Login(expectedRole, revoked: true)).Client, HttpStatusCode.Unauthorized));
        return denied.ToArray();
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body = null, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task Deny((HttpClient Client, HttpStatusCode Status)[] actors, HttpMethod method, string path, object? body = null)
    {
        foreach (var (client, status) in actors)
        {
            using var response = await Send(client, method, path, body, Guid.NewGuid().ToString("N"));
            Assert.Equal(status, response.StatusCode);
        }
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private string Otp(Actor actor) => database.Factory.Mail.Codes[actor.Email];
    private static string WrongCode(string code) => code == "111111" ? "222222" : "111111";

    private sealed class FixtureBanks : IPaymentBankDirectory
    {
        public Task<IReadOnlyList<BankOption>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<BankOption>>([new("970436", "VCB", "VCB", "Vietcombank", null)]);
    }

    private sealed class FakeSePay : HttpMessageHandler
    {
        public const string Access = "synthetic-sepay-access";
        public const string Refresh = "synthetic-sepay-refresh";
        public readonly ConcurrentQueue<(HttpMethod Method, string Path)> Calls = new();
        public bool FailAccounts { get; set; }
        public bool FailExchange { get; set; }
        public bool FailWebhook { get; set; }
        public long? ConfiguredBank { get; private set; }
        public bool ValidExchange { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Assert.Equal("my.sepay.vn", uri.Host);
            Calls.Enqueue((request.Method, uri.AbsolutePath));
            if (uri.AbsolutePath == "/oauth/token")
            {
                var body = await request.Content!.ReadAsStringAsync(ct);
                var fields = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(body);
                ValidExchange = fields["grant_type"] == "authorization_code" && fields["code"] == "fixture-code" &&
                    fields["redirect_uri"] == "https://app.example.invalid/admin/sepay/callback" &&
                    fields["client_id"] == "fixture-client" && fields["client_secret"] == "fixture-client-secret";
                return FailExchange ? Json(HttpStatusCode.BadGateway, "{}") : Json(HttpStatusCode.OK,
                    $$"""{"access_token":"{{Access}}","refresh_token":"{{Refresh}}","expires_in":3600}""");
            }
            Assert.True(request.Headers.Authorization?.Parameter == Access, "Fake provider received unexpected authorization.");
            if (uri.AbsolutePath == "/api/v1/bank-accounts")
                return FailAccounts ? Json(HttpStatusCode.BadGateway, "{}") : Json(HttpStatusCode.OK, """
                    {"status":"success","data":[{"id":19,"account_number":"123456789","account_holder_name":"FIXTURE A","active":true,"bank":{"bin":"970436","code":"VCB","full_name":"Vietcombank"}},
                    {"id":20,"account_number":"123450001","account_holder_name":"FIXTURE B","active":true,"bank":{"bin":"970436","code":"VCB","full_name":"Vietcombank"}}]}
                    """);
            if (uri.AbsolutePath == "/api/v1/webhooks" && request.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, """{"status":"success","data":[]}""");
            if (uri.AbsolutePath == "/api/v1/webhooks" && request.Method == HttpMethod.Post)
            {
                using var payload = JsonDocument.Parse(await request.Content!.ReadAsStreamAsync(ct));
                ConfiguredBank = payload.RootElement.GetProperty("bank_account_id").GetInt64();
                Assert.Equal("https://api.example.invalid/api/v1/payments/webhooks/sepay",
                    payload.RootElement.GetProperty("webhook_url").GetString());
                return FailWebhook ? Json(HttpStatusCode.BadGateway, "{}") : Json(HttpStatusCode.OK, """{"status":"success","data":{"id":44}}""");
            }
            throw new InvalidOperationException("Unexpected test provider request.");
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
