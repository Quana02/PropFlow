using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Finance.Components;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;
namespace PropFlow.UnitTests;
public sealed class LateCompletionGapCheck
{
    [Fact]
    public async Task Navigating_away_before_reminder_response_must_not_show_old_page_feedback()
    {
        var transport = new DelayedTransport();
        using var http = new HttpClient(transport) { BaseAddress = new("https://test.invalid/") };
        var service = new ToastService();
        var component = new FinanceReminderButton();
        var type = typeof(FinanceReminderButton);
        type.GetProperty("Toasts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component, service);
        type.GetProperty("Api", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(component,
            new FinanceCycleApi(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance)));
        var work = (Task)type.GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.NavigationChanged("account");
        transport.Response.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(new ReminderResult(1)) });
        await work;
        Assert.Empty(service.Messages);
    }
    private sealed class DelayedTransport : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Started.TrySetResult(); return Response.Task; }
    }
}
