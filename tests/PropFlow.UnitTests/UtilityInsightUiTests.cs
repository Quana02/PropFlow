using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.UtilityInsights.Components;
using PropFlow.Web.Client.Features.UtilityInsights.Pages;
using PropFlow.Web.Client.Features.UtilityInsights.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

[Trait("Feature", "UtilityConsumption-Task1")]
public sealed class UtilityInsightUiTests
{
    private static UtilityInsightView Sample()
    {
        var period = new DateOnly(2026, 10, 1);
        var electricity = new UtilitySignal("ELECTRICITY", "kWh", "HIGH", "HIGHER", 930, 310, 200, 20, .01, 8, "utility-daily-iid-mad-v1", "Cần kiểm tra chỉ số và xác minh sinh hoạt.");
        var water = electricity with { Utility = "WATER", Unit = "m³", Severity = "NORMAL", Current = 12.4m, Reference = 12.4m, ChangePercent = 0, Reason = "Trong mức tham chiếu." };
        var history = Enumerable.Range(-8, 9).Select(i => new UtilityHistoryPoint(period.AddMonths(i), i == 0 ? 930 : 310, 12.4m, true, true)).ToArray();
        return new(Guid.NewGuid(), Guid.NewGuid(), "A-101", period, "test-fingerprint", 1, true, "PENDING", null, null, new(electricity, water, history), null, "STATISTICAL", DateTimeOffset.UtcNow);
    }
    [Fact]
    public async Task Detail_renders_values_history_and_ai_fallback_without_raw_html()
    {
        await using var provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = Sample() with { AiExplanation = "<script>alert('x')</script>", AiMode = "GEMINI" };
            var root = await renderer.RenderComponentAsync<UtilityInsightDetail>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Data"] = view }));
            var html = root.ToHtmlString();
            Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>", html);
            Assert.Contains("930", html); Assert.Contains("utility-bars", html); Assert.Contains("10/2026", html);
            if (Environment.GetEnvironmentVariable("PROPFLOW_UTILITY_PREVIEW_FILE") is { Length: > 0 } file) await File.WriteAllTextAsync(file, html);
        });
    }
    [Theory]
    [InlineData("MANAGER", true)]
    [InlineData("ACCOUNTANT", false)]
    public async Task Review_actions_are_only_visible_to_manager_and_post_real_result(string role, bool canReview)
    {
        var sample = Sample(); using var handler = new Responses(sample);
        using var http = new HttpClient(handler) { BaseAddress = new("https://utilities.test/") };
        var publicApi = new ApiClient(http, NullLogger<ApiClient>.Instance); var session = new AuthSession(publicApi);
        session.Accept(new("test-token", DateTimeOffset.UtcNow.AddMinutes(10), new AccountResponse(Guid.NewGuid(), "tester", "Tester", null, null, "ACTIVE", true, role, [])));
        var capture = new Capture();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(session)
            .AddSingleton(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance)).AddSingleton<UtilityInsightsApi>()
            .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<UtilityInsightsManagement>(ParameterView.Empty);
            var page = capture.Page!;
            var method = typeof(UtilityInsightsManagement).GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Instance)!;
            method.Invoke(page, [sample]); Refresh(page);
            var html = WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Equal(canReview, html.Contains("Xác nhận và báo cư dân"));
            if (canReview)
            {
                typeof(UtilityInsightsManagement).GetField("note", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, "Đã đối chiếu công tơ.");
                await (Task)typeof(UtilityInsightsManagement).GetMethod("Review", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, ["CONFIRMED"])!;
                Refresh(page); Assert.Equal(1, handler.Reviews); Assert.DoesNotContain("Xác nhận và báo cư dân", WebUtility.HtmlDecode(root.ToHtmlString()));
            }
        });
    }
    private static void Refresh(ComponentBase component) => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
    private sealed class Capture : IComponentActivator
    {
        public UtilityInsightsManagement? Page;
        public IComponent CreateInstance(Type type) { var item = (IComponent)Activator.CreateInstance(type)!; if (item is UtilityInsightsManagement page) Page = page; return item; }
    }
    private sealed class Responses(UtilityInsightView view) : HttpMessageHandler
    {
        public int Reviews;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK) { Content = JsonContent.Create(new UtilityInsightPage([view], 1, 20, 1)) };
            Reviews++;
            var body = await request.Content!.ReadAsStringAsync(ct); Assert.Contains(view.Fingerprint, body);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(view with { Review = "CONFIRMED", ReviewNote = "Đã đối chiếu công tơ.", ReviewedAt = DateTimeOffset.UtcNow }) };
        }
    }
}
