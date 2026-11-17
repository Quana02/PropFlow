using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Domain.BankTransfers;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class PaymentAuthorizationHttpTests
{
    private async Task<string> FinancialSnapshot()
    {
        using var scope = host.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var notices = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        return Digest(new
        {
            Invoices = await billing.Invoices.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.TotalAmount }).ToArrayAsync(),
            Recipients = await billing.InvoiceRecipients.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Payments = await payments.Payments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            History = await payments.PaymentStatusHistory.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Instructions = await payments.PaymentInstructions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Operations = await payments.PaymentOperations.AsNoTracking().OrderBy(x => x.Key).ToArrayAsync(),
            Transfers = await payments.BankTransfers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Notices = await notices.Notifications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Mail = await notices.FinanceEmails.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }

    private async Task<RegistrationResidentSeed> LinkResident(Actor owner)
    {
        var identity = await database.SeedResidentAsync($"contact-{Guid.NewGuid():N}@example.invalid");
        using var scope = host.Services.CreateScope();
        var residents = scope.ServiceProvider.GetRequiredService<ResidentsDbContext>();
        var row = await residents.Residents.SingleAsync(x => x.Id == identity.ResidentId);
        row.LinkUserAccount(owner.Id, owner.Id, Clock.GetUtcNow());
        await residents.SaveChangesAsync();
        return identity;
    }

    private async Task<Invoice> SeedInvoice(RegistrationResidentSeed identity, Actor owner, Guid issuer, bool settled = false)
    {
        using var scope = host.Services.CreateScope();
        var billing = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var now = Clock.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var period = settled ? day.AddMonths(-1) : day;
        var invoice = new Invoice("B2-" + Guid.NewGuid().ToString("N"), identity.ApartmentId, period, period, issuer, now);
        invoice.AddItem("SERVICE", "Phí fixture", "FIXED", 1, 100000, issuer, now);
        invoice.Issue(issuer, day, now, day.AddDays(7));
        billing.Invoices.Add(invoice);
        billing.InvoiceRecipients.Add(new InvoiceRecipient(invoice.Id, owner.Id, "Cư dân fixture", identity.Email,
            "A-101", 70, identity.ResidentId));
        await billing.SaveChangesAsync();
        if (settled) await SeedPaid(invoice.Id, issuer, invoice.TotalAmount);
        return invoice;
    }

    private async Task SeedPaid(Guid invoiceId, Guid actor, decimal amount)
    {
        using var scope = host.Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var now = Clock.GetUtcNow();
        var payment = new Payment("B2-" + Guid.NewGuid().ToString("N"), invoiceId, amount, "CASH", now, now);
        payment.Confirm(actor, amount, now);
        payments.Payments.Add(payment);
        await payments.SaveChangesAsync();
    }

    private async Task<decimal> Paid(Guid invoiceId)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IConfirmedPaymentSource>().TotalAsync(invoiceId, default);
    }

    [Fact]
    public async Task Accountant_assign_and_dismiss_are_scoped_and_settlement_is_single_use()
    {
        var accountant = await Login("ACCOUNTANT");
        var denied = await DeniedActors("ACCOUNTANT");
        var owner = await Login("RESIDENT");
        var other = await Login("RESIDENT");
        var ownerIdentity = await LinkResident(owner);
        var otherIdentity = await LinkResident(other);
        var invoice = await SeedInvoice(ownerIdentity, owner, accountant.Id);
        var unpinned = await SeedInvoice(otherIdentity, other, accountant.Id);
        var destination = await SeedDestination(accountant.Id);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        db.PaymentInstructions.Add(new PaymentInstruction(invoice.Id, destination.Id, Clock.GetUtcNow()));
        await db.SaveChangesAsync();

        async Task<BankTransfer> Review(long id)
        {
            using var worker = host.Services.CreateScope();
            var store = worker.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var inbox = worker.ServiceProvider.GetRequiredService<SePayInbox>();
            await inbox.ReceiveAsync(new SePayTransfer(id, "VCB", "123456789", "in", 90000, null, "fixture unmatched", $"FIXTURE-{id}"), default);
            var transferId = await store.BankTransfers.Where(x => x.TransactionId == id).Select(x => x.Id).SingleAsync();
            await inbox.ProcessAsync(transferId, default);
            var row = await store.BankTransfers.AsNoTracking().SingleAsync(x => x.Id == transferId);
            Assert.Equal("REVIEW", row.Status);
            return row;
        }
        var assign = await Review(1901);
        var dismiss = await Review(1902);
        var race = await Review(1903);
        string Path(Guid id, string action) => $"/api/v1/payments/bank-transfers/{id}/{action}";
        var assignment = new AssignBankTransferRequest(invoice.Id, 90000, "Đối soát theo chứng từ fixture", assign.Revision);
        var dismissRequest = new DismissBankTransferRequest("Giao dịch fixture xử lý ngoài hàng chờ", dismiss.Revision);
        var initial = await FinancialSnapshot();
        await Deny(denied, HttpMethod.Post, Path(assign.Id, "assign"), assignment);
        await Deny(denied, HttpMethod.Post, Path(dismiss.Id, "dismiss"), dismissRequest);
        using var noInstruction = await Send(accountant.Client, HttpMethod.Post, Path(assign.Id, "assign"), assignment with { InvoiceId = unpinned.Id });
        await AssertProblem(noInstruction, HttpStatusCode.Conflict, "bank_invoice_unavailable");
        using var over = await Send(accountant.Client, HttpMethod.Post, Path(assign.Id, "assign"), assignment with { Amount = 100001 });
        await AssertProblem(over, HttpStatusCode.Conflict, "bank_allocation_exceeds_balance");
        using var overTransfer = await Send(accountant.Client, HttpMethod.Post, Path(assign.Id, "assign"), assignment with { Amount = 95000 });
        await AssertProblem(overTransfer, HttpStatusCode.Conflict, "bank_assignment_invalid");
        Assert.Equal(initial, await FinancialSnapshot());
        using var assigned = await Send(accountant.Client, HttpMethod.Post, Path(assign.Id, "assign"), assignment);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        var queued = await db.BankTransfers.AsNoTracking().SingleAsync(x => x.Id == assign.Id);
        Assert.Equal("MANUAL_MATCH_REQUESTED", queued.Outcome);
        Assert.Equal(invoice.Id, queued.AssignedInvoiceId);
        Assert.Equal(assignment.Amount, queued.RequestedAmount);
        Assert.Equal(accountant.Id, queued.ReviewedBy);
        Assert.Equal(0, await Paid(invoice.Id));
        using var repeated = await Send(accountant.Client, HttpMethod.Post, Path(assign.Id, "assign"), assignment);
        await AssertProblem(repeated, HttpStatusCode.Conflict, "bank_transfer_changed");
        await using (var worker1 = host.Services.CreateAsyncScope())
        await using (var worker2 = host.Services.CreateAsyncScope())
            await Task.WhenAll(worker1.ServiceProvider.GetRequiredService<SePayInbox>().ProcessAsync(assign.Id, default),
                worker2.ServiceProvider.GetRequiredService<SePayInbox>().ProcessAsync(assign.Id, default));
        var settled = await db.BankTransfers.AsNoTracking().SingleAsync(x => x.Id == assign.Id);
        Assert.Equal("CONFIRMED", settled.Status);
        Assert.Equal(invoice.Id, settled.InvoiceId);
        Assert.Equal(90000, settled.AllocatedAmount);
        var payment = Assert.Single(await db.Payments.AsNoTracking().ToArrayAsync());
        Assert.Equal(settled.PaymentId, payment.Id);
        Assert.Equal(90000, await Paid(invoice.Id));
        Assert.Equal(0, await Paid(unpinned.Id));
        using var cannotDismiss = await Send(accountant.Client, HttpMethod.Post, Path(assign.Id, "dismiss"), dismissRequest with { ExpectedRevision = settled.Revision });
        await AssertProblem(cannotDismiss, HttpStatusCode.Conflict, "bank_resolution_invalid");
        using var dismissed = await Send(accountant.Client, HttpMethod.Post, Path(dismiss.Id, "dismiss"), dismissRequest);
        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);
        var resolved = await db.BankTransfers.AsNoTracking().SingleAsync(x => x.Id == dismiss.Id);
        Assert.Equal("DISMISSED", resolved.Status);
        Assert.Equal(accountant.Id, resolved.ReviewedBy);
        Assert.Null(resolved.PaymentId);
        using var duplicateDismiss = await Send(accountant.Client, HttpMethod.Post, Path(dismiss.Id, "dismiss"), dismissRequest with { ExpectedRevision = resolved.Revision });
        await AssertProblem(duplicateDismiss, HttpStatusCode.Conflict, "bank_resolution_invalid");
        var simultaneous = await Task.WhenAll(
            Send(accountant.Client, HttpMethod.Post, Path(race.Id, "assign"), assignment with { ExpectedRevision = race.Revision, Amount = 10000 }),
            Send(accountant.Client, HttpMethod.Post, Path(race.Id, "dismiss"), dismissRequest with { ExpectedRevision = race.Revision }));
        Assert.Single(simultaneous, response => response.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(simultaneous, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in simultaneous) response.Dispose();
        Assert.Equal(3, await db.BankTransfers.CountAsync());
        Assert.Single(await db.Payments.AsNoTracking().ToArrayAsync());
        Assert.Equal(90000, await Paid(invoice.Id));
        var notices = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        var notice = Assert.Single(await notices.Notifications.Where(x => x.SourceId == payment.Id).ToArrayAsync());
        Assert.Equal(owner.Id, notice.RecipientUserId);
        Assert.Equal("Thanh toán hóa đơn thành công", notice.Title);
        var detail = await accountant.Client.GetStringAsync(Path(assign.Id, "").TrimEnd('/'));
        Assert.True(!detail.Contains(ownerIdentity.Email, StringComparison.OrdinalIgnoreCase) &&
            !detail.Contains(ownerIdentity.PhoneNumber, StringComparison.Ordinal));
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task Resident_payment_submission_checks_invoice_recipient_and_is_pending_once()
    {
        const string path = "/api/v1/resident/payments";
        var accountant = await Login("ACCOUNTANT");
        var owner = await Login("RESIDENT");
        var other = await Login("RESIDENT");
        var unlinked = await Login("RESIDENT");
        var denied = await DeniedActors("RESIDENT");
        var ownerIdentity = await LinkResident(owner);
        var otherIdentity = await LinkResident(other);
        var invoice = await SeedInvoice(ownerIdentity, owner, accountant.Id);
        var unrelated = await SeedInvoice(otherIdentity, other, accountant.Id);
        var paidInvoice = await SeedInvoice(otherIdentity, other, accountant.Id, settled: true);
        var payload = new CollectPaymentRequest(invoice.Id, 30000, "BANK_TRANSFER", "B2-SUBMIT-REFERENCE", null);
        var initial = await FinancialSnapshot();
        await Deny(denied, HttpMethod.Post, path, payload);
        foreach (var actor in new[] { other, unlinked })
        {
            using var foreign = await Send(actor.Client, HttpMethod.Post, path, payload, Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        }
        using var overpayment = await Send(owner.Client, HttpMethod.Post, path, payload with { Amount = 100001 }, "b2-overpayment");
        Assert.Equal(HttpStatusCode.Conflict, overpayment.StatusCode);
        using var paid = await Send(other.Client, HttpMethod.Post, path, payload with { InvoiceId = paidInvoice.Id }, "b2-paid");
        Assert.Equal(HttpStatusCode.Conflict, paid.StatusCode);
        Assert.Equal(initial, await FinancialSnapshot());
        var manipulated = new
        {
            payload.InvoiceId, payload.Amount, payload.PaymentMethodCode, payload.ReferenceNumber,
            status = "CONFIRMED", payerId = other.Id, submittedBy = other.Id, residentId = otherIdentity.ResidentId,
            apartmentId = otherIdentity.ApartmentId, destinationId = Guid.NewGuid(), recipientEmail = "injected@example.invalid"
        };
        using var submitted = await Send(owner.Client, HttpMethod.Post, path, manipulated, "b2-submit");
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        var reported = (await submitted.Content.ReadFromJsonAsync<PaymentRow>())!;
        Assert.Equal(invoice.Id, reported.InvoiceId);
        Assert.Equal(30000, reported.Amount);
        Assert.Equal("PENDING", reported.Status);
        Assert.True(reported.ConfirmedAt is null);
        var after = await FinancialSnapshot();
        using var replay = await Send(owner.Client, HttpMethod.Post, path, manipulated, "b2-submit");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(reported.Id, (await replay.Content.ReadFromJsonAsync<PaymentRow>())!.Id);
        using var changed = await Send(owner.Client, HttpMethod.Post, path, payload with { Amount = 31000 }, "b2-submit");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var sameBankReference = await Send(other.Client, HttpMethod.Post, path, payload with { InvoiceId = unrelated.Id }, "b2-unrelated-reference");
        await AssertProblem(sameBankReference, HttpStatusCode.Conflict, "transfer_reference_exists");
        Assert.Equal(after, await FinancialSnapshot());
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var row = await db.Payments.AsNoTracking().SingleAsync(x => x.Id == reported.Id);
        Assert.Equal(owner.Id, row.SubmittedBy);
        Assert.Equal(PaymentStatus.PENDING, row.Status);
        Assert.Equal(0, await Paid(invoice.Id));
        Assert.Equal(0, await Paid(unrelated.Id));
        Assert.Empty(await db.PaymentInstructions.ToArrayAsync());
        var communication = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
        var notice = Assert.Single(await communication.Notifications.Where(x => x.SourceType == "PAYMENT").ToArrayAsync());
        Assert.Equal(reported.Id, notice.SourceId);
        Assert.Equal(owner.Id, notice.RecipientUserId);
        Assert.Equal("Thanh toán đang chờ xác minh", notice.Title);
        Assert.False(await communication.FinanceEmails.AnyAsync(x => x.AttachmentFileName != null));
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task Resident_service_request_transfer_checks_scope_and_never_confirms_the_report()
    {
        var manager = await Login("MANAGER");
        var owner = await Login("RESIDENT");
        var other = await Login("RESIDENT");
        var unlinked = await Login("RESIDENT");
        var denied = await DeniedActors("RESIDENT");
        var ownerIdentity = await LinkResident(owner);
        var otherIdentity = await LinkResident(other);
        await SeedDestination(manager.Id);
        using var scope = host.Services.CreateScope();
        var requests = scope.ServiceProvider.GetRequiredService<ServiceRequestsDbContext>();
        await requests.Database.MigrateAsync();
        var billing = scope.ServiceProvider.GetRequiredService<IServiceRequestBilling>();
        var billDb = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var now = Clock.GetUtcNow();

        async Task<ServiceRequest> Seed(RegistrationResidentSeed identity, Actor resident, bool issue = true)
        {
            var request = new ServiceRequest("B2-" + Guid.NewGuid().ToString("N")[..12], identity.ResidentId, identity.ResidencyId!.Value,
                "Yêu cầu fixture", "Đối chứng báo chuyển khoản", now, apartmentUnitId: identity.ApartmentId, submittedByUserId: resident.Id);
            if (issue)
            {
                request.MarkUnderReview(now);
                request.MarkAssigned(now);
                request.Resolve(now);
                var settled = await billing.IssueAsync(new(request.Id, request.RequestNumber, identity.ApartmentId, identity.ResidentId,
                    resident.Id, "Cư dân fixture", 100000, 0, null, manager.Id, now), default);
                request.LinkServiceInvoice(settled!.InvoiceId, now);
            }
            requests.ServiceRequests.Add(request);
            await requests.SaveChangesAsync();
            return request;
        }

        var request = await Seed(ownerIdentity, owner);
        var foreignRequest = await Seed(otherIdentity, other);
        var unapproved = await Seed(ownerIdentity, owner, false);
        var paidRequest = await Seed(ownerIdentity, owner);
        var cancelled = await Seed(ownerIdentity, owner);
        var cancelledBill = await billDb.Invoices.SingleAsync(x => x.Id == cancelled.ServiceInvoiceId);
        cancelledBill.Cancel(manager.Id, "Hủy hóa đơn fixture", now);
        await billDb.SaveChangesAsync();
        await SeedPaid(paidRequest.ServiceInvoiceId!.Value, manager.Id, 100000);
        string Path(Guid id) => $"/api/v1/resident/service-requests/{id}/payment/report-transfer";
        var report = new ReportResidentServiceRequestTransferRequest("B2-REPORT-REFERENCE", now, "Đã chuyển khoản fixture");
        var initial = await FinancialSnapshot();
        await Deny(denied, HttpMethod.Post, Path(request.Id), report);
        foreach (var (actor, target) in new[] { (other, request.Id), (owner, foreignRequest.Id),
            (unlinked, request.Id), (owner, unapproved.Id), (owner, Guid.NewGuid()) })
        {
            using var deniedScope = await Send(actor.Client, HttpMethod.Post, Path(target), report);
            await AssertProblem(deniedScope, HttpStatusCode.NotFound, "service_request_invoice_not_found");
        }
        using var paid = await Send(owner.Client, HttpMethod.Post, Path(paidRequest.Id), report);
        await AssertProblem(paid, HttpStatusCode.Conflict, "invoice_already_paid");
        using var canceled = await Send(owner.Client, HttpMethod.Post, Path(cancelled.Id), report);
        await AssertProblem(canceled, HttpStatusCode.Conflict, "invoice_cancelled");
        using var future = await Send(owner.Client, HttpMethod.Post, Path(request.Id), report with { TransferredAt = now.AddHours(1) });
        await AssertProblem(future, HttpStatusCode.BadRequest, "invalid_transfer_time");
        Assert.Equal(initial, await FinancialSnapshot());
        var forged = new { report.ReferenceNumber, report.TransferredAt, report.Note, amount = 1,
            invoiceId = foreignRequest.ServiceInvoiceId, payerId = other.Id, ownerId = other.Id, status = "CONFIRMED" };
        var results = await Task.WhenAll(Send(owner.Client, HttpMethod.Post, Path(request.Id), forged),
            Send(owner.Client, HttpMethod.Post, Path(request.Id), forged));
        var success = Assert.Single(results, response => response.StatusCode == HttpStatusCode.OK);
        var rejected = Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertProblem(rejected, HttpStatusCode.Conflict, "payment_pending");
        var view = (await success.Content.ReadFromJsonAsync<ResidentServiceRequestPaymentView>())!;
        foreach (var result in results) result.Dispose();
        Assert.Equal(request.ServiceInvoiceId, view.InvoiceId);
        Assert.Equal("PENDING", view.PaymentStatus);
        Assert.Equal(100000, view.OutstandingAmount);
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var pending = await db.Payments.AsNoTracking().SingleAsync(x => x.InvoiceId == view.InvoiceId);
        Assert.Equal(owner.Id, pending.SubmittedBy);
        Assert.Equal(100000, pending.Amount);
        Assert.Equal(PaymentStatus.PENDING, pending.Status);
        Assert.False(await db.Payments.AnyAsync(x => x.InvoiceId == foreignRequest.ServiceInvoiceId));
        Assert.Equal(0, await Paid(view.InvoiceId));
        var unchanged = await requests.ServiceRequests.AsNoTracking().SingleAsync(x => x.Id == request.Id);
        Assert.Equal(request.Status, unchanged.Status);
        Assert.Equal(request.ServiceInvoiceId, unchanged.ServiceInvoiceId);
        var snapshot = await FinancialSnapshot();
        using var duplicateBankReference = await Send(other.Client, HttpMethod.Post, Path(foreignRequest.Id), report);
        await AssertProblem(duplicateBankReference, HttpStatusCode.Conflict, "transfer_reference_exists");
        Assert.Equal(snapshot, await FinancialSnapshot());
        await scope.ServiceProvider.GetRequiredService<IBillingCycle>().NotifyPaymentAsync(view.InvoiceId, default);
        var notice = await scope.ServiceProvider.GetRequiredService<CommunicationDbContext>().Notifications
            .SingleAsync(x => x.SourceId == pending.Id);
        Assert.Equal(owner.Id, notice.RecipientUserId);
        Assert.Equal("Thanh toán đang chờ xác minh", notice.Title);
        Assert.Empty(provider.Calls);
    }
}
