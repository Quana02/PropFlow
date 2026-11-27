using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Infrastructure.Finance;

namespace PropFlow.BillingCycleTests;

public sealed partial class BillingCyclePostgresTests
{
    [Fact]
    public async Task Pre_upgrade_collect_key_replay_returns_original_payment_without_duplicate()
    {
        await using var b=Billing();await using var p=Payments();await using var c=Communication();
        var cycle=Cycle(b,p,c);await cycle.SaveRateAsync(new(10000,Month),actor,default);await cycle.RunAsync(default);
        var invoice=await b.Invoices.SingleAsync();
        var request=new CollectPaymentRequest(invoice.Id,100000,"CASH","legacy-replay","Fixture");
        var store=new PaymentFinanceStore(p,new InvoiceFinancialSource(b));
        var result=await store.CollectAsync(request,"legacy-collect",actor,default);
        Assert.Equal(MutationOutcome.Success,result.Outcome);
        var legacyJson=JsonSerializer.Serialize(new { request.InvoiceId,request.Amount,request.PaymentMethodCode,request.ReferenceNumber,request.Note });
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyJson)));
        await p.Database.ExecuteSqlInterpolatedAsync($"UPDATE payments.payment_operations SET payload_hash={hash} WHERE idempotency_key={"legacy-collect"}");
        p.ChangeTracker.Clear();
        var replay=await store.CollectAsync(request,"legacy-collect",actor,default);
        Assert.Equal(MutationOutcome.Success,replay.Outcome);
        Assert.Equal(result.Payment!.Id,replay.Payment!.Id);
        Assert.Equal(1,await p.Payments.CountAsync());
        Assert.Equal(MutationOutcome.IdempotencyConflict,(await store.CollectAsync(request with {Amount=1},"legacy-collect",actor,default)).Outcome);
    }
}
