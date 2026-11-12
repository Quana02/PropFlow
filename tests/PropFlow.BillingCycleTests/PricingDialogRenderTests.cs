using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Finance.Components;
using PropFlow.Web.Client.Features.Finance.Pages;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.BillingCycleTests;

public sealed class PricingDialogRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pricing_forms_render_grouped_money_and_explicit_submit_and_cancel(bool rental)
    {
        var services = new ServiceCollection().AddSingleton<PropFlow.Web.Client.Shared.Feedback.ToastService>().AddLogging();
        services.AddSingleton(new FinanceCycleApi(new AuthenticatedApiClient(
            new HttpClient(new PreviewTransport()) { BaseAddress = new("https://test.invalid") }, NullLogger<ApiClient>.Instance)));
        services.AddSingleton<IJSRuntime, NoJs>();
        services.AddSingleton<IComponentActivator>(new PreviewActivator(rental));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ServiceFeeManagement>());
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Assert.Contains("pricing-dialog-body", html);
        Assert.Contains("pricing-form-actions", html);
        Assert.Contains("type=\"submit\"", html);
        Assert.Contains(rental ? "5.000.000" : "10.000", html);
        Assert.DoesNotContain("không dùng dấu phân cách", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("Tự động phân cách", WebUtility.HtmlDecode(html));
        Assert.Contains(rental ? "Nhập giá thuê mỗi tháng" : "Nhập đơn giá nước", WebUtility.HtmlDecode(html));
        if (Environment.GetEnvironmentVariable("PROPFLOW_PRICING_PREVIEW") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, rental ? "rental-preview.html" : "service-preview.html"),
                "<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                "<link rel=\"stylesheet\" href=\"https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&amp;family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@24,400,0,0\">" +
                "<link rel=\"stylesheet\" href=\"pricing-preview.css\"><style>body{margin:0;background:#eef3f8;font-family:Inter,Segoe UI,sans-serif}*{box-sizing:border-box}.material-symbols-outlined{font-family:'Material Symbols Outlined';font-weight:normal;font-style:normal;line-height:1;letter-spacing:normal;text-transform:none;white-space:nowrap;word-wrap:normal;direction:ltr;font-feature-settings:'liga'}</style><body>" + html + "</body></html>");
        }
    }

    private sealed class PreviewActivator(bool rental) : IComponentActivator
    {
        public IComponent CreateInstance(Type type)
        {
            var instance = (IComponent)Activator.CreateInstance(type)!;
            void Set(string name, object value) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(instance, value);
            if (type == typeof(ServiceFeeManagement)) { Set("serviceDialog", !rental); Set("waterRate", 10000m); }
            if (rental && type == typeof(RentalPricingEditor)) { Set("open", true); Set("amount", 5000000m); }
            return instance;
        }
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private sealed class PreviewTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object data = request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/billing/cycle/rates" => new[] { new ServiceRateRow(Guid.Empty, 10000m, new(2026, 10, 1), Guid.Empty, ElectricityUnitRate: 3000m, WaterM3UnitRate: 10000m) },
                "/api/v1/billing/cycle/invoices" => Array.Empty<HouseholdInvoice>(),
                "/api/v1/apartment-unit-types" => new[] { new { id = Guid.NewGuid(), name = "Căn hộ thông thường" }, new { id = Guid.NewGuid(), name = "Studio" }, new { id = Guid.NewGuid(), name = "Officetel" }, new { id = Guid.NewGuid(), name = "Penthouse" } },
                _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data, data.GetType()) });
        }
    }
}
