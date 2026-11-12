using Microsoft.AspNetCore.SignalR.Client;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text;
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
using Microsoft.Extensions.Hosting;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Complaints.Application;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Application.Announcements;
using PropFlow.Modules.Communication.Domain.Announcements;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;

namespace PropFlow.IntegrationTests;

// Real routing, authorization, use cases and EF persistence. Database transport and
// authoritative external-module read ports are replaced, never the workflow under test.
public sealed class ComplaintAnnouncementHttpTests
{
    private static readonly Guid Resident = Guid.NewGuid(), OtherResident = Guid.NewGuid(), Unverified = Guid.NewGuid(), Staff = Guid.NewGuid();
    private static readonly Guid ResidentId = Guid.NewGuid(), OtherResidentId = Guid.NewGuid(), Apartment = Guid.NewGuid();

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string database = Guid.NewGuid().ToString();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) =>
            {
                using var rsa = RSA.Create(2048);
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PropFlowDatabase"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                    ["Authentication:Issuer"] = "complaint-tests",
                    ["Authentication:Audience"] = "complaint-tests",
                    ["Authentication:PrivateKeyPem"] = rsa.ExportRSAPrivateKeyPem(),
                    ["Authentication:OtpHashKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Authentication:Mail:Host"] = "invalid.test",
                    ["Authentication:Mail:From"] = "test@example.invalid",
                    ["Logging:LogLevel:Default"] = "None"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // These HTTP tests must never start finance, email or maintenance workers.
                services.RemoveAll<IHostedService>();
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "ComplaintTest"; o.DefaultChallengeScheme = "ComplaintTest"; o.DefaultForbidScheme = "ComplaintTest"; })
                    .AddScheme<AuthenticationSchemeOptions, Authentication>("ComplaintTest", _ => { });
                services.RemoveAll<ComplaintsDbContext>(); services.RemoveAll<CommunicationDbContext>();
                services.AddScoped(_ => new ComplaintsDbContext(new DbContextOptionsBuilder<ComplaintsDbContext>().UseInMemoryDatabase(database + "complaints").Options));
                services.AddScoped(_ => new CommunicationDbContext(new DbContextOptionsBuilder<CommunicationDbContext>().UseInMemoryDatabase(database + "communication").Options));
                services.RemoveAll<IResidentResidenceSource>(); services.AddSingleton<IResidentResidenceSource, Residences>();
                services.RemoveAll<ICurrentBuildingTimeZone>(); services.AddSingleton<ICurrentBuildingTimeZone, Zone>();
                services.RemoveAll<IMaintenanceStaffDirectory>(); services.AddSingleton<IMaintenanceStaffDirectory, People>();
                services.RemoveAll<IComplaintContextSource>(); services.AddSingleton<IComplaintContextSource, Contexts>();
                services.RemoveAll<IComplaintCommitter>(); services.AddScoped<IComplaintCommitter, Committer>();
            });
        }
        public HttpClient Client(string role, Guid? actor = null, bool permission = true)
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            client.DefaultRequestHeaders.Add("X-Role", role);
            client.DefaultRequestHeaders.Add("X-Actor", (actor ?? Guid.NewGuid()).ToString());
            if (permission) client.DefaultRequestHeaders.Add("X-Permission", role == "MANAGER" ? "MANAGE_OPERATIONS" : "PERFORM_ASSIGNED_OPERATIONS");
            return client;
        }
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Role")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim> { new(ClaimTypes.Role, Request.Headers["X-Role"].ToString()), new("sub", Request.Headers["X-Actor"].ToString()) };
            if (Request.Headers.TryGetValue("X-Permission", out var p)) claims.Add(new("permission", p.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
    private sealed class Residences : IResidentResidenceSource
    {
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid userId, DateOnly date, CancellationToken ct) => Task.FromResult(
            userId == Resident ? new ActiveResidentResidence(ResidentId, ResidentId, Apartment) :
            userId == OtherResident ? new ActiveResidentResidence(OtherResidentId, OtherResidentId, Guid.NewGuid()) : null);
    }
    private sealed class Zone : ICurrentBuildingTimeZone { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh"); }
    private sealed class People : IMaintenanceStaffDirectory
    {
        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>([new(Staff, "staff", "Nhân viên")]);
        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid id, CancellationToken ct) => Task.FromResult(id == Staff ? new MaintenanceStaffRecord(Staff, "staff", "Nhân viên") : null);
    }
    private sealed class Contexts : IComplaintContextSource
    {
        public Task<bool> IsValidAsync(ComplaintContext context, CancellationToken ct) => Task.FromResult(context.FacilityId is null);
        public Task<ComplaintContextOptions> OptionsAsync(string? search, CancellationToken ct) => Task.FromResult(new ComplaintContextOptions([], [], [], []));
    }
    private sealed class Committer(ComplaintsDbContext db) : IComplaintCommitter
    {
        public async Task SaveAsync(ComplaintClosed? closed, CancellationToken ct) => await db.SaveChangesAsync(ct);
    }

    private static async Task<ComplaintDetail> Submit(HttpClient resident)
    {
        var response = await resident.PostAsJsonAsync("/api/v1/complaints", new { subject = "Phản ánh tiếng ồn", description = "Đề nghị Ban Quản lý xem xét." });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ComplaintDetail>())!;
    }

    [Fact, Trait("Feature", "FE-06.1–FE-06.4"), Trait("UseCase", "UC-21 / UC-22")]
    public async Task Resident_submits_without_service_request_and_cannot_read_another_residents_complaint()
    {
        await using var factory = new Factory();
        using var resident = factory.Client("RESIDENT", Resident);
        using var other = factory.Client("RESIDENT", OtherResident);
        using var unverified = factory.Client("RESIDENT", Unverified);
        var created = await Submit(resident);
        Assert.Null(created.Context.RelatedServiceRequestId);
        Assert.Equal(HttpStatusCode.OK, (await resident.GetAsync($"/api/v1/complaints/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/complaints/{created.Id}")).StatusCode);
        Assert.Equal(0, (await other.GetFromJsonAsync<ComplaintPage>("/api/v1/complaints"))!.Total);
        Assert.Single((await resident.GetFromJsonAsync<ComplaintPage>("/api/v1/complaints?search=tiếng&status=SUBMITTED"))!.Items);
        var decomposed = Uri.EscapeDataString("tiếng".Normalize(NormalizationForm.FormD));
        Assert.Single((await resident.GetFromJsonAsync<ComplaintPage>($"/api/v1/complaints?search={decomposed}"))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.PostAsJsonAsync("/api/v1/complaints", new { subject = "Title", description = "Description" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await resident.PostAsJsonAsync("/api/v1/complaints", new { subject = " ", description = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await resident.GetAsync("/api/v1/complaints?status=unknown")).StatusCode);
    }

    [Fact, Trait("Feature", "FE-06.5–FE-06.11"), Trait("UseCase", "UC-23 / UC-24")]
    public async Task Manager_owns_lifecycle_and_staff_only_updates_assigned_followup_without_leaking_internal_content()
    {
        await using var factory = new Factory();
        using var resident = factory.Client("RESIDENT", Resident);
        using var manager = factory.Client("MANAGER");
        using var staff = factory.Client("STAFF", Staff);
        using var stranger = factory.Client("STAFF");
        var complaint = await Submit(resident);
        var root = $"/api/v1/complaints/{complaint.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync(root + "/review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(root + "/context", new ComplaintContext(null, null, Guid.NewGuid(), null))).StatusCode);
        var assignedResponse = await manager.PostAsJsonAsync(root + "/followups", new { staffUserId = Staff, title = "Kiểm tra", instruction = "Ghi chú nội bộ bí mật" });
        Assert.Equal(HttpStatusCode.OK, assignedResponse.StatusCode);
        var assigned = (await assignedResponse.Content.ReadFromJsonAsync<ComplaintDetail>())!;
        var followup = Assert.Single(assigned.Followups);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(root)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PutAsJsonAsync(root + $"/followups/{followup.Id}", new { complete = true, result = "Kết quả" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync(root + "/close", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(root + "/resolve", new { response = "Không được phép" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(root + "/context", new ComplaintContext(null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsJsonAsync(root + "/resolve", new { response = "Chưa hoàn thành follow-up" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync(root + $"/followups/{followup.Id}", new { complete = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync(root + $"/followups/{followup.Id}", new { complete = true, result = " " })).StatusCode);
        var completedResponse = await staff.PutAsJsonAsync(root + $"/followups/{followup.Id}", new { complete = true, result = "Kết quả nội bộ bí mật" });
        Assert.Equal(HttpStatusCode.OK, completedResponse.StatusCode);
        var completed = (await completedResponse.Content.ReadFromJsonAsync<ComplaintDetail>())!;
        Assert.Equal("UNDER_REVIEW", completed.Status);
        Assert.Equal("COMPLETED", Assert.Single(completed.Followups).Status);
        Assert.Contains(completed.History, entry => entry.Detail == "Kết quả nội bộ bí mật" && entry.ActorLabel == "Bạn");
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(root + $"/followups/{followup.Id}", new { complete = true, result = "Trùng thao tác" })).StatusCode);
        var managerView = (await manager.GetFromJsonAsync<ComplaintDetail>(root))!;
        Assert.Equal("UNDER_REVIEW", managerView.Status);
        Assert.Contains(managerView.History, entry => entry.FromStatus == "FOLLOW_UP" && entry.ToStatus == "UNDER_REVIEW");
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync(root + "/resolve", new { response = "Ban Quản lý đã xử lý." })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync(root + "/close", null)).StatusCode);
        var visible = (await resident.GetFromJsonAsync<ComplaintDetail>(root))!;
        Assert.Equal("CLOSED", visible.Status);
        Assert.Equal("Ban Quản lý đã xử lý.", visible.OfficialResponse);
        Assert.Empty(visible.Followups);
        Assert.Contains(visible.History, x => x.Type == "STATUS_CHANGED" && x.ToStatus == "FOLLOW_UP");
        Assert.DoesNotContain("bí mật", await resident.GetStringAsync(root));
        Assert.Contains(visible.History, x => x.Type == "CLOSED");
        Assert.Equal("Phản ánh tiếng ồn", visible.Subject);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync(root + "/review", null)).StatusCode);
    }

    [Fact, Trait("Feature", "FE-06.9")]
    public async Task Manager_can_resolve_directly_without_staff()
    {
        await using var factory = new Factory();
        using var resident = factory.Client("RESIDENT", Resident);
        using var manager = factory.Client("MANAGER");
        var complaint = await Submit(resident);
        var root = $"/api/v1/complaints/{complaint.Id}";
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync(root + "/close", null)).StatusCode);
        await manager.PostAsync(root + "/review", null);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync(root + "/resolve", new { response = "Đã phản hồi trực tiếp." })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsync(root + "/close", null)).StatusCode);
    }

    [Fact, Trait("Feature", "FE-13.4–FE-13.8"), Trait("UseCase", "UC-47 / UC-48")]
    public async Task Manager_publishes_persistent_announcement_and_resident_can_read_but_cannot_publish()
    {
        await using var factory = new Factory();
        using var manager = factory.Client("MANAGER");
        using var resident = factory.Client("RESIDENT", Resident);
        using var unverified = factory.Client("RESIDENT", Unverified);
        var response = await manager.PostAsJsonAsync("/api/v1/announcements", new { title = "Cúp nước", content = "Thông báo thời gian tạm ngừng cấp nước." });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var posted = (await response.Content.ReadFromJsonAsync<AnnouncementView>())!;
        Assert.Equal(posted, await resident.GetFromJsonAsync<AnnouncementView>($"/api/v1/announcements/{posted.Id}"));
        Assert.Single((await resident.GetFromJsonAsync<AnnouncementPage>("/api/v1/announcements"))!.Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await resident.PostAsJsonAsync("/api/v1/announcements", new { title = "Không được", content = "Không được" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unverified.GetAsync("/api/v1/announcements")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Announcements.Include(a => a.Versions).Include(a => a.Audiences).SingleAsync();
        Assert.Equal(AnnouncementStatus.PUBLISHED, stored.Status);
        Assert.Single(stored.Versions);
        Assert.Equal(AnnouncementAudienceType.ALL_RESIDENTS, Assert.Single(stored.Audiences).AudienceType);
    }

    [Fact, Trait("Feature", "FE-13.8")]
    public async Task Resident_cannot_read_drafts_or_other_resident_and_apartment_audiences()
    {
        await using var factory = new Factory();
        using var resident = factory.Client("RESIDENT", Resident);
        var now = DateTimeOffset.UtcNow;
        var hidden = new Announcement("Riêng tư", "Không được hiển thị", Guid.NewGuid(), now);
        hidden.AddResidentAudience(OtherResidentId, now); hidden.Publish(Guid.NewGuid(), now);
        var otherApartment = new Announcement("Căn hộ khác", "Không được hiển thị", Guid.NewGuid(), now);
        otherApartment.AddApartmentAudience(Guid.NewGuid(), now); otherApartment.Publish(Guid.NewGuid(), now);
        var draft = new Announcement("Bản nháp", "Chưa đăng", Guid.NewGuid(), now);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
            db.AddRange(hidden, otherApartment, draft); await db.SaveChangesAsync();
        }
        foreach (var id in new[] { hidden.Id, otherApartment.Id, draft.Id })
            Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync($"/api/v1/announcements/{id}")).StatusCode);
        Assert.Empty((await resident.GetFromJsonAsync<AnnouncementPage>("/api/v1/announcements"))!.Items);
    }

    [Theory, InlineData("ADMIN"), InlineData("ACCOUNTANT"), InlineData("MANAGER"), InlineData("STAFF")]
    public async Task Wrong_roles_or_missing_permissions_are_denied(string role)
    {
        await using var factory = new Factory();
        using var client = factory.Client(role, permission: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/complaints")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/announcements", new { title = "Title", content = "Content" })).StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/complaints")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/announcements")).StatusCode);
    }
    [Fact]
    public async Task Publishing_invalidates_connected_resident_without_sending_private_content()
    {
        await using var factory = new Factory();
        using var manager = factory.Client("MANAGER");
        await using var connection = new Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder()
            .WithUrl("https://localhost/hubs/announcements", options =>
            {
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Headers["X-Role"] = "RESIDENT";
                options.Headers["X-Actor"] = Resident.ToString();
            }).Build();
        var received = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<Guid>("AnnouncementPublished", id => received.TrySetResult(id));
        await connection.StartAsync();
        var response = await manager.PostAsJsonAsync("/api/v1/announcements", new { title = "Thông báo realtime", content = "Nội dung mới" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(Guid.Empty, await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        using var resident = factory.Client("RESIDENT", Resident);
        Assert.Contains((await resident.GetFromJsonAsync<AnnouncementPage>("/api/v1/announcements"))!.Items, x => x.Title == "Thông báo realtime");
        await connection.StopAsync();
    }

    [Fact]
    public async Task Anonymous_cannot_connect_to_announcement_realtime()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/hubs/announcements/negotiate?negotiateVersion=1", null)).StatusCode);
    }}
