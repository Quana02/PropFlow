namespace PropFlow.Modules.AiRecommendation.Domain.Recommendations;

public sealed class FacilityRecommendationRun
{
    private FacilityRecommendationRun() { }
    public FacilityRecommendationRun(Guid actor, string fingerprint, DateTimeOffset now)
    { Id = Guid.NewGuid(); Actor = actor; Fingerprint = fingerprint; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid Actor { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public string? ResultJson { get; private set; }
    public string? Model { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public void Complete(string? result, string? model, int? inputTokens, int? outputTokens, DateTimeOffset now)
    { ResultJson = result; Model = model; InputTokens = inputTokens; OutputTokens = outputTokens; CompletedAt = now; }
}
