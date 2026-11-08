using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Web.Client.Features.AiChatbot.Services;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-12-ScopeExtension-Knowledge")]
public sealed class KnowledgeDocumentsUiTests
{
    [Theory]
    [InlineData("ADMIN", true)] [InlineData("MANAGER", true)] [InlineData("STAFF", false)]
    public async Task StaffReadsWhileAdminAndManagerCanCreate(string role, bool create)
    {
        var html = await Render(role, false);
        Assert.Contains("Tài liệu chung cư", html);
        Assert.Equal(create, html.Contains("+ Thêm tài liệu"));
        Assert.Contains(create ? "Chưa có tài liệu." : "Chưa có tài liệu được xuất bản.", html);
    }
    [Fact]
    public async Task ApiErrorIsVisibleAndNeverShownAsEmptySuccess()
    {
        var html = await Render("STAFF", true);
        Assert.Contains("Bạn không có quyền thực hiện thao tác này.", html);
        Assert.DoesNotContain("Chưa có tài liệu được xuất bản.", html);
    }
    private static async Task<string> Render(string role, bool forbidden)
    {
        using var services = new ServiceCollection().AddSingleton<PropFlow.Web.Client.Shared.Feedback.ToastService>().AddLogging()
            .AddSingleton<AuthenticationStateProvider>(new Authentication(role))
            .AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton<AuthenticatedApiClient>(sp => new(new HttpClient(new Response(forbidden)) { BaseAddress = new("https://test.invalid") }, sp.GetRequiredService<ILogger<ApiClient>>()))
            .AddSingleton<KnowledgeApiClient>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode((await renderer.RenderComponentAsync<PropFlow.Web.Client.Features.AiChatbot.Pages.KnowledgeDocuments>(ParameterView.Empty)).ToHtmlString()));
    }
    private sealed class Authentication(string role) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([
            new(ClaimTypes.Role, role), new("permission", role switch { "ADMIN" => "MANAGE_INTERNAL_ACCOUNTS", "MANAGER" => "MANAGE_OPERATIONS", _ => "PERFORM_ASSIGNED_OPERATIONS" })], "test"))));
    }
    private sealed class Response(bool forbidden) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(forbidden ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            { Content = forbidden ? JsonContent.Create(new { title = "Bạn không có quyền thực hiện thao tác này.", code = "forbidden" }) : JsonContent.Create(new KnowledgeDocumentPage([], 0, 1, 12)) });
    }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new InvalidOperationException("Không gọi JavaScript khi tải trang.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
