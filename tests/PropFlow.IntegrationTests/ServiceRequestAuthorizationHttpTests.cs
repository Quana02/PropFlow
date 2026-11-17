using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PropFlow.Modules.Administration.Domain.UserRoleAssignments;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Application;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FE-05")]
public sealed partial class ServiceRequestAuthorizationHttpTests : IAsyncLifetime
{
    private readonly AuthDatabaseFixture database;
    private readonly List<HttpClient> clients = [];
    private readonly EvidenceStorage storage = new();
    private readonly RealtimeRecorder realtime = new();
    private WebApplicationFactory<Program> host = null!;
    private Actor manager = null!, staffA = null!, staffB = null!, unrelatedStaff = null!;
    private Actor residentA = null!, residentB = null!, admin = null!, accountant = null!;
    private RegistrationResidentSeed homeA = null!, homeB = null!;
    private readonly Dictionary<string, HttpClient> revoked = [];
    private HttpClient anonymous = null!;
    private DateTimeOffset Now => database.Factory.Clock.GetUtcNow();

    public ServiceRequestAuthorizationHttpTests()
    {
        database = new AuthDatabaseFixture { ConfigureFixtureServices = ConfigureServices };
    }

    private void ConfigureServices(IServiceCollection services)
    {
        foreach (var worker in services.Where(service => service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType is { } type && typeof(BackgroundService).IsAssignableFrom(type)).ToArray())
            services.Remove(worker);
        services.RemoveAll<IServiceRequestEvidenceStorage>();
        services.AddSingleton<IServiceRequestEvidenceStorage>(storage);
        services.RemoveAll<IServiceRequestRealtimeNotifier>();
        services.AddSingleton<IServiceRequestRealtimeNotifier>(realtime);
    }

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        host = database.Factory;
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AiClassificationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Database.MigrateAsync();
        anonymous = Client();
        manager = await Login("MANAGER");
        staffA = await Login("STAFF");
        staffB = await Login("STAFF");
        unrelatedStaff = await Login("STAFF");
        residentA = await Login("RESIDENT");
        residentB = await Login("RESIDENT");
        admin = await Login("ADMIN");
        accountant = await Login("ACCOUNTANT");
        homeA = await LinkResident(residentA);
        homeB = await LinkResident(residentB);
        foreach (var role in new[] { "MANAGER", "STAFF", "RESIDENT" })
            revoked.Add(role, (await Login(role, logout: true)).Client);
    }

    public async Task DisposeAsync()
    {
        foreach (var client in clients) client.Dispose();
        await database.DisposeAsync();
    }

    private HttpClient Client()
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        clients.Add(client);
        return client;
    }

    private sealed record Actor(Guid Id, string Role, HttpClient Client);

    private async Task<Actor> Login(string role, bool logout = false)
    {
        const string password = "Synthetic B3 request password!";
        using var scope = host.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticationDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
        var name = "b3_" + Guid.NewGuid().ToString("N");
        var user = new UserAccount(name, "pending", "Tài khoản fixture", Now, name + "@example.invalid",
            status: AccountStatus.ACTIVE, emailVerified: true);
        user.ChangePasswordHash(scope.ServiceProvider.GetRequiredService<IAuthSecrets>().HashPassword(user, password), null, Now);
        auth.UserAccounts.Add(user);
        await auth.SaveChangesAsync();
        var roleId = await access.Roles.Where(row => row.Code == role).Select(row => row.Id).SingleAsync();
        access.UserRoleAssignments.Add(new UserRoleAssignment(user.Id, roleId, Now));
        await access.SaveChangesAsync();
        var client = Client();
        using var login = await CookiePost(client, "login", new LoginRequest(name, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = (await login.Content.ReadFromJsonAsync<SessionResponse>())!;
        Assert.Equal(role, session.User.Role);
        if (logout)
        {
            using var response = await CookiePost(client, "logout", new { });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.AccessToken);
        return new(user.Id, role, client);
    }

    private static async Task<HttpResponseMessage> CookiePost(HttpClient client, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf"))!;
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/" + path) { Content = JsonContent.Create(body) };
        message.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await client.SendAsync(message);
    }

    private async Task<RegistrationResidentSeed> LinkResident(Actor resident)
    {
        var home = await database.SeedResidentAsync($"b3-{resident.Id:N}@example.invalid");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var row = await db.Residents.SingleAsync(x => x.Id == home.ResidentId);
        row.LinkUserAccount(resident.Id, resident.Id, Now);
        await db.SaveChangesAsync();
        return home;
    }

    private async Task<ServiceRequest> Seed(bool reviewed = false, bool secondHome = false)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var home = secondHome ? homeB : homeA;
        var owner = secondHome ? residentB : residentA;
        var request = new ServiceRequest("B3-" + Guid.NewGuid().ToString("N")[..16], home.ResidentId, home.ResidencyId!.Value,
            "Yêu cầu thử B3", "Cần kiểm tra đường ống trong căn hộ.", Now,
            apartmentUnitId: home.ApartmentId, serviceAreaCode: "IN_APARTMENT", submittedByUserId: owner.Id);
        if (reviewed) request.MarkUnderReview(Now);
        db.ServiceRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private static string Route(string role, Guid id, string operation) => $"/api/v1/{role}/service-requests/{id}/{operation}";
    private static HttpMethod Method(string operation) =>
        operation is "assignment" or "property-context" or "feedback" ? HttpMethod.Put : HttpMethod.Post;

    private static async Task<HttpResponseMessage> Send(HttpClient client, string role, Guid id, string operation, object? body)
    {
        using var message = new HttpRequestMessage(Method(operation), Route(role, id, operation));
        if (operation == "evidence")
        {
            var image = new ByteArrayContent(EvidenceStorage.Image);
            image.Headers.ContentType = new("image/png");
            var form = new MultipartFormDataContent();
            form.Add(image, "files", "b3-fixture.png");
            message.Content = form;
        }
        else if (body is not null)
            message.Content = JsonContent.Create(body);
        return await client.SendAsync(message);
    }

    private async Task<JsonElement> Call(Actor actor, Guid id, string operation, object? body = null)
    {
        using var response = await Send(actor.Client, actor.Role.ToLowerInvariant(), id, operation, body);
        var json = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{operation}: {(int)response.StatusCode} {json}");
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private async Task Denied(string role, Guid id, string operation, object? body = null)
    {
        var before = await Snapshot();
        var clientsToDeny = new[] { manager, staffA, residentA, admin, accountant }
            .Where(actor => actor.Role != role)
            .Select(actor => (actor.Client, HttpStatusCode.Forbidden))
            .Append((anonymous, HttpStatusCode.Unauthorized))
            .Append((revoked[role], HttpStatusCode.Unauthorized));
        foreach (var (client, status) in clientsToDeny)
            await Expect(client, role.ToLowerInvariant(), id, operation, body, status);
        Assert.Equal(before, await Snapshot());
    }

    private async Task Reject(Actor actor, Guid id, string operation, HttpStatusCode status, object? body = null)
    {
        var before = await Snapshot();
        await Expect(actor.Client, actor.Role.ToLowerInvariant(), id, operation, body, status);
        Assert.Equal(before, await Snapshot());
    }

    private static async Task Expect(HttpClient client, string role, Guid id, string operation, object? body, HttpStatusCode expected)
    {
        using var response = await Send(client, role, id, operation, body);
        Assert.True(response.StatusCode == expected,
            $"{role}/{operation}: expected {(int)expected}, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<ServiceRequest> Read(Guid id)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().ServiceRequests.AsNoTracking()
            .Include(row => row.Assignments).Include(row => row.Activities).SingleAsync(row => row.Id == id);
    }

    private async Task<string> Snapshot()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var communication = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        return JsonSerializer.Serialize(new
        {
            Requests = await db.ServiceRequests.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Assignments = await db.ServiceRequestAssignments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Activities = await db.ServiceRequestActivities.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Feedback = await db.ServiceRequestFeedbacks.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Evidence = await db.ServiceRequestEvidenceFiles.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Outbox = await db.ResidentEvents.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Invoices = await billing.Invoices.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Recipients = await billing.InvoiceRecipients.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Items = await billing.InvoiceItems.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Payment = await payments.Payments.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            PaymentHistory = await payments.PaymentStatusHistory.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Instructions = await payments.PaymentInstructions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Transfers = await payments.BankTransfers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Notices = await communication.Notifications.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Emails = await communication.FinanceEmails.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
            Realtime = realtime.Events.ToArray(),
            Uploads = storage.Uploads.ToArray(),
            Deletes = storage.Deletes.ToArray()
        });
    }

    private sealed class EvidenceStorage : IServiceRequestEvidenceStorage
    {
        public static readonly byte[] Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aA1sAAAAASUVORK5CYII=");
        public readonly ConcurrentQueue<string> Uploads = new();
        public readonly ConcurrentQueue<string> Deletes = new();
        public Func<Task>? BeforeReturn { get; set; }

        public async Task<StoredEvidence> SaveAsync(Guid id, string fileName, string contentType, long sizeBytes, Stream content, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);
            Assert.Equal(Image, buffer.ToArray());
            var key = $"fixture/{id}/{Guid.NewGuid()}";
            Uploads.Enqueue(key);
            if (BeforeReturn is { } callback) await callback();
            return new(key, sizeBytes);
        }

        public Task DeleteAsync(string key, CancellationToken ct)
        {
            Deletes.Enqueue(key);
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct) =>
            Task.FromResult<Stream?>(Uploads.Contains(key) && !Deletes.Contains(key) ? new MemoryStream(Image, writable: false) : null);
    }

    private sealed class RealtimeRecorder : IServiceRequestRealtimeNotifier
    {
        public sealed record Change(Guid RequestId, Guid? ResidentUserId);
        public readonly ConcurrentQueue<Change> Events = new();

        public Task NotifyChangedAsync(Guid id, Guid? residentUserId = null, CancellationToken ct = default)
        {
            Events.Enqueue(new(id, residentUserId));
            return Task.CompletedTask;
        }
    }
}
