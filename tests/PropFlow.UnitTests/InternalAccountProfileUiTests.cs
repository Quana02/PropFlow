using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Administration.Components;
using PropFlow.Web.Client.Features.Administration.Models;
using PropFlow.Web.Client.Features.Administration.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

public sealed class InternalAccountProfileUiTests
{
    private static readonly InternalAccountProfile Account = new(Guid.NewGuid(), "manager-ui", "Quản lý mẫu",
        "manager@example.invalid", "0901234567", "MANAGER", "LOCKED", true);

    [Fact]
    public async Task Row_actions_expose_distinct_names_and_a_labeled_touch_menu()
    {
        var html = await RenderAsync<InternalAccountTable>(new()
        {
            [nameof(InternalAccountTable.Accounts)] = new[]
            {
                new InternalAccount(Account.Id, Account.Username, Account.DisplayName, Account.Email, Account.Role, Account.Status)
            }
        });

        foreach (var label in new[] { "Xem chi tiết tài khoản", "Xem quyền", "Đổi vai trò", "Thay đổi trạng thái" })
        {
            Assert.Contains($"data-action-tooltip=\"{label}\"", html);
            Assert.Contains($"aria-label=\"{label} của manager-ui\"", html);
            Assert.Contains($">{label}</button>", html);
        }
        Assert.Contains("aria-label=\"Thao tác với manager-ui\"", html);
        Assert.Contains("role=\"tooltip\" popover=\"manual\"", html);
    }

    [Fact]
    public async Task Detail_renders_contact_and_login_information_read_only_before_editing()
    {
        var html = await RenderAsync<InternalAccountProfileDialog>(new()
        {
            [nameof(InternalAccountProfileDialog.AccountId)] = Account.Id
        });

        Assert.Contains("role=\"dialog\" aria-modal=\"true\" aria-labelledby=\"admin-profile-title\"", html);
        foreach (var value in new[] { Account.DisplayName, Account.Username, Account.Email!, Account.PhoneNumber!, Account.Role,
            "Đang bị khóa", "Email đã xác minh", "Chỉnh sửa thông tin" })
            Assert.Contains(value, html);
        Assert.DoesNotContain("<input", html);
    }

    private static async Task<string> RenderAsync<T>(Dictionary<string, object?> parameters) where T : IComponent
    {
        using var http = new HttpClient(new ProfileTransport()) { BaseAddress = new("https://test.invalid/") };
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance))
            .AddSingleton<IAdministrationApiClient, AdministrationApiClient>()
            .AddSingleton<ToastService>()
            .BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () => WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    private sealed class ProfileTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/administration/internal-accounts/{Account.Id}", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Account) });
        }
    }

    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException();
    }
}
