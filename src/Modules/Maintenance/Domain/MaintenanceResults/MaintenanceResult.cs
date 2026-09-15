using PropFlow.Modules.Maintenance.Domain.MaintenanceTasks;

namespace PropFlow.Modules.Maintenance.Domain.MaintenanceResults;

public class MaintenanceResult
{
    private MaintenanceResult()
    {
    }

    public MaintenanceResult(
        Guid maintenanceTaskId,
        int attemptNo,
        Guid submittedBy,
        string summary,
        DateTimeOffset now,
        string? workPerformed = null,
        string? issueFound = null,
        string? partsOrResourcesUsed = null,
        string? recommendation = null)
    {
        ThrowIfEmpty(maintenanceTaskId, nameof(maintenanceTaskId));
        ThrowIfEmpty(submittedBy, nameof(submittedBy));
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (attemptNo < 1)
            throw new ArgumentOutOfRangeException(nameof(attemptNo), "Attempt number must be greater than or equal to 1.");

        Id = Guid.NewGuid();
        MaintenanceTaskId = maintenanceTaskId;
        AttemptNo = attemptNo;
        SubmittedBy = submittedBy;
        Summary = summary.Trim();
        WorkPerformed = string.IsNullOrWhiteSpace(workPerformed) ? null : workPerformed.Trim();
        IssueFound = string.IsNullOrWhiteSpace(issueFound) ? null : issueFound.Trim();
        PartsOrResourcesUsed = string.IsNullOrWhiteSpace(partsOrResourcesUsed) ? null : partsOrResourcesUsed.Trim();
        Recommendation = string.IsNullOrWhiteSpace(recommendation) ? null : recommendation.Trim();
        ResultStatus = MaintenanceResultStatus.SUBMITTED;
        SubmittedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid MaintenanceTaskId { get; private set; }
    public int AttemptNo { get; private set; }
    public Guid SubmittedBy { get; private set; }
    public string Summary { get; private set; } = null!;
    public string? WorkPerformed { get; private set; }
    public string? IssueFound { get; private set; }
    public string? PartsOrResourcesUsed { get; private set; }
    public string? Recommendation { get; private set; }
    public MaintenanceResultStatus ResultStatus { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public string? ReviewNote { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public MaintenanceTask? MaintenanceTask { get; private set; }

    public void Approve(Guid reviewedBy, DateTimeOffset now, string? reviewNote = null)
    {
        EnsureSubmitted();
        ThrowIfEmpty(reviewedBy, nameof(reviewedBy));
        EnsureReviewedAfterSubmitted(now);

        ResultStatus = MaintenanceResultStatus.APPROVED;
        ReviewedBy = reviewedBy;
        ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        ReviewedAt = now;
        UpdatedAt = now;
    }

    public void RequestRevision(Guid reviewedBy, DateTimeOffset now, string? reviewNote = null)
    {
        EnsureSubmitted();
        ThrowIfEmpty(reviewedBy, nameof(reviewedBy));
        EnsureReviewedAfterSubmitted(now);

        ResultStatus = MaintenanceResultStatus.REVISION_REQUIRED;
        ReviewedBy = reviewedBy;
        ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        ReviewedAt = now;
        UpdatedAt = now;
    }

    private void EnsureSubmitted()
    {
        if (ResultStatus != MaintenanceResultStatus.SUBMITTED)
            throw new InvalidOperationException("Only submitted maintenance results can be reviewed.");
    }

    private void EnsureReviewedAfterSubmitted(DateTimeOffset reviewedAt)
    {
        if (reviewedAt < SubmittedAt)
            throw new ArgumentException("Review time cannot be before submitted time.", nameof(reviewedAt));
    }

    private static void ThrowIfEmpty(Guid value, string paramName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException($"{paramName} cannot be empty.", paramName);
    }
}
