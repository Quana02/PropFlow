using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Authentication.Pages;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Features.Authentication.State;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

public sealed class AuthenticationUiTests
{
    [Theory]
    [InlineData("ADMIN", "Quản trị viên", "", "/admin/dashboard?tab=overview")]
    [InlineData("MANAGER", "Quản lý", "MANAGE_OPERATIONS", "/buildings")]
    [InlineData("ACCOUNTANT", "Kế toán", "", "/finance/dashboard")]
    [InlineData("STAFF", "Nhân viên", "PERFORM_ASSIGNED_OPERATIONS", "/buildings")]
    [InlineData("RESIDENT", "Cư dân", "", null)]
    public async Task Account_uses_the_role_shell_and_renders_authorized_security_actions(
        string role, string roleLabel, string permission, string? workspaceLink)
    {
        var account = new AccountResponse(Guid.NewGuid(), "profile-test", "Hồ sơ mẫu", "profile@example.invalid",
            "0900000001", "ACTIVE", true, role, permission == "" ? [] : [permission]);
        using var http = new HttpClient(new AccountTransport(account)) { BaseAddress = new("https://test.invalid/") };
        var publicApi = new ApiClient(http, NullLogger<ApiClient>.Instance);
        var session = new AuthSession(publicApi);
        session.Accept(new SessionResponse("test-token", DateTimeOffset.UtcNow.AddMinutes(15), account));

        await using var services = new ServiceCollection().AddLogging().AddAuthorizationCore(options =>
        {
            options.AddPolicy(AssetPermissions.ViewPolicy, policy => policy.RequireAssertion(context => AssetPermissions.CanView(context.User)));
            options.AddPolicy(KnowledgePolicies.Read, policy => policy.RequireAssertion(context => KnowledgePolicies.CanRead(context.User)));
        })
            .AddSingleton(session)
            .AddSingleton<AuthenticationStateProvider>(session)
            .AddSingleton<NavigationManager, AccountNavigation>()
            .AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton<ToastService>()
            .AddSingleton<OnboardingState>()
            .AddSingleton(publicApi)
            .AddSingleton(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance))
            .AddSingleton<AuthenticationService>()
            .AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddSingleton<PropFlow.Web.Client.Features.Finance.Services.FinanceCycleApi>()
            .AddSingleton<PropFlow.Web.Client.Features.Resident.Portal.Services.ResidentFinanceNoticeState>()
            .AddSingleton<PropFlow.Web.Client.Features.Resident.Portal.Services.ResidentNotificationConnectionFactory>()
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(AuthorizeRouteView.RouteData)] = new RouteData(typeof(Account), new Dictionary<string, object?>())
            });
            var view = await renderer.RenderComponentAsync<AuthorizeRouteView>(parameters);
            return WebUtility.HtmlDecode(view.ToHtmlString());
        });

        Assert.Matches($@"<dd\b[^>]*>{Regex.Escape(roleLabel)}</dd>", html);
        Assert.Matches($@"<dd\b[^>]*>{Regex.Escape(account.Email!)}</dd>", html);
        Assert.Contains("href=\"/account/change-password\"", html, StringComparison.Ordinal);
        Assert.Equal(role != "RESIDENT", html.Contains("href=\"/account/change-email\"", StringComparison.Ordinal));
        if (workspaceLink is null)
        {
            Assert.Contains("Để thay đổi email đăng nhập, vui lòng liên hệ Ban quản lý chung cư.", html, StringComparison.Ordinal);
            Assert.Contains("public-overview", html, StringComparison.Ordinal);
            Assert.DoesNotContain("app-nav-scroll", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("app-nav-scroll", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"{workspaceLink}\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("public-overview", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Self_service_email_route_is_limited_to_internal_roles()
    {
        var authorization = Assert.Single(typeof(ChangeEmail).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal("ADMIN,MANAGER,STAFF,ACCOUNTANT", authorization.Roles);
        Assert.Null(authorization.Policy);
        Assert.Empty(typeof(ChangeEmail).GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Fact]
    public void Login_validation_is_attached_to_fields_and_operation_errors_use_shared_toast()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionDirectory(),
            "src",
            "PropFlow.Web.Client",
            "Features",
            "Authentication",
            "Pages",
            "Login.razor"));

        Assert.Contains("<ValidationMessage For=\"() => form.Username\"", source, StringComparison.Ordinal);
        Assert.Contains("<ValidationMessage For=\"() => form.Password\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"login-username-error\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-describedby=\"login-password-error\"", source, StringComparison.Ordinal);
        Assert.Contains("Toasts.Error(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<PropFlowToast", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ValidationSummary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<ApiFeedback", source, StringComparison.Ordinal);
    }

    private static string FindSolutionDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PropFlow.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Không tìm thấy PropFlow.sln từ thư mục kiểm thử.");
    }

    private sealed class AccountTransport(AccountResponse account) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/auth/me", request.RequestUri?.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(account) });
        }
    }

    private sealed class AccountNavigation : NavigationManager
    {
        public AccountNavigation() => Initialize("https://test.invalid/", "https://test.invalid/account");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<T>(identifier, args);
    }
}
