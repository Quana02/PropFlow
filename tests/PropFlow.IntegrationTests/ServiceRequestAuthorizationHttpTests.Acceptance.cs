using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Api.Composition;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.IntegrationTests;

public sealed partial class ServiceRequestAuthorizationHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Acceptance_cost_consent_processing_invoice_http_payment_and_feedback_form_one_journey(bool charged)
    {
        var id = await Start(); // Real manager assignment and Staff acceptance; no initial fee.
        const string note = "Vật tư theo khảo sát đã xác nhận";
        if (charged)
        {
            var quote = await Call(staffB, id, "additional-cost", new ProposeAdditionalCostRequest(30_000, note));
            var proposal = quote.GetProperty("proposalId").GetGuid();
            Assert.Equal("AWAITING_MANAGER", quote.GetProperty("status").GetString());
            await Reject(residentA, id, "additional-cost/response", HttpStatusCode.Conflict, new RespondAdditionalCostRequest(proposal, true));
            await Reject(staffB, id, "progress", HttpStatusCode.Conflict, Progress);
            await Call(manager, id, "additional-cost/approve", new ApproveAdditionalCostProposalRequest(proposal));
            await Call(residentA, id, "additional-cost/response", new RespondAdditionalCostRequest(proposal, true));
        }
        await Call(staffB, id, "progress", Progress);
        await Call(staffB, id, "complete", charged ? Work with { AdditionalCost = 30_000, AdditionalCostNote = note } : Work);
        await Call(staffB, id, "evidence");
        await Call(manager, id, "resolution/approve", Resolution with { ApprovedAdditionalCost = charged ? 30_000 : 0 });
        await Call(manager, id, "close", Closure);
        using var scope = host.Services.CreateScope();
        var billDb = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var payDb = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        if (charged)
        {
            var invoice = await billDb.Invoices.SingleAsync();
            Assert.Equal(30_000, invoice.TotalAmount);
            Assert.Equal(residentA.Id, (await billDb.InvoiceRecipients.SingleAsync()).UserId);
            var dispatch = scope.ServiceProvider.GetRequiredService<ServiceRequestCostNoticeDispatcher>();
            await dispatch.DispatchAsync(default); await dispatch.DispatchAsync(default);
            var notice = await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Notifications
                .SingleAsync(row => row.SourceId == id && row.Title == "Hóa đơn dịch vụ đã sẵn sàng");
            Assert.Equal(residentA.Id, notice.RecipientUserId);
            var protector = scope.ServiceProvider.GetRequiredService<IPaymentAccountProtector>();
            payDb.PaymentDestinations.Add(new PaymentDestination(1, "970436", "VCB", "Vietcombank",
                protector.Protect("123456789"), "6789", "SYNTHETIC ACCEPTANCE", admin.Id, Now));
            await payDb.SaveChangesAsync();
            var transfer = new ReportResidentServiceRequestTransferRequest("LOCAL-ACCEPTANCE-TRANSFER", Now, "Chuyển khoản tổng hợp");
            var path = $"/api/v1/resident/service-requests/{id}/payment/report-transfer";
            using var reported = await residentA.Client.PostAsJsonAsync(path, transfer);
            Assert.Equal(HttpStatusCode.OK, reported.StatusCode);
            var pending = await payDb.Payments.AsNoTracking().SingleAsync();
            Assert.Equal(PaymentStatus.PENDING, pending.Status);
            await Reject(residentA, id, "feedback", HttpStatusCode.Conflict, Feedback);
            using var duplicate = await residentA.Client.PostAsJsonAsync(path, transfer);
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            accountant.Client.DefaultRequestHeaders.Add("Idempotency-Key", "acceptance-confirm-payment");
            using var confirmed = await accountant.Client.PostAsync($"/api/v1/payments/{pending.Id}/confirm", null);
            Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
            using var replay = await accountant.Client.PostAsync($"/api/v1/payments/{pending.Id}/confirm", null);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(PaymentStatus.CONFIRMED, (await payDb.Payments.AsNoTracking().SingleAsync()).Status);
            using var receipt = await residentA.Client.GetAsync($"/api/v1/resident/billing/payments/{pending.Id}/receipt");
            Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
            using var foreignReceipt = await residentB.Client.GetAsync($"/api/v1/resident/billing/payments/{pending.Id}/receipt");
            Assert.Equal(HttpStatusCode.NotFound, foreignReceipt.StatusCode);
            Assert.Single(await billDb.Invoices.ToArrayAsync());
            Assert.Single(await payDb.Payments.ToArrayAsync());
        }
        else { Assert.Empty(await billDb.Invoices.ToArrayAsync()); Assert.Empty(await payDb.Payments.ToArrayAsync()); }
        await Call(residentA, id, "feedback", Feedback);
        await Reject(residentB, id, "feedback", HttpStatusCode.NotFound, Feedback);
    }
}
