using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Components;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;
namespace PropFlow.UnitTests;
public sealed class LateAsyncCallbackTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Parent_feedback_is_invoked_only_for_a_current_child_payment_operation(bool navigate, bool dispose)
    {
        var transport = new DelayedPayment();
        using var http = new HttpClient(transport) { BaseAddress = new("https://test.invalid/") };
        var service = new ToastService();
        var invoiceId = Guid.NewGuid();
        var component = new ResidentPaymentDialog();
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = typeof(ResidentPaymentDialog);
        type.GetProperty(nameof(ResidentPaymentDialog.Invoice))!.SetValue(component,
            new HouseholdInvoice(invoiceId, "INV-1", "A-1", "Cư dân", new(2026, 11, 1), new(2026, 11, 30), 100000, 0, 100000));
        type.GetProperty(nameof(ResidentPaymentDialog.Saved))!.SetValue(component,
            EventCallback.Factory.Create(this, () => service.BeginOperation().Publish(() => service.Success("Callback cha"))));
        type.GetProperty("Toasts", flags)!.SetValue(component, service);
        type.GetProperty("Api", flags)!.SetValue(component, new FinanceCycleApi(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance)));
        type.GetField("amount", flags)!.SetValue(component, 100000m);
        type.GetField("reference", flags)!.SetValue(component, "TRANSFER-1");
        var work = (Task)type.GetMethod("Submit", flags)!.Invoke(component, null)!;
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (navigate) service.NavigationChanged("new");
        if (dispose) await component.DisposeAsync();
        if (navigate || dispose) service.Info("Thông báo mới");
        transport.Response.SetResult(new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResidentPayment(Guid.NewGuid(), invoiceId, "PAY-1", 100000, "BANK_TRANSFER", "PENDING", DateTimeOffset.UtcNow, null, null))
        });
        await work;
        Assert.Equal(navigate || dispose ? "Thông báo mới" : "Callback cha", Assert.Single(service.Messages).Message);
        await component.DisposeAsync();
    }
    private sealed class DelayedPayment : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Started.TrySetResult(); return Response.Task; }
    }
}
