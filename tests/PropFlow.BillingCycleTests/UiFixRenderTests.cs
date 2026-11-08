using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Features.Resident.Management.Pages;
using PropFlow.Web.Client.Features.Resident.Shared.Models;
using PropFlow.Web.Client.Features.Resident.Shared.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Forms;

namespace PropFlow.BillingCycleTests;

public sealed class UiFixRenderTests
{
    [Fact]
    public async Task Resident_paging_and_calendar_render_with_accessible_controls()
    {
        var services = new ServiceCollection().AddSingleton<PropFlow.Web.Client.Shared.Feedback.ToastService>().AddLogging();
        var http = new HttpClient(new Transport()) { BaseAddress = new("https://test.invalid") };
        services.AddSingleton(new ApiClient(http, NullLogger<ApiClient>.Instance));
        services.AddSingleton(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance));
        services.AddSingleton<AuthSession>();
        services.AddSingleton<IResidentApiClient, ResidentApiClient>();
        services.AddSingleton<NavigationManager, Navigation>();
        services.AddSingleton<IJSRuntime, NoJs>();
        services.AddSingleton<IComponentActivator, Activator>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var residents = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ResidentManagement>());
        var residentHtml = await renderer.Dispatcher.InvokeAsync(residents.ToHtmlString);
        Assert.Contains("resident-pagination", residentHtml);
        Assert.Contains("Trang 1 / 3", residentHtml);
        var date = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<PropFlowDatePicker>(ParameterView.FromDictionary(
            new Dictionary<string, object?> { ["Value"] = new DateOnly(2026,10,16), ["AriaLabel"] = "Ngày bắt đầu bảo trì" })));
        var dateHtml = await renderer.Dispatcher.InvokeAsync(date.ToHtmlString);
        Assert.True(dateHtml.Contains("pf-date__days"), dateHtml);
        Assert.Equal(42, Regex.Matches(dateHtml, "aria-label=\"[0-9]{2}/[0-9]{2}/2026\"").Count);
        Assert.Contains("Chọn năm", WebUtility.HtmlDecode(dateHtml));
        if (Environment.GetEnvironmentVariable("PROPFLOW_UI_FIX_PREVIEW") is { Length: > 0 } output)
        {
            var root = Path.GetFullPath(Path.Combine(output, "../.."));
            var css = await File.ReadAllTextAsync(Path.Combine(root,"src/PropFlow.Web.Client/Shared/Forms/PropFlowDatePicker.razor.css"));
            css += await File.ReadAllTextAsync(Path.Combine(root,"src/PropFlow.Web.Client/Features/Resident/Management/Pages/ResidentManagement.razor.css"));
            var pager = Regex.Match(residentHtml,"<nav class=\"resident-pagination\"[\\s\\S]*?</nav>").Value;
            await File.WriteAllTextAsync(Path.Combine(output,"ui-preview.html"),
                "<!doctype html><html lang='vi'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
                "<link rel='stylesheet' href='https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined'><style>" + css + "body{margin:0;padding:24px;font-family:Segoe UI,sans-serif;background:#f0f5fb}h1{font-size:20px}main{max-width:800px;margin:auto}.preview-card{padding:24px;background:white;border-radius:16px;margin:20px 0}.date-field{width:154px}.material-symbols-outlined{font-family:'Material Symbols Outlined';font-weight:normal;font-style:normal;line-height:1;letter-spacing:normal;text-transform:none;white-space:nowrap;word-wrap:normal;font-feature-settings:'liga'}.preview-card:first-of-type{min-height:520px}.pf-date__calendar button{font-family:inherit}</style>" +
                "<main><h1>Kiểm tra giao diện</h1><section class='preview-card'><h2>Ngày bảo trì</h2><div class='date-field'>" + dateHtml +
                "</div></section><section class='preview-card'><h2>Phân trang cư dân</h2>" + pager + "</section></main>" +
                "<script type='module'>import{open}from'./PropFlowDatePicker.razor.js';open(document.querySelector('.pf-date'),{invokeMethodAsync(){}});</script></html>");
            File.Copy(Path.Combine(root,"src/PropFlow.Web.Client/Shared/Forms/PropFlowDatePicker.razor.js"),Path.Combine(output,"PropFlowDatePicker.razor.js"),true);
        }
    }

    [Theory]
    [InlineData("PUMP", "Máy bơm nước")]
    [InlineData("ELECTRICAL_PANEL", "Tủ điện")]
    [InlineData("Thang cuốn", "Thang cuốn")]
    public void Equipment_types_show_Vietnamese_and_preserve_custom_names(string name, string display) => Assert.Equal(display,EquipmentTypeText.Display(name));

    private sealed class Activator : IComponentActivator
    {
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)System.Activator.CreateInstance(type)!;
            if (type == typeof(PropFlowDatePicker)) type.GetField("open",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(component,true);
            return component;
        }
    }
    private sealed class Navigation : NavigationManager { public Navigation() => Initialize("https://test.invalid/","https://test.invalid/residents"); }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string name,object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string name,CancellationToken ct,object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    private sealed class Transport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            object result = request.RequestUri!.AbsolutePath == "/api/v1/residents"
                ? new PagedResidentsResponse([new(Guid.NewGuid(),"CD-0001","Nguyễn Văn An","0900000001","demo@anphu.example","ACTIVE",false,[])],46,1,20)
                : Array.Empty<ActiveApartmentOption>();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result,result.GetType()) });
        }
    }
}


