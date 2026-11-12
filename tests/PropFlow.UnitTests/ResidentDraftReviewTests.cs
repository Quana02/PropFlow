using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PropFlow.Api.Composition;
using PropFlow.Modules.AiClassification.Application.ResidentDraftReview;
using PropFlow.Modules.AiClassification.Infrastructure;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Shared;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.1")]
public sealed class ResidentDraftReviewTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [Theory]
    [InlineData("VISITOR", false)]
    [InlineData("VISITOR_STAY", true)]
    public async Task Only_explicit_overnight_stays_can_ask_for_duration(string kind, bool expectsDuration)
    {
        using var transport = new GeminiResponse(["visitor_name", "visit_duration", "appointment_date", "appointment_time"], requestKind: kind);
        var result = await Model(transport).CheckAsync("Tôi muốn đăng ký khách.", Today, default);
        Assert.NotNull(result);
        Assert.Contains("visitor_name", result.MissingFields);
        Assert.Equal(expectsDuration, result.MissingFields.Contains("visit_duration"));
        Assert.DoesNotContain("appointment_date", result.MissingFields);
        Assert.DoesNotContain("appointment_time", result.MissingFields);
    }

    [Theory]
    [InlineData("VISITOR", "Tôi muốn đăng ký khách đến thăm vào sáng ngày mai.")]
    [InlineData("MOVING", "Tôi muốn chuyển đồ vào căn hộ vào sáng ngày mai.")]
    public async Task Registration_times_are_not_staff_appointments(string kind, string description)
    {
        using var transport = new GeminiResponse(["appointment_date", "appointment_time"],
            "2026-11-13", "MORNING", "sáng ngày mai", "sáng", kind);
        var result = await Model(transport).CheckAsync(description, Today, default);
        Assert.NotNull(result);
        Assert.Empty(result.MissingFields);
        Assert.Null(result.PreferredDate);
        Assert.Null(result.PreferredTimeCode);
    }

    [Fact]
    public async Task Moving_requests_ask_about_items_and_direction_instead_of_visitors_or_appointments()
    {
        using var transport = new GeminiResponse(["moving_items", "moving_direction", "visitor_name", "appointment_date", "appointment_time"], requestKind: "MOVING");
        var result = await Model(transport).CheckAsync("Tôi muốn đăng ký chuyển đồ", Today, default);
        Assert.Equal(new[] { "moving_items", "moving_direction" }, result!.MissingFields);
    }

    [Fact]
    public async Task Service_registration_can_ask_which_service_without_forcing_a_schedule()
    {
        using var transport = new GeminiResponse(["service_type", "appointment_date", "appointment_time"], requestKind: "SERVICE");
        var result = await Model(transport).CheckAsync("Tôi muốn đăng ký dịch vụ chung cư", Today, default);
        Assert.Equal(new[] { "service_type" }, result!.MissingFields);
        Assert.Null(result.PreferredDate);
    }

    [Fact]
    public async Task Vehicle_registration_does_not_ask_for_appointments_or_copy_registration_dates_into_appointment_fields()
    {
        using var transport = new GeminiResponse(["appointment_date", "appointment_time"],
            "2026-11-13", "MORNING", "sáng ngày mai", "sáng", "VEHICLE");
        var description = "Tôi muốn đăng ký xe máy màu đỏ biển số 59A1-123.45 từ sáng ngày mai.";

        var result = await Model(transport).CheckAsync(description, Today, default);

        Assert.NotNull(result);
        Assert.Empty(result.MissingFields);
        Assert.Null(result.PreferredDate);
        Assert.Null(result.PreferredTimeCode);
    }

    [Fact]
    public async Task Incomplete_vehicle_registration_only_asks_for_vehicle_information()
    {
        using var transport = new GeminiResponse(["vehicle_type", "vehicle_plate", "vehicle_color", "appointment_date", "appointment_time", "visitor_name"], requestKind: "VEHICLE");

        var result = await Model(transport).CheckAsync("Tôi muốn đăng ký xe", Today, default);

        Assert.Equal(new[] { "vehicle_type", "vehicle_plate", "vehicle_color" }, result!.MissingFields);
    }

    [Theory]
    [InlineData("Tôi muốn đăng ký người viếng thăm", "visitor_name", "Họ tên người viếng thăm")]
    [InlineData("Tôi muốn đăng ký xe", "vehicle_plate", "Biển số xe")]
    public async Task Draft_checker_maps_model_missing_fields_to_safe_vietnamese_questions_without_creating_requests(string description, string field, string message)
    {
        var reviewer = new Reviewer(new([field, "appointment_date", "appointment_time"], null, null));
        var checker = new ResidentServiceRequestDraftChecker(reviewer, new Residence(true), new Zone(), new Clock());
        var result = await checker.CheckAsync(Guid.NewGuid(), description, default);
        Assert.True(result.Review!.Available);
        Assert.Equal(message, result.Review.MissingInformation[0].Message);
        Assert.Equal(Today, reviewer.Today);
    }

    [Fact]
    public async Task Unlinked_resident_is_rejected_before_calling_ai()
    {
        var reviewer = new Reviewer(new([], null, null));
        var checker = new ResidentServiceRequestDraftChecker(reviewer, new Residence(false), new Zone(), new Clock());
        Assert.Equal(403, (await checker.CheckAsync(Guid.NewGuid(), "Tôi muốn đăng ký xe", default)).Status);
        Assert.Equal(0, reviewer.Calls);
    }

    [Fact]
    public async Task Unavailable_ai_is_explicit_and_does_not_claim_the_draft_is_complete()
    {
        var reviewer = new Reviewer(null);
        var result = await new ResidentServiceRequestDraftChecker(reviewer, new Residence(true), new Zone(), new Clock())
            .CheckAsync(Guid.NewGuid(), "Tôi muốn đăng ký xe", default);
        Assert.False(result.Review!.Available);
        Assert.NotNull(result.Review.Notice);
        Assert.Null(result.Review.PreferredDate);
    }

    [Fact]
    public async Task Provider_receives_masked_names_plates_and_contacts_but_keeps_the_original_request_semantics()
    {
        using var transport = new GeminiResponse(new[] { "appointment_date", "appointment_time" });
        var reviewer = Model(transport);
        var result = await reviewer.CheckAsync("Đăng ký khách tên Nguyễn Văn Mẫu, xe máy màu đỏ biển số 59A1-123.45. Liên hệ 0901234567 sample@example.invalid", Today, default);
        Assert.NotNull(result);
        Assert.DoesNotContain("Nguyễn Văn Mẫu", transport.Input);
        Assert.DoesNotContain("59A1-123.45", transport.Input);
        Assert.DoesNotContain("0901234567", transport.Input);
        Assert.DoesNotContain("sample@example.invalid", transport.Input);
        Assert.Contains("[HO_TEN_DA_CUNG_CAP]", transport.Input);
        Assert.Contains("[BIEN_SO_DA_CUNG_CAP]", transport.Input);
        Assert.Contains("xe máy màu đỏ", transport.Input);
    }

    [Fact]
    public async Task Extracted_date_and_time_require_quotes_from_the_description()
    {
        using var transport = new GeminiResponse([], "2026-11-13", "MORNING", "sáng ngày mai", "sáng");
        var result = await Model(transport).CheckAsync("Tôi muốn sửa vòi nước trong bếp vào sáng ngày mai.", Today, default);
        Assert.Equal(Today.AddDays(1), result!.PreferredDate);
        Assert.Equal("MORNING", result.PreferredTimeCode);
        Assert.Empty(result.MissingFields);
    }

    [Theory]
    [InlineData("2026-11-11", "ngày hôm qua", true)]
    [InlineData("2026-11-13", "chi tiết AI tự nghĩ ra", false)]
    [InlineData("không phải ngày", "ngày hôm qua", true)]
    public async Task Past_invalid_or_unsupported_date_is_not_applied(string date, string evidence, bool asksToCorrectUserDate)
    {
        using var transport = new GeminiResponse([], date, null, evidence, null);
        var result = await Model(transport).CheckAsync("Tôi nói ngày hôm qua nhưng chưa chốt lịch mới.", Today, default);
        Assert.Null(result!.PreferredDate);
        Assert.Equal(asksToCorrectUserDate, result.MissingFields.Contains("appointment_date"));
    }

    [Fact]
    public async Task Ai_generated_schedule_without_evidence_does_not_create_a_missing_schedule_question()
    {
        using var transport = new GeminiResponse(["appointment_date", "appointment_time"], "2026-11-13", "MORNING", "ngày không có trong mô tả", "giờ không có trong mô tả");
        var result = await Model(transport).CheckAsync("Tôi muốn vệ sinh để lau sàn và dọn rác trong bếp.", Today, default);
        Assert.Empty(result!.MissingFields);
        Assert.Null(result.PreferredDate);
        Assert.Null(result.PreferredTimeCode);
    }

    [Fact]
    public async Task Invented_missing_field_is_rejected_instead_of_asking_for_unapproved_personal_information()
    {
        using var transport = new GeminiResponse(["resident_password"]);
        Assert.Null(await Model(transport).CheckAsync("Tôi muốn đăng ký xe", Today, default));
    }

    [Fact]
    public async Task Provider_429_returns_unavailable_instead_of_a_successful_review()
    {
        using var transport = new GeminiResponse([]) { Status = HttpStatusCode.TooManyRequests };
        Assert.Null(await Model(transport).CheckAsync("Tôi muốn đăng ký xe", Today, default));
    }

    private static GeminiResidentDraftReviewer Model(HttpMessageHandler handler) =>
        new(new GeminiStructuredClient(new HttpClient(handler) { BaseAddress = new Uri("https://gemini.test/") }, "test-key", "test-model", true));

    private sealed class GeminiResponse(string[] fields, string? date = null, string? time = null,
        string? dateEvidence = null, string? timeEvidence = null, string requestKind = "SERVICE") : HttpMessageHandler
    {
        public string Input { get; private set; } = "";
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            using var input = JsonDocument.Parse(body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
            Input = input.RootElement.GetProperty("description").GetString()!;
            var output = JsonSerializer.Serialize(new { requestKind, missingFields = fields, preferredDate = date, preferredTimeCode = time, dateEvidence, timeEvidence });
            return new(Status) { Content = JsonContent.Create(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = output } } } } } }) };
        }
    }

    private sealed class Reviewer(ResidentDraftPrediction? prediction) : IResidentDraftReviewer
    {
        public int Calls { get; private set; }
        public DateOnly Today { get; private set; }
        public bool Ready => true;
        public Task<ResidentDraftPrediction?> CheckAsync(string text, DateOnly today, CancellationToken ct)
        { Calls++; Today = today; return Task.FromResult(prediction); }
    }
    private sealed class Residence(bool active) : IResidentResidenceSource
    {
        public Task<ActiveResidentResidence?> FindActiveAsync(Guid actor, DateOnly date, CancellationToken ct) =>
            Task.FromResult(active ? new ActiveResidentResidence(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()) : null);
    }
    private sealed class Zone : ICurrentBuildingTimeZone
    { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh"); }
    private sealed class Clock : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero); }
}
