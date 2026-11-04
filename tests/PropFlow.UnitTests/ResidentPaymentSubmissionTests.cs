using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Api.Composition;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Presentation;
using System.Reflection;
using System.Security.Claims;

namespace PropFlow.UnitTests;

public sealed class ResidentPaymentSubmissionTests
{
    [Fact]
    public async Task SandboxRejectsRealSubmissionWithoutWritingPaymentsAndKeepsOwnershipCheck()
    {
        await using var db = new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.SePaySandboxConnections.Add(new SePaySandboxConnection { DemoEnabled = true });
        await db.SaveChangesAsync();
        var owner = Guid.NewGuid();
        var store = new PaymentFinanceStore(db, new OwnedInvoice(owner));
        var request = new CollectPaymentRequest(Guid.NewGuid(), 1000, "BANK_TRANSFER", "REF", null);
        Assert.Equal(MutationOutcome.NotFound, (await store.SubmitAsync(request, "other", Guid.NewGuid(), default)).Outcome);
        Assert.Equal(MutationOutcome.SandboxEnabled, (await store.SubmitAsync(request, "owner", owner, default)).Outcome);
        Assert.Empty(db.Payments); Assert.Empty(db.PaymentOperations); Assert.Empty(db.PaymentStatusHistory);
    }

    [Fact]
    public async Task DecoratedResidentSubmissionDelegatesAndNotifiesCommittedPayment()
    {
        var actor = Guid.NewGuid(); var request = new CollectPaymentRequest(Guid.NewGuid(), 1000, "BANK_TRANSFER", "REF", null);
        var store = new SubmissionStore();
        var cycle = DispatchProxy.Create<IBillingCycle, BillingProbe>();
        var probe = (BillingProbe)(object)cycle;
        IPaymentFinanceStore decorated = new PaymentFinanceNotifications(store, cycle, NullLogger<PaymentFinanceNotifications>.Instance);
        var result = await new PaymentFinanceUseCases(decorated).SubmitAsync(request, "idempotency", actor, default);
        Assert.Equal(MutationOutcome.Success, result.Outcome);
        Assert.Equal((request, "idempotency", actor), store.Received);
        Assert.Equal(request.InvoiceId, Assert.Single(probe.Notified));
        probe.Fail = true;
        Assert.Equal(MutationOutcome.Success, (await decorated.SubmitAsync(request, "retry", actor, default)).Outcome);
        store.Outcome = MutationOutcome.SandboxEnabled;
        probe.Notified.Clear();
        Assert.Equal(MutationOutcome.SandboxEnabled, (await decorated.SubmitAsync(request, "blocked", actor, default)).Outcome);
        Assert.Empty(probe.Notified);
    }

    [Fact]
    public async Task ResidentEndpointExplainsSandboxConflict()
    {
        var cycle = DispatchProxy.Create<IBillingCycle, BillingProbe>();
        var decorated = new PaymentFinanceNotifications(new SubmissionStore { Outcome = MutationOutcome.SandboxEnabled },
            cycle, NullLogger<PaymentFinanceNotifications>.Instance);
        var controller = new ResidentPaymentsController(new PaymentFinanceUseCases(decorated))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "test")) } }
        };
        var response = Assert.IsType<ObjectResult>(await controller.Submit(new(Guid.NewGuid(), 1000, "BANK_TRANSFER", "REF", null), "key", default));
        Assert.Equal(409, response.StatusCode);
        Assert.Contains("Sandbox", Assert.IsType<ProblemDetails>(response.Value).Title!);
    }

    public class BillingProbe : DispatchProxy
    {
        public readonly List<Guid> Notified = [];
        public bool Fail;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IBillingCycle.NotifyPaymentAsync)) throw new NotSupportedException();
            Notified.Add((Guid)args![0]!);
            return Fail ? Task.FromException(new InvalidOperationException("Test delivery failure")) : Task.CompletedTask;
        }
    }

    private sealed class OwnedInvoice(Guid owner) : IInvoiceFinancialSource
    {
        public Task<bool> IsRecipientAsync(Guid invoiceId, Guid userId, CancellationToken ct) => Task.FromResult(userId == owner);
        public Task<InvoiceFinancialSnapshot?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<InvoiceFinancialSnapshot>> ListIssuedAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SubmissionStore : IPaymentFinanceStore
    {
        public MutationOutcome Outcome = MutationOutcome.Success;
        public (CollectPaymentRequest Request, string Key, Guid Actor)? Received;
        public Task<MutationResult> SubmitAsync(CollectPaymentRequest request, string key, Guid actorId, CancellationToken ct)
        {
            Received = (request, key, actorId);
            return Task.FromResult(new MutationResult(Outcome, Outcome == MutationOutcome.Success
                ? new(Guid.NewGuid(), "PT-TEST", request.InvoiceId, request.Amount, request.PaymentMethodCode, DateTimeOffset.UtcNow, request.ReferenceNumber, null, "PENDING", null) : null));
        }
        public Task<IReadOnlyList<PaymentRow>> ByInvoiceAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<PagedPayments> ListAsync(string status, string? search, int page, int pageSize, string sort, CancellationToken ct, string? filter = null) => throw new NotSupportedException();
        public Task<PaymentRow?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<MutationResult> ChangeAsync(Guid id, string action, string? reason, string key, Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<MutationResult> CollectAsync(CollectPaymentRequest request, string key, Guid actor, CancellationToken ct) => throw new NotSupportedException();
    }
}
