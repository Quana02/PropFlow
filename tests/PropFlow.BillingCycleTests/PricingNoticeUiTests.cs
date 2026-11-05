using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Components;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Billing.Domain.PricingChanges;

namespace PropFlow.BillingCycleTests;
public sealed class PricingNoticeUiTests
{
    [Theory]
    [InlineData(true, "ASSET_STATUS")]
    [InlineData(false, "ASSET_STATUS")]
    [InlineData(true, "MAINTENANCE")]
    [InlineData(false, "MAINTENANCE")]
    [InlineData(true, "PRICING")]
    [InlineData(false, "PRICING")]
    public async Task Management_notice_updates_existing_render_without_reload(bool compact, string source)
    {
        var state = new ResidentFinanceNoticeState(new FinanceCycleApi(new AuthenticatedApiClient(new HttpClient(), NullLogger<ApiClient>.Instance)));
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(state); services.AddSingleton<NavigationManager, Navigation>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ResidentFinanceNotifications>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Compact"] = compact, ["Cards"] = !compact })));
        var before = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        var title = source == "PRICING" ? "Thông báo đơn giá dịch vụ, nước và điện" : "Thang máy A đang bảo trì";
        var message = source == "PRICING" ? "Nước: 12.000 đ/m³; Điện: 3.500 đ/kWh; áp dụng từ 01/11/2026." : "Thiết bị tại sảnh A đang sửa chữa.";
        Assert.DoesNotContain(title, before);
        var notice = new ResidentNotice(Guid.NewGuid(), title, message, "/resident?tab=notifications", false, DateTimeOffset.UtcNow, source);
        await renderer.Dispatcher.InvokeAsync(() => state.Receive(notice));
        var after = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Assert.Contains(title, after); Assert.Contains(message, after); Assert.Contains("Ban quản lý", after);
        Assert.DoesNotContain("Xem hóa đơn / phiếu thu", after);
        Assert.Equal(1, state.UnreadCount);
        await renderer.Dispatcher.InvokeAsync(() => state.Receive(notice));
        Assert.Single(state.Items);
        Assert.Equal(1, state.UnreadCount);
    }
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Pricing_notice_is_visible_in_both_dashboard_surfaces_with_correct_sender(bool compact, bool pricing)
    {
        var api = new FinanceCycleApi(new AuthenticatedApiClient(new HttpClient(), NullLogger<ApiClient>.Instance));
        var state = new ResidentFinanceNoticeState(api);
        state.Receive(new(Guid.NewGuid(), pricing ? "Thông báo đơn giá thuê căn hộ" : "Hóa đơn tháng mới",
            "Nước 12.000 đ/m³; hiệu lực 01/11/2026", pricing ? "/resident?tab=notifications" : "/resident/billing",
            false, DateTimeOffset.UtcNow, pricing ? "PRICING" : "INVOICE"));
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(state); services.AddSingleton<NavigationManager, Navigation>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<ResidentFinanceNotifications>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Compact"] = compact, ["Cards"] = !compact })));
        var html = await renderer.Dispatcher.InvokeAsync(() => WebUtility.HtmlDecode(root.ToHtmlString()));
        Assert.Contains("12.000 đ/m³", html);
        Assert.Contains(pricing ? "Ban quản lý" : "Tài chính", html);
        if (pricing) { Assert.DoesNotContain("Xem hóa đơn / phiếu thu", html); Assert.DoesNotContain("Ban Kế toán", html); }
        else if (!compact) Assert.Contains("Xem hóa đơn / phiếu thu", html);
    }
    [Fact]
    public void Pricing_messages_show_whole_vnd_units_effective_date_and_rental_scope()
    {
        var actor = Guid.NewGuid(); var date = new DateOnly(2026, 11, 1);
        var service = PricingChangeMessages.Service(new(10000, date, null, 3500, 12000), actor);
        Assert.Contains("12.000 đ/m³", service.Message); Assert.Contains("3.500 đ/kWh", service.Message);
        Assert.Contains("01/11/2026", service.Message);
        var rental = PricingChangeMessages.Rental(new(Guid.Empty, "MONTHLY", 6000000, date, Guid.NewGuid()), "2 phòng ngủ", actor);
        Assert.Contains("2 phòng ngủ: 6.000.000 đ/tháng", rental.Message);
        Assert.Contains("căn chưa có chủ sở hữu", rental.Message);
        var item = new PricingChange("Title", "Message", actor, DateTimeOffset.UtcNow);
        for (var i = 0; i < 8; i++) item.Failed("Simulated", DateTimeOffset.UtcNow);
        Assert.Equal("Failed", item.Status); Assert.Equal(8, item.Attempts);
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://test.invalid/", "https://test.invalid/resident");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
