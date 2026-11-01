using Microsoft.AspNetCore.Hosting;
using PropFlow.Modules.ServiceRequests.Application.WorkAssignedServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Infrastructure;

public sealed class LocalServiceRequestEvidenceStorage(IWebHostEnvironment environment) : IServiceRequestEvidenceStorage
{
    private const long MaxBytes = 10 * 1024 * 1024;
    private readonly string root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "App_Data", "service-request-evidence"));

    public async Task<StoredEvidence> SaveAsync(Guid serviceRequestId, string fileName, string contentType,
        long sizeBytes, Stream content, CancellationToken ct)
    {
        var normalizedType = contentType.Trim().ToLowerInvariant();
        var extension = normalizedType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new ArgumentException("Chỉ chấp nhận ảnh JPEG, PNG hoặc WebP.")
        };
        if (sizeBytes is <= 0 or > MaxBytes) throw new ArgumentException("Mỗi ảnh bằng chứng không được vượt quá 10 MB.");

        var storageKey = $"{serviceRequestId:N}/{Guid.NewGuid():N}{extension}";
        var path = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".upload";
        try
        {
            var total = 0L;
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var header = new byte[12];
                var headerLength = 0;
                while (headerLength < header.Length)
                {
                    var read = await content.ReadAsync(header.AsMemory(headerLength, header.Length - headerLength), ct);
                    if (read == 0) break;
                    headerLength += read;
                }
                if (!HasValidSignature(normalizedType, header.AsSpan(0, headerLength)))
                    throw new ArgumentException("Nội dung tệp không đúng định dạng ảnh đã khai báo.");

                await output.WriteAsync(header.AsMemory(0, headerLength), ct);
                total = headerLength;
                var buffer = new byte[81920];
                int readCount;
                while ((readCount = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += readCount;
                    if (total > MaxBytes) throw new ArgumentException("Mỗi ảnh bằng chứng không được vượt quá 10 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, readCount), ct);
                }
                await output.FlushAsync(ct);
                if (total != sizeBytes) throw new ArgumentException("Kích thước tệp tải lên không hợp lệ.");
            }

            File.Move(tempPath, path);
            return new(storageKey, total);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var path = Resolve(storageKey);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        var path = Path.GetFullPath(Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Khóa lưu trữ không hợp lệ.", nameof(storageKey));
        return path;
    }

    private static bool HasValidSignature(string contentType, ReadOnlySpan<byte> bytes) => contentType switch
    {
        "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
        "image/png" => bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
        _ => false
    };
}
