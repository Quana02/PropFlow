using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.Resident.Portal.Components;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

public sealed class ResidentPaymentCardTests
{
    [Fact]
    public async Task AutomaticCardShowsTransferMemoAndDoesNotRequireResidentConfirmation()
    {
        var html = await RenderAsync(View(automatic: true, remaining: 75000));
        Assert.Contains("Bạn không cần báo chuyển khoản", html);
        Assert.Contains("PF1234567890123456789012", html);
        Assert.Contains("Đã ghi nhận một phần", html);
        Assert.Contains("data:image/png;base64,", html);
    }

    [Fact]
    public async Task PaidCardHidesQrAndReportingForm()
    {
        var html = await RenderAsync(View(automatic: true, remaining: 0));
        Assert.Contains("Đã thanh toán đầy đủ", html);
        Assert.DoesNotContain("payment-qr", html);
        Assert.DoesNotContain("Mã giao dịch ngân hàng", html);
    }

    [Fact]
    public async Task ManualPendingCardWarnsAgainstPayingAgain()
    {
        var html = await RenderAsync(View(automatic: false, remaining: 100000) with { PaymentStatus = "PENDING", QrImageUrl = null });
        Assert.Contains("Đang chờ kế toán kiểm tra", html);
        Assert.Contains("Không cần chuyển lại", html);
        Assert.DoesNotContain("payment-qr", html);
    }

    [Fact]
    public async Task SandboxShowsSimulationAndSuppressesRealQrEvenIfAUrlIsPresent()
    {
        var html = await RenderAsync(View(false, 100000) with { PaymentEnvironment = "SANDBOX", SandboxSimulationAvailable = true });
        Assert.Contains("Lấy nội dung thanh toán SePay", html);
        Assert.Contains("KHÔNG CHUYỂN TIỀN THẬT", html);
        Assert.DoesNotContain("payment-qr", html);
        Assert.DoesNotContain("NGUYEN VAN A", html);
        Assert.DoesNotContain("Báo chuyển khoản cho kế toán", html);
    }

    [Fact]
    public async Task LiveNeverDisplaysSimulationControls()
    {
        var html = await RenderAsync(View(true, 100000));
        Assert.DoesNotContain("giả lập", html);
        Assert.DoesNotContain("SANDBOX", html);
        Assert.Contains("payment-qr", html);
    }

    [Fact]
    public async Task SimulationSuccessIsDistinctFromConfirmedPaymentAndRating()
    {
        var html = await RenderAsync(View(false, 0) with { PaymentEnvironment = "SANDBOX", SimulatedTransferAmount = 100000 });
        Assert.Contains("SePay đã xác nhận giao dịch thử", html);
        Assert.Contains("Đã thanh toán trong Sandbox", html);
        Assert.Contains("Đã thanh toán (Sandbox)", html);
        Assert.DoesNotContain("Bạn có thể đánh giá dịch vụ", html);
        Assert.DoesNotContain("Đã thanh toán đầy đủ", html);
    }

    private static ResidentServiceRequestPaymentView View(bool automatic, decimal remaining) => new(Guid.NewGuid(), Guid.NewGuid(), "DV-TEST", 100000,
        100000 - remaining, remaining, remaining == 0 ? "PAID" : remaining < 100000 ? "PARTIAL" : "UNPAID", null, null, null, null,
        "Vietcombank", "VCB", "******6789", "NGUYEN VAN A", remaining == 0 ? null : "/qr", automatic, "PF1234567890123456789012");

    private static async Task<string> RenderAsync(ResidentServiceRequestPaymentView view)
    {
        using var services = new ServiceCollection().AddLogging().AddSingleton<IResidentServiceRequestApiClient>(new Api(view)).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<ResidentServiceRequestPaymentCard>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { ["RequestId"] = view.ServiceRequestId, ["Completed"] = true }));
            return WebUtility.HtmlDecode(component.ToHtmlString());
        });
    }
    private sealed class Api(ResidentServiceRequestPaymentView view) : IResidentServiceRequestApiClient
    {
        public Task<ApiResult<ResidentServiceRequestPaymentView>> SimulateTransferAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(new ApiResult<ResidentServiceRequestPaymentView>(true, view with { SimulatedTransferAmount = view.OutstandingAmount }));
        public Task<ApiResult<ResidentServiceRequestPaymentView>> GetPaymentAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(new ApiResult<ResidentServiceRequestPaymentView>(true, view));
        public Task<ApiResult<ApiFileContent>> GetPaymentQrAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(new ApiResult<ApiFileContent>(true, new([137,80,78,71], "image/png")));
        public Task<ApiResult<IReadOnlyList<ResidentServiceRequestListItem>>> ListAsync(ResidentServiceRequestListQuery? query = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult<ResidentServiceRequestDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult<ResidentServiceRequestCreatedResponse>> CreateAsync(CreateResidentServiceRequestRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult<ResidentServiceRequestFeedbackResponse>> RateAsync(Guid id, RateResidentServiceRequestRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ApiResult<ResidentServiceRequestPaymentView>> ReportTransferAsync(Guid id, ReportResidentServiceRequestTransferRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
