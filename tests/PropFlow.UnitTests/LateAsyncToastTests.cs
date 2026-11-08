using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Finance.Components;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.UnitTests;

public sealed class LateAsyncToastTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Current_page_receives_success_or_failure(bool invoice, bool fail)
    {
        using var fixture = new Fixture(invoice);
        var work = fixture.Send(); var response = await fixture.Pending();
        response.Complete(fail); await work;
        Assert.Equal(fail ? "error" : "success", Assert.Single(fixture.Toasts.Messages).Severity);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Late_response_after_navigation_cannot_publish_or_remove_destination_feedback(bool invoice, bool fail)
    {
        using var fixture = new Fixture(invoice);
        var work = fixture.Send(); var response = await fixture.Pending();
        fixture.Toasts.NavigationChanged("account");
        fixture.Toasts.Info("Thông báo trang đích");
        var destination = Assert.Single(fixture.Toasts.Messages);
        Assert.False(response.Token.IsCancellationRequested);
        response.Complete(fail); await work;
        Assert.Equal(destination, Assert.Single(fixture.Toasts.Messages));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Late_response_after_component_dispose_does_not_publish_or_cancel_mutation(bool invoice, bool fail)
    {
        using var fixture = new Fixture(invoice);
        var work = fixture.Send(); var response = await fixture.Pending();
        fixture.DisposeComponent();
        Assert.False(response.Token.IsCancellationRequested);
        response.Complete(fail); await work;
        Assert.Empty(fixture.Toasts.Messages);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task New_execution_can_publish_the_same_message_after_previous_dismissal(bool invoice)
    {
        using var fixture = new Fixture(invoice);
        var first = fixture.Send(); (await fixture.Pending()).Complete(false); await first;
        var old = Assert.Single(fixture.Toasts.Messages); fixture.Toasts.Dismiss(old.Id);
        var second = fixture.Send(); (await fixture.Pending()).Complete(false); await second;
        var next = Assert.Single(fixture.Toasts.Messages);
        Assert.Equal(old.Message, next.Message); Assert.NotEqual(old.Id, next.Id);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Stale_callback_does_not_remove_existing_persistent_notification(bool invoice)
    {
        using var fixture = new Fixture(invoice);
        fixture.Toasts.Show("Cần xử lý", severity: "error", durationMs: 0);
        var persistent = Assert.Single(fixture.Toasts.Messages);
        var work = fixture.Send(); var response = await fixture.Pending();
        fixture.Toasts.NavigationChanged("next"); fixture.DisposeComponent();
        response.Complete(false); await work;
        Assert.Equal(persistent, Assert.Single(fixture.Toasts.Messages));
    }

    private sealed class Fixture : IDisposable
    {
        public ToastService Toasts { get; } = new();
        private readonly DelayedTransport transport = new();
        private readonly HttpClient http;
        private readonly object component;
        public Fixture(bool invoice)
        {
            http = new(transport) { BaseAddress = new("https://test.invalid/") };
            component = invoice ? new FinanceSendInvoiceButton() : new FinanceReminderButton();
            var type = component.GetType();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            type.GetProperty("Toasts", flags)!.SetValue(component, Toasts);
            type.GetProperty("Api", flags)!.SetValue(component, new FinanceCycleApi(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance)));
            if (!invoice) type.GetField("selectedIds", flags)!.SetValue(component, new[] { Guid.NewGuid() });
        }
        public Task Send() => (Task)component.GetType().GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;
        public async Task<PendingResponse> Pending() => await transport.Requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public void DisposeComponent() => Assert.IsAssignableFrom<IDisposable>(component).Dispose();
        public void Dispose() { DisposeComponent(); http.Dispose(); }
    }
    private sealed record PendingResponse(TaskCompletionSource<HttpResponseMessage> Result, CancellationToken Token)
    {
        public void Complete(bool fail) => Result.SetResult(new(fail ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
        { Content = fail ? JsonContent.Create(new { title = "Chưa thể gửi. Vui lòng thử lại." }) : JsonContent.Create(new ReminderResult(1)) });
    }
    private sealed class DelayedTransport : HttpMessageHandler
    {
        public Channel<PendingResponse> Requests { get; } = Channel.CreateUnbounded<PendingResponse>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var result = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(Requests.Writer.TryWrite(new(result, ct)));
            return result.Task;
        }
    }
}
