using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Authentication.Models;
using PropFlow.Web.Client.Features.Authentication.Pages;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

public sealed class LogoutRenderingTests
{
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("https://test.invalid/", "https://test.invalid/login");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    public sealed class LoginProbe : Login
    {
        [Parameter] public Action<LoginProbe>? Capture { get; set; }
        protected override void OnInitialized() => Capture!(this);
        public Task SubmitForm() => (Task)typeof(Login).GetMethod("Submit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null)!;
        public void InputAndRender(string username, string password)
        {
            var model = (LoginForm)typeof(Login).GetField("form", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
            model.Username = username;
            model.Password = password;
            StateHasChanged();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incorrect_login_and_network_failure_still_publish_error_toasts(bool networkFailure)
    {
        using var http = new HttpClient(new Transport(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("csrf"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new CsrfResponse("csrf")) });
            if (networkFailure) throw new HttpRequestException("API unavailable");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = JsonContent.Create(new { title = "Thông tin đăng nhập không đúng." }) });
        })) { BaseAddress = new("https://test.invalid/") };
        var session = new AuthSession(new ApiClient(http, NullLogger<ApiClient>.Instance));
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton(session).AddSingleton<ToastService>().AddSingleton<NavigationManager, Navigation>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        LoginProbe? login = null;
        await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<LoginProbe>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(LoginProbe.Capture)] = (Action<LoginProbe>)(value => login = value) })));
        await renderer.Dispatcher.InvokeAsync(() => login!.InputAndRender("resident", "incorrect"));
        await renderer.Dispatcher.InvokeAsync(() => login!.SubmitForm());
        var message = Assert.Single(services.GetRequiredService<ToastService>().Messages);
        Assert.Equal(networkFailure ? "Không kết nối được máy chủ. Vui lòng kiểm tra kết nối." : "Thông tin đăng nhập không đúng.", message.Message);
        Assert.Null(session.User);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_rendered_while_logout_pending_stays_silent_on_success_and_retains_failure_retry(bool serverFailure)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Transport(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("csrf"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new CsrfResponse("csrf")) };
            started.TrySetResult();
            await release.Task;
            return new(serverFailure ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.NoContent);
        })) { BaseAddress = new("https://test.invalid/") };
        var session = new AuthSession(new ApiClient(http, NullLogger<ApiClient>.Instance));
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton(session).AddSingleton<ToastService>().AddSingleton<NavigationManager, Navigation>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var logout = session.LogoutAsync();
        await started.Task;
        LoginProbe? login = null;
        var view = await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<LoginProbe>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(LoginProbe.Capture)] = (Action<LoginProbe>)(value => login = value) })));
        Assert.DoesNotContain("role=\"status\"", await renderer.Dispatcher.InvokeAsync(view.ToHtmlString));
        release.TrySetResult();
        await logout;
        // Real Login markup is rerendered with username first, password first, both
        // autofill values, and unchanged values (the state seen on blur/rerender).
        // Native browser autofill/blur event delivery still requires browser QA.
        foreach (var (username, password) in new[] { ("resident", ""), ("", "password"), ("resident", "password"), ("resident", "password") })
        {
            await renderer.Dispatcher.InvokeAsync(() => login!.InputAndRender(username, password));
            var html = await renderer.Dispatcher.InvokeAsync(view.ToHtmlString);
            if (serverFailure)
            {
                Assert.Contains("role=\"status\"", html);
                Assert.Contains("RetryLogout", typeof(Login).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Select(method => method.Name));
                Assert.Contains("class=\"secondary\"", html);
            }
            else
            {
                Assert.Null(session.Notice);
                Assert.DoesNotContain("role=\"status\"", html);
                Assert.DoesNotContain("class=\"secondary\"", html);
            }
        }
        Assert.Empty(services.GetRequiredService<ToastService>().Messages);
    }
}
