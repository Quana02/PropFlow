namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequestEvidence;

public sealed class ServiceRequestEvidenceFile
{
    private ServiceRequestEvidenceFile()
    {
    }

    public ServiceRequestEvidenceFile(
        Guid serviceRequestId,
        Guid activityId,
        string fileName,
        string contentType,
        long sizeBytes,
        string storageKey,
        Guid uploadedBy,
        DateTimeOffset uploadedAt)
    {
        if (serviceRequestId == Guid.Empty) throw new ArgumentException("Service request is required.", nameof(serviceRequestId));
        if (activityId == Guid.Empty) throw new ArgumentException("Work result activity is required.", nameof(activityId));
        if (uploadedBy == Guid.Empty) throw new ArgumentException("Uploader is required.", nameof(uploadedBy));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        if (sizeBytes <= 0) throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        Id = Guid.NewGuid();
        ServiceRequestId = serviceRequestId;
        ActivityId = activityId;
        FileName = fileName.Trim();
        ContentType = contentType.Trim().ToLowerInvariant();
        SizeBytes = sizeBytes;
        StorageKey = storageKey.Trim();
        UploadedBy = uploadedBy;
        UploadedAt = uploadedAt;
    }

    public Guid Id { get; private set; }
    public Guid ServiceRequestId { get; private set; }
    public Guid ActivityId { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public Guid UploadedBy { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
}
