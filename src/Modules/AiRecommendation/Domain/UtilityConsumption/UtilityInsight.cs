namespace PropFlow.Modules.AiRecommendation.Domain.UtilityConsumption;

public sealed class UtilityInsight
{
    private UtilityInsight() { }
    public UtilityInsight(Guid apartmentId, DateOnly month) { Id = Guid.NewGuid(); ApartmentId = apartmentId; Month = month; }
    public Guid Id { get; private set; }
    public Guid ApartmentId { get; private set; }
    public DateOnly Month { get; private set; }
    public string UnitNumber { get; private set; } = "";
    public string Fingerprint { get; private set; } = "";
    public string MethodVersion { get; private set; } = "";
    public int Revision { get; private set; }
    public bool IsFinal { get; private set; }
    public Guid[] Recipients { get; private set; } = [];
    public string AnalysisJson { get; private set; } = "{}";
    public string Review { get; private set; } = "PENDING";
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? ReviewNote { get; private set; }
    public string? AiExplanation { get; private set; }
    public string AiMode { get; private set; } = "STATISTICAL";
    public DateTimeOffset CalculatedAt { get; private set; }
    public void Recalculate(string unit, string hash, string method, bool final, Guid[] recipients, string analysis, DateTimeOffset now)
    {
        UnitNumber = unit; Fingerprint = hash; MethodVersion = method; IsFinal = final; Recipients = recipients;
        AnalysisJson = analysis; Revision++; CalculatedAt = now; Review = "PENDING";
        ReviewedBy = null; ReviewedAt = null; ReviewNote = null; AiExplanation = null; AiMode = "STATISTICAL";
    }
    public void RecordReview(string decision, string note, Guid actor, DateTimeOffset now)
    {
        if (decision is not ("CONFIRMED" or "DISMISSED") || actor == Guid.Empty || string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Thông tin xét duyệt không hợp lệ.");
        if (Review != "PENDING") throw new InvalidOperationException("Kết quả đã được xét duyệt.");
        Review = decision; ReviewNote = note; ReviewedBy = actor; ReviewedAt = now;
    }
    public void Explain(string? text) { AiExplanation = text; AiMode = text is null ? "FALLBACK" : "GEMINI"; }
}

public sealed class UtilityInsightReview
{
    private UtilityInsightReview() { }
    public UtilityInsightReview(UtilityInsight insight)
    {
        Id = Guid.NewGuid(); InsightId = insight.Id; Revision = insight.Revision; Fingerprint = insight.Fingerprint;
        Decision = insight.Review; Note = insight.ReviewNote!; Actor = insight.ReviewedBy!.Value; CreatedAt = insight.ReviewedAt!.Value;
    }
    public Guid Id { get; private set; }
    public Guid InsightId { get; private set; }
    public int Revision { get; private set; }
    public string Fingerprint { get; private set; } = "";
    public string Decision { get; private set; } = "";
    public string Note { get; private set; } = "";
    public Guid Actor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class UtilityAiAttempt
{
    private UtilityAiAttempt() { }
    public UtilityAiAttempt(Guid actor, Guid insightId, DateTimeOffset now)
    { Id = Guid.NewGuid(); Actor = actor; InsightId = insightId; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid Actor { get; private set; }
    public Guid InsightId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? Model { get; private set; }
    public int? InputTokens { get; private set; }
    public int? OutputTokens { get; private set; }
    public void Complete(string? model, int? input, int? output) { Model = model; InputTokens = input; OutputTokens = output; }
}
