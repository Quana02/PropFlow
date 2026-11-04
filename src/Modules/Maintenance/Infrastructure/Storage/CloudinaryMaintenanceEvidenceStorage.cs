using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;
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
    IHttpClientFactory httpClientFactory,
    IConfiguration? configuration = null) : IMaintenanceEvidenceStorage
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
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        if (TryLegacyLocalPath(storageKey, out var path))
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var (publicId, deliveryType) = Resource(storageKey);
        var url = cloudinary.Api.UrlImgUp.Secure(true).Signed(true).Type(deliveryType).BuildUrl(publicId);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        if (TryLegacyLocalPath(storageKey, out var path))
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }
        var (publicId, deliveryType) = Resource(storageKey);
        var response = await cloudinary.DestroyAsync(new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Image,
            Type = deliveryType,
            Invalidate = true
        }).WaitAsync(ct);
        if (response.Error is not null)
            throw new InvalidOperationException("Không thể xóa ảnh minh chứng khỏi kho lưu trữ.");
    }

    // Preserve evidence stored before authenticated Cloudinary delivery was introduced.
    private static (string PublicId, string DeliveryType) Resource(string key) =>
        key.StartsWith("cloudinary:", StringComparison.Ordinal)
            ? (key["cloudinary:".Length..], "upload") : (key, "authenticated");

    private bool TryLegacyLocalPath(string key, out string path)
    {
        path = string.Empty;
        if (!Regex.IsMatch(key, @"^[a-fA-F0-9]{32}/[a-fA-F0-9]{32}\.(jpg|png|webp)$")) return false;
        var root = Path.GetFullPath(configuration?["MaintenanceEvidence:RootPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "maintenance-evidence"));
        path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("Không tìm thấy ảnh minh chứng.");
        return true;
    }

    private static async Task<MemoryStream> ReadAndValidateImageAsync(Stream source, long declaredLength, CancellationToken ct)
    {
        var image = new MemoryStream(capacity: checked((int)declaredLength));
        try
        {
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0) break;
                if (image.Length + read > MaxImageBytes || image.Length + read > declaredLength)
                    throw new ArgumentException("Kích thước tệp tải lên không hợp lệ.");
                await image.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (image.Length != declaredLength)
                throw new ArgumentException("Kích thước tệp tải lên không hợp lệ.");
            var bytes = image.ToArray();
            var valid = bytes.Length >= 3 && bytes.Take(3).SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })
                || bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || bytes.Length >= 12 && bytes.Take(4).SequenceEqual("RIFF"u8.ToArray()) && bytes.Skip(8).Take(4).SequenceEqual("WEBP"u8.ToArray());
            if (!valid) throw new ArgumentException("Ảnh minh chứng phải là JPG, PNG hoặc WebP hợp lệ.");
            image.Position = 0;
            return image;
        }
        catch { image.Dispose(); throw; }
    }

    private static string ContentTypeFromName(string? format) => format?.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        _ => throw new InvalidOperationException("Cloudinary trả về định dạng ảnh không được hỗ trợ.")
    };
}
