using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Communication.Contracts;
using PropFlow.Web.Client.Features.Communication.SupportChat;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-13-ScopeExtension-SupportChat")]
public sealed class SupportChatUiTests
{
    [Fact]
    public async Task ResidentSeesGreetingAndDraftDisclosureBeforeSending()
    {
        await using var fixture = new RenderFixture(false, false);
        var html = await fixture.Render();
        Assert.Contains("Bạn cần Ban quản lý hỗ trợ việc gì?", html);
        Assert.Contains("trước khi bấm Gửi", html);
        Assert.DoesNotContain("Cư dân đang soạn — chưa gửi", html);
        Assert.DoesNotContain(fixture.Transport.DraftText!, html);
    }

    [Fact]
    public async Task ManagerSeesSeparateEscapedUnsentDraftAndSavedMessages()
    {
        await using var fixture = new RenderFixture(true, true);
        var html = await fixture.Render(raw: true);
        Assert.Contains("Cư dân đang soạn — chưa gửi", WebUtility.HtmlDecode(html));
        Assert.Contains("&lt;b&gt;", html); Assert.DoesNotContain("<b>PRIVATE_DRAFT", html);
        Assert.Contains("TIN_DA_GUI", html);
        fixture.Transport.DraftText = null;
        var cleared = await fixture.RenderAgain();
        Assert.DoesNotContain("PRIVATE_DRAFT", cleared); Assert.Contains("TIN_DA_GUI", cleared);
    }

    [Fact]
    public async Task ManagerInboxShowsActiveConversationWithoutAResidentOnlineIndicator()
    {
        await using var fixture = new RenderFixture(true, true);
        var html = await fixture.Render();
        Assert.Contains("Hộp thư của bạn", html);
        Assert.Contains("class=\"inbox-avatar\"", html);
        Assert.Contains("aria-current=\"true\"", html);
        Assert.Contains("Bạn: Tin gần nhất", html);
        Assert.Matches("class=\"inbox-status\"[^>]*>Đang trao đổi", html);
        Assert.DoesNotContain("Phân trang hội thoại", html);
    }

    [Fact]
    public async Task ContinuousTypingPublishesBeforeTheResidentStopsAndNeverPostsUnsentText()
    {
        await using var fixture = new RenderFixture(false, true);
        await fixture.Render();
        using var producer = new CancellationTokenSource();
        var typing = Task.Run(async () => {
            try
            {
                for (var i = 0; i < 100; i++)
                {
                    producer.Token.ThrowIfCancellationRequested();
                    await fixture.Renderer.Dispatcher.InvokeAsync(() => fixture.Widget!.GetType().GetMethod("Input", BindingFlags.NonPublic | BindingFlags.Instance)!
                        .Invoke(fixture.Widget, [new ChangeEventArgs { Value = "Đang gõ liên tục " + i }]));
                    await Task.Delay(50, producer.Token);
                }
            }
            catch (OperationCanceledException) when (producer.IsCancellationRequested) { }
        });
        try
        {
            var draft = await fixture.Transport.FirstDraft.Task.WaitAsync(TimeSpan.FromSeconds(4));
            Assert.Contains("Đang gõ liên tục", draft); Assert.False(typing.IsCompleted);
            Assert.Equal(0, fixture.Http.Posts);
        }
        finally { producer.Cancel(); await typing; }
    }

    [Fact]
    public async Task ChangingAccountClearsPrivateConversationFromRenderedUi()
    {
        await using var fixture = new RenderFixture(true, true);
        Assert.Contains("TIN_DA_GUI", await fixture.Render());
        await fixture.Renderer.Dispatcher.InvokeAsync(() => fixture.Session.Clear());
        var html = await fixture.RenderAgain();
        Assert.DoesNotContain("TIN_DA_GUI", html); Assert.DoesNotContain("PRIVATE_DRAFT", html);
        Assert.Contains("Phiên đăng nhập đã thay đổi", html);
    }
    [Fact]
    public async Task ConversationLoadFailureRemainsVisibleAfterListSucceeds()
    {
        await using var fixture = new RenderFixture(true, true); fixture.Http.FailDetail = true;
        Assert.Contains("Không thể mở cuộc trò chuyện này", await fixture.Render());
    }

    [Fact]
    public async Task EndingConversationRequiresPopupConfirmationAndCancelPreservesDraft()
    {
        await using var fixture = new RenderFixture(false, true);
        await fixture.Render();
        await Invoke("Input", new ChangeEventArgs { Value = "Nội dung chưa gửi" });
        await Invoke("CloseConversation");
        var popup = await fixture.RenderAgain();
        Assert.Contains("role=\"alertdialog\"", popup);
        Assert.Contains("Kết thúc cuộc trò chuyện?", popup);
        Assert.Contains("Ở lại", popup);
        Assert.Equal(0, fixture.Http.Posts);

        await Invoke("CancelConfirmation");
        var cancelled = await fixture.RenderAgain();
        Assert.DoesNotContain("role=\"alertdialog\"", cancelled);
        Assert.Contains("Nội dung chưa gửi", cancelled);
        Assert.Equal(0, fixture.Http.Posts);

        await Invoke("CloseConversation");
        await Invoke("ConfirmAction");
        await Invoke("ConfirmAction");
        Assert.Equal(1, fixture.Http.Posts);
        Assert.DoesNotContain("role=\"alertdialog\"", await fixture.RenderAgain());

        Task Invoke(string method, params object?[] args) => fixture.Renderer.Dispatcher.InvokeAsync(async () => {
            var result = fixture.Widget!.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(fixture.Widget, args);
            if (result is Task task) await task;
        });
    }

