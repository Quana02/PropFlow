using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Infrastructure.Storage;
using PropFlow.Shared;

namespace PropFlow.UnitTests;

public sealed class CloudinaryMaintenanceEvidenceStorageTests
{
    [Fact]
    public async Task StoreAsync_UploadsValidatedImage_AndReturnsCloudinaryStorageKey()
    {
        var handler = new UploadHandler();
        var legacy = new LocalMaintenanceEvidenceStorage(new ConfigurationBuilder().Build());
        var storage = new CloudinaryMaintenanceEvidenceStorage(
            new HttpClient(handler),
            Options.Create(new CloudinaryOptions
            {
                CloudName = "propflow-test",
                ApiKey = "test-key",
                ApiSecret = "test-secret"
            }),
            legacy);
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };

        var saved = await storage.StoreAsync(
            Guid.NewGuid(),
            new MaintenanceEvidenceUpload(
                "result.jpg",
                "image/jpeg",
                jpeg.Length,
                _ => Task.FromResult<Stream>(new MemoryStream(jpeg))),
            CancellationToken.None);

        Assert.StartsWith("cloudinary:propflow/maintenance-evidence/", saved.StorageKey);
        Assert.Equal("result.jpg", saved.OriginalFileName);
        Assert.Equal("image/jpeg", saved.ContentType);
        Assert.Equal(jpeg.Length, saved.SizeBytes);
        Assert.Equal("https://api.cloudinary.com/v1_1/propflow-test/image/upload", handler.RequestUri?.AbsoluteUri);
        Assert.Contains("name=\"signature\"", handler.RequestBody);
    }

    private sealed class UploadHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"public_id\":\"propflow/maintenance-evidence/result/image\",\"bytes\":12}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
