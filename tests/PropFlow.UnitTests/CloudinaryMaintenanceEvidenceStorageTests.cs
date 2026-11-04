using System.Net;
using System.Text;
using CloudinaryDotNet;
using Microsoft.Extensions.Configuration;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Infrastructure.Storage;

namespace PropFlow.UnitTests;

public sealed class CloudinaryMaintenanceEvidenceStorageTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    [Fact]
    public async Task StoreAsync_UploadsAuthenticatedImage_AndKeepsSizeWhenSdkClosesStream()
    {
        var handler = new CaptureHandler();
        var storage = Create(handler);
        var saved = await storage.StoreAsync(Guid.NewGuid(), Upload(Jpeg.Length), CancellationToken.None);
        Assert.StartsWith("propflow/maintenance/evidence/", saved.StorageKey);
        Assert.Equal("result.jpg", saved.OriginalFileName);
        Assert.Equal("image/jpeg", saved.ContentType);
        Assert.Equal(Jpeg.Length, saved.SizeBytes);
        Assert.Contains("authenticated", handler.RequestBody);
        Assert.Contains("signature", handler.RequestBody);
        Assert.Equal("https://api.cloudinary.com/v1_1/propflow-test/image/upload", handler.RequestUri?.AbsoluteUri);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(13)]
    public async Task StoreAsync_RejectsFalseDeclaredSize_BeforeCallingProvider(int declared)
    {
        var handler = new CaptureHandler();
        await Assert.ThrowsAsync<ArgumentException>(() => Create(handler).StoreAsync(Guid.NewGuid(), Upload(declared), CancellationToken.None));
        Assert.Null(handler.RequestUri);
    }

    [Theory]
    [InlineData("propflow/maintenance/evidence/result/image", "/image/authenticated/")]
    [InlineData("cloudinary:propflow/maintenance-evidence/result/image", "/image/upload/")]
    public async Task OpenReadAsync_PreservesDeliveryType_ForNewAndLegacyResources(string key, string urlPart)
    {
        var handler = new CaptureHandler();
        await using var stream = await Create(handler).OpenReadAsync(key, CancellationToken.None);
        Assert.Contains(urlPart, handler.RequestUri!.AbsoluteUri);
        Assert.DoesNotContain("cloudinary:", handler.RequestUri.AbsoluteUri);
        Assert.Contains("/s--", handler.RequestUri.AbsoluteUri);
    }

    [Fact]
    public async Task DeleteAsync_LegacyCloudinary_UsesPublicUploadType()
    {
        var handler = new CaptureHandler();
        await Create(handler).DeleteAsync("cloudinary:propflow/maintenance-evidence/result/image", CancellationToken.None);
        Assert.Contains("propflow/maintenance-evidence/result/image", Uri.UnescapeDataString(handler.RequestBody));
        Assert.Contains("upload", handler.RequestBody);
        Assert.DoesNotContain("cloudinary:", Uri.UnescapeDataString(handler.RequestBody));
    }

    [Fact]
    public async Task ExistingLocalEvidence_RemainsReadableAndDeletable_WithoutProviderRequest()
    {
        var root = Path.Combine(Path.GetTempPath(), "propflow-evidence-test-" + Guid.NewGuid().ToString("N"));
        var key = Guid.NewGuid().ToString("N") + "/" + Guid.NewGuid().ToString("N") + ".jpg";
        var path = Path.Combine(root, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, Jpeg);
        try
        {
            var handler = new CaptureHandler();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MaintenanceEvidence:RootPath"] = root }).Build();
            var storage = Create(handler, config);
            await using (var stream = await storage.OpenReadAsync(key, CancellationToken.None))
            {
                using var image = new MemoryStream(); await stream.CopyToAsync(image);
                Assert.Equal(Jpeg, image.ToArray());
            }
            await storage.DeleteAsync(key, CancellationToken.None);
            Assert.False(File.Exists(path));
            Assert.Null(handler.RequestUri);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static MaintenanceEvidenceUpload Upload(long declared) => new("result.jpg", "image/jpeg", declared,
        _ => Task.FromResult<Stream>(new MemoryStream(Jpeg)));

    private static CloudinaryMaintenanceEvidenceStorage Create(CaptureHandler handler, IConfiguration? configuration = null)
    {
        var client = new HttpClient(handler);
        var cloudinary = new Cloudinary(new Account("propflow-test", "test-key", "test-secret"));
        cloudinary.Api.Client = client;
        return new(cloudinary, new ClientFactory(client), configuration);
    }
    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(
                "{\"public_id\":\"propflow/maintenance/evidence/result/image\",\"bytes\":12,\"format\":\"jpg\",\"result\":\"ok\"}", Encoding.UTF8, "application/json") };
        }
    }
}
