using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class PropertyOperationsAuthorizationHttpTests : IClassFixture<PropertyOperationsAuthorizationFixture>
{
    private readonly PropertyOperationsAuthorizationFixture fixture;
    private AuthDatabaseFixture Database => fixture.Database;
    private Actor Manager => fixture.Manager;
    private DateTimeOffset Now => Database.Factory.Clock.GetUtcNow();

    public PropertyOperationsAuthorizationHttpTests(PropertyOperationsAuthorizationFixture fixture) => this.fixture = fixture;

    public sealed record Actor(Guid Id, HttpClient Client, string Token);

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string route, object? input = null)
    {
        using var request = new HttpRequestMessage(method, "/api/v1/" + route);
        if (input is not null) request.Content = JsonContent.Create(input);
        return await client.SendAsync(request);
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.True(response.StatusCode == status,
            $"{response.RequestMessage!.RequestUri!.AbsolutePath}: expected {(int)status}, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task Check(HttpClient client, HttpMethod method, string route, object? input, HttpStatusCode status)
    {
        using var response = await Send(client, method, route, input);
        await Expect(response, status);
    }

    private async Task Denied(HttpMethod method, string route, object? input, Func<Task<string>> snapshot,
        IReadOnlyList<(HttpClient Client, HttpStatusCode Status)>? actors = null)
    {
        var before = await snapshot();
        foreach (var (client, status) in actors ?? fixture.ManagerDenials)
            await Check(client, method, route, input, status);
        Assert.Equal(before, await snapshot());
    }

    private async Task<string> ResidentSnapshot()
    {
        using var scope = Database.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var residents = services.GetRequiredService<ResidentsDbContext>();
        var apartments = services.GetRequiredService<ApartmentsDbContext>();
        var accounts = services.GetRequiredService<AuthenticationDbContext>();
        var administration = services.GetRequiredService<AdministrationDbContext>();
        return JsonSerializer.Serialize(new
        {
            Profiles = await residents.Residents.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Residencies = await residents.ResidentApartments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Ownerships = await apartments.ApartmentOwnerships.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Accounts = await accounts.UserAccounts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Username, x.Email, x.Status }).ToArrayAsync(),
            Roles = await administration.UserRoleAssignments.AsNoTracking().OrderBy(x => x.UserId)
                .Select(x => new { x.UserId, x.RoleId }).ToArrayAsync()
        });
    }
}

public sealed partial class PropertyOperationsAuthorizationFixture : IAsyncLifetime
{
    private readonly List<HttpClient> clients = [];
    private readonly Dictionary<string, PropertyOperationsAuthorizationHttpTests.Actor> actors = [];
    internal AuthDatabaseFixture Database { get; }
    internal PropertyOperationsAuthorizationHttpTests.Actor Manager => actors["MANAGER"];
    internal PropertyOperationsAuthorizationHttpTests.Actor Actor(string role) => actors[role];
    internal List<(HttpClient Client, HttpStatusCode Status)> ManagerDenials { get; } = [];

    public PropertyOperationsAuthorizationFixture()
    {
        Database = new() { ConfigureFixtureServices = ConfigureServices };
    }

    private void ConfigureServices(IServiceCollection services)
    {
        foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType is { } type && typeof(BackgroundService).IsAssignableFrom(type)).ToArray())
            services.Remove(worker);
        ConfigureProviders(services);
    }

    partial void ConfigureProviders(IServiceCollection services);

    public async Task InitializeAsync()
    {
        await Database.InitializeAsync();
        foreach (var role in new[] { "MANAGER", "STAFF", "ACCOUNTANT", "ADMIN", "RESIDENT" })
            actors.Add(role, await Login(role));
        ManagerDenials.Add((Client(), HttpStatusCode.Unauthorized));
        foreach (var role in new[] { "STAFF", "ACCOUNTANT", "ADMIN", "RESIDENT" })
            ManagerDenials.Add((Actor(role).Client, HttpStatusCode.Forbidden));
        ManagerDenials.Add(((await Login("MANAGER", revoked: true)).Client, HttpStatusCode.Unauthorized));
    }

    public async Task DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await Database.DisposeAsync();
    }

    internal HttpClient Client()
    {
        var client = Database.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        clients.Add(client);
        return client;
    }

    internal async Task<PropertyOperationsAuthorizationHttpTests.Actor> Login(string role, bool revoked = false, Guid? residentId = null)
    {
        const string password = "Synthetic B6 property operations password!";
        using var scope = Database.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var auth = services.GetRequiredService<AuthenticationDbContext>();
        var access = services.GetRequiredService<AdministrationDbContext>();
        var name = "b6_" + Guid.NewGuid().ToString("N");
        var now = Database.Factory.Clock.GetUtcNow();
        var user = new UserAccount(name, "pending", "Tài khoản thử B6", now, name + "@example.invalid",
            status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(services.GetRequiredService<IAuthSecrets>().HashPassword(user, password), null, now);
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        var roleId = await access.Roles.Where(x => x.Code == role).Select(x => x.Id).SingleAsync();
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, now));
        await access.SaveChangesAsync();
        if (residentId is { } id)
        {
            var residents = services.GetRequiredService<ResidentsDbContext>();
            var resident = await residents.Residents.SingleAsync(x => x.Id == id);
            resident.LinkUserAccount(user.Id, user.Id, now);
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
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + action) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }
}
