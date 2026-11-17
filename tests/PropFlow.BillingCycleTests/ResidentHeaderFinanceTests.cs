using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Components;
using PropFlow.Web.Client.Features.Resident.Portal.Models;
using PropFlow.Web.Client.Features.Resident.Shared.Models;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.BillingCycleTests;

#pragma warning disable BL0006
public sealed class ResidentHeaderFinanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Header_uses_authorized_service_balance_for_current_apartment_and_does_not_fake_a_wallet(bool failure)
    {
        var transport = new Transport(failure);
        var client = new HttpClient(transport) { BaseAddress = new("https://test.invalid") };
        var api = new AuthenticatedApiClient(client, NullLogger<ApiClient>.Instance);
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(api);
        services.AddSingleton(new FinanceCycleApi(api));
        services.AddSingleton(new AuthSession(api));
        services.AddSingleton<NavigationManager, Navigation>();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddSingleton<PropFlow.Web.Client.Features.Resident.Portal.Services.ResidentFinanceNoticeState>();
        services.AddSingleton<PropFlow.Web.Client.Features.Resident.Portal.Services.ResidentNotificationConnectionFactory>();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HeaderRenderer(provider);
        var header = await renderer.MountAsync();
        Assert.Null(transport.Path);
        await renderer.OpenProfileAsync(header);
        var html = await renderer.ContentAsync(header);
        Assert.Contains("resident-header", html);
        Assert.Contains("Phí quản lý cần thanh toán", html);
        Assert.Contains("Thanh toán ngay", html);
        Assert.DoesNotContain("12.500.000", html);
        Assert.DoesNotContain("1.850.000", html);
        Assert.Equal("/api/v1/resident/billing/invoices", transport.Path);
        if (!failure) Assert.Contains("600.000 đ", html);
        else Assert.DoesNotContain("600.000 đ", html);
    }

    private sealed class HeaderRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Header rendering failed.", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

        public Task<int> MountAsync() => Dispatcher.InvokeAsync(async () =>
        {
            var root = AssignRootComponentId(InstantiateComponent(typeof(HeaderHarness)));
            await RenderRootComponentAsync(root, ParameterView.Empty);
            var context = Frames(root).Single(frame => frame.FrameType == RenderTreeFrameType.Component).ComponentId;
            return Frames(context).Single(frame => frame.FrameType == RenderTreeFrameType.Component).ComponentId;
        });

        public Task OpenProfileAsync(int header) => Dispatcher.InvokeAsync(() =>
        {
            var frames = Frames(header);
            var trigger = Array.FindIndex(frames, frame => frame.FrameType == RenderTreeFrameType.Attribute &&
                frame.AttributeName == "id" && Equals(frame.AttributeValue, "profileDropdownBtn"));
            Assert.True(trigger >= 0);
            var click = frames.Skip(trigger + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute)
                .Single(frame => frame.AttributeName == "onclick");
            return DispatchEventAsync(click.AttributeEventHandlerId, null, new MouseEventArgs());
        });

        public Task<string> ContentAsync(int header) => Dispatcher.InvokeAsync(() =>
            WebUtility.HtmlDecode(string.Concat(Frames(header).Select(frame => frame.FrameType switch
            {
                RenderTreeFrameType.Markup => frame.MarkupContent,
                RenderTreeFrameType.Text => frame.TextContent,
                RenderTreeFrameType.Attribute when frame.AttributeName == "class" => frame.AttributeValue?.ToString(),
                _ => null
            }))));

        private RenderTreeFrame[] Frames(int componentId)
        {
            var tree = GetCurrentRenderTreeFrames(componentId);
            return tree.Array[..tree.Count];
        }
    }

    public sealed class HeaderHarness : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            var profile = new ResidentDetailResponse(Guid.NewGuid(), "R-TEST", "Cư dân kiểm thử", null, null, null,
                null, null, null, null, null, null, null, "ACTIVE", true,
                [new(Guid.NewGuid(), Guid.NewGuid(), "A-101", 1, new(2020, 1, 1), null)], []);
            builder.OpenComponent<CascadingValue<ResidentPortalProfileContext>>(0);
            builder.AddAttribute(1, "Value", new ResidentPortalProfileContext(profile, false, null));
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(child => { child.OpenComponent<ResidentHeader>(0); child.CloseComponent(); }));
            builder.CloseComponent();
        }
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://test.invalid/", "https://test.invalid/resident");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class Transport(bool failure) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath != "/api/v1/resident/billing/invoices")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            Path = request.RequestUri.AbsolutePath;
            HouseholdInvoice Row(string unit, string type, decimal amount) => new(Guid.NewGuid(), "HD-TEST", unit, "Cư dân", new(2026, 10, 1), new(2026, 10, 10), amount, 0, amount, InvoiceType: type);
            return Task.FromResult(new HttpResponseMessage(failure ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = failure ? JsonContent.Create(new { title = "Không thể tải" })
                    : JsonContent.Create(new[] { Row("A-101", "SERVICE_FEE", 600000), Row("A-101", "MONTHLY_RENTAL", 5000000), Row("B-102", "SERVICE_FEE", 999000) })
            });
        }
    }
}
#pragma warning restore BL0006

