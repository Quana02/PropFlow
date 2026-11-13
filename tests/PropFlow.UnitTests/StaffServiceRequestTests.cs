using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestActivities;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestAssignments;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequestEvidence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Shared;
using PropFlow.Web.Client.Features.ServiceRequests.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.10-FE-05.12")]
public sealed class StaffServiceRequestTests
{
    [Fact]
    public async Task List_ReturnsOnlyRequestsAssignedToCurrentStaff()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 13, 1, 0, 0, TimeSpan.Zero);
        var staffId = Guid.NewGuid();
        var own = AddAssigned(db, staffId, now, "SR-OWN-001");
        AddAssigned(db, Guid.NewGuid(), now.AddMinutes(1), "SR-OTHER-001");
        await db.SaveChangesAsync();

        var page = await new StaffServiceRequestStore(db).ListAsync(
            staffId, new StaffServiceRequestListQuery(Page: 1, PageSize: 10), CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(own.Id, item.Id);
        Assert.Equal(1, page.Summary.Total);
        Assert.Equal(1, page.Summary.Assigned);
    }

    [Fact]
    public async Task AcceptAndComplete_AdvanceWorkflowAndWriteAuditActivities()
    {
        await using var db = CreateDb();
        var now = new DateTimeOffset(2026, 10, 13, 2, 0, 0, TimeSpan.Zero);
        var staffId = Guid.NewGuid();
        var request = AddAssigned(db, staffId, now, "SR-WORK-001");
        await db.SaveChangesAsync();
        var store = new StaffServiceRequestStore(db);

        var accepted = await store.AcceptAsync(staffId, request.Id, now.AddMinutes(1), CancellationToken.None);
        var progressed = await store.AddProgressAsync(staffId, request.Id, "Đã kiểm tra hiện trường.", now.AddMinutes(2), CancellationToken.None);
        var completed = await store.CompleteWorkAsync(staffId, request.Id, "Đã xử lý xong và vận hành ổn định.", now.AddMinutes(3), CancellationToken.None);

        Assert.Equal(StaffServiceRequestOutcome.Success, accepted);
        Assert.Equal(StaffServiceRequestOutcome.Success, progressed);
        Assert.Equal(StaffServiceRequestOutcome.Success, completed);
        Assert.Equal(ServiceRequestStatus.IN_PROGRESS, request.Status);
        Assert.Contains(await db.ServiceRequestActivities.ToListAsync(), x => x.ActivityType == ServiceActivityType.STATUS_CHANGED && x.PerformedBy == staffId);
        Assert.Contains(await db.ServiceRequestActivities.ToListAsync(), x => x.ActivityType == ServiceActivityType.PROGRESS_UPDATED && x.Detail == "Đã kiểm tra hiện trường.");
        Assert.Contains(await db.ServiceRequestActivities.ToListAsync(), x => x.ActivityType == ServiceActivityType.WORK_RESULT_ADDED && x.WorkResult == "Đã xử lý xong và vận hành ổn định.");
    }

    [Fact]
    public async Task Accept_ReturnsNotFound_WhenRequestBelongsToAnotherStaff()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var request = AddAssigned(db, Guid.NewGuid(), now, "SR-PRIVATE-001");
        await db.SaveChangesAsync();

        var outcome = await new StaffServiceRequestStore(db).AcceptAsync(
            Guid.NewGuid(), request.Id, now.AddMinutes(1), CancellationToken.None);

        Assert.Equal(StaffServiceRequestOutcome.NotFound, outcome);
        Assert.Equal(ServiceRequestStatus.ASSIGNED, request.Status);
    }

    [Fact]
    public async Task EvidenceLookup_RequiresTheRightRequestAndCurrentAssignment()
    {
        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var staffId = Guid.NewGuid();
        var request = AddAssigned(db, staffId, now, "SR-EVIDENCE-001");
        var otherRequest = AddAssigned(db, staffId, now, "SR-EVIDENCE-002");
        var assignment = db.ServiceRequestAssignments.Local.Single(x => x.ServiceRequestId == request.Id);
        var activity = new ServiceRequestActivity(request.Id, ServiceActivityType.WORK_RESULT_ADDED, now, assignment.Id);
        var evidence = new ServiceRequestEvidenceFile(request.Id, activity.Id, "photo.jpg", "image/jpeg", 12,
            "cloudinary-authenticated:synthetic-asset", staffId, now);
        db.AddRange(activity, evidence);
        await db.SaveChangesAsync();
        var store = new StaffServiceRequestStore(db);

        Assert.NotNull(await store.EvidenceFileAsync(staffId, request.Id, evidence.Id, default));
        Assert.Null(await store.EvidenceFileAsync(staffId, otherRequest.Id, evidence.Id, default));
        Assert.Null(await store.EvidenceFileAsync(Guid.NewGuid(), request.Id, evidence.Id, default));

        assignment.EndAsReassigned(now.AddMinutes(1));
        await db.SaveChangesAsync();
        Assert.Null(await store.EvidenceFileAsync(staffId, request.Id, evidence.Id, default));
    }

    [Fact]
    public async Task ListApi_EncodesRequestCodeStatusAndPagination()
    {
        var handler = new CapturingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://propflow.test/") };
        var client = new StaffServiceRequestApiClient(
            new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance));

        var result = await client.ListAsync(new StaffServiceRequestListQuery("SR 001/26", "IN_PROGRESS", 2, 10));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "/api/v1/staff/service-requests?page=2&pageSize=10&search=SR%20001%2F26&status=IN_PROGRESS",
            handler.RequestUri?.PathAndQuery);
    }

    [Fact]
    public async Task EvidenceStorage_SavesValidJpeg_WithoutMovingAnOpenFile()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"propflow-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var storage = new LocalServiceRequestEvidenceStorage(new TestWebHostEnvironment(contentRoot));
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
            await using var content = new MemoryStream(jpeg);

            var saved = await storage.SaveAsync(
                Guid.NewGuid(), "evidence.jpg", "image/jpeg", jpeg.Length, content, CancellationToken.None);

            var savedPath = Path.Combine(contentRoot, "App_Data", "service-request-evidence",
                saved.StorageKey.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(savedPath));
            Assert.Equal(jpeg.Length, new FileInfo(savedPath).Length);
        }
        finally
        {
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CloudinaryEvidenceStorage_RejectsInvalidContentAndOversizeBeforeNetwork()
    {
        var handler = new CloudinaryUploadHandler();
        using var client = new HttpClient(handler);
        var storage = new CloudinaryServiceRequestEvidenceStorage(client, Options.Create(new CloudinaryOptions
        {
            CloudName = "propflow-test", ApiKey = "test-key", ApiSecret = "test-secret"
        }), new LocalServiceRequestEvidenceStorage(new TestWebHostEnvironment(Path.GetTempPath())));
        using var content = new MemoryStream([0, 1, 2]);
        var requestId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(requestId, "bad.png", "image/png", 3, content, default));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(requestId, "bad.png", "image/png", 10 * 1024 * 1024 + 1, content, default));
        Assert.Null(handler.RequestUri);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            storage.SaveAsync(requestId, "bad.png", "image/png", 3, content, cancelled.Token));
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task CloudinaryEvidenceStorage_UploadsToCloudWithoutCreatingProjectFile()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"propflow-cloud-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);
        try
        {
            var handler = new CloudinaryUploadHandler();
            var legacy = new LocalServiceRequestEvidenceStorage(new TestWebHostEnvironment(contentRoot));
            var storage = new CloudinaryServiceRequestEvidenceStorage(
                new HttpClient(handler),
                Options.Create(new CloudinaryOptions
                {
                    CloudName = "propflow-test",
                    ApiKey = "test-key",
                    ApiSecret = "test-secret"
                }),
                legacy);
            var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
            await using var content = new MemoryStream(jpeg);

            var saved = await storage.SaveAsync(
                Guid.NewGuid(), "evidence.jpg", "image/jpeg", jpeg.Length, content, CancellationToken.None);

            Assert.Equal("cloudinary-authenticated:synthetic-asset", saved.StorageKey);
            Assert.Equal(jpeg.Length, saved.SizeBytes);
            Assert.Equal("https://api.cloudinary.com/v1_1/propflow-test/image/upload", handler.RequestUri?.AbsoluteUri);
            Assert.Matches("name=\"?signature\"?(?:\\r\\n|;)", handler.RequestBody);
            Assert.Matches("name=\"?type\"?\\r\\n\\r\\nauthenticated", handler.RequestBody);
            Assert.False(Directory.Exists(Path.Combine(contentRoot, "App_Data", "service-request-evidence")));

            await using var downloaded = await storage.OpenReadAsync(saved.StorageKey, default);
            Assert.NotNull(downloaded);
            Assert.Equal("/v1_1/propflow-test/asset/download", handler.RequestUri?.AbsolutePath);
            Assert.Equal(string.Empty, handler.RequestUri!.UserInfo);
            Assert.Contains("asset_id=synthetic-asset", handler.RequestBody);
            Assert.Contains("signature=", handler.RequestBody);

            await storage.DeleteAsync(saved.StorageKey, default);
            Assert.Equal("/v1_1/propflow-test/asset/destroy", handler.RequestUri?.AbsolutePath);
            Assert.Contains("asset_id=synthetic-asset", handler.RequestBody);
            Assert.Contains("invalidate=true", handler.RequestBody);

            await using var existing = await storage.OpenReadAsync("cloudinary:legacy/request/image", default);
            Assert.NotNull(existing);
            Assert.Equal("/propflow-test/image/upload/legacy/request/image", handler.RequestUri?.AbsolutePath);
        }
        finally
        {
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static ServiceRequestsDbContext CreateDb() => new(new DbContextOptionsBuilder<ServiceRequestsDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static ServiceRequest AddAssigned(
        ServiceRequestsDbContext db,
        Guid staffId,
        DateTimeOffset now,
        string requestNumber)
    {
        var request = new ServiceRequest(
            requestNumber, Guid.NewGuid(), Guid.NewGuid(),
            "Kiểm tra hệ thống kỹ thuật", "Mô tả yêu cầu đủ dài để Staff thực hiện.", now);
        request.MarkUnderReview(now.AddSeconds(1));
        request.MarkAssigned(now.AddSeconds(2));
        db.AddRange(request, new ServiceRequestAssignment(request.Id, staffId, Guid.NewGuid(), now.AddSeconds(2)));
        return request;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            const string body = "{\"items\":[],\"totalCount\":0,\"page\":2,\"pageSize\":10,\"summary\":{\"total\":0,\"assigned\":0,\"inProgress\":0,\"resolved\":0}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CloudinaryUploadHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (RequestUri!.AbsolutePath.EndsWith("/destroy"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":\"ok\"}") };
            if (RequestUri.AbsolutePath.EndsWith("/download") || request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) };
            var id = System.Text.RegularExpressions.Regex.Match(RequestBody, "name=\"?public_id\"?\\r\\n\\r\\n([^\\r\\n]+)").Groups[1].Value;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new
                {
                    asset_id = "synthetic-asset", public_id = id, type = "authenticated", bytes = 12
                })
            };
        }
    }

    private sealed class TestWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "PropFlow.UnitTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
