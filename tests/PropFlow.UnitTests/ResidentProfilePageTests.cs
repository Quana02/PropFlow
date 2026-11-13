using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.Authentication.Components;
using PropFlow.Web.Client.Features.Authentication.Models;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Models;
using PropFlow.Web.Client.Features.Resident.Portal.Pages;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Features.Resident.Shared.Models;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

public sealed class ResidentProfilePageTests
{
    [Fact]
    public void Profile_route_is_resident_only_and_avatar_has_one_profile_entry()
    {
        Assert.Equal("RESIDENT", Assert.Single(typeof(ResidentProfile).GetCustomAttributes<AuthorizeAttribute>()).Roles);
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "PropFlow.sln"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var source = File.ReadAllText(Path.Combine(folder!.FullName, "src/PropFlow.Web.Client/Features/Resident/Portal/Components/ResidentHeader.razor"));
        var menu = Regex.Match(source, "<div class=\"profile-actions\">([\\s\\S]*?)</div>").Groups[1].Value;
        Assert.Single(Regex.Matches(menu, "<a\\b").Cast<Match>());
        Assert.Contains("href=\"/resident/profile\"", menu);
        Assert.Single(Regex.Matches(menu, "<button\\b").Cast<Match>());
        Assert.Contains("profile-logout", menu);
        Assert.Contains("LogoutAsync", menu);
        Assert.Contains("Số dư ví dịch vụ", source);
        Assert.Contains("Phí quản lý cần thanh toán", source);
        Assert.Contains("href=\"/resident/billing\"", source);
        Assert.DoesNotContain("profileModal", source);
        Assert.DoesNotContain("30K-888.99", source);
    }

    [Fact]
    public async Task Profile_combines_real_account_residence_vehicle_requests_and_inline_security()
    {
        using var transport = new Transport();
        await Scenario(transport, async (capture, session, html) =>
        {
            var rendered = html();
            foreach (var value in new[] { "Hồ sơ cá nhân", "Tên hiển thị", "Hồ sơ cư trú", "CD-TEST", "Họ tên trên hồ sơ",
                "Xe đăng ký", "Yêu cầu xe của cư dân", "Bảo mật tài khoản", "id=\"current-pass\"", "id=\"change-pass\"",
                "href=\"/resident/account-email\"", "href=\"/resident/service-requests/new\"" }) Assert.Contains(value, rendered);
            Assert.DoesNotContain("Yêu cầu sửa chữa khác", rendered);
            Assert.DoesNotContain("role=\"dialog\"", rendered);
            Assert.DoesNotContain("30K-888.99", rendered);
            if (Environment.GetEnvironmentVariable("PROPFLOW_PROFILE_PREVIEW_FILE") is { Length: > 0 } file)
                await File.WriteAllTextAsync(file, rendered);
        });
    }

