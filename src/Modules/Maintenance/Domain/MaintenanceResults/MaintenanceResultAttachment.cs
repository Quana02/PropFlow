namespace PropFlow.Modules.Maintenance.Domain.MaintenanceResults;

public sealed class MaintenanceResultAttachment
{
    private MaintenanceResultAttachment() { }

    public MaintenanceResultAttachment(Guid maintenanceResultId, string storageKey, string originalFileName, string contentType, long sizeBytes, Guid uploadedBy, DateTimeOffset uploadedAt)
    {
        if (maintenanceResultId == Guid.Empty || uploadedBy == Guid.Empty) throw new ArgumentException("Liên kết ảnh minh chứng không hợp lệ.");
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        Id = Guid.NewGuid();
        MaintenanceResultId = maintenanceResultId;
        StorageKey = storageKey;
        OriginalFileName = originalFileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
    }

    public Guid Id { get; private set; }
    public Guid MaintenanceResultId { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public MaintenanceResult? MaintenanceResult { get; private set; }
}
