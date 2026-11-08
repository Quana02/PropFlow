using Microsoft.AspNetCore.Components.Forms;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;
using PropFlow.Web.Client.Shared.Forms;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.UnitTests;

public sealed class ToastServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Credential_success_preserves_login_redirect_and_uses_a_single_deferred_toast(bool changeEmail)
    {
        using var http = new HttpClient(new SuccessTransport()) { BaseAddress = new("https://test.invalid/") };
        var api = new ApiClient(http, NullLogger<ApiClient>.Instance);
        var session = new AuthSession(api);
        session.Accept(new("test-token", DateTimeOffset.UtcNow.AddMinutes(15),
            new(Guid.NewGuid(), "admin", "Admin", "admin@example.invalid", null, "ACTIVE", true, "ADMIN", [])));
        var toasts = new ToastService();
        var service = new AuthenticationService(api, new(http, NullLogger<ApiClient>.Instance), session, toasts);
        var response = changeEmail
            ? await service.VerifyEmailChangeAsync(new(Guid.NewGuid(), "new@example.invalid", "123456"))
            : await service.ChangePasswordAsync(new("Current password 123!", "New password 456!"));
        Assert.True(response.IsSuccess);
        Assert.Null(session.User);
        Assert.Null(session.Notice);
        Assert.Empty(toasts.Messages);
        toasts.NavigationChanged("login");
        var toast = Assert.Single(toasts.Messages);
        Assert.Equal(changeEmail ? "Đổi email thành công" : "Đổi mật khẩu thành công", toast.Title);
    }

    [Fact]
    public void Navigation_discards_visible_messages_and_old_pending_success()
    {
        var toasts = new ToastService();
        toasts.Show("Lỗi", "Thông tin lỗi", "error");
        toasts.SuccessAfterNavigation("Thành công", "Đăng nhập lại", "login");
        toasts.NavigationChanged("account");
        Assert.Empty(toasts.Messages);
        toasts.NavigationChanged("login");
        Assert.Empty(toasts.Messages);
    }

    [Fact]
    public void Credential_success_is_shown_once_at_login_for_three_seconds()
    {
        var toasts = new ToastService();
        toasts.Show("Cũ");
        toasts.SuccessAfterNavigation("Đổi email thành công", "Đăng nhập lại", "login");
        toasts.NavigationChanged("login?returnUrl=%2Faccount");
        var message = Assert.Single(toasts.Messages);
        Assert.Equal("Đổi email thành công", message.Title);
        Assert.Equal("success", message.Severity);
        Assert.Equal(3000, message.DurationMs);
        toasts.NavigationChanged("account");
        Assert.Empty(toasts.Messages);
    }

    [Fact]
    public void Stack_is_bounded_and_manual_close_removes_only_the_chosen_message()
    {
        var toasts = new ToastService();
        toasts.Show("Một", severity: "success");
        toasts.Show("Hai", severity: "error");
        toasts.Show("Ba", severity: "warning");
        toasts.Show("Bốn", severity: "info");
        Assert.Equal(new[] { "Hai", "Ba", "Bốn" }, toasts.Messages.Select(item => item.Title));
        Assert.Equal(8000, toasts.Messages[0].DurationMs);
        toasts.Dismiss(toasts.Messages[1].Id);
        Assert.Equal(new[] { "Hai", "Bốn" }, toasts.Messages.Select(item => item.Title));
    }

    [Fact]
    public void Server_validation_maps_aliases_to_fields_and_clears_only_the_edited_field()
    {
        var form = new PasswordForm();
        var context = new EditContext(form);
        var fields = new ApiValidationMessages(context, ("NewPassword", "Password"), ("CurrentPassword", "CurrentPassword"));
        fields.Apply(new ApiResult(false, ValidationErrors: new Dictionary<string, string[]>
        {
            ["request.newPassword"] = ["Mật khẩu mới không hợp lệ"],
            ["CurrentPassword"] = ["Mật khẩu hiện tại không đúng"],
            ["Unknown"] = ["Không gán vào trường ngoài form"]
        }));
        Assert.Equal(2, context.GetValidationMessages().Count());
        context.NotifyFieldChanged(new(form, "Password"));
        Assert.Single(context.GetValidationMessages());
        fields.Dispose();
        context.NotifyFieldChanged(new(form, "CurrentPassword"));
        Assert.Single(context.GetValidationMessages());
    }

    private sealed class PasswordForm { public string Password { get; set; } = ""; public string CurrentPassword { get; set; } = ""; }
    private sealed class SuccessTransport : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("csrf", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new CsrfResponse("csrf")) }
                : new HttpResponseMessage(HttpStatusCode.NoContent));
    }
}
