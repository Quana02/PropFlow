using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.UtilityConsumption;
using PropFlow.Modules.Billing.Contracts;

namespace PropFlow.UnitTests;

[Trait("Feature", "UtilityConsumption-Task1")]
public sealed class UtilityConsumptionTests
{
    private static readonly Guid Apartment = Guid.NewGuid(), Resident = Guid.NewGuid(), Manager = Guid.NewGuid();
    private static readonly DateOnly Month = new(2026, 10, 1);
    private static UtilityConsumptionWindow Window(decimal currentDaily = 10, int history = 8, bool final = true)
    {
        var rows = Enumerable.Range(0, history + 1).Select(i =>
        {
            var month = Month.AddMonths(i - history);
            var days = DateTime.DaysInMonth(month.Year, month.Month);
            return new UtilityConsumptionPoint(month, (i == history ? currentDaily : 10m) * days, .4m * days,
                true, true, [Resident], final);
        }).ToArray();
        return new(Apartment, "A-101", Month, rows, "snapshot-v1");
    }
    [Fact]
    public void MlNet_model_and_day_normalization_keep_stable_usage_normal()
    {
        var analysis = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new()).Analyze(Window());
        Assert.Equal("NORMAL", analysis.Electricity.Severity); Assert.Equal("NORMAL", analysis.Water.Severity);
        Assert.Equal(310m, analysis.Electricity.Reference); Assert.Equal(0, analysis.Electricity.ChangePercent);
        Assert.NotNull(analysis.Electricity.PValue); Assert.DoesNotContain("fallback", analysis.Electricity.Method);
    }
    [Fact]
    public void Sharp_increase_is_flagged_but_does_not_claim_a_leak()
    {
        var analysis = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new()).Analyze(Window(30));
        Assert.Equal("HIGH", analysis.Electricity.Severity); Assert.Equal(200m, analysis.Electricity.ChangePercent);
        Assert.Equal("NORMAL", analysis.Water.Severity); Assert.Contains("chưa phải kết luận", analysis.Electricity.Reason);
    }
    [Fact]
    public void Missing_history_is_not_imputed_as_zero()
    {
        var result = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new()).Analyze(Window(history: 3));
        Assert.Equal("INSUFFICIENT", result.Electricity.Severity); Assert.Null(result.Electricity.Reference); Assert.Equal(3, result.Electricity.HistoryCount);
    }
    [Theory]
    [InlineData("water-consumption-demo.json", false)]
    [InlineData("utility-consumption-demo.json", true)]
    public void Demo_consumption_data_keeps_readings_continuous_and_matches_actual_model_results(string fileName, bool includeElectricity)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "docs", "testing", "data", fileName)));
        var points = new List<UtilityConsumptionPoint>(); decimal previous = 0, previousElectricity = 0; DateOnly? previousMonth = null;
        foreach (var request in document.RootElement.GetProperty("requests").EnumerateArray())
        {
            var month = DateOnly.ParseExact(request.GetProperty("usageMonth").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var reading = request.GetProperty("currentWaterReading").GetDecimal();
            Assert.True(reading >= previous); Assert.Equal(1, month.Day);
            if (previousMonth is { } prior) Assert.Equal(prior.AddMonths(1), month);
            decimal? electricity = includeElectricity ? request.GetProperty("currentElectricityReading").GetDecimal() : null;
            if (electricity is { } current) Assert.True(current >= previousElectricity);
            points.Add(new(month, electricity - previousElectricity, reading - previous, includeElectricity && previousMonth is not null, previousMonth is not null, [], false));
            previousElectricity = electricity ?? previousElectricity;
            previous = reading; previousMonth = month;
        }
        var analyzer = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new());
        foreach (var expected in document.RootElement.GetProperty("expectedResults").EnumerateArray())
        {
            var month = DateOnly.ParseExact(expected.GetProperty("usageMonth").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var analysis = analyzer.Analyze(new(Apartment, "WATER-DEMO", month, points.Where(x => x.Month <= month).ToArray(), "sample-data"));
            var water = analysis.Water;
            decimal? Number(string key) => expected.GetProperty(key).ValueKind == System.Text.Json.JsonValueKind.Null ? null : expected.GetProperty(key).GetDecimal();
            Assert.Equal(expected.GetProperty("severity").GetString(), water.Severity);
            Assert.Equal(Number("waterM3"), water.Current); Assert.Equal(Number("referenceM3"), water.Reference); Assert.Equal(Number("changePercent"), water.ChangePercent);
            if (includeElectricity)
            {
                Assert.Equal(expected.GetProperty("electricitySeverity").GetString(), analysis.Electricity.Severity);
                Assert.Equal(Number("kwh"), analysis.Electricity.Current); Assert.Equal(Number("electricityReferenceKwh"), analysis.Electricity.Reference);
                Assert.Equal(Number("electricityChangePercent"), analysis.Electricity.ChangePercent);
            }
        }
    }
    [Fact]
    public void Zero_and_constant_history_are_valid_and_do_not_divide_by_zero()
    {
        var window = Window() with { Points = Window().Points.Select(x => x with { Electricity = 0 }).ToArray() };
        var detector = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new());
        Assert.Equal("NORMAL", detector.Analyze(window).Electricity.Severity);
        var spike = window with { Points = window.Points.Select(x => x.Month == Month ? x with { Electricity = 200 } : x).ToArray() };
        var result = detector.Analyze(spike).Electricity;
        Assert.Equal("HIGH", result.Severity); Assert.Null(result.ChangePercent);
    }
    [Fact]
    public void A_small_change_in_constant_history_does_not_create_noise()
    {
        var result = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new()).Analyze(Window(10.1m));
        Assert.Equal("NORMAL", result.Electricity.Severity);
    }
    [Fact]
    public void Meter_gap_is_insufficient_and_previous_household_history_is_hidden()
    {
        var w = Window();
        var changed = w with { Points = w.Points.Select(x => x.Month == Month ? x : x with { Recipients = [Guid.NewGuid()] }).ToArray() };
        var detector = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new());
        var result = detector.Analyze(changed);
        Assert.Equal("INSUFFICIENT", result.Electricity.Severity); Assert.Single(result.History);
        var gap = w with { Points = w.Points.Select(x => x.Month == Month ? x with { ElectricityComparable = false } : x).ToArray() };
        Assert.Equal("INSUFFICIENT", detector.Analyze(gap).Electricity.Severity);
    }
    [Fact]
    public void Model_outage_has_an_explicit_statistical_fallback()
    {
        var result = new UtilityConsumptionAnalyzer(new UnavailableModel(), new()).Analyze(Window(30));
        Assert.Equal("HIGH", result.Electricity.Severity); Assert.Contains("fallback", result.Electricity.Method); Assert.Null(result.Electricity.PValue);
    }
    [Fact]
    public async Task Review_is_idempotent_and_data_change_resets_review_without_sending_old_notice()
    {
        await using var scenario = new Scenario(Window(30));
        var view = await scenario.Service.GetAsync(Apartment, Month, default);
        var request = new UtilityReviewInput(view.Fingerprint, "CONFIRMED", "Đã đối chiếu công tơ, cần cư dân xác minh.");
        await scenario.Service.ReviewAsync(Apartment, Month, request, Manager, default);
        await scenario.Service.ReviewAsync(Apartment, Month, request, Manager, default);
        Assert.Equal(1, await scenario.Db.UtilityInsightReviews.CountAsync()); Assert.Equal(1, await scenario.Db.UtilityNotices.CountAsync());
        scenario.Source.Current = Window(10) with { Fingerprint = "changed-v2" };
        var revised = await scenario.Service.GetAsync(Apartment, Month, default);
        Assert.Equal(2, revised.Revision); Assert.Equal("PENDING", revised.Review); Assert.Equal("NORMAL", revised.Analysis.Electricity.Severity);
        var publisher = new Publisher(); await scenario.Service.DispatchAsync(publisher, default);
        Assert.Empty(publisher.Notices);
        Assert.Equal(409, (await Assert.ThrowsAsync<UtilityInsightFailure>(() => scenario.Service.ReviewAsync(Apartment, Month, request, Manager, default))).Status);
    }
    [Fact]
    public async Task Draft_period_cannot_notify_and_residents_cannot_read_another_account()
    {
        await using var scenario = new Scenario(Window(30, final: false));
        var view = await scenario.Service.GetAsync(Apartment, Month, default);
        Assert.Equal(409, (await Assert.ThrowsAsync<UtilityInsightFailure>(() => scenario.Service.ReviewAsync(Apartment, Month,
            new(view.Fingerprint, "CONFIRMED", "Đã xem"), Manager, default))).Status);
        Assert.Empty(await scenario.Service.ResidentAsync(Resident, default));
        scenario.Source.Current = Window(30) with { Fingerprint = "final-v2" };
        Assert.Empty(await scenario.Service.ResidentAsync(Guid.NewGuid(), default));
        Assert.Single(await scenario.Service.ResidentAsync(Resident, default));
    }
    [Fact]
    public async Task Confirmed_notice_is_delivered_once_and_ai_failure_keeps_numeric_result()
    {
        await using var scenario = new Scenario(Window(30));
        var view = await scenario.Service.GetAsync(Apartment, Month, default);
        var explained = await scenario.Service.ExplainAsync(Apartment, Month, new(view.Fingerprint), Manager, default);
        Assert.Equal("FALLBACK", explained.AiMode); Assert.Equal("HIGH", explained.Analysis.Electricity.Severity);
        Assert.Equal(429, (await Assert.ThrowsAsync<UtilityInsightFailure>(() => scenario.Service.ExplainAsync(Apartment, Month, new(view.Fingerprint), Manager, default))).Status);
        await scenario.Service.ReviewAsync(Apartment, Month, new(view.Fingerprint, "CONFIRMED", "Đã kiểm tra"), Manager, default);
        var publisher = new Publisher();
        await scenario.Service.DispatchAsync(publisher, default); await scenario.Service.DispatchAsync(publisher, default);
        Assert.Single(publisher.Notices); Assert.Equal(Resident, publisher.Notices[0].UserId); Assert.Equal("/resident/utilities", publisher.Notices[0].ActionPath);
    }
    [Theory]
    [InlineData("Mức sử dụng thay đổi đáng chú ý; nên đối chiếu chỉ số với Ban quản lý.", true)]
    [InlineData("Chắc chắn có rò rỉ.", false)]
    [InlineData("Tăng 200 phần trăm.", false)]
    public async Task Gemini_explains_only_numeric_signals_and_rejects_invented_numbers_or_certainty(string answer, bool accepted)
    {
        using var handler = new GeminiResponse(answer);
        using var http = new HttpClient(handler) { BaseAddress = new("https://gemini.test/") };
        var client = new PropFlow.Shared.GeminiStructuredClient(http, "test-only-key", "test-model", true);
        var analysis = new UtilityConsumptionAnalyzer(new MlNetUtilitySpikeModel(), new()).Analyze(Window(30));
        var result = await new GeminiUtilityExplanation(client).ExplainAsync(analysis, default);
        Assert.Equal(accepted, result is not null);
        using var body = System.Text.Json.JsonDocument.Parse(handler.Body!);
        var input = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        using var signals = System.Text.Json.JsonDocument.Parse(input);
        Assert.Equal(new[] { "electricity", "water" }, signals.RootElement.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.DoesNotContain("A-101", input); Assert.DoesNotContain(Resident.ToString(), input);
        if (result is not null) { Assert.Equal(answer, result.Text); Assert.Equal(12, result.InputTokens); }
    }
    private sealed class GeminiResponse(string answer) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = System.Text.Json.JsonSerializer.Serialize(new { explanation = answer }) } } } } },
                usageMetadata = new { promptTokenCount = 12, candidatesTokenCount = 8 }
            });
            return new(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
        }
    }
    private sealed class UnavailableModel : IUtilitySpikeModel
    { public (bool, double) Evaluate(IReadOnlyList<float> history, float current, double confidence) => throw new DllNotFoundException(); }
    private sealed class Source(UtilityConsumptionWindow current) : IUtilityConsumptionSource
    {
        public UtilityConsumptionWindow Current = current;
        public Task<IReadOnlyList<BillingApartment>> ApartmentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingApartment>>([new(Apartment, "A-101")]);
        public Task<UtilityConsumptionWindow?> ReadAsync(Guid apartmentId, DateOnly month, CancellationToken ct) => Task.FromResult<UtilityConsumptionWindow?>(apartmentId == Apartment ? Current : null);
        public Task<IReadOnlyList<UtilityConsumptionPeriod>> ResidentPeriodsAsync(Guid userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<UtilityConsumptionPeriod>>([new(Apartment, Month)]);
    }
    private sealed class Explain : IUtilityExplanation
    {
        public bool Ready => true;
        public Task<UtilityAiResult?> ExplainAsync(UtilityAnalysis analysis, CancellationToken ct) => Task.FromResult<UtilityAiResult?>(null);
    }
    private sealed class Publisher : IFinanceNoticePublisher
    {
        public List<FinanceNotice> Notices = [];
        public Task PublishAsync(FinanceNotice notice, CancellationToken ct) { Notices.Add(notice); return Task.CompletedTask; }
    }
    private sealed class Scenario : IAsyncDisposable
    {
        public AiRecommendationDbContext Db { get; } = new(new DbContextOptionsBuilder<AiRecommendationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Source Source { get; }
        public UtilityInsights Service { get; }
        public Scenario(UtilityConsumptionWindow window)
        {
            Source = new(window); var options = new UtilityInsightOptions();
            Service = new(Db, Source, new(new MlNetUtilitySpikeModel(), options), new Explain(), options, TimeProvider.System, NullLogger<UtilityInsights>.Instance);
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
