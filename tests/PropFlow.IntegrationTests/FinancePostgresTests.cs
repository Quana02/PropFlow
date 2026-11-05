using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Application.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;
public sealed class FinancePostgresTests
{
    [Fact]
    public async Task Record_retry_concurrent_confirm_and_cancelled_invoice_preserve_exact_payable_amount()
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
            billing.Invoices.AddRange(invoice,cancelled);await billing.SaveChangesAsync();
            var store=new PaymentFinanceStore(payments,new InvoiceFinancialSource(billing));
            var request=new RecordPaymentRequest(invoice.Id,700,"BANK_TRANSFER",now,"REF-001",null);
            var first=await store.RecordAsync(request,"create-one",actor,default);Assert.Equal(MutationOutcome.Success,first.Outcome);
            var retry=await store.RecordAsync(request,"create-one",actor,default);Assert.Equal(first.Payment!.Id,retry.Payment!.Id);
            Assert.Equal(MutationOutcome.IdempotencyConflict,(await store.RecordAsync(request with{Amount=600},"create-one",actor,default)).Outcome);
            Assert.Equal(MutationOutcome.IdempotencyConflict,(await store.RecordAsync(request,"create-one",Guid.NewGuid(),default)).Outcome);
            Assert.Equal(MutationOutcome.DuplicateReference,(await store.RecordAsync(request,"duplicate-ref",actor,default)).Outcome);
            var second=await store.RecordAsync(request with{ReferenceNumber="REF-002"},"create-two",actor,default);
            Assert.Equal(MutationOutcome.Success,second.Outcome);
            Assert.Equal(0,await new ConfirmedPaymentSource(payments).TotalAsync(invoice.Id,default));
            async Task<MutationResult> Confirm(Guid id,string key) {
                await using var b=Billing(); await using var p=Payments();
                return await new PaymentFinanceStore(p,new InvoiceFinancialSource(b)).ChangeAsync(id,"CONFIRM",null,key,actor,default);
            }
            var results=await Task.WhenAll(Confirm(first.Payment.Id,"confirm-one"),Confirm(second.Payment!.Id,"confirm-two"));
            Assert.Single(results.Where(x=>x.Outcome==MutationOutcome.Success));Assert.Single(results.Where(x=>x.Outcome==MutationOutcome.Overpayment));
            Assert.Equal(700m,await new ConfirmedPaymentSource(payments).TotalAsync(invoice.Id,default));
            var failedIndex=Array.FindIndex(results,x=>x.Outcome==MutationOutcome.Overpayment);
            var pendingId=failedIndex==0?first.Payment.Id:second.Payment.Id;
            await using(var b=Billing()) await using(var p=Payments()) {
                var result=await new PaymentFinanceStore(p,new InvoiceFinancialSource(b)).ChangeAsync(pendingId,"REJECT","Sai khoản thu","reject-one",actor,default);
                Assert.Equal(MutationOutcome.Success,result.Outcome);
            }
            Assert.Equal(MutationOutcome.InvalidInvoice,(await store.RecordAsync(request with{InvoiceId=cancelled.Id},"create-cancelled",actor,default)).Outcome);
            // Replayed confirmation returns the same logical payment without an extra confirmed row.
            var succeeded=results.Single(x=>x.Outcome==MutationOutcome.Success).Payment!;
            var successKey=results[0].Outcome==MutationOutcome.Success?"confirm-one":"confirm-two";
            Assert.Equal(MutationOutcome.Success,(await Confirm(succeeded.Id,successKey)).Outcome);
            Assert.Equal(700m,await new ConfirmedPaymentSource(payments).TotalAsync(invoice.Id,default));
            Assert.Equal(2,await payments.Payments.CountAsync());
            var directory = new FinanceWorkflowHttpTests.Directory();
            var workspace = new BillingWorkspace(billing, directory, directory, new ConfirmedPaymentSource(payments), directory);
            var fee = await workspace.SaveFeeAsync(null,new("FEE-SQL","Phí","AREA","MONTHLY",null),actor,default);
            var rate = await workspace.AddRateAsync(new(fee,"Gốc",1000,"m²",new(2020,1,1),null),actor,default);
            var draft = new DraftInput("INV-SQL",FinanceWorkflowHttpTests.Directory.Apartment,new(2026,1,1),new(2026,1,31),new(2026,2,10),null,[new(rate,12.3456m,null)]);
            var created = await workspace.SaveDraftAsync(null,draft,actor,default);
            await workspace.SaveDraftAsync(created,draft with { Lines=[new(rate,2.5m,"Sửa số lượng")] },actor,default);
            await workspace.IssueAsync(created,new(new(2026,2,1),new(2026,2,10)),actor,default);
            var view=(await workspace.DetailAsync(created,default))!;
            Assert.Equal(2500m,view.TotalAmount);Assert.Single(view.Lines);
            var report=await workspace.ReportAsync(new(Search:"INV-SQL",Settlement:"OVERDUE"),default);
            Assert.Single(report);Assert.Equal(2500m,report[0].OutstandingAmount);
            var page=await store.ListAsync("CONFIRMED",null,1,20,"desc",default);
            Assert.Equal("INV-CONCURRENT",page.Items.Single().InvoiceNumber);Assert.Equal(700m,page.TotalAmount);
            var racePayment=await store.RecordAsync(request with{InvoiceId=created,Amount=2500,ReferenceNumber="RACE"},"race-record",actor,default);
            async Task<bool> CancelRace() {
                await using var b=Billing(); await using var p=Payments();
                try { await new BillingWorkspace(b,directory,directory,new ConfirmedPaymentSource(p),directory).CancelAsync(created,"Hủy đồng thời",actor,default);return true; }
                catch(BillingException e) when(e.Code=="invoice_has_payments") { return false; }
            }
            var cancellation=CancelRace();var confirmation=Confirm(racePayment.Payment!.Id,"race-confirm");
            await Task.WhenAll(cancellation,confirmation);
            var final=(await new InvoiceFinancialSource(billing).FindAsync(created,default))!;
            var paidFinal=await new ConfirmedPaymentSource(payments).TotalAsync(created,default);
            if(await cancellation) { Assert.Equal("CANCELLED",final.Status);Assert.Equal(0,paidFinal);Assert.Equal(MutationOutcome.InvalidInvoice,(await confirmation).Outcome); }
            else { Assert.Equal("ISSUED",final.Status);Assert.Equal(2500m,paidFinal);Assert.Equal(MutationOutcome.Success,(await confirmation).Outcome); }
        }
        finally
        {
            Assert.StartsWith("propflow_finance_test_",database);Assert.Equal(54,database.Length);
            NpgsqlConnection.ClearAllPools();
            await using var drop=new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)",admin);await drop.ExecuteNonQueryAsync();
        }
    }
}

