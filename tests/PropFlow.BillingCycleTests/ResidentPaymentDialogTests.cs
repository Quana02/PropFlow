using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Finance.Components;
using PropFlow.Web.Client.Features.Finance.Models;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Services.Api;
namespace PropFlow.BillingCycleTests;
public sealed class ResidentPaymentDialogTests
{
    [Theory]
    [InlineData(800000, 0, 800000, "800.000")]
    [InlineData(800000, 700000, 100000, "100.000")]
    public async Task Resident_payment_popup_prefills_remaining_amount_and_allows_editing(decimal total, decimal paid, decimal outstanding, string expected)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IJSRuntime, NoJs>();
        services.AddSingleton(new AuthenticatedApiClient(new HttpClient(new Transport()) { BaseAddress = new Uri("https://test.invalid") }, NullLogger<ApiClient>.Instance));
        services.AddSingleton<FinanceCycleApi>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var invoice = new HouseholdInvoice(Guid.NewGuid(), "INV-800", "A-101", "Cư dân", new(2026, 10, 1), new(2026, 10, 31), total, paid, outstanding);
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<PropFlow.Web.Client.Features.Resident.Portal.Components.ResidentPaymentDialog>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Invoice"] = invoice })));
        var html = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Assert.Contains("Bạn đã thanh toán bao nhiêu?", html);
        var input = System.Text.RegularExpressions.Regex.Match(html, "<input[^>]*aria-label=\"Số tiền đã chuyển\"[^>]*>").Value;
        Assert.Contains($"value=\"{expected}\"", input);
        Assert.DoesNotContain("readonly", input);
        Assert.DoesNotContain("disabled", input);
        Assert.Contains("Gửi kế toán kiểm tra", html);
    }

    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private sealed class Transport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("Popup initialization must not submit a payment.");
    }
}