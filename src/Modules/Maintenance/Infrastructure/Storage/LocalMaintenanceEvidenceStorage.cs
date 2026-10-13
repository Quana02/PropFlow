using Microsoft.Extensions.Configuration;
using PropFlow.Modules.Maintenance.Application;

namespace PropFlow.Modules.Maintenance.Infrastructure.Storage;

public sealed class LocalMaintenanceEvidenceStorage(IConfiguration configuration) : IMaintenanceEvidenceStorage
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    private const long MaxTotalBytes = 25 * 1024 * 1024;
    private readonly string root = configuration["MaintenanceEvidence:RootPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "maintenance-evidence");

    public async Task<StoredMaintenanceEvidence> StoreAsync(Guid resultId, MaintenanceEvidenceUpload upload, CancellationToken ct)
    {
        if (upload.Length <= 0 || upload.Length > MaxImageBytes || upload.Length > MaxTotalBytes)
            throw new ArgumentException("Mỗi ảnh minh chứng phải có dung lượng từ 1 byte đến 5 MB.");

        await using var source = await upload.OpenReadAsync(ct);
        var (contentType, extension, prefix) = await InspectImageAsync(source, ct);
        var key = Path.Combine(resultId.ToString("N"), $"{Guid.NewGuid():N}{extension}").Replace('\\', '/');
        var fullPath = Path.GetFullPath(Path.Combine(root, key));
        var expectedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(expectedRoot, StringComparison.Ordinal)) throw new InvalidOperationException("Đường dẫn lưu ảnh không hợp lệ.");

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            await using var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            await target.WriteAsync(prefix, ct);
            await source.CopyToAsync(target, ct);
            if (target.Length > MaxImageBytes) throw new ArgumentException("Mỗi ảnh minh chứng không được vượt quá 5 MB.");
        }
        catch
        {
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }

        return new(key, Path.GetFileName(upload.OriginalFileName), contentType, new FileInfo(fullPath).Length);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, storageKey));
        var expectedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(expectedRoot, StringComparison.Ordinal) || !File.Exists(fullPath)) throw new FileNotFoundException("Không tìm thấy ảnh minh chứng.");
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, storageKey));
        var expectedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(expectedRoot, StringComparison.Ordinal) && File.Exists(fullPath)) File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private static async Task<(string ContentType, string Extension, byte[] Prefix)> InspectImageAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[12];
        var read = 0;
        while (read < prefix.Length)
        {
            var count = await stream.ReadAsync(prefix.AsMemory(read, prefix.Length - read), ct);
            if (count == 0) break;
            read += count;
        }
        Array.Resize(ref prefix, read);
        if (prefix.Length >= 3 && prefix[0] == 0xFF && prefix[1] == 0xD8 && prefix[2] == 0xFF) return ("image/jpeg", ".jpg", prefix);
        if (prefix.Length >= 8 && prefix.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ("image/png", ".png", prefix);
        if (prefix.Length >= 12 && prefix.AsSpan(0, 4).SequenceEqual("RIFF"u8) && prefix.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", ".webp", prefix);
        throw new ArgumentException("Ảnh minh chứng phải là JPG, PNG hoặc WebP hợp lệ.");
    }
}
