using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.UtilityInsights.Services;

public sealed record UtilitySignal(string Utility, string Unit, string Severity, string Direction,
    decimal? Current, decimal? Reference, decimal? ChangePercent, decimal? Deviation,
    double? PValue, int HistoryCount, string Method, string Reason);
public sealed record UtilityHistoryPoint(DateOnly Month, decimal? Electricity, decimal? Water, bool ElectricityComparable, bool WaterComparable);
public sealed record UtilityAnalysis(UtilitySignal Electricity, UtilitySignal Water, UtilityHistoryPoint[] History);
public sealed record UtilityInsightView(Guid Id, Guid ApartmentId, string UnitNumber, DateOnly Month,
    string Fingerprint, int Revision, bool IsFinal, string Review, DateTimeOffset? ReviewedAt,
    string? ReviewNote, UtilityAnalysis Analysis, string? AiExplanation, string AiMode, DateTimeOffset CalculatedAt);
public sealed record UtilityInsightPage(UtilityInsightView[] Items, int Page, int PageSize, int Total);

public sealed class UtilityInsightsApi(AuthenticatedApiClient api)
{
    private static string Path(Guid apartment, DateOnly month) => $"api/v1/utility-insights/apartments/{apartment}?month={month:yyyy-MM-dd}";
    public Task<ApiResult<UtilityInsightPage>> ListAsync(DateOnly month, int page, string? search, CancellationToken ct) =>
        api.SendAsync<UtilityInsightPage>(HttpMethod.Get, $"api/v1/utility-insights?month={month:yyyy-MM-dd}&page={page}&pageSize=20&search={Uri.EscapeDataString(search ?? "")}", ct: ct);
    public Task<ApiResult<UtilityInsightView>> GetAsync(Guid apartment, DateOnly month, CancellationToken ct) =>
        api.SendAsync<UtilityInsightView>(HttpMethod.Get, Path(apartment, month), ct: ct);
    public Task<ApiResult<UtilityInsightView[]>> ResidentAsync(CancellationToken ct) =>
        api.SendAsync<UtilityInsightView[]>(HttpMethod.Get, "api/v1/resident/utility-insights", ct: ct);
    public Task<ApiResult<UtilityInsightView>> ExplainAsync(UtilityInsightView view, CancellationToken ct) =>
        api.SendAsync<UtilityInsightView>(HttpMethod.Post, Path(view.ApartmentId, view.Month).Replace("?", "/explain?"), new { view.Fingerprint }, ct: ct);
    public Task<ApiResult<UtilityInsightView>> ReviewAsync(UtilityInsightView view, string decision, string note, CancellationToken ct) =>
        api.SendAsync<UtilityInsightView>(HttpMethod.Post, Path(view.ApartmentId, view.Month).Replace("?", "/review?"), new { view.Fingerprint, Decision = decision, Note = note }, ct: ct);
    public static string SeverityLabel(string code) => code switch { "HIGH" => "Tăng bất thường cao", "WATCH" => "Cần theo dõi", "NORMAL" => "Trong mức thông thường", _ => "Chưa đủ dữ liệu" };
    public static string ReviewLabel(string code) => code switch { "CONFIRMED" => "BQL đã xác nhận", "DISMISSED" => "BQL đã bỏ qua", _ => "Chưa xét duyệt" };
}
