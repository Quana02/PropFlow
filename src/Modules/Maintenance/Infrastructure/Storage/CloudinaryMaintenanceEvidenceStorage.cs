using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Shared;

namespace PropFlow.Modules.Maintenance.Infrastructure.Storage;

public sealed class CloudinaryMaintenanceEvidenceStorage(
    HttpClient http,
    IOptions<CloudinaryOptions> configuredOptions,
    LocalMaintenanceEvidenceStorage legacyStorage) : IMaintenanceEvidenceStorage
{
    private const string StoragePrefix = "cloudinary:";
    private readonly CloudinaryOptions options = configuredOptions.Value;

    public async Task<StoredMaintenanceEvidence> StoreAsync(
        Guid resultId,
        MaintenanceEvidenceUpload upload,
        CancellationToken ct)
    {
        EnsureConfigured();
        if (upload.Length is <= 0 or > LocalMaintenanceEvidenceStorage.MaxImageBytes)
            throw new ArgumentException("Mỗi ảnh minh chứng phải có dung lượng từ 1 byte đến 5 MB.");

        await using var source = await upload.OpenReadAsync(ct);
        using var image = await ReadValidatedImageAsync(source, upload.Length, ct);
        var contentType = DetectContentType(image.GetBuffer().AsSpan(0, (int)image.Length));
        image.Position = 0;

        var folder = options.MaintenanceFolder.Trim().Trim('/');
        var publicId = string.IsNullOrEmpty(folder)
            ? $"{resultId:N}/{Guid.NewGuid():N}"
            : $"{folder}/{resultId:N}/{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Sign($"public_id={publicId}&timestamp={timestamp}");

        using var form = new MultipartFormDataContent();
        using var imageContent = new StreamContent(image);
        imageContent.Headers.ContentType = new(contentType);
        form.Add(imageContent, "file", Path.GetFileName(upload.OriginalFileName));
        form.Add(new StringContent(options.ApiKey), "api_key");
        form.Add(new StringContent(timestamp), "timestamp");
        form.Add(new StringContent(publicId), "public_id");
        form.Add(new StringContent(signature), "signature");

        using var response = await http.PostAsync(ApiUri("upload"), form, ct);
        response.EnsureSuccessStatusCode();
        var uploaded = await response.Content.ReadFromJsonAsync<CloudinaryUploadResponse>(cancellationToken: ct);
        if (uploaded is null || string.IsNullOrWhiteSpace(uploaded.PublicId) || uploaded.Bytes <= 0)
            throw new InvalidOperationException("Cloudinary không trả về thông tin ảnh đã lưu.");

        return new(
            StoragePrefix + uploaded.PublicId,
            Path.GetFileName(upload.OriginalFileName),
            contentType,
            uploaded.Bytes);
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using PropFlow.Modules.Maintenance.Application;

namespace PropFlow.Modules.Maintenance.Infrastructure.Storage;

/// <summary>
/// Stores FE-07 evidence as authenticated Cloudinary images. Public IDs, not URLs,
/// are persisted so authorization remains at the PropFlow API boundary.
/// </summary>
public sealed class CloudinaryMaintenanceEvidenceStorage(
    Cloudinary cloudinary,
    IHttpClientFactory httpClientFactory) : IMaintenanceEvidenceStorage
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    private const string Folder = "propflow/maintenance/evidence";

    public async Task<StoredMaintenanceEvidence> StoreAsync(Guid resultId, MaintenanceEvidenceUpload upload, CancellationToken ct)
    {
        if (resultId == Guid.Empty) throw new ArgumentException("Kết quả bảo trì không hợp lệ.", nameof(resultId));
        if (upload.Length is <= 0 or > MaxImageBytes)
            throw new ArgumentException("Mỗi ảnh minh chứng phải có dung lượng từ 1 byte đến 5 MB.");

        await using var source = await upload.OpenReadAsync(ct);
        await using var image = await ReadAndValidateImageAsync(source, upload.Length, ct);
        // Cloudinary owns the FileDescription stream and may close it after UploadAsync.
        // Capture every required stream property before passing it to the SDK.
        var sizeBytes = image.Length;
        var publicId = $"{Folder}/{resultId:N}/{Guid.NewGuid():N}";
        var response = await cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(Path.GetFileName(upload.OriginalFileName), image),
            PublicId = publicId,
            Overwrite = false,
            UseFilename = false,
            UniqueFilename = false,
            Type = "authenticated"
        }, ct);

        if (response.Error is not null || string.IsNullOrWhiteSpace(response.PublicId))
            throw new InvalidOperationException("Không thể tải ảnh minh chứng lên kho lưu trữ.");

        return new(response.PublicId, Path.GetFileName(upload.OriginalFileName),
            ContentTypeFromName(response.Format), sizeBytes);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        if (!TryGetPublicId(storageKey, out var publicId))
            return await legacyStorage.OpenReadAsync(storageKey, ct);

        EnsureConfigured();
        using var response = await http.GetAsync(DeliveryUri(publicId), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new FileNotFoundException("Không tìm thấy ảnh minh chứng.");
        response.EnsureSuccessStatusCode();
        var copy = new MemoryStream();
        await response.Content.CopyToAsync(copy, ct);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        var url = cloudinary.Api.UrlImgUp.Secure(true).Signed(true).Type("authenticated").BuildUrl(storageKey);
        using var response = await httpClientFactory.CreateClient("MaintenanceEvidenceCloudinary")
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new FileNotFoundException("Không tìm thấy ảnh minh chứng.");

        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var copy = new MemoryStream();
        await source.CopyToAsync(copy, ct);
        copy.Position = 0;
        return copy;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        if (!TryGetPublicId(storageKey, out var publicId))
        {
            await legacyStorage.DeleteAsync(storageKey, ct);
            return;
        }

        EnsureConfigured();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Sign($"invalidate=true&public_id={publicId}&timestamp={timestamp}");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["api_key"] = options.ApiKey,
            ["timestamp"] = timestamp,
            ["public_id"] = publicId,
            ["invalidate"] = "true",
            ["signature"] = signature
        });
        using var response = await http.PostAsync(ApiUri("destroy"), form, ct);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<MemoryStream> ReadValidatedImageAsync(
        Stream content,
        long declaredSize,
        CancellationToken ct)
    {
        var copy = new MemoryStream((int)declaredSize);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await content.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (copy.Length + read > LocalMaintenanceEvidenceStorage.MaxImageBytes)
            {
                copy.Dispose();
                throw new ArgumentException("Mỗi ảnh minh chứng không được vượt quá 5 MB.");
            }
            await copy.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        if (copy.Length != declaredSize)
        {
            copy.Dispose();
            throw new ArgumentException("Kích thước tệp tải lên không hợp lệ.");
        }

        try
        {
            _ = DetectContentType(copy.GetBuffer().AsSpan(0, (int)copy.Length));
        }
        catch
        {
            copy.Dispose();
            throw;
        }
        copy.Position = 0;
        return copy;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.CloudName) ||
            string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.ApiSecret))
            throw new InvalidOperationException("Cloudinary chưa được cấu hình cho kho ảnh minh chứng Maintenance.");
    }

    private Uri ApiUri(string action) =>
        new($"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(options.CloudName)}/image/{action}");

    private Uri DeliveryUri(string publicId)
    {
        var escapedPublicId = string.Join('/', publicId.Split('/').Select(Uri.EscapeDataString));
        return new($"https://res.cloudinary.com/{Uri.EscapeDataString(options.CloudName)}/image/upload/{escapedPublicId}");
    }

    private string Sign(string payload)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(payload + options.ApiSecret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryGetPublicId(string storageKey, out string publicId)
    {
        if (storageKey.StartsWith(StoragePrefix, StringComparison.Ordinal) && storageKey.Length > StoragePrefix.Length)
        {
            publicId = storageKey[StoragePrefix.Length..];
            return true;
        }
        publicId = string.Empty;
        return false;
    }

    private static string DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";
        throw new ArgumentException("Ảnh minh chứng phải là JPG, PNG hoặc WebP hợp lệ.");
    }

    private sealed record CloudinaryUploadResponse(
        [property: JsonPropertyName("public_id")] string PublicId,
        [property: JsonPropertyName("bytes")] long Bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        var response = await cloudinary.DestroyAsync(new DeletionParams(storageKey)
        {
            ResourceType = ResourceType.Image,
            Type = "authenticated",
            Invalidate = true
        }).WaitAsync(ct);
        if (response.Error is not null)
            throw new InvalidOperationException("Không thể xóa ảnh minh chứng khỏi kho lưu trữ.");
    }

    private static async Task<MemoryStream> ReadAndValidateImageAsync(Stream source, long declaredLength, CancellationToken ct)
    {
        var image = new MemoryStream(capacity: checked((int)declaredLength));
        await source.CopyToAsync(image, ct);
        if (image.Length is <= 0 or > MaxImageBytes)
            throw new ArgumentException("Mỗi ảnh minh chứng không được vượt quá 5 MB.");

        var bytes = image.ToArray();
        var valid = bytes.Length >= 3 && bytes.Take(3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })
            || bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            || bytes.Length >= 12 && bytes.Take(4).SequenceEqual("RIFF"u8.ToArray()) && bytes.Skip(8).Take(4).SequenceEqual("WEBP"u8.ToArray());
        if (!valid) throw new ArgumentException("Ảnh minh chứng phải là JPG, PNG hoặc WebP hợp lệ.");
        image.Position = 0;
        return image;
    }

    private static string ContentTypeFromName(string? format) => format?.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        _ => throw new InvalidOperationException("Cloudinary trả về định dạng ảnh không được hỗ trợ.")
    };
}
