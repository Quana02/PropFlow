using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Billing.Application.Invoices;
using PropFlow.Modules.Billing.Domain.FeeRateRules;
using PropFlow.Modules.Billing.Domain.FeeTypes;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;
public sealed class BillingWorkspaceTests
{
    private sealed class Apartments : IApartmentOverviewSource, IApartmentResidentRelationshipSource
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<IReadOnlyList<Guid>> GetActiveApartmentIdsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([Id]);
        public Task<IReadOnlyList<ActiveApartmentOption>> GetActiveApartmentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ActiveApartmentOption>>([new(Id,"A-101",1)]);
        public Task<IReadOnlyList<ApartmentReference>> GetApartmentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApartmentReference>>([new(Id,"A-101",1)]);
        public Task<IReadOnlyList<ResidentApartmentOwnership>> GetOwnershipsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Task.FromResult<IReadOnlyList<ResidentApartmentOwnership>>([]);
        public Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(Guid id, DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<IReadOnlyList<Guid>> GetCurrentOwnerResidentIdsAsync(DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
    private sealed class Zone : ICurrentBuildingTimeZone { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh"); }
    private sealed class Fixture : IAsyncDisposable
    {
        public BillingDbContext Db { get; } = new(new DbContextOptionsBuilder<BillingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public PaymentsDbContext Payments { get; } = new(new DbContextOptionsBuilder<PaymentsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Apartments Apartments { get; } = new();
        public BillingWorkspace Service { get; }
        public Guid Actor { get; } = Guid.NewGuid();
        public Fixture() => Service = new(Db, Apartments, Apartments, new ConfirmedPaymentSource(Payments), new Zone());
        public async Task<DraftInput> Draft(string number = "INV-01")
        {
            var feeId = await Service.SaveFeeAsync(null, new("FEE-" + number, "Phí quản lý", "AREA", "MONTHLY", null), Actor, default);
            var rateId = await Service.AddRateAsync(new(feeId, "Đơn giá gốc", 1001m, "m²", new(2020,1,1), null), Actor, default);
            return new(number, Apartments.Id, new(2026,1,1), new(2026,1,31), new(2026,2,10), "Đối chiếu", [new(rateId, 12.3456m, "Diện tích kế toán kiểm tra")]);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Payments.DisposeAsync(); }
    }

    [Fact]
    public async Task Draft_issue_detail_preserves_source_lines_and_confirmed_only_balances()
    {
        await using var f = new Fixture(); var input = await f.Draft();
        var id = await f.Service.SaveDraftAsync(null, input, f.Actor, default);
        var draft = (await f.Service.DetailAsync(id, default))!;
        Assert.Equal("A-101", draft.ApartmentNumber); Assert.Equal(12358m, draft.TotalAmount);
        Assert.Equal(0, draft.OutstandingAmount); Assert.Equal("DRAFT", draft.Status);
        await f.Service.IssueAsync(id, new(new(2026,2,1), new(2026,2,10)), f.Actor, default);
        var rate = await f.Db.FeeRateRules.SingleAsync(); rate.UpdateRule("Đổi sau phát hành", "AREA", "MONTHLY", 9999m, new(2020,1,1), f.Actor, DateTimeOffset.UtcNow);
        await f.Db.SaveChangesAsync();
        var pending = new Payment("P1",id,2000,"CASH",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        var confirmed = new Payment("P2",id,3000,"CASH",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        var rejected = new Payment("P3",id,1000,"CASH",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        confirmed.Confirm(f.Actor,12358,DateTimeOffset.UtcNow); rejected.Reject(f.Actor,"Sai",DateTimeOffset.UtcNow);
        f.Payments.Payments.AddRange(pending,confirmed,rejected); await f.Payments.SaveChangesAsync();
        var detail = (await f.Service.DetailAsync(id, default))!;
        Assert.Equal(1001m, detail.Lines.Single().UnitRate); Assert.Equal(12358m, detail.TotalAmount);
        Assert.Equal(3000m, detail.ConfirmedPaidAmount); Assert.Equal(9358m, detail.OutstandingAmount);
        Assert.Equal("PARTIAL", detail.SettlementStatus); Assert.Equal(2,detail.History.Count);
        Assert.Equal(409,(await Assert.ThrowsAsync<BillingException>(()=>f.Service.SaveDraftAsync(id,input,f.Actor,default))).Status);
        Assert.Equal("invoice_has_payments",(await Assert.ThrowsAsync<BillingException>(()=>f.Service.CancelAsync(id,"Hủy",f.Actor,default))).Code);
    }

    [Fact]
    public async Task Draft_update_replaces_lines_and_rejects_unknown_apartment_invalid_precision_and_expired_rates()
    {
        await using var f = new Fixture(); var input = await f.Draft();
        Assert.Equal("apartment_invalid", (await Assert.ThrowsAsync<BillingException>(()=>f.Service.SaveDraftAsync(null,input with { ApartmentUnitId=Guid.NewGuid() },f.Actor,default))).Code);
        Assert.Equal("quantity_invalid", (await Assert.ThrowsAsync<BillingException>(()=>f.Service.SaveDraftAsync(null,input with { Lines=[new(input.Lines[0].RateId,1.00001m,null)] },f.Actor,default))).Code);
        var id=await f.Service.SaveDraftAsync(null,input,f.Actor,default);
        await f.Service.SaveDraftAsync(id,input with { Lines=[new(input.Lines[0].RateId,2m,null)] },f.Actor,default);
        Assert.Equal(2002m,(await f.Service.DetailAsync(id,default))!.TotalAmount);
        Assert.Single(await f.Db.InvoiceItems.ToArrayAsync());
        Assert.Equal("invoice_exists",(await Assert.ThrowsAsync<BillingException>(()=>f.Service.SaveDraftAsync(null,input,f.Actor,default))).Code);
        await f.Service.SetRateActiveAsync(input.Lines[0].RateId,false,f.Actor,default);
        Assert.Equal("rate_not_applicable",(await Assert.ThrowsAsync<BillingException>(()=>f.Service.SaveDraftAsync(id,input,f.Actor,default))).Code);
    }

    [Theory]
    [InlineData("TIERED")]
    [InlineData("CUSTOM")]
    public async Task Unknown_calculation_methods_cannot_silently_use_quantity_times_rate(string method)
    {
        await using var f = new Fixture(); var input = await f.Draft();
        var rate = await f.Db.FeeRateRules.SingleAsync();
        rate.UpdateRule("Quy tắc riêng", method, "MONTHLY", 1001m, new(2020,1,1), f.Actor, DateTimeOffset.UtcNow);
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<BillingException>(() => f.Service.SaveDraftAsync(null, input, f.Actor, default));
        Assert.Equal("rule_not_supported", error.Code);
        Assert.Empty(await f.Db.Invoices.ToArrayAsync());
    }

    [Fact]
    public async Task Search_filters_totals_and_export_cover_all_pages_and_exclude_cancelled_obligations()
    {
        await using var f = new Fixture();
        for(var i=0;i<25;i++) {
            var invoice=new Invoice("INV-"+i,f.Apartments.Id,new(2024,1,1),new(2024,1,31),f.Actor,DateTimeOffset.UtcNow);
            invoice.AddItem("FEE","Phí","FIXED",1,100,f.Actor,DateTimeOffset.UtcNow);
            invoice.Issue(f.Actor,new(2024,2,1),DateTimeOffset.UtcNow,new(2024,2,10));
            if(i==24) invoice.Cancel(f.Actor,"Hủy",DateTimeOffset.UtcNow);
            f.Db.Invoices.Add(invoice);
        }
        await f.Db.SaveChangesAsync();
        var filter=new InvoiceFilter(Search:"INV",Settlement:"OVERDUE");
        var page=await f.Service.ListAsync(filter,1,20,default); Assert.Equal(24,page.Total); Assert.Equal(20,page.Items.Count); Assert.Equal(2400,page.OutstandingAmount);
        var report=await f.Service.ReportAsync(filter,default); Assert.Equal(24,report.Count);
        Assert.Empty(await f.Service.ReportAsync(new(ApartmentId:Guid.NewGuid()),default));
        var bytes=InvoiceDocuments.Excel(report,filter,DateTimeOffset.UtcNow);
        using var zip=new ZipArchive(new MemoryStream(bytes)); XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream=zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); var xml=XDocument.Load(stream);
        Assert.Equal(25,xml.Descendants(ns+"row").Count());
        Assert.Equal(2400m,xml.Descendants(ns+"c").Where(c=>((string?)c.Attribute("r"))?.StartsWith("J")==true).Elements(ns+"v").Sum(v=>decimal.Parse(v.Value,System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Print_and_excel_escape_untrusted_text_and_preserve_large_amounts()
    {
        await using var f=new Fixture(); var input=await f.Draft("=DANGEROUS"); var id=await f.Service.SaveDraftAsync(null,input with { Note="<script>alert(1)</script>" },f.Actor,default);
        var invoice=(await f.Service.DetailAsync(id,default))!;
        var html=Encoding.UTF8.GetString(InvoiceDocuments.Print(invoice,DateTimeOffset.UtcNow));
        Assert.DoesNotContain("<script>",html); Assert.Contains("&lt;script&gt;",html); Assert.Contains("A-101",html);
        var bytes=InvoiceDocuments.Excel([invoice with { TotalAmount=123456789012345678m }],new(),DateTimeOffset.UtcNow);
        using var zip=new ZipArchive(new MemoryStream(bytes)); XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream=zip.GetEntry("xl/worksheets/sheet2.xml")!.Open(); var xml=XDocument.Load(stream);
        Assert.Empty(xml.Descendants(ns+"f"));
        var cell=xml.Descendants(ns+"c").Single(c=>(string?)c.Attribute("r")=="H2");
        Assert.Equal("inlineStr",(string?)cell.Attribute("t")); Assert.Equal("123456789012345678",cell.Descendants(ns+"t").Single().Value);
    }

    [Fact]
    public void Payment_report_uses_numeric_amounts_and_preserves_status_and_invoice_reference()
    {
        var invoiceId=Guid.NewGuid();
        var rows=new PropFlow.Modules.Payments.Contracts.PaymentReportRow[] {
            new(Guid.NewGuid(),"PAY-1",invoiceId,123456m,"CASH",DateTimeOffset.UtcNow,"REF",null,"PENDING",null,null)
        };
        var bytes=InvoiceDocuments.PaymentExcel(rows,new Dictionary<Guid,string>{{invoiceId,"INV-1"}},"PENDING",null,null,DateTimeOffset.UtcNow);
        using var zip=new ZipArchive(new MemoryStream(bytes)); XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var data=zip.GetEntry("xl/worksheets/sheet2.xml")!.Open();var xml=XDocument.Load(data);
        Assert.Equal("123456",xml.Descendants(ns+"c").Single(c=>(string?)c.Attribute("r")=="D2").Element(ns+"v")!.Value);
        Assert.Contains("INV-1",xml.ToString());
        using var summary=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();var totals=XDocument.Load(summary);
        Assert.Equal("0",totals.Descendants(ns+"c").Single(c=>(string?)c.Attribute("r")=="B9").Element(ns+"v")!.Value);
    }
}
