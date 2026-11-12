using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.Resident.Portal.Components;
using PropFlow.Web.Client.Features.Resident.Portal.Models;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-05")]
[Trait("UseCase", "FE-05.1")]
public sealed class ResidentVoiceRequestDialogTests
{
    [Fact]
    public async Task Voice_completion_shows_missing_information_popup_and_keeps_text_and_attachments_without_submitting()
    {
        using var api = new Responses(new(true, [new("visitor_name", "Họ tên người viếng thăm"), new("visit_duration", "Khách ở lại bao lâu")]));
        await Scenario(api, async (dialog, js, html) =>
        {
            await Invoke(dialog, "ToggleVoiceAsync");
            await dialog.OnVoiceResult(js.Epoch, "Tôi muốn đăng ký người viếng thăm", "");
            await dialog.OnVoiceEnded(js.Epoch, "");
            Assert.Equal(1, api.Checks);
            Assert.Equal(0, api.Submissions);
            var rendered = html();
            Assert.Contains("Cần bổ sung thông tin", rendered);
            Assert.Contains("Họ tên người viếng thăm", rendered);
            Assert.Contains("Khách ở lại bao lâu", rendered);
            Assert.Contains("Tôi muốn đăng ký người viếng thăm", rendered);
            Assert.Contains("type=\"file\"", rendered);
            Assert.DoesNotContain("id=\"service-date\"", rendered);
            Assert.DoesNotContain("Khung giờ thuận tiện</label>", rendered);
            await Invoke(dialog, "SubmitAsync");
            Assert.Equal(1, api.Checks);
            Assert.Equal(0, api.Submissions);
            Assert.Contains("Vẫn gửi nội dung này", html());
            await Invoke(dialog, "SendFromPopupAsync");
            Assert.Equal(1, api.Submissions);
        });
    }

    [Fact]
    public async Task Supplementary_speech_keeps_existing_text_and_complete_review_is_reused_at_submit()
    {
        using var api = new Responses(new(true, [], new DateOnly(2026, 11, 13), "MORNING"));
        await Scenario(api, async (dialog, js, html) =>
        {
            Form(dialog).Description = "Tôi cần sửa vòi nước bị rò trong bếp.";
            await Invoke(dialog, "ToggleVoiceAsync");
            await dialog.OnVoiceResult(js.Epoch, "Nhờ kiểm tra vào sáng mai.", "");
            await dialog.OnVoiceEnded(js.Epoch, "");
            Assert.Contains("Tôi cần sửa vòi nước bị rò trong bếp. Nhờ kiểm tra vào sáng mai.", html());
            Assert.Contains("13/11/2026", html());
            Assert.Equal(0, api.Submissions);
            await Invoke(dialog, "SubmitAsync");
            Assert.Equal(1, api.Checks);
            Assert.Equal(1, api.Submissions);
            Assert.Equal(new DateOnly(2026, 11, 13), api.Submitted!.PreferredDate);
            Assert.Equal("MORNING", api.Submitted.PreferredTimeCode);
        });
    }

    [Fact]
    public async Task Complete_vehicle_registration_submits_original_description_with_no_appointment()
    {
        using var api = new Responses(new(true, []));
        const string text = "Tôi muốn đăng ký xe máy màu đỏ, biển số 59A1-123.45.";
        await Scenario(api, async (dialog, js, html) =>
        {
            await Invoke(dialog, "ToggleVoiceAsync");
            await dialog.OnVoiceResult(js.Epoch, text, "");
            await dialog.OnVoiceEnded(js.Epoch, "");
            Assert.DoesNotContain("Cần bổ sung thông tin", html());
            Assert.DoesNotContain("Lịch ghi nhận từ nội dung", html());
            await Invoke(dialog, "SubmitAsync");
            Assert.Equal(1, api.Submissions);
            Assert.Equal(text, api.Submitted!.Description);
            Assert.Null(api.Submitted.PreferredDate);
            Assert.Null(api.Submitted.PreferredTimeCode);
        });
    }

