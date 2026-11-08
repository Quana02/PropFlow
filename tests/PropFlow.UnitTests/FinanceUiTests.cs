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
namespace PropFlow.UnitTests;
public sealed class FinanceUiTests
{
    private static readonly Guid Id=Guid.NewGuid();
    [Theory]
    [InlineData(true, null, "Hóa đơn và công nợ")]
    [InlineData(false, null, "Hóa đơn và công nợ")]
    [InlineData(true, "OUTSTANDING", "Hóa đơn chưa thanh toán")]
    [InlineData(true, "OVERDUE", "Công nợ quá hạn")]
    public async Task Invoice_view_shows_source_lines_print_and_only_authorized_write_controls(bool canWrite, string? settlement, string title)
    {
        var services=new ServiceCollection().AddSingleton<PropFlow.Web.Client.Shared.Feedback.ToastService>().AddLogging();
        services.AddSingleton<IJSRuntime,NoJs>();
        services.AddSingleton(new AuthenticatedApiClient(new HttpClient(new Transport()){BaseAddress=new Uri("https://test.invalid")},NullLogger<ApiClient>.Instance));
        services.AddSingleton<BillingWorkspaceApi>();services.AddSingleton<IFinanceApiClient,FinanceApiClient>();
        await using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var root=await renderer.Dispatcher.InvokeAsync(()=>renderer.RenderComponentAsync<InvoiceWorkspace>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"CanWrite",canWrite},{"InitialInvoiceId",Id},{"InitialSettlement",settlement}})));
        var html=await renderer.Dispatcher.InvokeAsync(()=>WebUtility.HtmlDecode(root.ToHtmlString()));
        Assert.Matches("<h1[^>]*>" + System.Text.RegularExpressions.Regex.Escape(title) + "</h1>",html); Assert.Contains("summary-card",html); Assert.Contains("billing-breadcrumb",html); Assert.Contains("tabindex=\"-1\"",html); Assert.Contains("A-101",html);Assert.Contains("Phí quản lý",html);Assert.Contains("Diện tích đã kiểm tra",html);
        Assert.Contains("In / Lưu PDF",html);Assert.Contains("Xuất báo cáo Excel",html);
        Assert.Equal(canWrite,html.Contains("Ghi nhận chờ xác nhận"));Assert.Equal(canWrite,html.Contains("Tạo hóa đơn nháp"));
        if(Environment.GetEnvironmentVariable("PROPFLOW_FINANCE_QA_OUTPUT") is { Length: > 0 } output && canWrite)
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output,"finance-ui.html"),"<!doctype html><html lang='vi'><meta charset='utf-8'><link rel='stylesheet' href='finance-ui.css'><body>"+await renderer.Dispatcher.InvokeAsync(()=>root.ToHtmlString())+"</body></html>");
        }
    }
    private sealed class NoJs:IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string name,object?[]? args)=>ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string name,CancellationToken ct,object?[]? args)=>ValueTask.FromResult(default(T)!);
    }
    private sealed class Transport:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var invoice=new InvoiceView(Id,"INV-TEST",Guid.NewGuid(),"A-101",new(2026,1,1),new(2026,1,31),new(2026,2,1),new(2026,2,10),"ISSUED",12346,3000,9346,"PARTIAL",DateTimeOffset.UtcNow,Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),null,
                [new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"MANAGEMENT","Phí quản lý","AREA",12.3456m,"m²",1000,12346,"Diện tích đã kiểm tra")],[]);
            var path=request.RequestUri!.AbsolutePath;
            object result=path switch {
                "/api/v1/billing/workspace/apartments"=>new BillingApartment[]{new(invoice.ApartmentUnitId,"A-101")},
                "/api/v1/billing/workspace/rates"=>Array.Empty<RateView>(),
                "/api/v1/billing/workspace/invoices"=>new InvoicePage(1,1,20,12346,3000,9346,[invoice]),
                _ when path=="/api/v1/billing/workspace/invoices/"+Id=>invoice,
                _ when path=="/api/v1/payments/by-invoice/"+Id=>Array.Empty<PaymentRow>(),
                _=>throw new InvalidOperationException(path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(result,result.GetType())});
        }
    }
}
