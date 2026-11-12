using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Modules.ServiceRequests.Domain.ServiceRequestFeedbacks;

public sealed class ServiceRequestFeedback
{
    private ServiceRequestFeedback() { }

    public ServiceRequestFeedback(
        Guid serviceRequestId,
        Guid residentId,
        int rating,
        string comment,
        DateTimeOffset now)
    {
        if (serviceRequestId == Guid.Empty) throw new ArgumentException("Service request is required.", nameof(serviceRequestId));
        if (residentId == Guid.Empty) throw new ArgumentException("Resident is required.", nameof(residentId));

        Id = Guid.NewGuid();
        ServiceRequestId = serviceRequestId;
        ResidentId = residentId;
        SetFeedback(rating, comment);
        SubmittedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ServiceRequestId { get; private set; }
    public Guid ResidentId { get; private set; }
    public int Rating { get; private set; }
    public string Comment { get; private set; } = null!;
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public ServiceRequest ServiceRequest { get; private set; } = null!;

    public void Update(int rating, string comment, DateTimeOffset now)
    {
        SetFeedback(rating, comment);
        UpdatedAt = now;
    }

    private void SetFeedback(int rating, string comment)
    {
        if (rating is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(rating));
        ArgumentException.ThrowIfNullOrWhiteSpace(comment);
        var trimmed = comment.Trim();
        if (trimmed.Length is < 3 or > 1000) throw new ArgumentOutOfRangeException(nameof(comment));
        Rating = rating;
        Comment = trimmed;
    }
}
