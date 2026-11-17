using PropFlow.Modules.Billing.Contracts;

namespace PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;

public sealed class UtilityInsightOptions
{
    public int MinimumHistory { get; set; } = 6;
    public int AiRequestsPerUserDay { get; set; } = 20;
    public int AiRequestsPerDay { get; set; } = 200;
    public double Confidence { get; set; } = 95;
    public decimal MediumDeviation { get; set; } = 3;
    public decimal HighDeviation { get; set; } = 6;
    public decimal MinimumRelativeChange { get; set; } = .15m;
    public decimal MinimumElectricityChange { get; set; } = 20;
    public decimal MinimumWaterChange { get; set; } = 2;
}

public sealed record UtilitySignal(string Utility, string Unit, string Severity, string Direction,
    decimal? Current, decimal? Reference, decimal? ChangePercent, decimal? Deviation,
    double? PValue, int HistoryCount, string Method, string Reason);
public sealed record UtilityHistoryPoint(DateOnly Month, decimal? Electricity, decimal? Water, bool ElectricityComparable, bool WaterComparable);
public sealed record UtilityAnalysis(UtilitySignal Electricity, UtilitySignal Water, UtilityHistoryPoint[] History);
public sealed record UtilityInsightView(Guid Id, Guid ApartmentId, string UnitNumber, DateOnly Month,
    string Fingerprint, int Revision, bool IsFinal, string Review, DateTimeOffset? ReviewedAt,
    string? ReviewNote, UtilityAnalysis Analysis, string? AiExplanation, string AiMode, DateTimeOffset CalculatedAt);
public sealed record UtilityInsightPage(UtilityInsightView[] Items, int Page, int PageSize, int Total);
public sealed record UtilityReviewInput([System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)] string Fingerprint,
    [System.ComponentModel.DataAnnotations.Required] string Decision,
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(500)] string? Note);
public sealed record UtilityExplainInput([System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100)] string Fingerprint);
public sealed record UtilityAiResult(string Text, string Model, int? InputTokens, int? OutputTokens);
public interface IUtilityExplanation
{
    bool Ready { get; }
    Task<UtilityAiResult?> ExplainAsync(UtilityAnalysis analysis, CancellationToken ct);
}
public interface IUtilitySpikeModel
{
    (bool Spike, double PValue) Evaluate(IReadOnlyList<float> history, float current, double confidence);
}
public sealed class UtilityInsightFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
