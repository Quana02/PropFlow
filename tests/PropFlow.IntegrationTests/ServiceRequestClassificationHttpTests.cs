using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Domain.Equipment;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Domain.Residents;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestEvidence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed class ServiceRequestClassificationHttpTests(AuthDatabaseFixture database) : IClassFixture<AuthDatabaseFixture>
{
    [Fact, Trait("Feature", "FE-05")]
    public async Task Evidence_requires_manager_or_assigned_staff_and_is_never_cached()
    {
        await using var baseFactory = Factory(new GeminiStub());
        var storage = new SyntheticEvidenceStorage();
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IServiceRequestEvidenceStorage>();
            services.AddSingleton<IServiceRequestEvidenceStorage>(storage);
        }));
        var now = DateTimeOffset.UtcNow;
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var resident = new Resident("EVIDENCE-TEST", "Cư dân kiểm thử", now);
        var request = new ServiceRequest("EVIDENCE-REQUEST", resident.Id, Guid.NewGuid(), "Ảnh kiểm thử", "Yêu cầu kiểm tra ảnh tổng hợp.", now);
        request.MarkUnderReview(now);
        request.MarkAssigned(now);
        var assignment = new ServiceRequestAssignment(request.Id, staffId, managerId, now);
        var activity = new ServiceRequestActivity(request.Id, ServiceActivityType.WORK_RESULT_ADDED, now, assignment.Id);
        var evidence = new ServiceRequestEvidenceFile(request.Id, activity.Id, "synthetic.png", "image/png",
            SyntheticEvidenceStorage.Image.Length, "cloudinary-authenticated:synthetic", staffId, now);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        await db.Database.MigrateAsync();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        residents.Residents.Add(resident);
        await residents.SaveChangesAsync();
        db.AddRange(request, assignment, activity, evidence);
        await db.SaveChangesAsync();

        var suffix = $"/service-requests/{request.Id}/evidence/{evidence.Id}";
        var managerPath = "/api/v1/manager" + suffix;
        var staffPath = "/api/v1/staff" + suffix;
        using var anonymous = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var residentClient = Client(factory, "RESIDENT", "USE_RESIDENT_SERVICES", ownerId);
        using var otherResident = Client(factory, "RESIDENT", "USE_RESIDENT_SERVICES", Guid.NewGuid());
        using var wrongStaff = Client(factory, "STAFF", "PERFORM_ASSIGNED_OPERATIONS", Guid.NewGuid());
        using var manager = Client(factory, "MANAGER", "MANAGE_OPERATIONS", managerId);
        using var staff = Client(factory, "STAFF", "PERFORM_ASSIGNED_OPERATIONS", staffId);
        foreach (var (client, path, status) in new[]
        {
            (anonymous, managerPath, HttpStatusCode.Unauthorized),
            (anonymous, staffPath, HttpStatusCode.Unauthorized),
            (residentClient, managerPath, HttpStatusCode.Forbidden),
            (otherResident, staffPath, HttpStatusCode.Forbidden),
            (wrongStaff, staffPath, HttpStatusCode.NotFound)
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(status, response.StatusCode);
        }
        Assert.Equal(0, storage.Reads);
        foreach (var (client, path) in new[] { (manager, managerPath), (staff, staffPath) })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Null(response.Headers.Location);
            Assert.Equal(SyntheticEvidenceStorage.Image, await response.Content.ReadAsByteArrayAsync());
        }

        assignment.EndAsReassigned(now.AddMinutes(1));
        await db.SaveChangesAsync();
        using var revoked = await staff.GetAsync(staffPath);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal(2, storage.Reads);
    }

    private sealed class SyntheticEvidenceStorage : IServiceRequestEvidenceStorage
    {
        public static readonly byte[] Image = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aG1sAAAAASUVORK5CYII=");
        public int Reads { get; private set; }

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
        {
            Assert.Equal("cloudinary-authenticated:synthetic", storageKey);
            Reads++;
            return Task.FromResult<Stream?>(new MemoryStream(Image));
        }

        public Task<StoredEvidence> SaveAsync(Guid id, string fileName, string contentType, long sizeBytes, Stream content, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task DeleteAsync(string storageKey, CancellationToken ct) => throw new NotSupportedException();
    }

    [Fact, Trait("Feature", "FE-05"), Trait("UseCase", "FE-05.1")]
    public async Task Resident_draft_review_is_scoped_advisory_and_handles_provider_failure()
    {
        var actor = Guid.NewGuid();
        var provider = new GeminiStub();
        await using var factory = Factory(provider).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IResidentResidenceSource>(_ => new SubmissionResidence(actor, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())))));
        using var resident = Client(factory, "RESIDENT", "", actor);
        const string path = "/api/v1/resident/service-requests/draft-check";
        using var checkedDraft = await resident.PostAsJsonAsync(path, new CheckResidentServiceRequestDraft("Tôi muốn đăng ký người viếng thăm"));
        var review = await Read<ResidentServiceRequestDraftReview>(checkedDraft);
        Assert.True(review.Available);
        Assert.Contains(review.MissingInformation, x => x.Code == "visitor_name");
        Assert.DoesNotContain(review.MissingInformation, x => x.Code is "appointment_date" or "appointment_time" or "visit_duration");
        Assert.Null(review.PreferredDate);
        Assert.Null(review.PreferredTimeCode);
        Assert.DoesNotContain(review.MissingInformation, x => x.Code == "vehicle_plate");
        using var outsider = Client(factory, "RESIDENT", "", Guid.NewGuid());
        using var denied = await outsider.PostAsJsonAsync(path, new CheckResidentServiceRequestDraft("Tôi muốn đăng ký xe"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var manager = Client(factory, "MANAGER", "MANAGE_OPERATIONS", Guid.NewGuid());
        using var wrongRole = await manager.PostAsJsonAsync(path, new CheckResidentServiceRequestDraft("Tôi muốn đăng ký xe"));
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);
        Assert.Equal(1, provider.Calls);
        provider.Status = HttpStatusCode.TooManyRequests;
        using var unavailable = await resident.PostAsJsonAsync(path, new CheckResidentServiceRequestDraft("Tôi muốn đăng ký xe"));
        Assert.False((await Read<ResidentServiceRequestDraftReview>(unavailable)).Available);
    }

    [Fact, Trait("Feature", "FE-05"), Trait("UseCase", "FE-05.1")]
    public async Task Resident_can_submit_description_only_over_http_before_manager_classifies_it()
    {
        var userId = Guid.NewGuid();
        var resident = new Resident("FE05-MINIMAL", "Cư dân kiểm thử", DateTimeOffset.UtcNow);
        var residence = new ActiveResidentResidence(resident.Id, Guid.NewGuid(), Guid.NewGuid());
        var provider = new GeminiStub();
        await using var factory = Factory(provider).WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IResidentResidenceSource>(_ => new SubmissionResidence(userId, residence))));
        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            await services.GetRequiredService<ServiceRequestsDbContext>().Database.MigrateAsync();
            await services.GetRequiredService<AiClassificationDbContext>().Database.MigrateAsync();
            var residents = services.GetRequiredService<ResidentsDbContext>();
            residents.Residents.Add(resident);
            await residents.SaveChangesAsync();
        }

        using var client = Client(factory, "RESIDENT", "", userId);
        const string description = "Vòi nước trong bếp bị rò từ sáng nay, nhờ kiểm tra giúp tôi.";
        using var submitted = await client.PostAsJsonAsync("/api/v1/resident/service-requests", new { description });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var created = await Read<ResidentServiceRequestCreatedResponse>(submitted);
        using (var scope = factory.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>()
                .ServiceRequests.SingleAsync(x => x.Id == created.Id);
            Assert.Equal(description, stored.Title);
            Assert.Equal(description, stored.Description);
            Assert.Equal(residence.ApartmentUnitId, stored.ApartmentUnitId);
            Assert.Null(stored.CategoryId);
            Assert.Null(stored.FinalPriorityCode);
            Assert.Null(stored.ServiceAreaCode);
        }
        Assert.Equal(0, provider.Calls);
        using var manager = Client(factory, "MANAGER", "MANAGE_OPERATIONS", Guid.NewGuid());
        using var analyzed = await manager.PostAsync($"/api/v1/manager/service-requests/{created.Id}/classification", null);
        Assert.Equal("SUCCESS", (await View(analyzed)).Suggestion!.Status);
    }

    private sealed class SubmissionResidence(Guid userId, ActiveResidentResidence residence) : IResidentResidenceSource
    {
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid actor, DateOnly date, CancellationToken ct) =>
            Task.FromResult(actor == userId ? (ActiveResidentResidence?)residence : null);
    }

    [Fact, Trait("Feature", "FE-10")]
    public async Task Manager_can_correct_persisted_suggestion_and_staff_reads_only_the_final_category()
    {
        var provider = new GeminiStub();
        await using var factory = Factory(provider);

        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var resident = new Resident("FE10-TEST", "Nguyễn Văn Mẫu", now);
        var request = new ServiceRequest("FE10-REQUEST", resident.Id, Guid.NewGuid(), "Vòi nước bị rò",
            "Nguyễn Văn Mẫu ở căn hộ B-409, điện thoại 0901234567, thư sample@example.invalid. Vòi nước bị rò.", now);
        request.MarkUnderReview(now);
        request.MarkAssigned(now);
        var unavailable = new ServiceRequest("FE10-FAIL", resident.Id, Guid.NewGuid(), "Kiểm tra vòi nước", "Vòi nước bị rò.", now);

        Guid repair;
        Guid cleaning;
        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var requests = services.GetRequiredService<ServiceRequestsDbContext>();
            await requests.Database.MigrateAsync();
            await services.GetRequiredService<AiClassificationDbContext>().Database.MigrateAsync();
            await services.GetRequiredService<AiRecommendationDbContext>().Database.MigrateAsync();
            repair = await requests.ServiceRequestCategories.Where(x => x.Code == "REPAIR").Select(x => x.Id).SingleAsync();
            cleaning = await requests.ServiceRequestCategories.Where(x => x.Code == "CLEANING").Select(x => x.Id).SingleAsync();
            var residents = services.GetRequiredService<ResidentsDbContext>();
            residents.Residents.Add(resident);
            await residents.SaveChangesAsync();
            requests.ServiceRequests.AddRange(request, unavailable);
            requests.ServiceRequestAssignments.Add(new(request.Id, staffId, managerId, now));
            await requests.SaveChangesAsync();
        }

        using var manager = Client(factory, "MANAGER", "MANAGE_OPERATIONS", managerId);
        using var staff = Client(factory, "STAFF", "PERFORM_ASSIGNED_OPERATIONS", staffId);
        var path = $"/api/v1/manager/service-requests/{request.Id}/classification";
        using var forbidden = await staff.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(0, provider.Calls);
        using var analyzed = await manager.PostAsync(path, null);
        var suggestion = (await View(analyzed)).Suggestion!;
        Assert.Equal(repair, suggestion.SuggestedCategoryId);
        Assert.Null(suggestion.FinalCategoryId);
        Assert.DoesNotContain("Nguyễn Văn Mẫu", provider.Description);
        Assert.DoesNotContain("B-409", provider.Description);
        Assert.DoesNotContain("0901234567", provider.Description);
        Assert.DoesNotContain("sample@example.invalid", provider.Description);

        using var retry = await manager.PostAsync(path, null);
        Assert.Equal(suggestion.Id, (await View(retry)).Suggestion!.Id);
        Assert.Equal(1, provider.Calls);
        using var before = await staff.GetAsync($"/api/v1/staff/service-requests/{request.RequestNumber}");
        Assert.Null((await Read<StaffServiceRequestDetailResponse>(before)).CategoryName);
        using var reviewed = await manager.PutAsJsonAsync(path + "/review", new ReviewManagerServiceRequestClassification(suggestion.Id, cleaning));
        var final = (await View(reviewed)).Suggestion!;
        Assert.Equal("CORRECTED", final.ReviewDecision);
        Assert.Equal(repair, final.SuggestedCategoryId);
        Assert.Equal(cleaning, final.FinalCategoryId);
        using var after = await staff.GetAsync($"/api/v1/staff/service-requests/{request.RequestNumber}");
        Assert.Equal("Dịch vụ vệ sinh", (await Read<StaffServiceRequestDetailResponse>(after)).CategoryName);

        provider.Status = HttpStatusCode.TooManyRequests;
        using var failed = await manager.PostAsync($"/api/v1/manager/service-requests/{unavailable.Id}/classification", null);
        var failure = await View(failed);
        Assert.Equal("FAILED", failure.Suggestion!.Status);
        Assert.False(failure.CanReview);
        Assert.True(failure.CanAnalyze);

        using var verification = factory.Services.CreateScope();
        var storedRequests = verification.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>().ServiceRequests.AsNoTracking();
        var stored = await storedRequests.SingleAsync(x => x.Id == unavailable.Id);
        Assert.Equal(ServiceRequestStatus.SUBMITTED, stored.Status);
        Assert.Null(stored.CategoryId);

        provider.Status = HttpStatusCode.OK;
        var failedPath = $"/api/v1/manager/service-requests/{unavailable.Id}/classification";
        using var reanalyzed = await manager.PostAsync(failedPath, null);
        var next = (await View(reanalyzed)).Suggestion!;
        Assert.NotEqual(failure.Suggestion.Id, next.Id);
        using var confirmed = await manager.PutAsJsonAsync(failedPath + "/review", new ReviewManagerServiceRequestClassification(next.Id, repair));
        Assert.Equal("CONFIRMED", (await View(confirmed)).Suggestion!.ReviewDecision);
        Assert.Equal(repair, await storedRequests.Where(x => x.Id == unavailable.Id).Select(x => x.CategoryId).SingleAsync());
    }

    [Fact, Trait("Feature", "FE-11")]
    public async Task Recommendation_uses_linked_equipment_and_shares_only_the_reviewed_plan_with_assigned_staff()
    {
        var provider = new GeminiStub();
        await using var factory = Factory(provider);
        var managerId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var resident = new Resident("FE11-TEST", "Nguyễn Văn Kiểm Thử", now);
        var equipment = new Equipment("FE11-PUMP", "Bơm cấp nước", now);
        var pending = new ServiceRequest("FE11-FAIL", resident.Id, Guid.NewGuid(), "Kiểm tra bơm", "Nước cấp bị yếu.", now);
        ServiceRequest request;
        using (var scope = factory.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var requests = services.GetRequiredService<ServiceRequestsDbContext>();
            await requests.Database.MigrateAsync();
            await services.GetRequiredService<AiRecommendationDbContext>().Database.MigrateAsync();
            var repair = await requests.ServiceRequestCategories.Where(x => x.Code == "REPAIR").Select(x => x.Id).SingleAsync();
            var residents = services.GetRequiredService<ResidentsDbContext>();
            residents.Residents.Add(resident);
            await residents.SaveChangesAsync();
            var assets = services.GetRequiredService<PropertyAssetsDbContext>();
            assets.Equipment.Add(equipment);
            await assets.SaveChangesAsync();
            request = new("FE11-REQUEST", resident.Id, Guid.NewGuid(), "Bơm cấp nước hoạt động bất thường", "Cư dân ở căn hộ B-409 báo nước yếu.", now,
                categoryId: repair, equipmentId: equipment.Id, finalPriorityCode: "NORMAL");
            request.MarkUnderReview(now);
            request.MarkAssigned(now);
            requests.ServiceRequests.AddRange(request, pending);
            requests.ServiceRequestAssignments.Add(new(request.Id, staffId, managerId, now));
            await requests.SaveChangesAsync();
        }

        using var manager = Client(factory, "MANAGER", "MANAGE_OPERATIONS", managerId);
        using var staff = Client(factory, "STAFF", "PERFORM_ASSIGNED_OPERATIONS", staffId);
        var path = $"/api/v1/manager/service-requests/{request.Id}/recommendation";
        var staffPath = $"/api/v1/staff/service-requests/{request.RequestNumber}";
        using var denied = await staff.PostAsync(path, null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var analyzed = await manager.PostAsync(path, null);
        var suggested = (await Read<ManagerServiceRequestRecommendationView>(analyzed)).Suggestion!;
        Assert.Equal("URGENT", suggested.Proposed!.PriorityCode);
        Assert.Equal("Bơm cấp nước", provider.Equipment);
        Assert.Equal("Sửa chữa kỹ thuật", provider.Category);
        Assert.DoesNotContain("B-409", provider.Description);
        using var before = await staff.GetAsync(staffPath);
        var unreviewed = await Read<StaffServiceRequestDetailResponse>(before);
        Assert.Equal("NORMAL", unreviewed.PriorityCode);
        Assert.Null(unreviewed.ApprovedRecommendation);

        var correction = new ReviewManagerServiceRequestRecommendation(suggested.Id, "HIGH", true, "Kiểm tra bơm và đường cấp nước", "Kỹ thuật cấp thoát nước");
        using var missingNote = await manager.PutAsJsonAsync(path + "/review", correction);
        Assert.Equal(HttpStatusCode.BadRequest, missingNote.StatusCode);
        using var reviewed = await manager.PutAsJsonAsync(path + "/review", correction with { ReviewNote = "Mức ảnh hưởng đã được BQL xác minh." });
        var approved = await Read<ManagerServiceRequestRecommendationView>(reviewed);
        Assert.Equal("CORRECTED", approved.Suggestion!.ReviewDecision);
        Assert.Equal("URGENT", approved.Suggestion.Proposed!.PriorityCode);
        Assert.Equal("HIGH", approved.CurrentPriority);
        using var after = await staff.GetAsync(staffPath);
        var assigned = await Read<StaffServiceRequestDetailResponse>(after);
        Assert.Equal("ASSIGNED", assigned.Status);
        Assert.Equal("HIGH", assigned.PriorityCode);
        Assert.Equal(correction.Action, assigned.ApprovedRecommendation!.Action);

        provider.Status = HttpStatusCode.TooManyRequests;
        var pendingPath = $"/api/v1/manager/service-requests/{pending.Id}/recommendation";
        using var failed = await manager.PostAsync(pendingPath, null);
        var failure = await Read<ManagerServiceRequestRecommendationView>(failed);
        Assert.Equal("FAILED", failure.Suggestion!.Status);
        Assert.True(failure.CanSetPriority);
        using var manual = await manager.PutAsJsonAsync(pendingPath + "/review", new ReviewManagerServiceRequestRecommendation(null, "HIGH"));
        var fallback = await Read<ManagerServiceRequestRecommendationView>(manual);
        Assert.Equal("HIGH", fallback.CurrentPriority);
        Assert.Null(fallback.Suggestion!.Approved);
    }

    private WebApplicationFactory<Program> Factory(GeminiStub provider) => database.Factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiChatbot:Gemini:Enabled"] = "true", ["AiChatbot:Gemini:ApiKey"] = "test-only",
            ["Cloudinary:CloudName"] = "test-only", ["Cloudinary:ApiKey"] = "test-only", ["Cloudinary:ApiSecret"] = "test-only"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "ServiceRequestAiTest";
                options.DefaultChallengeScheme = "ServiceRequestAiTest";
                options.DefaultForbidScheme = "ServiceRequestAiTest";
            }).AddScheme<AuthenticationSchemeOptions, TestAuth>("ServiceRequestAiTest", _ => { });
            services.AddHttpClient("ServiceRequestGemini").ConfigurePrimaryHttpMessageHandler(() => provider);
        });
    });

    private static Task<ManagerServiceRequestClassificationView> View(HttpResponseMessage response) =>
        Read<ManagerServiceRequestClassificationView>(response);

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static HttpClient Client(WebApplicationFactory<Program> factory, string role, string permission, Guid actor)
    {
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permission", permission);
        client.DefaultRequestHeaders.Add("X-Test-Actor", actor.ToString());
        return client;
    }

    private sealed class GeminiStub : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public int Calls { get; private set; }
        public string Description { get; private set; } = "";
        public string? Category { get; private set; }
        public string? Equipment { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var properties = body.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("properties");
            if (properties.TryGetProperty("missingFields", out _))
                return new(Status) { Content = JsonContent.Create(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[]
                { new { text = """{"requestKind":"VISITOR","missingFields":["visitor_name","visit_duration","appointment_date","appointment_time"],"preferredDate":null,"preferredTimeCode":null,"dateEvidence":null,"timeEvidence":null}""" } } } } } }) };
            var isRecommendation = properties.TryGetProperty("priorityCode", out var choiceField);
            if (!isRecommendation) choiceField = properties.GetProperty("categoryCode");
            var choices = choiceField.GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()).ToArray();
            Assert.Contains(isRecommendation ? "URGENT" : "REPAIR", choices);
            Assert.DoesNotContain("", choices);
            using var data = JsonDocument.Parse(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
            Description = data.RootElement.GetProperty("description").GetString()!;
            if (isRecommendation)
            {
                Category = data.RootElement.GetProperty("category").GetString();
                Equipment = data.RootElement.GetProperty("equipment").GetString();
            }
            var output = isRecommendation
                ? """{"priorityCode":"URGENT","maintenanceRecommended":true,"action":"Kiểm tra bơm cấp nước","resources":"Nhân sự kỹ thuật","reason":"Có phản ánh nước cấp yếu."}"""
                : """{"categoryCode":"REPAIR","reason":"Phiếu mô tả vòi nước bị rò."}""";
            return new HttpResponseMessage(Status)
            {
                Content = JsonContent.Create(new { candidates = new[] { new { finishReason = "STOP", content = new
                    { parts = new[] { new { text = output } } } } },
                    usageMetadata = new { promptTokenCount = 15, candidatesTokenCount = 10, totalTokenCount = 25 } })
            };
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var user = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.Role, role.ToString()),
                new("permission", Request.Headers["X-Test-Permission"].ToString()), new("sub", Request.Headers["X-Test-Actor"].ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new(user, Scheme.Name)));
        }
    }
}
