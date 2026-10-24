using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class CloudinaryEvidenceOptions
{
    public const string SectionName = "ServiceRequests:EvidenceStorage:Cloudinary";
    public string CloudName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string Folder { get; set; } = "propflow/service-request-evidence";
}

public sealed class CloudinaryServiceRequestEvidenceStorage(
    HttpClient http,
    IOptions<CloudinaryEvidenceOptions> configuredOptions,
    LocalServiceRequestEvidenceStorage legacyStorage) : IServiceRequestEvidenceStorage
{
    private const string StoragePrefix = "cloudinary:";
    private const long MaxBytes = 10 * 1024 * 1024;
    private readonly CloudinaryEvidenceOptions options = configuredOptions.Value;

    public async Task<StoredEvidence> SaveAsync(
        Guid serviceRequestId,
        string fileName,
        string contentType,
        long sizeBytes,
        Stream content,
        CancellationToken ct)
    {
        EnsureConfigured();
        var normalizedType = contentType.Trim().ToLowerInvariant();
        if (normalizedType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new ArgumentException("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP.");
        if (sizeBytes is <= 0 or > MaxBytes)
            throw new ArgumentException("Mỗi ảnh bằng chứng không được vượt quá 10 MB.");

        using var image = await ReadValidatedImageAsync(content, normalizedType, sizeBytes, ct);
        var folder = options.Folder.Trim().Trim('/');
        var publicId = string.IsNullOrEmpty(folder)
            ? $"{serviceRequestId:N}/{Guid.NewGuid():N}"
            : $"{folder}/{serviceRequestId:N}/{Guid.NewGuid():N}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = Sign($"public_id={publicId}&timestamp={timestamp}");

        using var form = new MultipartFormDataContent();
        using var imageContent = new StreamContent(image);
        imageContent.Headers.ContentType = new(normalizedType);
        form.Add(imageContent, "file", Path.GetFileName(fileName));
        form.Add(new StringContent(options.ApiKey), "api_key");
        form.Add(new StringContent(timestamp), "timestamp");
        form.Add(new StringContent(publicId), "public_id");
        form.Add(new StringContent(signature), "signature");

        using var response = await http.PostAsync(ApiUri("upload"), form, ct);
        response.EnsureSuccessStatusCode();
        var uploaded = await response.Content.ReadFromJsonAsync<CloudinaryUploadResponse>(cancellationToken: ct);
        if (uploaded is null || string.IsNullOrWhiteSpace(uploaded.PublicId) || uploaded.Bytes <= 0)
            throw new InvalidOperationException("Cloudinary không trả về thông tin ảnh đã lưu.");

        return new StoredEvidence(StoragePrefix + uploaded.PublicId, uploaded.Bytes);
    }

    public async Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        if (!TryGetPublicId(storageKey, out var publicId))
            return await legacyStorage.OpenReadAsync(storageKey, ct);

        EnsureConfigured();
        using var response = await http.GetAsync(DeliveryUri(publicId), HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var copy = new MemoryStream();
        await response.Content.CopyToAsync(copy, ct);
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
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
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

    private async Task<MemoryStream> ReadValidatedImageAsync(Stream content, string contentType, long declaredSize, CancellationToken ct)
    {
        var copy = new MemoryStream((int)declaredSize);
        var buffer = new byte[81920];
        while (true)
        {
            var read = await content.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (copy.Length + read > MaxBytes)
            {
                copy.Dispose();
                throw new ArgumentException("Mỗi ảnh bằng chứng không được vượt quá 10 MB.");
            }
            await copy.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        if (copy.Length != declaredSize)
        {
            copy.Dispose();
            throw new ArgumentException("Kích thước tệp tải lên không hợp lệ.");
        }
        if (!HasValidSignature(contentType, copy.GetBuffer().AsSpan(0, (int)copy.Length)))
        {
            copy.Dispose();
            throw new ArgumentException("Nội dung tệp không đúng định dạng ảnh đã khai báo.");
        }
        copy.Position = 0;
        return copy;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(options.CloudName) ||
            string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.ApiSecret))
            throw new InvalidOperationException("Cloudinary chưa được cấu hình cho kho ảnh bằng chứng Service Request.");
    }

    private Uri ApiUri(string action) => new($"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(options.CloudName)}/image/{action}");

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

    private static bool HasValidSignature(string contentType, ReadOnlySpan<byte> bytes) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };

    private sealed record CloudinaryUploadResponse(
        [property: JsonPropertyName("public_id")] string PublicId,
        [property: JsonPropertyName("bytes")] long Bytes);
}
