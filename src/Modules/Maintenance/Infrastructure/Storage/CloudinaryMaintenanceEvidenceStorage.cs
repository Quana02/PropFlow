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