    private sealed class RenderFixture : IAsyncDisposable, IComponentActivator
    {
        public SupportChatWorkspace? Widget;
        public readonly Transport Transport = new(); public readonly Responses Http;
        public readonly AuthSession Session;
        public readonly HtmlRenderer Renderer;
        private readonly ServiceProvider services;
        private readonly bool manager;
        private Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent root;
        public RenderFixture(bool manager, bool existing)
        {
            this.manager = manager; Http = new(existing);
            var logger = LoggerFactory.Create(b => { });
            Session = new(new ApiClient(new HttpClient(Http) { BaseAddress = new("https://test.invalid") }, logger.CreateLogger<ApiClient>()));
            Session.Accept(new("test-token", DateTimeOffset.UtcNow.AddHours(1), new(Guid.NewGuid(), "demo", "Người kiểm thử", null, null, "ACTIVE", true,
                manager ? "MANAGER" : "RESIDENT", [manager ? "MANAGE_OPERATIONS" : "USE_RESIDENT_SERVICES"])));
            services = new ServiceCollection().AddSingleton<PropFlow.Web.Client.Shared.Feedback.ToastService>().AddLogging().AddSingleton<IComponentActivator>(this).AddSingleton(Session)
                .AddSingleton<NavigationManager, TestNavigation>()
                .AddSingleton<ISupportChatConnection>(Transport).AddSingleton<IJSRuntime, NoJavaScript>()
                .AddSingleton<AuthenticatedApiClient>(sp => new(new HttpClient(Http) { BaseAddress = new("https://test.invalid") }, sp.GetRequiredService<ILogger<ApiClient>>()))
                .AddSingleton<SupportChatApi>().BuildServiceProvider();
            Renderer = new(services, services.GetRequiredService<ILoggerFactory>());
        }
        public IComponent CreateInstance(Type type) { var instance = (IComponent)Activator.CreateInstance(type)!; if (instance is SupportChatWorkspace widget) Widget = widget; return instance; }
        public Task<string> Render(bool raw = false) => Renderer.Dispatcher.InvokeAsync(async () => {
            root = await Renderer.RenderComponentAsync<SupportChatWorkspace>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["ForManager"] = manager }));
            return raw ? root.ToHtmlString() : WebUtility.HtmlDecode(root.ToHtmlString());
        });
        public Task<string> RenderAgain() => Renderer.Dispatcher.InvokeAsync(() => {
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Widget, null);
            return WebUtility.HtmlDecode(root.ToHtmlString());
        });
        public async ValueTask DisposeAsync() { await Renderer.DisposeAsync(); await services.DisposeAsync(); }
    }
    private sealed class Responses(bool existing) : HttpMessageHandler
    {
        public int Posts;
        public bool FailDetail;
        private readonly Guid id = Guid.NewGuid();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) Posts++;
            if (FailDetail && !request.RequestUri!.AbsolutePath.EndsWith("/messages") && !request.RequestUri.AbsolutePath.EndsWith("/conversations"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { title = "Không thể mở cuộc trò chuyện này" }) });
            var now = DateTimeOffset.UtcNow;
            var chat = new SupportConversationView(id, "ACTIVE", "Cư dân", "Manager", true, now, now,
                new("MANAGER", "Tin gần nhất", now));
            object data = request.RequestUri!.AbsolutePath.EndsWith("/messages")
                ? new SupportMessagePage([new(Guid.NewGuid(), 1, "RESIDENT", "Cư dân", "TIN_DA_GUI", now)], false, "Asia/Ho_Chi_Minh")
                : request.RequestUri.AbsolutePath.EndsWith("/conversations") ? new SupportConversationPage(existing ? [chat] : [], existing ? 1 : 0, 1, "Asia/Ho_Chi_Minh") : chat;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data, data.GetType()) });
        }
    }
    private sealed class Transport : ISupportChatConnection
    {
        public bool Connected => true;
        public string? DraftText = "<b>PRIVATE_DRAFT</b>";
        public TaskCompletionSource<string> FirstDraft = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event Func<Guid, Task>? Changed { add { } remove { } }
        public event Action? TransientChanged { add { } remove { } }
        public event Action? StateChanged { add { } remove { } }
        public Task EnsureAsync(CancellationToken ct = default) => Task.CompletedTask;
        public string? Draft(Guid id) => DraftText;
        public void ClearDraft(Guid id) { }
        public bool IsTyping(Guid id) => false;
        public Task PublishDraft(Guid id, string text) { if (text.Length > 0) FirstDraft.TrySetResult(text); return Task.CompletedTask; }
        public Task PublishTyping(Guid id, bool value) => Task.CompletedTask;
        public Task ReleaseResidentAsync() => Task.CompletedTask;
    }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string id, object?[]? args) => new(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string id, CancellationToken ct, object?[]? args) => InvokeAsync<TValue>(id, args);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://test.invalid/", "https://test.invalid/manager/support-chat");
        protected override void NavigateToCore(string uri, bool forceLoad) { Uri = ToAbsoluteUri(uri).ToString(); NotifyLocationChanged(false); }
    }
}
