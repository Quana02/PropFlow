using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.ServiceRequests.Components;
using PropFlow.Web.Client.Features.ServiceRequests.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-10")]
[Trait("Feature", "FE-11")]
public sealed class ManagerRequestOverviewTests
{
    private static readonly Guid Repair = Guid.NewGuid();
    private static readonly Guid Cleaning = Guid.NewGuid();
    private static readonly ManagerClassificationCategory[] Categories = [new(Repair, "Sửa chữa kỹ thuật"), new(Cleaning, "Vệ sinh")];
    private static readonly ServiceRequestHandlingPlan Proposal = new("URGENT", true, "Kiểm tra thang máy bị kẹt.", "Nhân viên kỹ thuật và thiết bị cứu hộ.");

    [Fact]
    public async Task Pending_suggestions_appear_in_category_title_and_below_description_without_writing_business_state()
    {
        var category = new ManagerServiceRequestClassificationView(false, true, null,
            new(Guid.NewGuid(), "SUCCESS", Repair, "Sự cố thiết bị.", null, null, null), Categories);
        var plan = new ManagerServiceRequestRecommendationView(false, true, false, null, null,
            new(Guid.NewGuid(), "SUCCESS", Proposal, "Có nguy cơ mất an toàn.", null, null, null, null));

        var html = await RenderAsync(Request(), category, plan);

        Assert.Contains("Sửa chữa kỹ thuật", html);
        Assert.Contains("suggestion-tag", html);
        Assert.Contains("Đề xuất", html);
        Assert.Contains("priority-urgent", html);
        Assert.Contains("Khẩn cấp", html);
        Assert.True(html.IndexOf("priority-urgent", StringComparison.Ordinal) < html.IndexOf("fact-grid", StringComparison.Ordinal));
        Assert.True(html.IndexOf("Mô tả hiện trạng", StringComparison.Ordinal) < html.IndexOf(Proposal.Action!, StringComparison.Ordinal));
        Assert.Contains(Proposal.Resources!, html);
        Assert.Contains("Xác nhận phương án", html);
        Assert.DoesNotContain("AI phân loại yêu cầu", html);
        Assert.DoesNotContain("AI đề xuất ưu tiên và hướng xử lý", html);
    }

    [Fact]
    public async Task Approved_values_replace_the_original_ai_proposal_in_the_main_information()
    {
        var category = new ManagerServiceRequestClassificationView(false, false, null,
            new(Guid.NewGuid(), "SUCCESS", Cleaning, "Gợi ý trước khi duyệt.", null, "CORRECTED", Repair), Categories);
        var approved = new ServiceRequestHandlingPlan("HIGH", false, "Phương án Manager đã duyệt.", "Nguồn lực Manager đã duyệt.");
        var plan = new ManagerServiceRequestRecommendationView(false, false, false, null, "HIGH",
            new(Guid.NewGuid(), "SUCCESS", Proposal, "Cơ sở đề xuất.", null, "CORRECTED", approved, "Điều chỉnh theo kiểm tra thực tế."));

        var html = await RenderAsync(Request() with { CategoryName = "Sửa chữa kỹ thuật", PriorityCode = "HIGH" }, category, plan);

        Assert.Contains("Đã xác nhận", html);
        Assert.Contains("priority-high", html);
        Assert.Contains(approved.Action!, html);
        Assert.Contains(approved.Resources!, html);
        Assert.DoesNotContain(Proposal.Action!, html);
        Assert.DoesNotContain("priority-urgent", html);
        Assert.DoesNotContain("Xác nhận phương án", html);
    }

    [Fact]
    public async Task Unconfigured_ai_keeps_description_visible_and_allows_manual_priority_without_null_suggestion_failure()
    {
        var category = new ManagerServiceRequestClassificationView(false, false, "AI chưa được cấu hình.", null, Categories);
        var plan = new ManagerServiceRequestRecommendationView(false, false, true, "Chọn ưu tiên thủ công.", null, null);

        var html = await RenderAsync(Request(), category, plan);

        Assert.Contains("AI chưa được cấu hình.", html);
        Assert.Contains("Mô tả hiện trạng", html);
        Assert.Contains(Request().Description, html);
        Assert.Contains("Lưu ưu tiên thủ công", html);
        Assert.Contains("Chưa phân loại", html);
        Assert.DoesNotContain("suggestion-tag", html);
    }

    [Fact]
    public async Task Stale_suggestions_are_not_presented_as_current_category_priority_or_handling_plan()
    {
        var category = new ManagerServiceRequestClassificationView(true, false, null,
            new(Guid.NewGuid(), "SUCCESS", Repair, "Đề xuất cũ.", null, null, null), Categories);
        var plan = new ManagerServiceRequestRecommendationView(true, false, true, null, "NORMAL",
            new(Guid.NewGuid(), "SUCCESS", Proposal, "Đề xuất cũ.", null, null, null, null));

        var html = await RenderAsync(Request() with { CategoryName = "Vệ sinh", PriorityCode = "NORMAL" }, category, plan);

        Assert.Contains("Vệ sinh", html);
        Assert.Contains("Phân tích AI", html);
        Assert.DoesNotContain("priority-urgent", html);
        Assert.DoesNotContain(Proposal.Action!, html);
        Assert.DoesNotContain("suggestion-tag", html);
    }

    private static async Task<string> RenderAsync(ManagerServiceRequestDetailResponse request,
        ManagerServiceRequestClassificationView classification, ManagerServiceRequestRecommendationView recommendation)
    {
        using var handler = new Responses(classification, recommendation);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://propflow.test/") };
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<Microsoft.JSInterop.IJSRuntime, UnusedJavaScript>()
            .AddSingleton<IManagerServiceRequestApiClient>(new ManagerServiceRequestApiClient(
                new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance))).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<ManagerRequestOverview>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(ManagerRequestOverview.Request)] = request }));
            return WebUtility.HtmlDecode(root.ToHtmlString());
        });
        Assert.Equal(2, handler.Reads);
        return html;
    }

    private sealed class UnusedJavaScript : Microsoft.JSInterop.IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("Static rendering should not invoke JavaScript.");
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) =>
            throw new InvalidOperationException("Static rendering should not invoke JavaScript.");
    }

    private sealed class Responses(ManagerServiceRequestClassificationView classification,
        ManagerServiceRequestRecommendationView recommendation) : HttpMessageHandler
    {
        public int Reads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith("/classification", StringComparison.Ordinal)
                    ? JsonContent.Create(classification) : JsonContent.Create(recommendation)
            });
        }
    }

    private static ManagerServiceRequestDetailResponse Request() => new(
        Guid.NewGuid(), "YC-TEST", "Tôi bị kẹt thang máy", "Thang máy dừng ở tầng 2, cần người hỗ trợ.",
        null, null, Guid.NewGuid(), "CD-TEST", "Cư dân kiểm thử", Guid.NewGuid(), null, "A-201", 2,
        "SUBMITTED", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        null, null, null, null, [], []);
}
