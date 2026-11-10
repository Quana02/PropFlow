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
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Features.Resident.Management.Pages;
using PropFlow.Web.Client.Features.Resident.Shared.Models;
using PropFlow.Web.Client.Features.Resident.Shared.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;
using PropFlow.Web.Client.Shared.Tables;

namespace PropFlow.UnitTests;

public sealed class ResidentAccountsUiTests
{
    [Theory]
    [InlineData("MANAGER", true, true)]
    [InlineData("MANAGER", false, false)]
    [InlineData("ADMIN", true, false)]
    [InlineData("STAFF", true, false)]
    [InlineData("ACCOUNTANT", true, false)]
    [InlineData("RESIDENT", true, false)]
    public async Task Resident_header_shows_secondary_action_only_to_manager_with_read_permission(
        string role, bool hasReadPermission, bool visible)
    {
        var html = await RenderAsync<ResidentManagement>(role, hasReadPermission);
        Assert.Equal(visible, html.Contains("class=\"resident-secondary\" href=\"/residents/accounts\"", StringComparison.Ordinal));
        if (visible)
        {
            var accounts = html.IndexOf("href=\"/residents/accounts\"", StringComparison.Ordinal);
            var create = html.IndexOf("Thêm cư dân", StringComparison.Ordinal);
            Assert.True(accounts < create);
        }
    }

    [Fact]
    public async Task Directory_renders_server_total_email_labels_and_accessible_row_actions()
    {
        var html = await RenderAsync<ResidentAccounts>("MANAGER", true);
        Assert.Contains("aria-label=\"Bảng tài khoản cư dân\"", html);
        Assert.Contains("41 hồ sơ", html);
        Assert.Matches("aria-current=\"page\" aria-label=\"Trang 1 trên 3\"[^>]*>1 / 3</span>", html);
        Assert.Matches("<button type=\"button\" title=\"Trang sau\"", html);
        Assert.Contains("title=\"LOGIN@EXAMPLE.INVALID\"", html);
        Assert.Contains("contact@example.invalid", html);
        Assert.Contains("Email đã xác minh", html);
        Assert.Contains("Chưa liên kết", html);
        Assert.Contains("aria-label=\"Xem chi tiết tài khoản của Cư dân mẫu\"", html);
        Assert.DoesNotContain("Xác nhận và gửi mã", html);
    }

    [Fact]
    public void Direct_route_requires_read_policy()
    {
        Assert.Equal("/residents/accounts", Assert.Single(typeof(ResidentAccounts).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal(ResidentAccountPolicies.Read, Assert.Single(typeof(ResidentAccounts).GetCustomAttributes<AuthorizeAttribute>()).Policy);
    }

    [Theory]
    [InlineData(1, 3, false, true, false)]
    [InlineData(2, 3, false, false, false)]
    [InlineData(3, 3, false, false, true)]
    [InlineData(2, 3, true, true, true)]
    [InlineData(1, 0, false, true, true)]
    public async Task Pagination_marks_current_page_and_disables_arrows_at_boundaries_or_while_loading(
        int page, int totalPages, bool busy, bool previousDisabled, bool nextDisabled)
    {
        var html = await RenderAsync<TablePagination>("MANAGER", true, new Dictionary<string, object?>
        {
            [nameof(TablePagination.Page)] = page,
            [nameof(TablePagination.TotalPages)] = totalPages,
            [nameof(TablePagination.Busy)] = busy
        });

        if (totalPages == 0)
        {
            Assert.Matches("aria-label=\"Không có trang kết quả\"[^>]*>0 / 0</span>", html);
            Assert.DoesNotContain("aria-current", html);
        }
        else
        {
            Assert.Matches($"aria-current=\"page\" aria-label=\"Trang {page} trên {totalPages}\"[^>]*>{page} / {totalPages}</span>", html);
        }
        Assert.Equal(previousDisabled, Regex.IsMatch(html, "<button type=\"button\" disabled[^>]*title=\"Trang trước\""));
        Assert.Equal(nextDisabled, Regex.IsMatch(html, "<button type=\"button\" disabled[^>]*title=\"Trang sau\""));
    }

    private static async Task<string> RenderAsync<T>(string role, bool hasReadPermission,
        Dictionary<string, object?>? parameters = null) where T : IComponent
    {
        using var http = new HttpClient(new Transport()) { BaseAddress = new("https://test.invalid/") };
        var publicApi = new ApiClient(http, NullLogger<ApiClient>.Instance);
        var session = new AuthSession(publicApi);
        session.Accept(new SessionResponse("test-token", DateTimeOffset.UtcNow.AddMinutes(10),
            new AccountResponse(Guid.NewGuid(), "manager-ui", "Quản lý", null, null, "ACTIVE", true, role,
                hasReadPermission ? [ResidentAccountPolicies.Read] : [])));

        await using var services = new ServiceCollection()
            .AddLogging()
            .AddAuthorizationCore(options => options.AddPolicy(ResidentAccountPolicies.Read,
                policy => policy.RequireRole("MANAGER").RequireClaim("permission", ResidentAccountPolicies.Read)))
            .AddCascadingAuthenticationState()
            .AddSingleton(session)
            .AddSingleton<AuthenticationStateProvider>(session)
            .AddSingleton<NavigationManager, TestNavigation>()
            .AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton(publicApi)
            .AddSingleton(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance))
            .AddSingleton<AuthenticationService>()
            .AddSingleton<IResidentApiClient, ResidentApiClient>()
            .AddSingleton<ToastService>()
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
            WebUtility.HtmlDecode((await renderer.RenderComponentAsync<T>(
                parameters is null ? ParameterView.Empty : ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    private sealed class Transport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object result = request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/residents" => new PagedResidentsResponse([], 0, 1, 20),
                "/api/v1/residents/apartments" => Array.Empty<ActiveApartmentOption>(),
                "/api/v1/auth/resident-accounts" => new ResidentAccountPage(
                    [new(Guid.NewGuid(), "QA-01", "Cư dân mẫu", "contact@example.invalid", ["A-101"], "sample",
                         "LOGIN@EXAMPLE.INVALID", "ACTIVE", true, true),
                     new(Guid.NewGuid(), "QA-02", "Chưa có tài khoản", null, [], null, null, null, false, false)], 41, 1, 20),
                _ => throw new InvalidOperationException(request.RequestUri.AbsolutePath)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result, result.GetType()) });
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://test.invalid/", "https://test.invalid/residents");
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<T>(identifier, args);
    }
}
