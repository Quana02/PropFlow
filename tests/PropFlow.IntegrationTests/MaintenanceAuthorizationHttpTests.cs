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
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-07")]
public sealed partial class MaintenanceAuthorizationHttpTests : IAsyncLifetime
{
    private const string Root = "/api/v1/maintenance/";
    private readonly AuthDatabaseFixture database;
    private readonly List<HttpClient> clients = [];
    private readonly EvidenceStorage storage = new();
    private readonly MaintenanceNotifications notifier = new();
    private readonly List<(HttpClient Client, HttpStatusCode Status)> managerDenials = [];
    private Actor manager = null!, staffA = null!, staffB = null!, unrelated = null!;
    private Facility facility = null!, otherFacility = null!;
    private Equipment equipment = null!, otherEquipment = null!;
    private DateTimeOffset Now => database.Factory.Clock.GetUtcNow();

    public MaintenanceAuthorizationHttpTests()
    {
        database = new() { ConfigureFixtureServices = ConfigureServices };
    }

    private void ConfigureServices(IServiceCollection services)
    {
        foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType is { } type && typeof(BackgroundService).IsAssignableFrom(type)).ToArray())
            services.Remove(worker);
        services.RemoveAll<IMaintenanceEvidenceStorage>();
        services.AddSingleton<IMaintenanceEvidenceStorage>(storage);
        services.RemoveAll<IMaintenanceNotifier>();
        services.AddSingleton<IMaintenanceNotifier>(notifier);
    }

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<MaintenanceDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<BillingDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
        await services.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        facility = new Facility("B4-MAIN", "Phòng máy", Now);
        otherFacility = new Facility("B4-OTHER", "Phòng kỹ thuật khác", Now);
        equipment = new Equipment("B4-PUMP", "Bơm nước", Now, facilityId: facility.Id);
        otherEquipment = new Equipment("B4-SPARE", "Bơm dự phòng", Now, facilityId: otherFacility.Id);
        var assets = services.GetRequiredService<PropertyAssetsDbContext>();
        assets.AddRange(facility, otherFacility, equipment, otherEquipment);
        await assets.SaveChangesAsync();

        manager = await Login("MANAGER");
        staffA = await Login("STAFF");
        staffB = await Login("STAFF");
        unrelated = await Login("STAFF");
        managerDenials.Add((staffA.Client, HttpStatusCode.Forbidden));
        foreach (var role in new[] { "ADMIN", "ACCOUNTANT", "RESIDENT" })
            managerDenials.Add(((await Login(role)).Client, HttpStatusCode.Forbidden));
        managerDenials.Add((Client(), HttpStatusCode.Unauthorized));
        managerDenials.Add(((await Login("MANAGER", revoked: true)).Client, HttpStatusCode.Unauthorized));
    }

    public async Task DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await database.DisposeAsync();
    }

    private HttpClient Client()
    {
        var client = database.Factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        clients.Add(client);
        return client;
    }

    private sealed record Actor(Guid Id, HttpClient Client);

    private async Task<Actor> Login(string role, bool revoked = false)
    {
        const string password = "Synthetic B4 maintenance password!";
        await using var scope = database.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var auth = services.GetRequiredService<AuthenticationDbContext>();
        var access = services.GetRequiredService<AdministrationDbContext>();
        var name = "b4_" + Guid.NewGuid().ToString("N");
        var user = new UserAccount(name, "pending", "Tài khoản thử B4", Now, name + "@example.invalid",
            status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(services.GetRequiredService<IAuthSecrets>().HashPassword(user, password), null, Now);
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        var roleId = await access.Roles.Where(row => row.Code == role).Select(row => row.Id).SingleAsync();
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, Now));
        await access.SaveChangesAsync();
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
        return new(user.Id, client);
    }

    private static async Task<HttpResponseMessage> AuthPost(HttpClient client, string operation, object body)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + operation) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, Root + route);
        if (body is not null) request.Content = body is HttpContent content ? content : JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<T> Call<T>(Actor actor, HttpMethod method, string route, object? body = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await Send(actor.Client, method, route, body);
        await ExpectStatus(response, status);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task Denied(HttpMethod method, string route, object? body = null)
    {
        var before = await Snapshot();
        foreach (var (client, status) in managerDenials)
        {
            using var response = await Send(client, method, route, body);
            await ExpectStatus(response, status);
        }
        Assert.Equal(before, await Snapshot());
    }

    private async Task Reject(Actor actor, HttpMethod method, string route, HttpStatusCode status, object? body = null)
    {
        var before = await Snapshot();
        using var response = await Send(actor.Client, method, route, body);
        await ExpectStatus(response, status);
        Assert.Equal(before, await Snapshot());
    }

    private static async Task ExpectStatus(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.True(response.StatusCode == status,
            $"{response.RequestMessage!.RequestUri!.AbsolutePath}: expected {(int)status}, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<string> Snapshot()
    {
        using var scope = database.Factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<MaintenanceDbContext>();
        var assets = services.GetRequiredService<PropertyAssetsDbContext>();
        return JsonSerializer.Serialize(new
        {
            Schedules = await db.MaintenanceSchedules.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Tasks = await db.MaintenanceTasks.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Assignments = await db.MaintenanceAssignments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Activities = await db.MaintenanceTaskActivities.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Results = await db.MaintenanceResults.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Evidence = await db.MaintenanceResultAttachments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Outbox = await db.ResidentEvents.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Facilities = await assets.Facilities.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Equipment = await assets.Equipment.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Requests = await services.GetRequiredService<ServiceRequestsDbContext>().ServiceRequests.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Invoices = await services.GetRequiredService<BillingDbContext>().Invoices.CountAsync(),
            Payments = await services.GetRequiredService<PaymentsDbContext>().Payments.CountAsync(),
            Notifications = await services.GetRequiredService<CommunicationDbContext>().Notifications.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Realtime = notifier.Events.ToArray(),
            Uploads = storage.Uploads.Keys.Order().ToArray(),
            Deletes = storage.Deletes.ToArray(),
            StorageReads = storage.Reads.ToArray()
        });
    }

    private static MultipartFormDataContent ResultForm()
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent("Đã kiểm tra và sửa máy bơm."), "summary");
        var image = new ByteArrayContent(EvidenceStorage.Image);
        image.Headers.ContentType = new("image/png");
        form.Add(image, "evidenceImages", "b4-fixture.png");
        return form;
    }

    private sealed class MaintenanceNotifications : IMaintenanceNotifier
    {
        public sealed record Event(Guid TaskId, Guid[] Recipients);
        public readonly ConcurrentQueue<Event> Events = new();

        public Task NotifyTaskUpdatedAsync(Guid taskId, IReadOnlyCollection<Guid> recipientStaffUserIds, CancellationToken ct = default)
        {
            Events.Enqueue(new(taskId, recipientStaffUserIds.Order().ToArray()));
            return Task.CompletedTask;
        }

        public Task NotifyAssetStatusChangedAsync(CancellationToken ct = default)
        {
            Events.Enqueue(new(Guid.Empty, []));
            return Task.CompletedTask;
        }
    }

    private sealed class EvidenceStorage : IMaintenanceEvidenceStorage
    {
        public static readonly byte[] Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aA1sAAAAASUVORK5CYII=");
        public readonly ConcurrentDictionary<string, byte[]> Uploads = new();
        public readonly ConcurrentQueue<string> Deletes = new();
        public readonly ConcurrentQueue<string> Reads = new();
        public Func<Task>? BeforeStoreReturns { get; set; }

        public async Task<StoredMaintenanceEvidence> StoreAsync(Guid resultId, MaintenanceEvidenceUpload upload, CancellationToken ct)
        {
            await using var read = await upload.OpenReadAsync(ct);
            using var buffer = new MemoryStream();
            await read.CopyToAsync(buffer, ct);
            Assert.Equal(Image, buffer.ToArray());
            var key = $"fixture/{resultId}/{Guid.NewGuid()}";
            Assert.True(Uploads.TryAdd(key, buffer.ToArray()));
            if (BeforeStoreReturns is { } beforeStoreReturns) await beforeStoreReturns();
            return new(key, upload.OriginalFileName, "image/png", buffer.Length);
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
        {
            Reads.Enqueue(storageKey);
            return Task.FromResult<Stream>(new MemoryStream(Uploads[storageKey], writable: false));
        }

        public Task DeleteAsync(string storageKey, CancellationToken ct)
        {
            Deletes.Enqueue(storageKey);
            Uploads.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }
    }
}
