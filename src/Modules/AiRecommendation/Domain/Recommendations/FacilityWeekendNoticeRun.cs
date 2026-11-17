namespace PropFlow.Modules.AiRecommendation.Domain.Recommendations;

public sealed class FacilityWeekendNoticeRun
{
    private FacilityWeekendNoticeRun() { }
    public FacilityWeekendNoticeRun(DateOnly date, TimeOnly slot, DateOnly weekendStart, Guid facilityId, DateTimeOffset now, DateTimeOffset expiresAt)
    { Id = Guid.NewGuid(); LocalDate = date; Slot = slot; WeekendStart = weekendStart; FacilityId = facilityId; CreatedAt = now; NextAttemptAt = now; ExpiresAt = expiresAt; }
    public Guid Id { get; private set; }
    public DateOnly LocalDate { get; private set; }
    public TimeOnly Slot { get; private set; }
    public DateOnly WeekendStart { get; private set; }
    public Guid FacilityId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public Guid LeaseToken { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public int Attempts { get; private set; }
    public int Recipients { get; private set; }
    public bool CanClaim(DateTimeOffset now) => CompletedAt is null && now < ExpiresAt && NextAttemptAt <= now && (LeaseUntil is null || LeaseUntil <= now);
    public Guid Claim(DateTimeOffset now)
    { Attempts++; LeaseUntil = now.AddMinutes(5); LeaseToken = Guid.NewGuid(); return LeaseToken; }
    public void Complete(DateTimeOffset now, int recipients)
    { CompletedAt = now; Recipients = recipients; LeaseUntil = null; LeaseToken = Guid.NewGuid(); }
    public void Retry(DateTimeOffset now)
    { NextAttemptAt = now.AddMinutes(5); LeaseUntil = null; LeaseToken = Guid.NewGuid(); }
}
