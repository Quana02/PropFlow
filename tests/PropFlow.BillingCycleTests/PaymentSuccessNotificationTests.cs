using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Communication.Application;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Domain.Payments;

namespace PropFlow.BillingCycleTests;

public sealed partial class BillingCyclePostgresTests
{
    [Theory]
    [InlineData(false, false, 700000)]
    [InlineData(false, true, 700000)]
    [InlineData(false, true, 800000)]
    [InlineData(true, false, 800000)]
    public async Task Successful_collection_or_approval_pushes_resident_notice_and_queues_receipt_once(bool approve, bool overdue, int amount)
    {
        Guid invoiceId;
        Guid paymentId = Guid.Empty;
        await using (var b = Billing()) await using (var p = Payments())
        {
            var invoice = new Invoice("INV-PAYMENT-SUCCESS", apartment, Month, Month.AddMonths(1).AddDays(-1), actor, DateTimeOffset.UtcNow);
            invoice.AddItem("MGMT", "Phí quản lý", "FIXED", 1, 800000, actor, DateTimeOffset.UtcNow);
            invoice.Issue(actor, Month, DateTimeOffset.UtcNow, overdue ? DateOnly.FromDateTime(DateTime.Today).AddDays(-1) : Month.AddMonths(1));
            b.Invoices.Add(invoice);
            b.InvoiceRecipients.Add(new(invoice.Id, user, "Cư dân kiểm thử", "head@example.invalid", "A-101", 70));
            await b.SaveChangesAsync();
            invoiceId = invoice.Id;
            if (approve)
            {
                var payment = new Payment("PAY-PENDING", invoiceId, amount, "BANK_TRANSFER", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
                p.Payments.Add(payment); await p.SaveChangesAsync(); paymentId = payment.Id;
            }
        }
        await using var factory = new Factory(connection, user, apartment);
        using var accountant = factory.Client("ACCOUNTANT", actor, "MANAGE_FINANCE");
        await using var hub = new HubConnectionBuilder().WithUrl("https://localhost/hubs/resident-notifications", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Headers["X-Role"] = "RESIDENT"; options.Headers["X-User"] = user.ToString();
        }).Build();
        var received = new TaskCompletionSource<FinanceNotificationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<FinanceNotificationUpdate>("FinanceNotificationCreated", n => { if (n.Title == "Thanh toán hóa đơn thành công") received.TrySetResult(n); });
        await hub.StartAsync();
        accountant.DefaultRequestHeaders.Add("Idempotency-Key", "success-notice");
        async Task<HttpResponseMessage> Send() => approve
            ? await accountant.PostAsync($"api/v1/payments/{paymentId}/confirm", null)
            : await accountant.PostAsJsonAsync("api/v1/payments/collect", new CollectPaymentRequest(invoiceId, amount, "CASH", null, null));
        using var response = await Send();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notice = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("Bạn đã thanh toán thành công", notice.Message);
        Assert.Contains(amount == 700000 ? "Dư nợ còn lại: 100.000" : "không còn dư nợ", notice.Message);
        using var retry = await Send(); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await using var db = Communication();
        Assert.Single(await db.Notifications.Where(n => n.Title == "Thanh toán hóa đơn thành công" && n.RecipientUserId == user).ToArrayAsync());
        var mail = Assert.Single(await db.FinanceEmails.Where(m => m.Subject == "Thanh toán hóa đơn thành công").ToArrayAsync());
        Assert.Equal("head@example.invalid", mail.Address);
        Assert.Contains("Bạn đã thanh toán thành công", mail.Html);
        Assert.Contains("INV-PAYMENT-SUCCESS", mail.Html);
        Assert.Contains(amount == 700000 ? "100.000" : "Còn phải thanh toán: 0", mail.Html);
        Assert.Null(mail.SentAt);
    }
}
