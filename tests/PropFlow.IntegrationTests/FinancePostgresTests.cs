using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;
public sealed class FinancePostgresTests
{
    [Fact]
    public async Task Submit_retry_concurrent_confirm_and_cancelled_invoice_preserve_exact_payable_amount()
    {
        var database="propflow_finance_test_"+Guid.NewGuid().ToString("N");
        var config=new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString()); config.Database="postgres";
        await using var admin=new NpgsqlConnection(config.ConnectionString); await admin.OpenAsync();
        await using(var create=new NpgsqlCommand($"CREATE DATABASE \"{database}\"",admin)) await create.ExecuteNonQueryAsync();
        try
        {
            config.Database=database; var connection=config.ConnectionString;
            BillingDbContext Billing()=>new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(connection).Options);
            PaymentsDbContext Payments()=>new(new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql(connection).Options);
            await using var billing=Billing(); await using var payments=Payments();
            foreach(var db in new DbContext[]{billing,payments}) { Assert.Equal(database,db.Database.GetDbConnection().Database); await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript()); }
            var actor=Guid.NewGuid(); var now=DateTimeOffset.UtcNow;
            var invoice=new Invoice("INV-CONCURRENT",Guid.NewGuid(),new(2026,1,1),new(2026,1,31),actor,now);
            invoice.AddItem("FEE","Phí","FIXED",1,1000,actor,now); invoice.Issue(actor,new(2026,2,1),now,new(2026,2,10));
            var cancelled=new Invoice("INV-CANCELLED",Guid.NewGuid(),new(2026,1,1),new(2026,1,31),actor,now);
            cancelled.AddItem("FEE","Phí","FIXED",1,1000,actor,now);cancelled.Issue(actor,new(2026,2,1),now);cancelled.Cancel(actor,"Hủy",now);
            billing.Invoices.AddRange(invoice,cancelled);
            billing.InvoiceRecipients.AddRange(
                new InvoiceRecipient(invoice.Id, actor, "Cư dân", null, "A-101", 50),
                new InvoiceRecipient(cancelled.Id, actor, "Cư dân", null, "A-102", 50));
            await billing.SaveChangesAsync();
            var store=new PaymentFinanceStore(payments,new InvoiceFinancialSource(billing));
            var request=new CollectPaymentRequest(invoice.Id,700,"BANK_TRANSFER","REF-001",null);
            var first=await store.SubmitAsync(request,"create-one",actor,default);Assert.Equal(MutationOutcome.Success,first.Outcome);
            var retry=await store.SubmitAsync(request,"create-one",actor,default);Assert.Equal(first.Payment!.Id,retry.Payment!.Id);
            Assert.Equal(MutationOutcome.IdempotencyConflict,(await store.SubmitAsync(request with{Amount=600},"create-one",actor,default)).Outcome);
            Assert.Equal(MutationOutcome.NotFound,(await store.SubmitAsync(request,"create-one",Guid.NewGuid(),default)).Outcome);
            Assert.Equal(MutationOutcome.Conflict,(await store.SubmitAsync(request with{ReferenceNumber="REF-002"},"create-two",actor,default)).Outcome);
            // Existing pending payments can still race even though the current submit endpoint rejects a second one.
            var second = new Payment("PT-HISTORICAL", invoice.Id, 700m, "BANK_TRANSFER", now, now, actor, "REF-002");
            payments.Payments.Add(second);
            await payments.SaveChangesAsync();
            Assert.Equal(0,await new ConfirmedPaymentSource(payments).TotalAsync(invoice.Id,default));
            async Task<MutationResult> Confirm(Guid id,string key) {
                await using var b=Billing(); await using var p=Payments();
                return await new PaymentFinanceStore(p,new InvoiceFinancialSource(b)).ChangeAsync(id,"CONFIRM",null,key,actor,default);
            }
            var results=await Task.WhenAll(Confirm(first.Payment.Id,"confirm-one"),Confirm(second.Id,"confirm-two"));
            Assert.Single(results.Where(x=>x.Outcome==MutationOutcome.Success));Assert.Single(results.Where(x=>x.Outcome==MutationOutcome.Overpayment));
            Assert.Equal(700m,await new ConfirmedPaymentSource(payments).TotalAsync(invoice.Id,default));
            var failedIndex=Array.FindIndex(results,x=>x.Outcome==MutationOutcome.Overpayment);
            var pendingId=failedIndex==0?first.Payment.Id:second.Id;
            await using(var b=Billing()) await using(var p=Payments()) {
                var result=await new PaymentFinanceStore(p,new InvoiceFinancialSource(b)).ChangeAsync(pendingId,"REJECT","Sai khoản thu","reject-one",actor,default);
                Assert.Equal(MutationOutcome.Success,result.Outcome);
            }
            Assert.Equal(MutationOutcome.NotFound,(await store.SubmitAsync(request with{InvoiceId=cancelled.Id},"create-cancelled",actor,default)).Outcome);
            // Replayed confirmation returns the same logical payment without an extra confirmed row.
            var succeeded=results.Single(x=>x.Outcome==MutationOutcome.Success).Payment!;
            var successKey=results[0].Outcome==MutationOutcome.Success?"confirm-one":"confirm-two";
            Assert.Equal(MutationOutcome.Success,(await Confirm(succeeded.Id,successKey)).Outcome);
            Assert.Equal(700m,await new ConfirmedPaymentSource(payments).TotalAsync(invoice.Id,default));
            Assert.Equal(2,await payments.Payments.CountAsync());
            var page=await store.ListAsync("CONFIRMED",null,1,20,"desc",default);
            var confirmed = Assert.Single(page.Items);
            Assert.Equal("INV-CONCURRENT", confirmed.InvoiceNumber);
            Assert.Equal(700m, confirmed.Amount);
        }
        finally
        {
            Assert.StartsWith("propflow_finance_test_",database);Assert.Equal(54,database.Length);
            NpgsqlConnection.ClearAllPools();
            await using var drop=new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();
        }
    }
}