    [Fact]
    public async Task Failed_profile_save_preserves_edits_and_only_calls_self_service_account_endpoint()
    {
        using var transport = new Transport { FailProfileUpdate = true };
        await Scenario(transport, async (capture, session, html) =>
        {
            var form = Field<ProfileForm>(capture.Workspace!, "form");
            form.DisplayName = "Tên hiển thị mới"; form.PhoneNumber = "0909999999";
            await Invoke(capture.Workspace!, "Save");
            Assert.Equal(1, transport.ProfileUpdates);
            Assert.Equal("Tên hiển thị mới", transport.LastUpdate!.DisplayName);
            Assert.Contains("Tên hiển thị mới", html());
            Assert.Equal("Tài khoản thử nghiệm", session.User!.DisplayName);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Avatar_editor_uses_real_upload_response_and_never_reports_failed_upload_as_saved(bool fail)
    {
        using var transport = new Transport { FailAvatar = fail };
        await Scenario(transport, async (capture, session, html) =>
        {
            var editor = capture.Avatar!;
            await (Task)typeof(AccountAvatarEditor).GetMethod("UploadAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(editor, [new InputFileChangeEventArgs([new TestImage()])])!;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
            Assert.Equal(1, transport.AvatarUploads);
            if (fail)
            {
                Assert.Null(session.User!.AvatarVersion);
                Assert.Contains("Chưa lưu được ảnh thử nghiệm", html());
                Assert.DoesNotContain("Đã cập nhật ảnh đại diện", html());
            }
            else
            {
                Assert.NotNull(session.User!.AvatarVersion);
                Assert.Contains("Đã cập nhật ảnh đại diện", html());
                Assert.Contains("data:image/jpeg;base64,", html());
            }
        });
    }

    [Fact]
    public async Task Successful_profile_save_updates_the_current_account_without_modifying_residency()
    {
        using var transport = new Transport();
        await Scenario(transport, async (capture, session, html) =>
        {
            var form = Field<ProfileForm>(capture.Workspace!, "form");
            form.DisplayName = "Tên hiển thị mới";
            await Invoke(capture.Workspace!, "Save");
            Assert.Equal("Tên hiển thị mới", session.User!.DisplayName);
            Assert.Contains("Họ tên trên hồ sơ", html());
            Assert.Equal(1, transport.ProfileUpdates);
        });
    }

    [Fact]
    public async Task Password_failure_clears_sensitive_fields_and_keeps_the_session()
    {
        using var transport = new Transport();
        await Scenario(transport, async (capture, session, _) =>
        {
            var form = Field<ChangePasswordForm>(capture.Password!, "form");
            form.CurrentPassword = "Current password 123!";
            form.Password = form.Confirmation = "New password 456!";
            await Invoke(capture.Password!, "Submit");
            Assert.Equal(1, transport.PasswordChanges);
            Assert.Equal("", form.CurrentPassword); Assert.Equal("", form.Password); Assert.Equal("", form.Confirmation);
            Assert.NotNull(session.User);
        });
    }

    [Fact]
    public async Task Vehicle_loading_failure_is_an_error_not_an_empty_registered_vehicle_list()
    {
        using var transport = new Transport { FailVehicles = true };
        await Scenario(transport, (_, _, html) =>
        {
            Assert.Contains("Chưa tải được yêu cầu về xe", html());
            Assert.Contains("Thử lại", html());
            Assert.DoesNotContain("Chưa có yêu cầu về xe được phân loại", html());
            return Task.CompletedTask;
        });
    }

    private static async Task Scenario(Transport transport, Func<Capture, AuthSession, Func<string>, Task> action)
    {
        using var http = new HttpClient(transport) { BaseAddress = new("https://profile.test/") };
        var api = new ApiClient(http, NullLogger<ApiClient>.Instance);
        var session = new AuthSession(api);
        session.Accept(new("fake-test-token", DateTimeOffset.UtcNow.AddMinutes(10), transport.Account));
        var capture = new Capture();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(api).AddSingleton(session)
            .AddSingleton(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance)).AddSingleton<ToastService>()
            .AddSingleton<AuthenticationService>().AddSingleton<IResidentServiceRequestApiClient, ResidentServiceRequestApiClient>()
            .AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<NavigationManager, TestNavigation>()
            .AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var resident = new ResidentDetailResponse(Guid.NewGuid(), "CD-TEST", "Họ tên trên hồ sơ", null, null, null,
                null, null, null, null, "0901234567", "resident@example.invalid", null, "ACTIVE", true, [], []);
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CascadingValue<ResidentPortalProfileContext>.Value)] = new ResidentPortalProfileContext(resident, false, null),
                [nameof(CascadingValue<ResidentPortalProfileContext>.ChildContent)] = (RenderFragment)(builder =>
                { builder.OpenComponent<ResidentProfile>(0); builder.CloseComponent(); })
            });
            var root = await renderer.RenderComponentAsync<CascadingValue<ResidentPortalProfileContext>>(parameters);
            await action(capture, session, () => WebUtility.HtmlDecode(root.ToHtmlString()));
        });
    }

    private static T Field<T>(object component, string name) => (T)component.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component)!;
    private static async Task Invoke(object component, string method)
    {
        await (Task)component.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null)!;
        typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
    }
    private sealed class Capture : IComponentActivator
    {
        public AccountWorkspace? Workspace;
        public AccountPasswordForm? Password;
        public AccountAvatarEditor? Avatar;
        public IComponent CreateInstance(Type type)
        {
            var component = (IComponent)Activator.CreateInstance(type)!;
            if (component is AccountWorkspace workspace) Workspace = workspace;
            if (component is AccountPasswordForm password) Password = password;
            if (component is AccountAvatarEditor avatar) Avatar = avatar;
            return component;
        }
    }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => throw new NotSupportedException();
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args) => InvokeAsync<T>(id, args);
    }
    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://profile.test/", "https://profile.test/resident/profile");
        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
    private sealed class Transport : HttpMessageHandler
    {
        public AccountResponse Account { get; } = new(Guid.NewGuid(), "resident-test", "Tài khoản thử nghiệm", "resident@example.invalid", "0901234567", "ACTIVE", true, "RESIDENT", []);
        public bool FailProfileUpdate, FailVehicles, FailAvatar;
        public int AvatarUploads;
        public int ProfileUpdates, PasswordChanges;
        public UpdateProfileRequest? LastUpdate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/me/avatar" && request.Method == HttpMethod.Post)
            {
                AvatarUploads++;
                var form = Assert.IsType<MultipartFormDataContent>(request.Content);
                var image = Assert.Single(form);
                Assert.Equal("file", image.Headers.ContentDisposition!.Name!.Trim('"'));
                Assert.Equal("image/jpeg", image.Headers.ContentType!.MediaType);
                Assert.Equal(TestImage.Bytes, await image.ReadAsByteArrayAsync(ct));
                return FailAvatar ? Json(new { title = "Chưa lưu được ảnh thử nghiệm" }, HttpStatusCode.ServiceUnavailable)
                    : Json(Account with { AvatarVersion = Guid.NewGuid() });
            }
            if (path == "/api/v1/auth/me/avatar" && request.Method == HttpMethod.Get)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestImage.Bytes) };
                response.Content.Headers.ContentType = new("image/jpeg"); return response;
            }
            if (path == "/api/v1/auth/csrf" && request.Method == HttpMethod.Get) return Json(new CsrfResponse("fake-csrf"));
            if (path == "/api/v1/auth/me" && request.Method == HttpMethod.Get) return Json(Account);
            if (path == "/api/v1/auth/me" && request.Method == HttpMethod.Put)
            {
                ProfileUpdates++; LastUpdate = await request.Content!.ReadFromJsonAsync<UpdateProfileRequest>(ct);
                return FailProfileUpdate ? Json(new { title = "Không lưu được thông tin", code = "profile_failed" }, HttpStatusCode.BadRequest)
                    : Json(Account with { DisplayName = LastUpdate!.DisplayName, PhoneNumber = LastUpdate.PhoneNumber });
            }
            if (path == "/api/v1/auth/password/change" && request.Method == HttpMethod.Post)
            {
                Assert.Equal("fake-csrf", Assert.Single(request.Headers.GetValues("X-CSRF-TOKEN")));
                PasswordChanges++; return Json(new { title = "Mật khẩu hiện tại chưa đúng", code = "invalid_current_password" }, HttpStatusCode.BadRequest);
            }
            if (path == "/api/v1/resident/service-requests" && request.Method == HttpMethod.Get)
            {
                if (FailVehicles) return Json(new { title = "Chưa tải được yêu cầu về xe" }, HttpStatusCode.ServiceUnavailable);
                var now = DateTimeOffset.UtcNow;
                return Json(new[]
                {
                    new ResidentServiceRequestListItem(Guid.NewGuid(), "YC-VEHICLE-TEST", "Yêu cầu xe của cư dân", "VEHICLE", "UNDER_REVIEW", null, null, null, null, now, now, null, null, null),
                    new ResidentServiceRequestListItem(Guid.NewGuid(), "YC-REPAIR-TEST", "Yêu cầu sửa chữa khác", "REPAIR", "SUBMITTED", null, null, null, null, now, now, null, null, null)
                });
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        }
        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    }
    private sealed class TestImage : IBrowserFile
    {
        public static byte[] Bytes => [255, 216, 255, 224, 0, 0];
        public string Name => "test.jpg";
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => Bytes.Length;
        public string ContentType => "image/jpeg";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(Bytes);
    }
}
