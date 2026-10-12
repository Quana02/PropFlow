namespace PropFlow.Modules.Maintenance.Application;

public sealed record MaintenanceEvidenceUpload(
    string OriginalFileName,
    string? DeclaredContentType,
    long Length,
    Func<CancellationToken, Task<Stream>> OpenReadAsync);

public sealed record StoredMaintenanceEvidence(
    string StorageKey,
    string OriginalFileName,
    string ContentType,
    long SizeBytes);

public sealed record MaintenanceEvidenceDownload(Stream Content, string ContentType, string OriginalFileName);

public interface IMaintenanceEvidenceStorage
{
    Task<StoredMaintenanceEvidence> StoreAsync(Guid resultId, MaintenanceEvidenceUpload upload, CancellationToken ct);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
}