    [Fact]
    public async Task Editing_during_an_ai_check_discards_the_old_result_and_its_schedule()
    {
        using var api = new Responses(new(true, [])) { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await Scenario(api, async (dialog, js, html) =>
        {
            await Invoke(dialog, "ToggleVoiceAsync");
            await dialog.OnVoiceResult(js.Epoch, "Tôi muốn đăng ký xe vào sáng mai", "");
            var pending = dialog.OnVoiceEnded(js.Epoch, "");
            await api.Started.Task;
            Form(dialog).Description = "Tôi cần sửa điện trong bếp, không phải đăng ký xe.";
            await Invoke(dialog, "InvalidateReview");
            api.Pending.SetResult(new(true, [new("vehicle_plate", "Biển số xe")], new DateOnly(2026, 11, 13), "MORNING"));
            await pending;
            Assert.DoesNotContain("Cần bổ sung thông tin", html());
            Assert.Null(Form(dialog).PreferredDate);
            Assert.Contains("không phải đăng ký xe", html());
        });
    }

    [Fact]
    public async Task Closing_the_dialog_aborts_capture_and_late_results_cannot_change_or_check_the_draft()
    {
        using var api = new Responses(new(true, []));
        await Scenario(api, async (dialog, js, _) =>
        {
            await Invoke(dialog, "ToggleVoiceAsync");
            var epoch = js.Epoch;
            await Invoke(dialog, "Close");
            await dialog.OnVoiceResult(epoch, "Nội dung đến muộn", "");
            await dialog.OnVoiceEnded(epoch, "");
            Assert.Contains("cancel", js.Calls);
            Assert.Equal(0, api.Checks);
            Assert.Equal("", Form(dialog).Description);
        });
    }

    private static async Task Scenario(Responses api, Func<ResidentServiceRequestDialog, JavaScript, Func<string>, Task> action)
    {
        var capture = new Capture();
        var js = new JavaScript();
        using var http = new HttpClient(api) { BaseAddress = new("https://propflow.test/") };
        using var services = new ServiceCollection().AddLogging().AddSingleton<ToastService>()
            .AddSingleton<IComponentActivator>(capture).AddSingleton<IJSRuntime>(js)
            .AddSingleton<IResidentServiceRequestApiClient>(sp => new ResidentServiceRequestApiClient(new AuthenticatedApiClient(http, sp.GetRequiredService<ILogger<ApiClient>>()))).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<ResidentServiceRequestDialog>(ParameterView.Empty);
            var dialog = capture.Dialog!;
            dialog.Open("OTHER", "Yêu cầu dịch vụ");
            await Invoke(dialog, "OnAfterRenderAsync", false);
            await action(dialog, js, () => WebUtility.HtmlDecode(root.ToHtmlString()));
        });
    }

    private static SubmitServiceRequestForm Form(object component) =>
        (SubmitServiceRequestForm)component.GetType().GetField("_form", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component)!;
    private static async Task Invoke(object component, string name, params object[] args)
    {
        if (component.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, args) is Task task) await task;
        typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null);
    }
    private sealed class Capture : IComponentActivator
    {
        public ResidentServiceRequestDialog? Dialog;
        public IComponent CreateInstance(Type type)
        { var component = (IComponent)Activator.CreateInstance(type)!; if (component is ResidentServiceRequestDialog dialog) Dialog = dialog; return component; }
    }
    private sealed class JavaScript : IJSRuntime, IJSObjectReference
    {
        public int Epoch;
        public List<string> Calls { get; } = [];
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (identifier == "start") Epoch = (int)args![0]!;
            return ValueTask.FromResult(identifier is "import" or "create" ? (T)(object)this : identifier is "isSupported" or "start" ? (T)(object)true : default!);
        }
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => InvokeAsync<T>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Responses(ResidentServiceRequestDraftReview review) : HttpMessageHandler
    {
        public int Checks, Submissions;
        public CreateResidentServiceRequestRequest? Submitted;
        public TaskCompletionSource<ResidentServiceRequestDraftReview>? Pending { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("draft-check", StringComparison.Ordinal))
            {
                Checks++; Started.TrySetResult();
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(Pending is null ? review : await Pending.Task.WaitAsync(ct)) };
            }
            Submissions++;
            Submitted = await request.Content!.ReadFromJsonAsync<CreateResidentServiceRequestRequest>(ct);
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new ResidentServiceRequestCreatedResponse(Guid.NewGuid(), "YC-TEST", "SUBMITTED", DateTimeOffset.UtcNow)) };
        }
    }
}
