using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Web.Client.Features.AiChatbot.Components;
using PropFlow.Web.Client.Features.AiChatbot.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-12")]
public sealed class ResidentChatWidgetTests
{
    [Fact]
    public async Task ResidentCanOpenAskReadEscapedExcerptsAndClearTheSession()
    {
        var capture = new Capture();
        using var services = new ServiceCollection().AddSingleton<PropFlow.Web.Client.Shared.Feedback.ToastService>().AddLogging().AddSingleton<IComponentActivator>(capture)
            .AddSingleton<AuthenticatedApiClient>(sp => new(new HttpClient(new Responses()) { BaseAddress = new("https://test.invalid") }, sp.GetRequiredService<ILogger<ApiClient>>()))
            .AddSingleton<ResidentChatApiClient>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<ResidentChatWidget>(ParameterView.Empty);
            Assert.DoesNotContain("Trợ lý tài liệu</h2>", root.ToHtmlString());
            var widget = capture.Widget!;
            await Invoke(widget, "Toggle"); Refresh(widget);
            Assert.Contains("Trợ lý tài liệu", WebUtility.HtmlDecode(root.ToHtmlString()));
            typeof(ResidentChatWidget).GetField("question", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(widget, "Chi phí phát sinh?");
            await Invoke(widget, "Send"); Refresh(widget);
            var html = root.ToHtmlString();
            Assert.Contains("Chưa cấu hình AI", WebUtility.HtmlDecode(html));
            Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>", html);
            Assert.Contains("Đọc tài liệu nguồn", WebUtility.HtmlDecode(html));
            Assert.Contains("v1", WebUtility.HtmlDecode(html));
            Assert.DoesNotContain("v@citation.Revision", html);
            typeof(ResidentChatWidget).GetMethod("Clear", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(widget, null); Refresh(widget);
            Assert.DoesNotContain("Chưa cấu hình AI", WebUtility.HtmlDecode(root.ToHtmlString()));
        });
    }
    private static Task Invoke(object component, string method) =>
        (Task)component.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null)!;
    private static void Refresh(ComponentBase component) => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
    private sealed class Capture : IComponentActivator
    {
        public ResidentChatWidget? Widget;
        public IComponent CreateInstance(Type type)
        { var component = (IComponent)Activator.CreateInstance(type)!; if (component is ResidentChatWidget widget) Widget = widget; return component; }
    }
    private sealed class Responses : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.Method == HttpMethod.Get
                ? JsonContent.Create(new ResidentChatStatus(false))
                : JsonContent.Create(new ResidentChatReply("DOCUMENTS_ONLY", "Chưa cấu hình AI. Đây là trích đoạn.",
                    [new(1, Guid.NewGuid(), 1, 1, "Nội quy", "<script>alert('untrusted')</script>")])) });
    }
}
