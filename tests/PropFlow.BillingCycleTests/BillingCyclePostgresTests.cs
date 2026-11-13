using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.Billing.Domain.Invoices;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace PropFlow.BillingCycleTests;

public sealed partial class BillingCyclePostgresTests : IAsyncLifetime
{
    private readonly string name = "propflow_cycle_test_" + Guid.NewGuid().ToString("N");
    private string connection = "", admin = "";
    private readonly Guid actor = Guid.NewGuid(), user = Guid.NewGuid(), apartment = Guid.NewGuid();
    private DateOnly Month => new(DateTime.Today.Year, DateTime.Today.Month, 1);
    public async Task InitializeAsync()
    {
        var source = Environment.GetEnvironmentVariable("PROPFLOW_TEST_DATABASE") ?? throw new InvalidOperationException("Set PROPFLOW_TEST_DATABASE to a local PostgreSQL admin connection.");
        var builder = new NpgsqlConnectionStringBuilder(source) { Database = "postgres", Pooling = false }; admin = builder.ConnectionString;
        await using var c = new NpgsqlConnection(admin); await c.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {name}", c); await create.ExecuteNonQueryAsync();
        builder.Database = name; connection = builder.ConnectionString;
        await using var billing = Billing(); await billing.Database.MigrateAsync();
        await using var payment = Payments(); await payment.Database.MigrateAsync();
        await using var communication = Communication(); await communication.Database.MigrateAsync();
        await using var administration = new PropFlow.Modules.Administration.Infrastructure.Persistence.AdministrationDbContext(
            new DbContextOptionsBuilder<PropFlow.Modules.Administration.Infrastructure.Persistence.AdministrationDbContext>()
                .UseNpgsql(connection, options => options.MigrationsHistoryTable("__EFMigrationsHistory", "administration")).Options);
        await administration.Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        if (string.IsNullOrEmpty(admin)) return;
        await using var c = new NpgsqlConnection(admin); await c.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {name} WITH (FORCE)", c); await drop.ExecuteNonQueryAsync();
    }
    private BillingDbContext Billing() => new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(connection,
        x => x.MigrationsHistoryTable("__EFMigrationsHistory", "billing")).Options);
    private PaymentsDbContext Payments() => new(new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql(connection,
        x => x.MigrationsHistoryTable("__EFMigrationsHistory", "payments")).Options);
    private CommunicationDbContext Communication() => new(new DbContextOptionsBuilder<CommunicationDbContext>().UseNpgsql(connection,
        x => x.MigrationsHistoryTable("__EFMigrationsHistory", "communication")).Options);
    private sealed class Directory(BillingHousehold household) : IBillingHouseholdDirectory
    {
        public int ResidentCount { get; set; } = 3;
        public DateOnly? CountedOn { get; private set; }
        public Task<IReadOnlyDictionary<Guid, int>> ResidentCountsAsync(IReadOnlyCollection<Guid> apartmentIds, DateOnly date, CancellationToken ct)
        {
            CountedOn = date;
            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(new Dictionary<Guid, int> { [household.ApartmentId] = ResidentCount });
        }
        public Dictionary<Guid, Guid> LinkedAccounts { get; } = [];
        public Task<IReadOnlyDictionary<Guid, Guid>> LinkedAccountsAsync(IReadOnlyCollection<Guid> residentIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(LinkedAccounts.Where(x => residentIds.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value));
        public Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date, CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingHousehold>>([household]);
    }
    private BillingCycle Cycle(BillingDbContext b, PaymentsDbContext p, CommunicationDbContext c) => new(b,
        new Directory(new(apartment, "A-101", 70, user, "Chủ hộ <test>", "head@example.invalid")),
        new ConfirmedPaymentSource(p), new PaymentNoticeSource(p), new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)));
    private sealed class ExplicitZone(string zone) : PropFlow.Modules.PropertyAssets.Contracts.ICurrentBuildingTimeZone
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult(zone);
    }
    [Fact]
    public async Task Meter_readings_carry_across_months_and_years_and_reject_changes_that_break_later_usage()
    {
        await using var billing = Billing();
        await using var payments = Payments();
        await using var communication = Communication();
        var november = new DateOnly(Month.Year - 1, 11, 1);
        var december = november.AddMonths(1);
        var january = december.AddMonths(1);
        var cycle = Cycle(billing, payments, communication);

        var first = await cycle.ElectricityAsync(apartment, november, default);
        Assert.Equal(0, first.PreviousElectricityReading);
        Assert.Equal(0, first.PreviousWaterReading);
        await cycle.SaveUtilitiesAsync(apartment, new(november, 100, 20), actor, default);
        await cycle.SaveElectricityAsync(apartment, new(december, 150), actor, default);
        var next = await cycle.ElectricityAsync(apartment, december, default);
        Assert.Equal(100, next.PreviousElectricityReading);
        Assert.Equal(150, next.CurrentElectricityReading);
        Assert.Equal(50, next.Kwh);
        Assert.Null(next.WaterM3);

        await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveUtilitiesAsync(apartment, new(january, 149, 25), actor, default));
        await using var secondBilling = Billing();
        await using var secondPayments = Payments();
        await using var secondCommunication = Communication();
        var second = Cycle(secondBilling, secondPayments, secondCommunication);
        await Task.WhenAll(cycle.SaveUtilitiesAsync(apartment, new(january, 150, 25), actor, default),
            second.SaveUtilitiesAsync(apartment, new(january, 150, 25), actor, default));
        var newYear = await cycle.ElectricityAsync(apartment, january, default);
        Assert.Equal(150, newYear.PreviousElectricityReading);
        Assert.Equal(20, newYear.PreviousWaterReading);
        Assert.Equal(0, newYear.Kwh);
        Assert.Equal(5, newYear.WaterM3);
        Assert.Equal(0, (await cycle.ElectricityAsync(apartment, november.AddMonths(-1), default)).PreviousElectricityReading);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cycle.SaveElectricityAsync(apartment, new(december, 140), actor, default));
        Assert.Equal(50, (await cycle.ElectricityAsync(apartment, december, default)).Kwh);
        await cycle.SaveUtilitiesAsync(apartment, new(january, 170, 27), actor, default);
        Assert.Equal(20, (await cycle.ElectricityAsync(apartment, january, default)).Kwh);

        Assert.Equal(3, await billing.ElectricityUsages.CountAsync());
    }

    [Fact]
    public async Task Notification_api_paginates_newest_first_and_updates_only_the_current_user()
    {
        await using var communication = Communication();
        var otherUser = Guid.NewGuid();
        var time = DateTimeOffset.UtcNow.AddHours(-2);
        var latest = Guid.NewGuid();
        var action = $"/manager/service-requests/{latest}";
        var notifications = Enumerable.Range(0, 101)
            .Select(index => new PropFlow.Modules.Communication.Domain.Notifications.Notification(user,
                PropFlow.Modules.Communication.Domain.Notifications.NotificationType.SERVICE_REQUEST,
                $"Yêu cầu {index}", "Thông báo kiểm thử", time.AddMinutes(index), sourceType: "SERVICE_REQUEST", sourceId: latest, actionPath: action))
            .ToArray();
        var foreign = new PropFlow.Modules.Communication.Domain.Notifications.Notification(otherUser,
            PropFlow.Modules.Communication.Domain.Notifications.NotificationType.SYSTEM, "Thông báo riêng", "Nội dung riêng", time);
        communication.Notifications.AddRange(notifications);
        communication.Notifications.Add(foreign);
        await communication.SaveChangesAsync();

        await using var factory = new Factory(connection, user, apartment);
        using var manager = factory.Client("MANAGER", user, "MANAGE_OPERATIONS");
        using var staff = factory.Client("STAFF", otherUser, "PERFORM_ASSIGNED_OPERATIONS");
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        const string path = "api/v1/notifications";
        async Task<int> Count(HttpClient client) =>
            (await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"{path}/unread-count")).GetProperty("count").GetInt32();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(101, await Count(manager));
        var first = (await manager.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"{path}?pageSize=1"))!;
        Assert.Equal(notifications[^1].Id, Assert.Single(first).GetProperty("id").GetGuid());
        Assert.Equal(action, first[0].GetProperty("actionPath").GetString());
        Assert.False(first[0].GetProperty("isRead").GetBoolean());
        var next = (await manager.GetFromJsonAsync<System.Text.Json.JsonElement[]>($"{path}?page=2&pageSize=1"))!;
        Assert.Equal(notifications[^2].Id, Assert.Single(next).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.PostAsJsonAsync($"{path}/{foreign.Id}/read", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"{path}/{notifications[^1].Id}/read", new { })).StatusCode);
        Assert.Equal(100, await Count(manager));
        Assert.Equal(HttpStatusCode.OK, (await manager.PostAsJsonAsync($"{path}/read-all", new { })).StatusCode);
        Assert.Equal(0, await Count(manager));
        Assert.Equal(1, await Count(staff));
    }
    [Fact]
    public async Task Monthly_generation_obeys_the_configured_building_timezone_at_month_boundary()
    {
        await using var b=Billing();await using var p=Payments();await using var c=Communication();
        var clock=new Clock(new DateTimeOffset(Month.AddDays(-1).ToDateTime(new TimeOnly(19,0)),TimeSpan.Zero));
        var cycle=new BillingCycle(b,new Directory(new(apartment,"A-101",70,user,"Head",null)),new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)),clock,new ExplicitZone("UTC"));
        await cycle.SaveRateAsync(new(10000,Month.AddMonths(-1)),actor,default);await cycle.RunAsync(default);
        Assert.Equal(Month.AddMonths(-1),(await b.Invoices.SingleAsync()).BillingPeriodStart);
        Assert.False(await b.Invoices.AnyAsync(x=>x.BillingPeriodStart==Month));
    }
    [Fact]
    public async Task Audited_correction_bundles_parking_adds_electricity_resends_new_revision_and_exposes_payment_owner()
    {
        await using var b=Billing();await using var p=Payments();await using var c=Communication();var cycle=Cycle(b,p,c);
        var old=new Invoice("HD-TEST-A-0404",apartment,Month,Month.AddMonths(1).AddDays(-1),actor,DateTimeOffset.UtcNow);
        old.AddItem("MGMT","Phí quản lý","AREA",96.2m,12000,actor,DateTimeOffset.UtcNow,unitName:"m²");
        old.AddItem("WATER","Nước sinh hoạt","VOLUME",9,18000,actor,DateTimeOffset.UtcNow,unitName:"m³");
        old.AddItem("PARKING_MOTORBIKE","Phí giữ xe máy","VEHICLE",2,120000,actor,DateTimeOffset.UtcNow,unitName:"xe");
        old.Issue(actor,Month,DateTimeOffset.UtcNow,Month.AddDays(14));b.Invoices.Add(old);
        b.InvoiceRecipients.Add(new(old.Id,user,"Chủ hóa đơn kiểm thử","head@example.invalid","A-0404",96.2m));await b.SaveChangesAsync();
        Assert.Equal(1556400,old.TotalAmount);await cycle.SendInvoiceAsync(old.Id,default);
        var input=new ServiceInvoiceCorrection("correction:test:a0404",10000,10000,3000,9,50,"Điều chỉnh theo yêu cầu đã xác nhận.");
        await cycle.CorrectUnpaidServiceInvoiceAsync(old.Id,input,actor,default);
        await cycle.CorrectUnpaidServiceInvoiceAsync(old.Id,input,actor,default);
        await using var check=Billing();var updated=await check.Invoices.Include(x=>x.Items).SingleAsync();
        Assert.Equal(old.Id,updated.Id);Assert.Equal(1202000,updated.TotalAmount);Assert.Equal(3,updated.Items.Count);
        Assert.DoesNotContain(updated.Items,x=>x.FeeCodeSnapshot.Contains("PARK"));
        var audit=Assert.Single(await check.InvoiceStatusHistory.ToArrayAsync());using var json=System.Text.Json.JsonDocument.Parse(audit.Reason!);
        Assert.Equal(1556400,json.RootElement.GetProperty("Before").GetProperty("TotalAmount").GetDecimal());
        Assert.Equal(3,json.RootElement.GetProperty("Before").GetProperty("Items").GetArrayLength());
        Assert.Equal(50,(await check.ElectricityUsages.SingleAsync()).Kwh);Assert.Equal(9,(await check.ElectricityUsages.SingleAsync()).WaterM3);
        Assert.Empty(await p.Payments.ToArrayAsync());
        await cycle.SendInvoiceAsync(old.Id,default);await cycle.SendInvoiceAsync(old.Id,default);
        Assert.Single(await c.FinanceEmails.Where(x=>x.Subject=="Hóa đơn đã điều chỉnh").ToArrayAsync());
        var doc=WebUtility.HtmlDecode(Encoding.UTF8.GetString((await cycle.InvoiceDocumentAsync(old.Id,user,default))!.Content));
        Assert.DoesNotContain("<td>Phí giữ xe",doc);Assert.Contains("Tiền điện",doc);Assert.Contains("1.202.000",doc);
        var evidence=Environment.GetEnvironmentVariable("PROPFLOW_BILLING_EVIDENCE_DIRECTORY");
        if(!string.IsNullOrWhiteSpace(evidence)){System.IO.Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"a0404-correction-from-postgres-test.html"),doc);}
        var store=new PaymentFinanceStore(p,new InvoiceFinancialSource(b));var collected=await store.CollectAsync(new(old.Id,1000,"CASH",null,null),"owner-test",actor,default);
        Assert.Equal("Chủ hóa đơn kiểm thử",collected.Payment!.ResidentName);Assert.Equal("A-0404",collected.Payment.ApartmentNumber);Assert.Equal("HD-TEST-A-0404",collected.Payment.InvoiceNumber);
        var list=await store.ListAsync("CONFIRMED",null,1,20,"desc",default);Assert.Equal("Chủ hóa đơn kiểm thử",Assert.Single(list.Items).ResidentName);
        Assert.Single((await store.ListAsync("CONFIRMED","Chủ hóa đơn kiểm thử",1,20,"desc",default)).Items);
        Assert.Single((await store.ListAsync("CONFIRMED","A-0404",1,20,"desc",default)).Items);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>cycle.CorrectUnpaidServiceInvoiceAsync(old.Id,input with{Key="correction:second"},actor,default));
        Assert.Equal(1202000,(await check.Invoices.SingleAsync()).TotalAmount);
    }
    [Fact]
    public async Task Metered_water_and_electricity_wait_for_both_readings_and_generate_two_separate_stable_invoices()
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        var directory = new Directory(new(apartment, "A-101", 70, user, "Head", "head@example.invalid", Guid.NewGuid(), "RENTAL", Month));
        var clock = new Clock(new DateTimeOffset(Month.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(7)));
        var cycle = new BillingCycle(b, directory, new ConfirmedPaymentSource(p), new PaymentNoticeSource(p), new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)), clock);
        await cycle.SaveRateAsync(new(10000, Month, ElectricityUnitRate: 2000, WaterM3UnitRate: 12000), actor, default);
        await new RentalPricing(b, directory).SaveAsync(new(apartment, "MONTHLY", 3000000, Month), actor, default);
        await cycle.SaveUtilitiesAsync(apartment, new(Month.AddMonths(-1), null, 5), actor, default);
        await cycle.RunAsync(default);
        Assert.False(await b.Invoices.AnyAsync(x => x.Type == InvoiceType.SERVICE_FEE));
        Assert.Null((await cycle.ElectricityAsync(apartment, Month.AddMonths(-1), default)).Kwh);
        await cycle.SaveUtilitiesAsync(apartment, new(Month.AddMonths(-1), 50, 5), actor, default);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        var service = await b.Invoices.Include(x => x.Items).SingleAsync(x => x.Type == InvoiceType.SERVICE_FEE);
        Assert.Equal(860000, service.TotalAmount);
        Assert.Equal(3, service.Items.Count);
        Assert.Equal(10000, Assert.Single(service.Items.Where(x=>x.FeeCodeSnapshot==BillingCycle.ServiceFeeCode)).UnitRate);
        Assert.DoesNotContain(service.Items,x=>x.FeeCodeSnapshot.Contains("PARK",StringComparison.OrdinalIgnoreCase));
        var rendered=WebUtility.HtmlDecode(Encoding.UTF8.GetString((await cycle.InvoiceDocumentAsync(service.Id,user,default))!.Content));
        Assert.Contains("Tiền điện",rendered);Assert.Contains("kWh",rendered);Assert.Contains("Nước sinh hoạt",rendered);Assert.Contains("m³",rendered);
        Assert.DoesNotContain("<td>Phí giữ xe",rendered);
        Assert.Contains(await c.FinanceEmails.ToArrayAsync(),mail=>mail.Subject=="Hóa đơn phí dịch vụ mới" && WebUtility.HtmlDecode(mail.Html)==rendered && mail.UserId==user);
        var evidence=Environment.GetEnvironmentVariable("PROPFLOW_BILLING_EVIDENCE_DIRECTORY");
        if(!string.IsNullOrWhiteSpace(evidence)) {System.IO.Directory.CreateDirectory(evidence);await File.WriteAllTextAsync(Path.Combine(evidence,"new-service-invoice-from-postgres-test.html"),rendered);}

        Assert.Equal(60000, Assert.Single(service.Items.Where(x => x.FeeCodeSnapshot == BillingCycle.WaterVolumeFeeCode)).LineAmount);
        Assert.Equal(100000, Assert.Single(service.Items.Where(x => x.FeeCodeSnapshot == BillingCycle.ElectricityFeeCode)).LineAmount);
        var rental = await b.Invoices.SingleAsync(x => x.Type == InvoiceType.MONTHLY_RENTAL);
        Assert.Equal(3000000, rental.TotalAmount);
        var rentalNotice = await c.Notifications.SingleAsync(x => x.SourceId == rental.Id);
        Assert.Equal(user, rentalNotice.RecipientUserId);
        Assert.Equal($"/resident/billing?invoice={rental.Id}", rentalNotice.ActionPath);
        Assert.Contains("Tiền thuê: 3.000.000 đ", rentalNotice.Message);
        Assert.DoesNotContain("Phí dịch vụ:", rentalNotice.Message);
        var rentalMail = await c.FinanceEmails.SingleAsync(x => x.Subject == "Hóa đơn tiền thuê tháng mới");
        var rentalDocument = await cycle.InvoiceDocumentAsync(rental.Id, user, default);
        Assert.Equal(Encoding.UTF8.GetString(rentalDocument!.Content), rentalMail.Html);
        Assert.DoesNotContain("Phí dịch vụ: 0 đ", rentalMail.Html);
        Assert.Equal(3860000, await b.Invoices.SumAsync(x => x.TotalAmount));
        Assert.Equal(2, await b.Invoices.CountAsync()); Assert.Empty(await p.Payments.ToArrayAsync());
        Assert.Equal(2, await c.Notifications.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => cycle.SaveUtilitiesAsync(apartment, new(Month.AddMonths(-1), 1, 1), actor, default));
        await cycle.SaveRateAsync(new(10000, Month.AddMonths(1), ElectricityUnitRate: 3000, WaterM3UnitRate: 15000), actor, default);
        await cycle.SaveUtilitiesAsync(apartment, new(Month, 50, 5), actor, default);
        clock.Now = clock.Now.AddMonths(1); await cycle.RunAsync(default);
        Assert.Equal(700000, (await b.Invoices.SingleAsync(x => x.Type == InvoiceType.SERVICE_FEE && x.BillingPeriodStart == Month.AddMonths(1))).TotalAmount);
        Assert.Equal(860000, (await b.Invoices.SingleAsync(x => x.Id == service.Id)).TotalAmount);
        Assert.Equal(12000, (await cycle.RatesAsync(default)).Single(x => x.EffectiveFrom == Month).WaterM3UnitRate);
    }
    [Fact]
    public async Task Request_invoice_never_uses_another_household_members_email()
    {
        await using var billing = Billing();
        await using var payments = Payments();
        await using var communication = Communication();
        var directory = new Directory(new(apartment, "A-101", 70, user, "Chủ hộ", "household@example.invalid", Guid.NewGuid()));
        var totals = new ConfirmedPaymentSource(payments);
        var requester = Guid.NewGuid();
        var requesterAccount = Guid.NewGuid();
        var invoice = await new ServiceRequestBilling(billing, directory, totals).IssueAsync(
            new(Guid.NewGuid(), "YC-TEST", apartment, requester, null, "Người gửi yêu cầu", 100000, 0, null, actor, DateTimeOffset.UtcNow), default);
        var cycle = new BillingCycle(billing, directory, totals, new PaymentNoticeSource(payments),
            new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(communication)));

        await cycle.RunAsync(default);
        Assert.Empty(await communication.FinanceEmails.ToArrayAsync());
        Assert.Empty(await communication.Notifications.ToArrayAsync());
        directory.LinkedAccounts.Add(requester, requesterAccount);
        await cycle.RunAsync(default);

        var notice = Assert.Single(await communication.Notifications.ToArrayAsync());
        Assert.Equal(requesterAccount, notice.RecipientUserId);
        Assert.Equal(invoice!.InvoiceId, notice.SourceId);
        Assert.Equal("Hóa đơn yêu cầu dịch vụ mới", notice.Title);
        Assert.Empty(await communication.FinanceEmails.ToArrayAsync());
    }

    [Fact]
    public async Task Utility_validation_is_atomic_and_unknown_water_is_not_zero()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication(); var cycle=Cycle(b,p,c);
        foreach(var amount in new[] {-1m, 1000001m, 0.0001m})
            await Assert.ThrowsAsync<ArgumentException>(()=>cycle.SaveUtilitiesAsync(apartment,new(Month,50,amount),actor,default));
        Assert.Empty(await b.ElectricityUsages.ToArrayAsync());
        foreach(var rate in new[] {0m,-1m,1000001m,12.5m})
            await Assert.ThrowsAsync<ArgumentException>(()=>cycle.SaveRateAsync(new(10000,Month,WaterM3UnitRate:rate),actor,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>cycle.SaveRateAsync(new(10000,Month,WaterM3UnitRate:12000),actor,default));
        Assert.Empty(await b.FeeRateRules.ToArrayAsync());
        await cycle.SaveRateAsync(new(10000,Month,ElectricityUnitRate:2000,WaterM3UnitRate:12000),actor,default);
        await cycle.SaveUtilitiesAsync(apartment,new(Month.AddMonths(-1),50,null),actor,default);
        await cycle.RunAsync(default); Assert.Empty(await b.Invoices.ToArrayAsync());
    }
    [Fact]
    public async Task Overdue_collection_uses_normal_receipt_and_manual_reminder_sends_invoice_and_web_notice()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var clock=new Clock(new DateTimeOffset(Month.ToDateTime(new TimeOnly(12,0)),TimeSpan.FromHours(7)));
        var cycle=new BillingCycle(b,new Directory(new(apartment,"A-101",70,user,"Head","head@example.invalid")),
            new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)),clock);
        await cycle.SaveRateAsync(new(10000,Month),actor,default);await cycle.RunAsync(default);
        var invoice=await b.Invoices.SingleAsync(); clock.Now=clock.Now.AddDays(11);
        var key=Guid.NewGuid();
        Assert.Equal(1,await cycle.RemindAsync([invoice.Id],default,true,key));
        await cycle.RemindAsync([invoice.Id],default,true,key);
        Assert.Single(await c.FinanceEmails.Where(x=>x.Subject=="Thông báo công nợ quá hạn").ToArrayAsync());
        Assert.Equal(1,await cycle.RemindAsync([invoice.Id],default,true,Guid.NewGuid()));
        var reminderMails=await c.FinanceEmails.Where(x=>x.Subject=="Thông báo công nợ quá hạn").ToArrayAsync();
        Assert.Equal(2,reminderMails.Length);
        Assert.All(reminderMails,x=>{Assert.Contains(invoice.InvoiceNumber,x.Html);Assert.Contains("700.000",x.Html);Assert.Equal(user,x.UserId);});
        Assert.Equal(2,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
        var store=new PropFlow.Api.Composition.PaymentFinanceNotifications(new PaymentFinanceStore(p,new InvoiceFinancialSource(b)),cycle,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PropFlow.Api.Composition.PaymentFinanceNotifications>.Instance);
        var request=new CollectPaymentRequest(invoice.Id,700000,"CASH",null,null);
        var collected=await store.CollectAsync(request,"overdue-cash",actor,default);
        Assert.Equal(MutationOutcome.Success,collected.Outcome);
        await store.CollectAsync(request,"overdue-cash",actor,default);
        var receipt=await c.FinanceEmails.SingleAsync(x=>x.Subject=="Thanh toán hóa đơn thành công");
        Assert.Contains(invoice.InvoiceNumber,receipt.Html);Assert.Contains("700.000",receipt.Html);
        Assert.DoesNotContain("thanh toán hóa đơn nợ",receipt.Html,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(user,receipt.UserId);
        Assert.Single(await c.Notifications.Where(x=>x.Title=="Thanh toán hóa đơn thành công").ToArrayAsync());
        Assert.Equal(0,Assert.Single(await cycle.InvoicesAsync(user,default)).Outstanding);
        Assert.Equal(0,await cycle.RemindAsync([invoice.Id],default,true,Guid.NewGuid()));
        clock.Now=clock.Now.AddDays(1);await cycle.RunAsync(default);
        Assert.Equal(2,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
    }
    [Fact]
    public async Task Realtime_finance_notification_is_committed_deduplicated_and_sent_only_to_recipient()
    {
        await using var factory = new Factory(connection,user,apartment);
        using var resident = factory.Client("RESIDENT",user);
        using var accountant = factory.Client("ACCOUNTANT",actor,"MANAGE_FINANCE");
        Assert.Equal(HttpStatusCode.Forbidden,(await accountant.PostAsync("/hubs/resident-notifications/negotiate?negotiateVersion=1",null)).StatusCode);
        HubConnection Connect(Guid id) => new HubConnectionBuilder().WithUrl("https://localhost/hubs/resident-notifications", options => {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Headers["X-Role"] = "RESIDENT"; options.Headers["X-User"] = id.ToString();
        }).Build();
        await using var own = Connect(user); await using var other = Connect(Guid.NewGuid());
        var received = new TaskCompletionSource<PropFlow.Modules.Communication.Application.FinanceNotificationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ownCount = 0; var otherCount = 0;
        own.On<PropFlow.Modules.Communication.Application.FinanceNotificationUpdate>("FinanceNotificationCreated", n => { Interlocked.Increment(ref ownCount); received.TrySetResult(n); });
        other.On<PropFlow.Modules.Communication.Application.FinanceNotificationUpdate>("FinanceNotificationCreated", _ => Interlocked.Increment(ref otherCount));
        await own.StartAsync(); await other.StartAsync();
        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Communication.Application.IFinanceNotificationStore>();
        var notice = new PropFlow.Modules.Communication.Application.FinanceNotification(Guid.NewGuid(),user,"head@example.invalid",
            "Hóa đơn realtime","Hóa đơn đã phát hành","<p>Invoice</p>","INVOICE",Guid.NewGuid(),"/resident/billing");
        await publisher.PublishAsync(notice,default);
        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var db = Communication();
        Assert.Equal(user,(await db.Notifications.SingleAsync(x=>x.Id==pushed.Id)).RecipientUserId);
        Assert.Equal(notice.Title,pushed.Title); Assert.Equal(notice.ActionPath,pushed.ActionPath);
        await publisher.PublishAsync(notice,default);
        await publisher.PublishAsync(notice with { Email = "changed@example.invalid" }, default);
        await Task.Delay(150);
        Assert.Equal(1,ownCount); Assert.Equal(0,otherCount);
        Assert.Single(await db.FinanceEmails.ToArrayAsync());
        var state = new PropFlow.Web.Client.Features.Resident.Portal.Services.ResidentFinanceNoticeState(null!);
        var update = new PropFlow.Web.Client.Features.Finance.Services.ResidentNotice(pushed.Id,pushed.Title,pushed.Message,pushed.ActionPath,pushed.IsRead,pushed.CreatedAt);
        state.Receive(update); state.Receive(update);
        Assert.Single(state.Items); Assert.Equal(1,state.UnreadCount);
        state.Reset(); Assert.Empty(state.Items);
    }
    [Fact]
    public async Task Manual_send_and_overdue_reminder_are_deduplicated_notifications_without_financial_mutation()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var clock=new Clock(new DateTimeOffset(Month.ToDateTime(new TimeOnly(12,0)),TimeSpan.FromHours(7)));
        var cycle=new BillingCycle(b,new Directory(new(apartment,"A-101",70,user,"Head","head@example.invalid")),new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)),clock);
        await cycle.SaveRateAsync(new(10000,Month),actor,default);await cycle.RunAsync(default);var id=(await b.Invoices.SingleAsync()).Id;
        var balance=Assert.Single(await cycle.InvoicesAsync(user,default));
        Assert.Equal(1,await cycle.SendInvoiceAsync(id,default));Assert.Equal(1,await cycle.SendInvoiceAsync(id,default));
        Assert.Single(await c.Notifications.Where(x=>x.Title=="Hóa đơn đã phát hành").ToArrayAsync());
        Assert.Single(await c.FinanceEmails.Where(x=>x.Subject=="Hóa đơn đã phát hành").ToArrayAsync());
        Assert.Equal(0,await cycle.RemindAsync([id],default,true));
        clock.Now=clock.Now.AddDays(11);Assert.Equal(1,await cycle.RemindAsync([id],default,true));await cycle.RemindAsync([id],default,true);
        Assert.Single(await c.Notifications.Where(x=>x.Title=="Thông báo công nợ quá hạn").ToArrayAsync());
        Assert.All(await c.Notifications.ToArrayAsync(),n=>Assert.Equal(user,n.RecipientUserId));
        Assert.Equal(balance,Assert.Single(await cycle.InvoicesAsync(user,default)));Assert.Empty(await p.Payments.ToArrayAsync());
        Assert.NotNull(await cycle.InvoiceDocumentAsync(id,user,default));Assert.Null(await cycle.InvoiceDocumentAsync(id,Guid.NewGuid(),default));
        var draft=new Invoice("DRAFT",apartment,Month.AddMonths(1),Month.AddMonths(2).AddDays(-1),actor,DateTimeOffset.UtcNow);b.Invoices.Add(draft);await b.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>cycle.SendInvoiceAsync(draft.Id,default));
        await Assert.ThrowsAsync<KeyNotFoundException>(()=>cycle.SendInvoiceAsync(Guid.NewGuid(),default));
    }
    [Fact]
    public async Task Unpaid_and_debt_filters_count_before_paging_and_search_actual_household_fields()
    {
        await using var b=Billing();await using var p=Payments();
        for(var index=0;index<25;index++){
            var bill=new Invoice($"F-{index:00}",apartment,Month.AddMonths(-index),Month.AddMonths(1-index).AddDays(-1),actor,DateTimeOffset.UtcNow);
            bill.AddItem("SERVICE","Service","AREA",1,100,actor,DateTimeOffset.UtcNow);bill.Issue(actor,Month.AddDays(-45),DateTimeOffset.UtcNow,Month.AddDays(-40));b.Invoices.Add(bill);
        }
        await b.SaveChangesAsync();
        var invoices=await b.Invoices.OrderBy(x=>x.InvoiceNumber).ToArrayAsync();
        foreach(var invoice in invoices.Take(23))p.Payments.Add(new Payment("PT-"+invoice.InvoiceNumber,invoice.Id,10,"CASH",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow));
        await p.SaveChangesAsync();
        var store=new PaymentFinanceStore(p,new InvoiceFinancialSource(b));
        foreach(var payment in await p.Payments.ToArrayAsync())await store.ChangeAsync(payment.Id,"CONFIRM",null,"confirm-"+payment.Id,actor,default);
        var clock=new Clock(new DateTimeOffset(Month.AddDays(-40).ToDateTime(new TimeOnly(12,0)),TimeSpan.FromHours(7)));
        var queries=new BillingFinanceQueries(new InvoiceFinancialSource(b),new ConfirmedPaymentSource(p),new BuildingZone(),clock:clock,directory:new Directory(new(apartment,"A-101",70,user,"Matching Resident",null)));
        var result=await queries.UnpaidAsync("Matching Resident",1,20,"desc",default,"UNPAID");Assert.Equal(2,result.Total);Assert.Equal(2,result.Items.Count);Assert.Equal(25,result.Counts!["ALL"]);Assert.Equal(23,result.Counts["PARTIAL"]);
        var page2=await queries.UnpaidAsync("A-101",2,20,"desc",default,"PARTIAL");Assert.Equal(23,page2.Total);Assert.Equal(3,page2.Items.Count);
        Assert.Empty((await queries.OverdueAsync(null,1,20,"desc",default)).Items);
        clock.Now=clock.Now.AddDays(40);
        Assert.Empty((await queries.UnpaidAsync(null,1,20,"desc",default)).Items);
        var debts=await queries.OverdueAsync(null,2,20,"desc",default,"30_60");Assert.Equal(25,debts.Total);Assert.Equal(5,debts.Items.Count);
        Assert.Empty((await queries.UnpaidAsync("no-such-person",1,20,"desc",default)).Items);
        Assert.Equal(23,(await store.ListAsync("CONFIRMED",null,1,20,"desc",default,"CASH")).Total);
        Assert.Equal(0,(await store.ListAsync("CONFIRMED",null,1,20,"desc",default,"BANK_TRANSFER")).Total);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(123.456)]
    public async Task Electricity_uses_previous_month_kwh_waits_for_entry_and_keeps_issued_snapshot(decimal kwh)
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        var directory = new Directory(new(apartment, "A-101", 70, user, "Head", "head@example.invalid", Capacity: "OWNER"));
        var clock = new Clock(new DateTimeOffset(Month.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(7)).AddDays(14));
        var cycle = new BillingCycle(b, directory, new ConfirmedPaymentSource(p), new PaymentNoticeSource(p),
            new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)), clock);
        await cycle.SaveRateAsync(new(10000, Month, 50000, 3000), actor, default);
        Assert.Equal(3000, Assert.Single(await cycle.RatesAsync(default)).ElectricityUnitRate);
        await cycle.RunAsync(default);
        Assert.Empty(await b.Invoices.ToArrayAsync());
        Assert.Null((await cycle.ElectricityAsync(apartment, Month.AddMonths(-1), default)).Kwh);
        await cycle.SaveElectricityAsync(apartment, new(Month.AddMonths(-1), 10), actor, default);
        await cycle.SaveElectricityAsync(apartment, new(Month.AddMonths(-1), kwh), actor, default);
        Assert.Equal(kwh, Assert.Single(await b.ElectricityUsages.ToArrayAsync()).Kwh);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        var invoice = Assert.Single(await b.Invoices.Include(x => x.Items).ToArrayAsync());
        var electricAmount = decimal.Round(kwh * 3000, 0, MidpointRounding.AwayFromZero);
        Assert.Equal(850000 + electricAmount, invoice.TotalAmount);
        Assert.Equal(kwh > 0 ? 3 : 2, invoice.Items.Count);
        if (kwh > 0) {
            var line = Assert.Single(invoice.Items.Where(x => x.FeeCodeSnapshot == BillingCycle.ElectricityFeeCode));
            Assert.Equal(kwh, line.Quantity); Assert.Equal(3000, line.UnitRate); Assert.Equal(electricAmount, line.LineAmount);
            Assert.Contains(Month.AddMonths(-1).ToString("MM/yyyy"), line.Description);
            var html = WebUtility.HtmlDecode(Encoding.UTF8.GetString((await cycle.InvoiceDocumentAsync(invoice.Id, user, default))!.Content));
            Assert.Contains("Tiền điện", html); Assert.Contains("kWh", html);
        }
        Assert.True((await cycle.ElectricityAsync(apartment, Month.AddMonths(-1), default)).IsLocked);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cycle.SaveElectricityAsync(apartment, new(Month.AddMonths(-1), 999), actor, default));
        await cycle.SaveRateAsync(new(10000, Month.AddMonths(1), 50000, 4000), actor, default);
        await cycle.SaveElectricityAsync(apartment, new(Month, kwh + 50), actor, default);
        clock.Now = clock.Now.AddMonths(1);
        await cycle.RunAsync(default);
        Assert.Equal(1050000, (await b.Invoices.SingleAsync(x => x.BillingPeriodStart == Month.AddMonths(1))).TotalAmount);
        Assert.Equal(850000 + electricAmount, (await b.Invoices.SingleAsync(x => x.Id == invoice.Id)).TotalAmount);
    }
    [Fact]
    public async Task Electricity_rejects_invalid_readings_and_rates_without_persisting_them()
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication(); var cycle = Cycle(b,p,c);
        foreach (var kwh in new[] { -1m, 1000001m, 0.0001m })
            await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveElectricityAsync(apartment, new(Month, kwh), actor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveElectricityAsync(Guid.NewGuid(), new(Month, 50), actor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveElectricityAsync(apartment, new(Month.AddDays(1), 50), actor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveElectricityAsync(apartment, new(Month.AddMonths(1), 50), actor, default));
        foreach (var rate in new[] { 0m, -1m, 0.1m, 1000001m })
            await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveRateAsync(new(10000, Month, 50000, rate), actor, default));
        Assert.Empty(await b.ElectricityUsages.ToArrayAsync()); Assert.Empty(await b.FeeRateRules.ToArrayAsync());
    }
    [Theory]
    [InlineData("OWNER")]
    [InlineData("RENTAL")]
    public async Task Service_bundles_parking_and_water_uses_day_one_people_without_changing_rental_or_history(string capacity)
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        var directory = new Directory(new(apartment, "A-101", 70, user, "Head", "head@example.invalid", Guid.NewGuid(), capacity, Month));
        var clock = new Clock(new DateTimeOffset(Month.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(7)).AddDays(14));
        var cycle = new BillingCycle(b, directory, new ConfirmedPaymentSource(p), new PaymentNoticeSource(p),
            new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)), clock);
        await cycle.SaveRateAsync(new(10000, Month, 50000), actor, default);
        Assert.Equal(50000, Assert.Single(await cycle.RatesAsync(default)).WaterUnitRate);
        await new RentalPricing(b, directory).SaveAsync(new(apartment, "MONTHLY", 5000000, Month), actor, default);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        var service = await b.Invoices.Include(x => x.Items).SingleAsync(x => x.Type == InvoiceType.SERVICE_FEE);
        Assert.Equal(Month, directory.CountedOn);
        Assert.Equal(850000, service.TotalAmount);
        Assert.Equal(2, service.Items.Count);
        var water = Assert.Single(service.Items.Where(x => x.FeeCodeSnapshot == BillingCycle.WaterFeeCode));
        Assert.Equal(3, water.Quantity); Assert.Equal("người", water.UnitName); Assert.Equal(150000, water.LineAmount);
        Assert.DoesNotContain(service.Items, x => x.FeeCodeSnapshot.Contains("PARK") || x.FeeCodeSnapshot.Contains("ELECTRIC"));
        Assert.Equal(capacity == "RENTAL" ? 1 : 0, await b.Invoices.CountAsync(x => x.Type == InvoiceType.MONTHLY_RENTAL));
        if (capacity == "RENTAL") Assert.Equal(5000000, (await b.Invoices.SingleAsync(x => x.Type == InvoiceType.MONTHLY_RENTAL)).TotalAmount);
        directory.ResidentCount = 4;
        await cycle.RunAsync(default);
        Assert.Equal(850000, (await b.Invoices.SingleAsync(x => x.Id == service.Id)).TotalAmount);
        await cycle.SaveRateAsync(new(12000, Month.AddMonths(1), 60000), actor, default);
        clock.Now = clock.Now.AddMonths(1);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        Assert.Equal(1080000, (await b.Invoices.SingleAsync(x => x.Type == InvoiceType.SERVICE_FEE && x.BillingPeriodStart == Month.AddMonths(1))).TotalAmount);
        Assert.Equal(850000, (await b.Invoices.SingleAsync(x => x.Id == service.Id)).TotalAmount);
        var html = System.Net.WebUtility.HtmlDecode(Encoding.UTF8.GetString((await cycle.InvoiceDocumentAsync(service.Id, user, default))!.Content));
        Assert.Contains("Nước sinh hoạt", html); Assert.Contains("Chốt 3 người", html);
    }

    [Fact]
    public async Task Water_configuration_rejects_invalid_money_atomically_and_empty_apartment_has_no_water_line()
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        var directory = new Directory(new(apartment, "A-101", 70, user, "Owner", "head@example.invalid")) { ResidentCount = 0 };
        var cycle = new BillingCycle(b, directory, new ConfirmedPaymentSource(p), new PaymentNoticeSource(p), new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)));
        await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveRateAsync(new(10000, Month, -1), actor, default));
        await Assert.ThrowsAsync<ArgumentException>(() => cycle.SaveRateAsync(new(10000, Month, 10.5m), actor, default));
        Assert.Empty(await b.FeeRateRules.ToArrayAsync());
        await cycle.SaveRateAsync(new(10000, Month, 50000), actor, default);
        await cycle.RunAsync(default);
        var invoice = await b.Invoices.Include(x => x.Items).SingleAsync();
        Assert.Equal(700000, invoice.TotalAmount); Assert.Single(invoice.Items);
    }
    [Theory]
    [InlineData("MONTHLY", "RENTAL", true)]
    [InlineData("MONTHLY", "RENTAL", false)]
    [InlineData("MONTHLY", "OWNER", true)]
    public async Task Rental_generation_uses_capacity_proration_snapshots_and_existing_email_outbox(string basis, string capacity, bool hasAccount)
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        var household = new BillingHousehold(apartment,"A-101",70,hasAccount ? user : Guid.Empty,"Recipient","recipient@example.invalid",Guid.NewGuid(),capacity,Month.AddDays(9),Month.AddDays(13));
        var directory = new Directory(household);
        var cycle = new BillingCycle(b,directory,new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)));
        var pricing = new RentalPricing(b,directory);
        var amount = basis == "MONTHLY" ? 8000000m : 350000m;
        await pricing.SaveAsync(new(apartment,basis,amount,Month),actor,default);
        await cycle.SaveRateAsync(new(10000,Month),actor,default);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        var invoices = await b.Invoices.Include(x=>x.Items).ToArrayAsync();
        var service = Assert.Single(invoices.Where(x=>x.Type == PropFlow.Modules.Billing.Domain.Invoices.InvoiceType.SERVICE_FEE));
        Assert.Equal(700000,service.TotalAmount);
        Assert.Single(service.Items);
        var expected = capacity == "OWNER" ? 0 : decimal.Round(amount * 5 / DateTime.DaysInMonth(Month.Year,Month.Month),0,MidpointRounding.AwayFromZero);
        Assert.Equal(capacity == "OWNER" ? 1 : 2,invoices.Length);
        if(capacity == "RENTAL") {
            var rental = Assert.Single(invoices.Where(x=>x.Type == PropFlow.Modules.Billing.Domain.Invoices.InvoiceType.MONTHLY_RENTAL));
            Assert.Equal(expected,rental.TotalAmount);
            var line = Assert.Single(rental.Items); Assert.Equal(5,line.Quantity); Assert.Equal(amount,line.UnitRate);
            Assert.Equal(expected,line.LineAmount);
            var doc=await cycle.InvoiceDocumentAsync(rental.Id,null,default);
            Assert.Contains("Tiền thuê",System.Net.WebUtility.HtmlDecode(Encoding.UTF8.GetString(doc!.Content)));
        }
        var staff = await cycle.InvoicesAsync(null,default);
        Assert.Equal(700000,staff.Sum(x=>x.ServiceTotal)); Assert.Equal(expected,staff.Sum(x=>x.RentalTotal));
        if(!hasAccount) { Assert.Empty(await c.Notifications.ToArrayAsync()); Assert.All(await c.FinanceEmails.ToArrayAsync(),x=>Assert.Null(x.UserId)); Assert.Empty(await cycle.InvoicesAsync(user,default)); }
        Assert.Null(await cycle.InvoiceDocumentAsync(service.Id,Guid.NewGuid(),default));
        if(capacity == "RENTAL") await Assert.ThrowsAsync<ArgumentException>(()=>pricing.SaveAsync(new(apartment,basis,amount+1,Month),actor,default));
        await pricing.SaveAsync(new(apartment,basis,amount+1,Month.AddMonths(1)),actor,default);
        await pricing.SaveAsync(new(apartment,basis,amount+2,Month.AddMonths(1)),actor,default);
        await using var verify = Billing(); Assert.Equal(700000+expected,await verify.Invoices.SumAsync(x=>x.TotalAmount));
        Assert.Single(await cycle.RatesAsync(default));
    }
    [Fact]
    public async Task Rental_configuration_rejects_invalid_scope_precision_and_mixed_bases_in_one_month()
    {
        await using var b=Billing(); var directory=new Directory(new(apartment,"A-101",70,user,"Head",null)); var pricing=new RentalPricing(b,directory);
        await Assert.ThrowsAsync<ArgumentException>(()=>pricing.SaveAsync(new(apartment,"WEEKLY",1,Month),actor,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>pricing.SaveAsync(new(apartment,"DAILY",-1,Month),actor,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>pricing.SaveAsync(new(apartment,"DAILY",1.5m,Month),actor,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>pricing.SaveAsync(new(Guid.NewGuid(),"DAILY",1,Month),actor,default));
        await pricing.SaveAsync(new(apartment,"MONTHLY",0,Month),actor,default);
        await Assert.ThrowsAsync<ArgumentException>(()=>pricing.SaveAsync(new(apartment,"DAILY",1,Month.AddDays(10)),actor,default));
        Assert.Single(await pricing.ListAsync(apartment,default));
    }
    [Fact]
    public async Task Staff_can_print_legacy_receipt_without_portal_snapshot_but_resident_cannot()
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        var cycle = Cycle(b, p, c);
        await cycle.SaveRateAsync(new(10000, Month), actor, default);
        await cycle.RunAsync(default);
        var invoice = Assert.Single(await cycle.InvoicesAsync(user, default));
        var store = new PaymentFinanceStore(p, new InvoiceFinancialSource(b));
        var collected = await store.CollectAsync(new(invoice.Id, 700000, "CASH", "legacy", "Đã nhận"), "legacy", actor, default);
        await b.InvoiceRecipients.ExecuteDeleteAsync();
        Assert.NotNull(await cycle.ReceiptAsync(collected.Payment!.Id, null, default));
        Assert.NotNull(await cycle.InvoiceDocumentAsync(invoice.Id, null, default));
        Assert.Single(await cycle.InvoicesAsync(null, default));
        Assert.Null(await cycle.ReceiptAsync(collected.Payment.Id, user, default));
        Assert.Empty(await cycle.InvoicesAsync(user, default));
    }
    [Fact]
    public async Task Monthly_invoice_is_unique_scoped_and_collection_produces_receipt_and_durable_mail()
    {
        await using var b = Billing(); await using var p = Payments(); await using var c = Communication();
        var cycle = Cycle(b, p, c);
        await cycle.SaveRateAsync(new(10000, Month), actor, default);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        var invoice = Assert.Single(await cycle.InvoicesAsync(user, default));
        Assert.Equal(700000, invoice.Total); Assert.Equal(Month.AddDays(9), invoice.DueDate);
        Assert.Empty(await cycle.InvoicesAsync(Guid.NewGuid(), default));
        Assert.Null(await cycle.InvoiceDocumentAsync(invoice.Id, Guid.NewGuid(), default));
        var doc = await cycle.InvoiceDocumentAsync(invoice.Id, user, default);
        Assert.Contains("Chủ hộ &lt;test&gt;", Encoding.UTF8.GetString(doc!.Content));
        var store = new PaymentFinanceStore(p, new InvoiceFinancialSource(b));
        var request = new CollectPaymentRequest(invoice.Id, 700000, "CASH", "receipt-test", "Đã nhận đủ");
        var result = await store.CollectAsync(request, "collect-1", actor, default);
        Assert.Equal(MutationOutcome.Success, result.Outcome);
        Assert.Equal(result.Payment!.Id, (await store.CollectAsync(request, "collect-1", actor, default)).Payment!.Id);
        Assert.Equal(MutationOutcome.IdempotencyConflict, (await store.CollectAsync(request with { Amount = 1 }, "collect-1", actor, default)).Outcome);
        Assert.Equal(MutationOutcome.Overpayment, (await store.CollectAsync(request, "collect-2", actor, default)).Outcome);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        Assert.Equal(0, Assert.Single(await cycle.InvoicesAsync(user, default)).Outstanding);
        Assert.Single(await p.Payments.ToArrayAsync());
        var receipt = await cycle.ReceiptAsync(result.Payment.Id, user, default);
        Assert.Contains("PHIẾU XÁC NHẬN THANH TOÁN", System.Net.WebUtility.HtmlDecode(Encoding.UTF8.GetString(receipt!.Content)));
        Assert.Null(await cycle.ReceiptAsync(result.Payment.Id, Guid.NewGuid(), default));
        Assert.Single(await c.Notifications.Where(x => x.SourceType == "PAYMENT").ToArrayAsync());
        Assert.Equal(await c.Notifications.CountAsync(), await c.FinanceEmails.CountAsync());
        Assert.All(await c.FinanceEmails.ToArrayAsync(), x => Assert.Null(x.SentAt));
    }
    [Fact]
    public async Task Concurrent_confirmations_of_distinct_payments_cannot_overpay_one_invoice()
    {
        Guid invoiceId, firstId, secondId;
        await using (var b = Billing()) await using (var p = Payments()) await using (var c = Communication()) {
            var cycle = Cycle(b,p,c); await cycle.SaveRateAsync(new(10000, Month), actor, default); await cycle.RunAsync(default);
            invoiceId = (await b.Invoices.SingleAsync()).Id;
            var first = new Payment("TEST-P1",invoiceId,400000,"BANK_TRANSFER",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
            var second = new Payment("TEST-P2",invoiceId,400000,"BANK_TRANSFER",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
            p.AddRange(first,second); await p.SaveChangesAsync(); firstId=first.Id; secondId=second.Id;
        }
        async Task<MutationResult> Confirm(Guid id) { await using var b=Billing(); await using var p=Payments();
            return await new PaymentFinanceStore(p,new InvoiceFinancialSource(b)).ChangeAsync(id,"CONFIRM",null,id.ToString(),actor,default); }
        var results = await Task.WhenAll(Confirm(firstId),Confirm(secondId));
        Assert.Single(results.Where(x=>x.Outcome==MutationOutcome.Success));
        Assert.Single(results.Where(x=>x.Outcome==MutationOutcome.Overpayment));
    }
    [Fact]
    public async Task Overdue_and_manual_reminders_are_deduplicated_and_stop_after_payment()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var cycle=Cycle(b,p,c);
        var oldMonth=Month.AddMonths(-1); var i=new Invoice("HD-OLD",apartment,oldMonth,oldMonth.AddMonths(1).AddDays(-1),actor,DateTimeOffset.UtcNow);
        i.AddItem("SERVICE","Phí dịch vụ","AREA",70,10000,actor,DateTimeOffset.UtcNow,unitName:"m²");
        i.Issue(actor,oldMonth,DateTimeOffset.UtcNow,oldMonth.AddDays(9));
        b.Invoices.Add(i); b.InvoiceRecipients.Add(new(i.Id,user,"Chủ hộ","head@example.invalid","A-101",70)); await b.SaveChangesAsync();
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        Assert.Single(await c.Notifications.Where(x=>x.Title=="Thông báo công nợ quá hạn").ToArrayAsync());
        await cycle.RemindAsync([i.Id],default); await cycle.RemindAsync([i.Id],default);
        Assert.Equal(2,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
        await new PaymentFinanceStore(p,new InvoiceFinancialSource(b)).CollectAsync(new(i.Id,700000,"CASH",null,null),"paid-old",actor,default);
        Assert.Equal(0,await cycle.RemindAsync([i.Id],default));
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public async Task Next_month_uses_new_rate_without_changing_issued_invoice_and_reminds_once_per_day_until_paid()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var clock=new Clock(new DateTimeOffset(Month.ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(7)).ToUniversalTime());
        var cycle=new BillingCycle(b,new Directory(new(apartment,"A-101",70,user,"Chủ hộ","head@example.invalid")),
            new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)),clock);
        await cycle.SaveRateAsync(new(10000,Month),actor,default);
        await cycle.SaveRateAsync(new(12000,Month.AddMonths(1)),actor,default);
        await cycle.RunAsync(default);
        Assert.Equal(700000,Assert.Single(await cycle.InvoicesAsync(user,default)).Total);
        clock.Now=clock.Now.AddDays(10); await cycle.RunAsync(default);
        Assert.Equal(1,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
        await cycle.RunAsync(default);
        Assert.Equal(1,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
        Assert.Equal(1,await c.Notifications.CountAsync(x=>x.Title=="Hóa đơn phí dịch vụ mới"));
        clock.Now=clock.Now.AddDays(1); await cycle.RunAsync(default);
        Assert.Equal(2,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
        await cycle.RunAsync(default);
        Assert.Equal(2,await c.FinanceEmails.CountAsync(x=>x.Subject=="Thông báo công nợ quá hạn"));
        clock.Now=clock.Now.AddDays(1); await cycle.RunAsync(default);
        Assert.Equal(3,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
        var oldInvoice = await b.Invoices.SingleAsync();
        var payment = new Payment("DAILY-REMINDER-PAID",oldInvoice.Id,700000,"CASH",clock.Now,clock.Now);
        payment.Confirm(actor,700000,clock.Now);p.Payments.Add(payment);await p.SaveChangesAsync();
        clock.Now=clock.Now.AddDays(1); await cycle.RunAsync(default);
        Assert.Equal(3,await c.Notifications.CountAsync(x=>x.Title=="Thông báo công nợ quá hạn"));
        clock.Now=new DateTimeOffset(Month.AddMonths(1).ToDateTime(TimeOnly.MinValue),TimeSpan.FromHours(7)).ToUniversalTime();
        await cycle.RunAsync(default);
        var invoices=await cycle.InvoicesAsync(user,default);
        Assert.Equal(840000,invoices[0].Total); Assert.Equal(700000,invoices[1].Total);
    }
    [Fact]
    public async Task Rejection_sends_reason_to_the_exact_household_and_does_not_reduce_debt()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var cycle=Cycle(b,p,c); await cycle.SaveRateAsync(new(10000,Month),actor,default); await cycle.RunAsync(default);
        var invoice=await b.Invoices.SingleAsync();
        var payment=new Payment("TEST-REJECT",invoice.Id,700000,"BANK_TRANSFER",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
        p.Payments.Add(payment); await p.SaveChangesAsync();
        await cycle.RunAsync(default);
        Assert.Single(await c.Notifications.Where(x=>x.Title=="Thanh toán đang chờ xác minh").ToArrayAsync());
        var store=new PaymentFinanceStore(p,new InvoiceFinancialSource(b));
        Assert.Equal(MutationOutcome.Success,(await store.ChangeAsync(payment.Id,"REJECT","Sai tham chiếu", "reject-key",actor,default)).Outcome);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        var notice=Assert.Single(await c.Notifications.Where(x=>x.Title=="Khoản thanh toán chưa được chấp nhận").ToArrayAsync());
        Assert.Equal(user,notice.RecipientUserId); Assert.Contains("Sai tham chiếu",notice.Message);
        Assert.Equal(700000,Assert.Single(await cycle.InvoicesAsync(user,default)).Outstanding);
        Assert.Null(await cycle.ReceiptAsync(payment.Id,user,default));
    }
    private sealed class Factory(string connection, Guid user, Guid apartment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_,config)=> {
                using var rsa=RSA.Create(2048);
                config.AddInMemoryCollection(new Dictionary<string,string?> {
                    ["ConnectionStrings:PropFlowDatabase"]=connection,
                    ["Authentication:Issuer"]="cycle-tests",["Authentication:Audience"]="cycle-tests",
                    ["Authentication:PrivateKeyPem"]=rsa.ExportRSAPrivateKeyPem(),
                    ["Authentication:OtpHashKey"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["Authentication:Mail:Host"]="invalid.test",["Authentication:Mail:From"]="test@example.invalid",
                    ["Cloudinary:CloudName"]="fixture",["Cloudinary:ApiKey"]="fixture",["Cloudinary:ApiSecret"]="fixture",
                    ["Logging:LogLevel:Default"]="None",
                    ["Logging:LogLevel:PropFlow.Modules.Authentication.Presentation.AuthExceptionHandler"]="Error"
                });
            });
            builder.ConfigureTestServices(services=> {
                // HTTP tests exercise real endpoints/stores, but never run timers or send mail.
                foreach (var service in services.Where(x => x.ServiceType == typeof(IHostedService)
                    && x.ImplementationType is not null && typeof(BackgroundService).IsAssignableFrom(x.ImplementationType)).ToArray())
                    services.Remove(service);
                services.RemoveAll<IBillingHouseholdDirectory>();
                services.RemoveAll<PropFlow.Modules.PropertyAssets.Contracts.ICurrentBuildingTimeZone>();
                services.AddSingleton<PropFlow.Modules.PropertyAssets.Contracts.ICurrentBuildingTimeZone, BuildingZone>();
                services.AddSingleton<IBillingHouseholdDirectory>(new Directory(new(apartment,"A-101",70,user,"Chủ hộ","head@example.invalid")));
                services.AddAuthentication(o=>o.DefaultAuthenticateScheme=o.DefaultChallengeScheme=o.DefaultForbidScheme="CycleTest")
                    .AddScheme<AuthenticationSchemeOptions,TestAuth>("CycleTest",_=>{});
            });
        }
        public HttpClient Client(string role, Guid id, string? permission=null) {
            var client=CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new Uri("https://localhost")});
            client.DefaultRequestHeaders.Add("X-Role",role); client.DefaultRequestHeaders.Add("X-User",id.ToString());
            if(permission is not null)client.DefaultRequestHeaders.Add("X-Permission",permission);
            return client;
        }
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() {
            if(!Request.Headers.TryGetValue("X-Role",out var role))return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new List<Claim>{new("sub",Request.Headers["X-User"].ToString()),new(ClaimTypes.Role,role.ToString())};
            if(Request.Headers.TryGetValue("X-Permission",out var permission))claims.Add(new("permission",permission.ToString()));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }
    private sealed class BuildingZone : PropFlow.Modules.PropertyAssets.Contracts.ICurrentBuildingTimeZone
    {
        public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh");
    }
    [Fact]
    public async Task Http_verification_approval_rejection_and_retry_preserve_balances_and_exact_notifications()
    {
        Guid invoiceId, approvedId, rejectedId;
        await using (var b = Billing()) await using (var p = Payments()) await using (var c = Communication())
        {
            var cycle = Cycle(b, p, c);
            await cycle.SaveRateAsync(new(10000, Month), actor, default);
            await cycle.RunAsync(default);
            invoiceId = (await b.Invoices.SingleAsync()).Id;
            var approved = new Payment("HTTP-APPROVE", invoiceId, 200000, "BANK_TRANSFER", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var rejected = new Payment("HTTP-REJECT", invoiceId, 100000, "BANK_TRANSFER", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            p.Payments.AddRange(approved, rejected); await p.SaveChangesAsync();
            approvedId = approved.Id; rejectedId = rejected.Id;
            await cycle.RunAsync(default);
        }
        await using var factory = new Factory(connection, user, apartment);
        using var accountant = factory.Client("ACCOUNTANT", actor, "MANAGE_FINANCE");
        using var resident = factory.Client("RESIDENT", user);
        using var stranger = factory.Client("RESIDENT", Guid.NewGuid());
        var pending = await accountant.GetFromJsonAsync<PagedPayments>("api/v1/payments/pending");
        Assert.Equal(2, pending!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync($"api/v1/resident/billing/payments/{approvedId}/receipt")).StatusCode);
        accountant.DefaultRequestHeaders.Add("Idempotency-Key", "http-approve");
        var approval = await accountant.PostAsync($"api/v1/payments/{approvedId}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal("CONFIRMED", (await approval.Content.ReadFromJsonAsync<PaymentRow>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsync($"api/v1/payments/{approvedId}/confirm", null)).StatusCode);
        accountant.DefaultRequestHeaders.Remove("Idempotency-Key");
        accountant.DefaultRequestHeaders.Add("Idempotency-Key", "http-reject");
        var rejection = await accountant.PostAsJsonAsync($"api/v1/payments/{rejectedId}/reject", new { reason = "Sai mã giao dịch" });
        Assert.Equal(HttpStatusCode.OK, rejection.StatusCode);
        Assert.Equal("REJECTED", (await rejection.Content.ReadFromJsonAsync<PaymentRow>())!.Status);
        Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync($"api/v1/payments/{rejectedId}/reject", new { reason = "Sai mã giao dịch" })).StatusCode);
        Assert.Equal(0, (await accountant.GetFromJsonAsync<PagedPayments>("api/v1/payments/pending"))!.Total);
        Assert.Equal(1, (await accountant.GetFromJsonAsync<PagedPayments>("api/v1/payments/confirmed"))!.Total);
        var balance = await accountant.GetFromJsonAsync<InvoiceBalance>($"api/v1/billing/invoices/{invoiceId}");
        Assert.Equal(200000, balance!.ConfirmedPaidAmount); Assert.Equal(500000, balance.OutstandingAmount);
        Assert.Equal(HttpStatusCode.OK, (await resident.GetAsync($"api/v1/resident/billing/payments/{approvedId}/receipt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"api/v1/resident/billing/payments/{approvedId}/receipt")).StatusCode);
        var notices = await resident.GetFromJsonAsync<System.Text.Json.JsonElement[]>("api/v1/resident/notifications");
        Assert.Single(notices!.Where(x => x.GetProperty("title").GetString() == "Thanh toán hóa đơn thành công"));
        var rejectedNotice = Assert.Single(notices!.Where(x => x.GetProperty("title").GetString() == "Khoản thanh toán chưa được chấp nhận"));
        Assert.Contains("Sai mã giao dịch", rejectedNotice.GetProperty("message").GetString());
        Assert.Empty((await stranger.GetFromJsonAsync<System.Text.Json.JsonElement[]>("api/v1/resident/notifications"))!);
        await using var db = Payments();
        Assert.Equal(2, await db.Payments.CountAsync());
        Assert.Equal(2, await db.PaymentOperations.CountAsync());
    }
    [Fact]
    public async Task Http_permissions_and_resident_scope_protect_rates_collections_and_documents()
    {
        Guid invoiceId;
        await using(var b=Billing())await using(var p=Payments())await using(var c=Communication()) {
            var cycle=Cycle(b,p,c);await cycle.SaveRateAsync(new(10000,Month),actor,default);await cycle.RunAsync(default);
            invoiceId=(await b.Invoices.SingleAsync()).Id;
        }
        await using var factory=new Factory(connection,user,apartment);
        using var accountant=factory.Client("ACCOUNTANT",actor,"MANAGE_FINANCE");
        using var manager=factory.Client("MANAGER",actor,"MANAGE_OPERATIONS");
        using var resident=factory.Client("RESIDENT",user);
        using var stranger=factory.Client("RESIDENT",Guid.NewGuid());
        var sendPath=$"api/v1/billing/cycle/invoices/{invoiceId}/send";
        await using var reminderNotices = Communication();
        var previousEmailIds = await reminderNotices.FinanceEmails.Select(x => x.Id).ToArrayAsync();
        const string reminderPath = "api/v1/billing/cycle/reminders";
        var reminder = new { invoiceIds = new[] { invoiceId }, requestId = Guid.NewGuid() };
        using (var denied = await resident.PostAsJsonAsync(reminderPath, reminder))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var accepted = await accountant.PostAsJsonAsync(reminderPath, reminder);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            var reminderResult = await accepted.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(1, reminderResult.GetProperty("recipients").GetInt32());
            Assert.Single(reminderResult.EnumerateObject());
        }
        var queued = Assert.Single(await reminderNotices.FinanceEmails.Where(x => !previousEmailIds.Contains(x.Id)).ToArrayAsync());
        Assert.Equal(user, queued.UserId);
        Assert.Equal(HttpStatusCode.Forbidden,(await resident.PostAsJsonAsync(sendPath,new{})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await manager.PostAsJsonAsync(sendPath,new{})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await accountant.PostAsJsonAsync(sendPath,new{})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await accountant.PostAsJsonAsync(sendPath,new{})).StatusCode);
        await using (var sendNotices = Communication())
        {
            var before = await sendNotices.FinanceEmails.CountAsync();
            var requestId = Guid.NewGuid().ToString();
            accountant.DefaultRequestHeaders.Add("Idempotency-Key", requestId);
            Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync(sendPath, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync(sendPath, new { })).StatusCode);
            Assert.Equal(before + 1, await sendNotices.FinanceEmails.CountAsync());
            accountant.DefaultRequestHeaders.Remove("Idempotency-Key");
            accountant.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync(sendPath, new { })).StatusCode);
            Assert.Equal(before + 2, await sendNotices.FinanceEmails.CountAsync());
            accountant.DefaultRequestHeaders.Remove("Idempotency-Key");
            accountant.DefaultRequestHeaders.Add("Idempotency-Key", "invalid");
            Assert.Equal(HttpStatusCode.BadRequest, (await accountant.PostAsJsonAsync(sendPath, new { })).StatusCode);
            Assert.Equal(before + 2, await sendNotices.FinanceEmails.CountAsync());
            accountant.DefaultRequestHeaders.Remove("Idempotency-Key");
        }
        Assert.Equal(HttpStatusCode.NotFound,(await accountant.PostAsJsonAsync($"api/v1/billing/cycle/invoices/{Guid.NewGuid()}/send",new{})).StatusCode);
        var electricPath = $"api/v1/billing/cycle/apartments/{apartment}/electricity";
        var usageInput = new ElectricityUsageInput(Month, 321);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.PutAsJsonAsync(electricPath, usageInput)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await resident.PutAsJsonAsync(electricPath, usageInput)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(electricPath, usageInput)).StatusCode);
        var usage = (await manager.GetFromJsonAsync<ElectricityUsageView>($"{electricPath}?usageMonth={Month:yyyy-MM-dd}"))!;
        Assert.Equal(321, usage.Kwh); Assert.False(usage.IsLocked);
        var utilitiesPath=$"api/v1/billing/cycle/apartments/{apartment}/utilities";
        var utilitiesInput=new UtilityUsageInput(Month,321,5);
        Assert.Equal(HttpStatusCode.Forbidden,(await accountant.PutAsJsonAsync(utilitiesPath,utilitiesInput)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await resident.PutAsJsonAsync(utilitiesPath,utilitiesInput)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await manager.PutAsJsonAsync(utilitiesPath,utilitiesInput)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await manager.PutAsJsonAsync(utilitiesPath,utilitiesInput with { CurrentWaterReading=-1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(utilitiesPath, new { usageMonth = Month, kwh = 999 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(utilitiesPath, new { usageMonth = Month, currentElectricityReading = "không hợp lệ" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.PutAsJsonAsync(utilitiesPath, new
        {
            usageMonth = Month, currentElectricityReading = 321, currentWaterReading = 5, kwh = 999, waterM3 = 999
        })).StatusCode);
        var utilityReading=(await manager.GetFromJsonAsync<ElectricityUsageView>($"{electricPath}?usageMonth={Month:yyyy-MM-dd}"))!;
        Assert.Equal(321,utilityReading.Kwh);Assert.Equal(5,utilityReading.WaterM3);
        Assert.Equal(HttpStatusCode.Conflict,(await manager.PutAsJsonAsync(utilitiesPath,utilitiesInput with { UsageMonth=Month.AddMonths(-1) })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync(electricPath, new ElectricityUsageInput(Month.AddMonths(-1), 50))).StatusCode);
        var detailPath = $"api/v1/billing/cycle/invoices/{invoiceId}";
        Assert.Equal(HttpStatusCode.Forbidden, (await resident.GetAsync(detailPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(detailPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await accountant.GetAsync($"api/v1/billing/cycle/invoices/{Guid.NewGuid()}")).StatusCode);
        var invoiceDetail = (await accountant.GetFromJsonAsync<InvoiceDetail>(detailPath))!;
        Assert.Equal("A-101", invoiceDetail.ApartmentNumber);
        Assert.Equal("Chủ hộ <test>", invoiceDetail.ResidentName);
        Assert.Equal(700000, invoiceDetail.Lines.Sum(x => x.Amount));
        Assert.Equal(700000, invoiceDetail.OutstandingAmount);
        Assert.Equal(0, invoiceDetail.ConfirmedPaidAmount);
        Assert.Empty((await accountant.GetFromJsonAsync<PaymentRow[]>($"api/v1/payments/by-invoice/{invoiceId}"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await resident.GetAsync($"api/v1/payments/by-invoice/{invoiceId}")).StatusCode);
        Assert.Contains(invoiceDetail.History, x => x.ToStatus == "ISSUED");
        Assert.Equal(HttpStatusCode.Forbidden,(await accountant.PostAsJsonAsync("api/v1/billing/cycle/rates",new ServiceRateInput(15000,Month.AddMonths(1)))).StatusCode);
        var rentInput = new RentalPricingInput(apartment,"DAILY",350000,Month.AddMonths(1));
        Assert.Equal(HttpStatusCode.Forbidden,(await accountant.PostAsJsonAsync("api/v1/billing/rental-pricing",rentInput)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await resident.PostAsJsonAsync("api/v1/billing/rental-pricing",rentInput)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await manager.PostAsJsonAsync("api/v1/billing/rental-pricing",rentInput)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await manager.PostAsJsonAsync("api/v1/billing/rental-pricing",rentInput with { ApartmentUnitId=Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await manager.PostAsJsonAsync("api/v1/billing/cycle/rates",new ServiceRateInput(15000,Month.AddMonths(1),50000))).StatusCode);
        var savedRates = await manager.GetFromJsonAsync<ServiceRateView[]>("api/v1/billing/cycle/rates");
        Assert.Equal(50000, Assert.Single(savedRates!.Where(x => x.EffectiveFrom == Month.AddMonths(1))).WaterUnitRate);
        Assert.Equal(HttpStatusCode.OK,(await manager.PostAsJsonAsync("api/v1/billing/cycle/rates",new ServiceRateInput(10000,Month.AddMonths(2),ElectricityUnitRate:2000,WaterM3UnitRate:12000))).StatusCode);
        var volumeRates=(await manager.GetFromJsonAsync<ServiceRateView[]>("api/v1/billing/cycle/rates"))!;
        Assert.Equal(12000,Assert.Single(volumeRates.Where(x=>x.EffectiveFrom==Month.AddMonths(2))).WaterM3UnitRate);

        Assert.Equal(HttpStatusCode.Forbidden,(await manager.PostAsJsonAsync("api/v1/payments/collect",new CollectPaymentRequest(invoiceId,700000,"CASH",null,null))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await resident.GetAsync($"api/v1/resident/billing/invoices/{invoiceId}/print")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"api/v1/resident/billing/invoices/{invoiceId}/print")).StatusCode);
        Assert.Empty((await stranger.GetFromJsonAsync<ResidentInvoice[]>("api/v1/resident/billing/invoices"))!);
        accountant.DefaultRequestHeaders.Add("Idempotency-Key","http-collect");
        var result=await accountant.PostAsJsonAsync("api/v1/payments/collect",new CollectPaymentRequest(invoiceId,700000,"BANK_TRANSFER","http-ref",null));
        Assert.Equal(HttpStatusCode.OK,result.StatusCode);
        var payment=(await result.Content.ReadFromJsonAsync<PaymentRow>())!;
        var paidDetail = (await accountant.GetFromJsonAsync<InvoiceDetail>(detailPath))!;
        Assert.Equal(700000, paidDetail.ConfirmedPaidAmount);
        Assert.Equal(0, paidDetail.OutstandingAmount);
        Assert.Equal("PAID", paidDetail.SettlementStatus);
        var invoicePayments = (await accountant.GetFromJsonAsync<PaymentRow[]>($"api/v1/payments/by-invoice/{invoiceId}"))!;
        Assert.Equal(payment.Id, Assert.Single(invoicePayments).Id);
        Assert.Equal(HttpStatusCode.OK,(await accountant.GetAsync($"api/v1/billing/cycle/payments/{payment.Id}/receipt")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await resident.GetAsync($"api/v1/resident/billing/payments/{payment.Id}/receipt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.GetAsync($"api/v1/resident/billing/payments/{payment.Id}/receipt")).StatusCode);
        var notices=await resident.GetFromJsonAsync<System.Text.Json.JsonElement[]>("api/v1/resident/notifications");
        Assert.Single(notices!.Where(x=>x.GetProperty("title").GetString()=="Thanh toán hóa đơn thành công"));
        var receiptNotice = Assert.Single(notices!.Where(x => x.GetProperty("title").GetString() == "Thanh toán hóa đơn thành công"));
        Assert.Contains("700.000", receiptNotice.GetProperty("message").GetString());
        Assert.Contains(paidDetail.InvoiceNumber, receiptNotice.GetProperty("message").GetString());
        await using (var receiptDb = Communication())
        {
            var receiptMail = await receiptDb.FinanceEmails.SingleAsync(x => x.Subject == "Thanh toán hóa đơn thành công");
            Assert.Equal(user, receiptMail.UserId);
            Assert.Contains("700.000", receiptMail.Html);
            Assert.Contains(paidDetail.InvoiceNumber, receiptMail.Html);
            Assert.Contains(payment.PaymentNumber, receiptMail.Html);
            Assert.EndsWith(".html", receiptMail.AttachmentFileName);
        }
        Assert.Empty((await stranger.GetFromJsonAsync<System.Text.Json.JsonElement[]>("api/v1/resident/notifications"))!);
        var id=Assert.Single(notices!.Where(x=>x.GetProperty("title").GetString()=="Hóa đơn phí dịch vụ mới")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,(await stranger.PostAsJsonAsync($"api/v1/resident/notifications/{id}/read",new{})).StatusCode);
    }
    [Fact]
    public async Task Later_account_receives_its_own_historical_invoice_and_notice_without_reassignment()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var person=Guid.NewGuid();
        var directory=new Directory(new(apartment,"A-101",70,Guid.Empty,"Head","head@example.invalid",person,"OWNER"));
        var cycle=new BillingCycle(b,directory,new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)));
        await cycle.SaveRateAsync(new(10000,Month),actor,default); await cycle.RunAsync(default);
        var invoice=await b.Invoices.SingleAsync();
        Assert.Empty(await cycle.InvoicesAsync(user,default));
        var originalEmails=await c.FinanceEmails.CountAsync();
        directory.LinkedAccounts[person]=user;
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        Assert.Equal(invoice.Id,Assert.Single(await cycle.InvoicesAsync(user,default)).Id);
        Assert.Single(await c.Notifications.Where(x=>x.Title=="Hóa đơn phí dịch vụ mới" && x.RecipientUserId==user).ToArrayAsync());
        Assert.Equal(originalEmails,await c.FinanceEmails.CountAsync());
        Assert.Null(await cycle.InvoiceDocumentAsync(invoice.Id,Guid.NewGuid(),default));
        directory.LinkedAccounts[person]=Guid.NewGuid(); await cycle.RunAsync(default);
        Assert.Equal(user,(await b.InvoiceRecipients.SingleAsync()).UserId);
        Assert.Single(await b.Invoices.ToArrayAsync()); Assert.Equal(700000,invoice.TotalAmount);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Later_account_web_notice_reuses_email_with_normalized_address(bool originalFirst)
    {
        await using var c=Communication(); var publisher=new FinanceNoticePublisher(c);
        var oldEvent=Guid.NewGuid(); var source=Guid.NewGuid();
        var linkedEvent=Guid.NewGuid();
        Task Original() => publisher.PublishAsync(new(oldEvent,null,"head@example.invalid","Invoice","Issued","<p>Invoice</p>","INVOICE",source,"/resident/billing"),default);
        Task Linked() => publisher.PublishAsync(new(linkedEvent,user,"HEAD@EXAMPLE.INVALID","Invoice","Issued","<p>Invoice</p>","INVOICE",source,"/resident/billing",PreviousRecipientEventId:oldEvent),default);
        if(originalFirst) { await Original(); await Linked(); } else { await Linked(); await Original(); }
        await publisher.PublishAsync(new(linkedEvent,user,"HEAD@EXAMPLE.INVALID","Invoice","Issued","<p>Invoice</p>","INVOICE",source,"/resident/billing",PreviousRecipientEventId:oldEvent),default);
        Assert.Single(await c.FinanceEmails.ToArrayAsync());
        Assert.Equal(user,(await c.Notifications.SingleAsync()).RecipientUserId);
    }
    [Fact]
    public async Task Type_prices_share_monthly_rate_and_keep_daily_alternative_without_charging_owners()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var type=Guid.NewGuid();
        var directory=new TypeDirectory(new BillingHousehold(apartment,"A-101",75,user,"Head",null,Guid.NewGuid(),"RENTAL",Month,Month.AddMonths(1).AddDays(-1),type),
            new BillingHousehold(Guid.NewGuid(),"A-102",90,Guid.NewGuid(),"Owner",null,Guid.NewGuid(),"OWNER",ApartmentTypeId:type),
            new BillingHousehold(Guid.NewGuid(),"A-103",50,Guid.NewGuid(),"Other tenant",null,Guid.NewGuid(),"RENTAL",Month,Month.AddMonths(1).AddDays(-1),type));
        var pricing=new RentalPricing(b,directory);
        await pricing.SaveAsync(new(Guid.Empty,"MONTHLY",5000000,Month,type),actor,default);
        await pricing.SaveAsync(new(Guid.Empty,"DAILY",250000,Month,type),actor,default);
        var rates=await pricing.ListForTypeAsync(type,default); Assert.Equal(2,rates.Count); Assert.All(rates,x=>Assert.Null(x.EffectiveTo));
        var cycle=new BillingCycle(b,directory,new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)));
        await cycle.SaveRateAsync(new(10000,Month),actor,default); await cycle.RunAsync(default); await cycle.RunAsync(default);
        var invoices=await b.Invoices.Include(x=>x.Items).ToArrayAsync(); Assert.Equal(5,invoices.Length);
        Assert.Equal(2,invoices.Count(x=>x.Type==InvoiceType.MONTHLY_RENTAL));
        Assert.All(invoices.Where(x=>x.Type==InvoiceType.MONTHLY_RENTAL),x=>Assert.Equal(5000000,x.TotalAmount));
        Assert.Equal(750000,invoices.Single(x=>x.ApartmentUnitId==apartment && x.Type==InvoiceType.SERVICE_FEE).TotalAmount);
        Assert.Single(invoices.Where(x=>x.ApartmentUnitId==directory.Households[1].ApartmentId));
        await Assert.ThrowsAsync<ArgumentException>(()=>pricing.SaveAsync(new(Guid.Empty,"MONTHLY",6000000,Month,type),actor,default));
        Assert.Equal(10000000,await b.Invoices.Where(x=>x.Type==InvoiceType.MONTHLY_RENTAL).SumAsync(x=>x.TotalAmount));
    }
    private sealed class TypeDirectory(params BillingHousehold[] households) : IBillingHouseholdDirectory
    {
        public BillingHousehold[] Households => households;
        public Task<IReadOnlyList<BillingHousehold>> ListAsync(DateOnly date,CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingHousehold>>(households);
        public Task<IReadOnlyList<BillingApartment>> ManagedApartmentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingApartment>>(households.Select(x=>new BillingApartment(x.ApartmentId,x.UnitNumber,x.ApartmentTypeId,"Test type")).ToArray());
    }
    [Fact]
    public async Task Service_fee_uses_actual_area_and_rental_does_not_require_service_configuration()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var directory=new Directory(new(apartment,"A-101",75,user,"Head","head@example.invalid",Guid.NewGuid(),"RENTAL",Month,Month.AddDays(4)));
        var cycle=new BillingCycle(b,directory,new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c)));
        await new RentalPricing(b,directory).SaveAsync(new(apartment,"MONTHLY",8000000,Month),actor,default);
        await cycle.RunAsync(default);
        Assert.Equal(InvoiceType.MONTHLY_RENTAL,(await b.Invoices.SingleAsync()).Type);
        await cycle.SaveRateAsync(new(10000,Month),actor,default);
        await cycle.RunAsync(default); await cycle.RunAsync(default);
        Assert.Equal(2,await b.Invoices.CountAsync());
        Assert.Equal(750000,(await b.Invoices.SingleAsync(x=>x.Type==InvoiceType.SERVICE_FEE)).TotalAmount);
    }
    [Fact]
    public async Task Committed_collection_survives_notification_failure_and_reconciles_once()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var cycle=Cycle(b,p,c); await cycle.SaveRateAsync(new(10000,Month),actor,default); await cycle.RunAsync(default);
        var invoice=await b.Invoices.SingleAsync();
        var failingCycle=new BillingCycle(b,new Directory(new(apartment,"A-101",70,user,"Head","head@example.invalid")),new ConfirmedPaymentSource(p),new PaymentNoticeSource(p),new FailingPublisher());
        var decorated=new PropFlow.Api.Composition.PaymentFinanceNotifications(new PaymentFinanceStore(p,new InvoiceFinancialSource(b)),failingCycle,Microsoft.Extensions.Logging.Abstractions.NullLogger<PropFlow.Api.Composition.PaymentFinanceNotifications>.Instance);
        var request=new CollectPaymentRequest(invoice.Id,700000,"CASH",null,null);
        Assert.Equal(MutationOutcome.Success,(await decorated.CollectAsync(request,"collect-retry",actor,default)).Outcome);
        Assert.Equal(MutationOutcome.Success,(await decorated.CollectAsync(request,"collect-retry",actor,default)).Outcome);
        Assert.Single(await p.Payments.ToArrayAsync());
        await cycle.NotifyPaymentAsync(invoice.Id,default); await cycle.NotifyPaymentAsync(invoice.Id,default);
        Assert.Single(await c.Notifications.Where(x=>x.SourceType=="PAYMENT").ToArrayAsync());
        Assert.NotNull((await c.FinanceEmails.SingleAsync(x=>x.Subject=="Thanh toán hóa đơn thành công")).AttachmentFileName);
        Assert.Equal(0,Assert.Single(await cycle.InvoicesAsync(user,default)).Outstanding);
    }
    private sealed class FailingPublisher : IFinanceNoticePublisher
    {
        public Task PublishAsync(FinanceNotice notice,CancellationToken ct) => throw new InvalidOperationException("Test delivery failure");
    }
    [Fact]
    public async Task Database_failure_rolls_back_collection_without_payment_notification()
    {
        await using var b=Billing(); await using var p=Payments(); await using var c=Communication();
        var cycle=Cycle(b,p,c); await cycle.SaveRateAsync(new(10000,Month),actor,default); await cycle.RunAsync(default);
        var invoice=await b.Invoices.SingleAsync();
        await p.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION payments.fail_test_insert() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Test database failure'; END $$;
            CREATE TRIGGER fail_test_insert BEFORE INSERT ON payments.payments FOR EACH ROW EXECUTE FUNCTION payments.fail_test_insert();
            """);
        var store=new PropFlow.Api.Composition.PaymentFinanceNotifications(new PaymentFinanceStore(p,new InvoiceFinancialSource(b)),cycle,Microsoft.Extensions.Logging.Abstractions.NullLogger<PropFlow.Api.Composition.PaymentFinanceNotifications>.Instance);
        await Assert.ThrowsAsync<DbUpdateException>(()=>store.CollectAsync(new(invoice.Id,700000,"CASH",null,null),"db-failure",actor,default));
        await using var verify=Payments(); Assert.Empty(await verify.Payments.ToArrayAsync());
        Assert.Empty(await c.Notifications.Where(x=>x.SourceType=="PAYMENT").ToArrayAsync());
        Assert.Equal(700000,Assert.Single(await cycle.InvoicesAsync(user,default)).Outstanding);
    }
    [Fact]
    public async Task Email_failure_keeps_receipt_pending_with_retry_information()
    {
        await using(var c=Communication()) await new FinanceNoticePublisher(c).PublishAsync(new(Guid.NewGuid(),user,"head@resident.local","Receipt","Paid","<p>Receipt</p>","PAYMENT",Guid.NewGuid(),"/resident/billing","PT.html"),default);
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0); listener.Start();
        var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var services=new ServiceCollection(); services.AddScoped(_=>Communication());
        services.AddSingleton<PropFlow.Modules.Communication.Application.IFinanceEmailRecipientDirectory>(new QueuedRecipientDirectory("head@resident.local"));
        await using var provider=services.BuildServiceProvider();
        using var worker=new FinanceEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(),Options.Create(new FinanceMailOptions {Host="127.0.0.1",Port=port,From="finance@propflow.local",EnableSsl=false,PollSeconds=1}),Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceEmailWorker>.Instance);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await worker.StartAsync(timeout.Token);
        while(true) { await using var check=Communication(); if(await check.FinanceEmails.AnyAsync(x=>x.Attempts>0,timeout.Token)) break; await Task.Delay(100,timeout.Token); }
        await worker.StopAsync(default);
        await using var verify=Communication(); var mail=await verify.FinanceEmails.SingleAsync(); Assert.Null(mail.SentAt); Assert.Equal(1,mail.Attempts); Assert.True(mail.NextAttemptAt>DateTimeOffset.UtcNow);
    }
    [Fact]
    public async Task Durable_email_worker_sends_html_through_smtp_and_marks_delivery()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        async Task<string> Receive() {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = socket.GetStream(); using var reader = new StreamReader(stream);
            using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\r\n" };
            await writer.WriteLineAsync("220 localhost SMTP test"); var body = new StringBuilder(); var data = false;
            while (await reader.ReadLineAsync(timeout.Token) is { } line) {
                if (data) { if (line == ".") { data = false; await writer.WriteLineAsync("250 queued"); } else body.AppendLine(line); }
                else if (line.StartsWith("EHLO") || line.StartsWith("HELO")) await writer.WriteLineAsync("250 localhost");
                else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase)) {
                    body.AppendLine(line);
                    await writer.WriteLineAsync("250 OK");
                }
                else if (line == "DATA") { data = true; await writer.WriteLineAsync("354 send data"); }
                else if (line == "QUIT") { await writer.WriteLineAsync("221 goodbye"); return body.ToString(); }
                else await writer.WriteLineAsync("250 OK");
            }
            return body.ToString();
        }
        var received = Receive();
        var inactiveUser = Guid.NewGuid();
        try {
            await using (var c = Communication())
            {
                var publisher = new PropFlow.Api.Composition.FinanceNoticePublisherBridge(new FinanceNoticePublisher(c));
                var notice = new FinanceNotice(Guid.NewGuid(), user, "head@resident.local", "Receipt", "Confirmed payment",
                    "<h1>Receipt</h1><p>Paid 700000 VND</p>", "PAYMENT", Guid.NewGuid(), "/resident/billing", "PT-test.html");
                await publisher.PublishAsync(notice, default);
                await publisher.PublishAsync(notice with { EventId = Guid.NewGuid(), UserId = inactiveUser, Email = "inactive@resident.local" }, default);
            }
            var services = new ServiceCollection(); services.AddScoped(_ => Communication());
            services.AddSingleton<PropFlow.Modules.Communication.Application.IFinanceEmailRecipientDirectory>(new QueuedRecipientDirectory("updated@resident.local", inactiveUser));
            await using var provider = services.BuildServiceProvider();
            using var worker = new FinanceEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new FinanceMailOptions { Host = "127.0.0.1", Port = port, From = "finance@propflow.local", EnableSsl = false, PollSeconds = 1 }),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceEmailWorker>.Instance);
            await worker.StartAsync(timeout.Token);
            var body = await received;
            Assert.Contains("updated@resident.local", body);
            Assert.DoesNotContain("head@resident.local", body);
            Assert.Contains("Receipt", body); Assert.Contains("text/html", body); Assert.DoesNotContain("multipart/mixed", body);
            while (true) {
                await using var check = Communication();
                if (await check.FinanceEmails.AnyAsync(x => x.SentAt != null, timeout.Token)) break;
                await Task.Delay(50, timeout.Token);
            }
            await worker.StopAsync(default);
            await using var db = Communication();
            Assert.NotNull((await db.FinanceEmails.SingleAsync(x => x.UserId == user)).SentAt);
            var deferred = await db.FinanceEmails.SingleAsync(x => x.UserId == inactiveUser);
            Assert.Null(deferred.SentAt);
            Assert.True(deferred.NextAttemptAt > DateTimeOffset.UtcNow.AddMinutes(50));
            Assert.False(listener.Pending());
        } finally { listener.Stop(); }
    }

    private sealed class QueuedRecipientDirectory(string currentAddress, Guid? inactiveUser = null)
        : PropFlow.Modules.Communication.Application.IFinanceEmailRecipientDirectory
    {
        public Task<string?> RecipientEmailAsync(Guid? userId, string? snapshotEmail, CancellationToken ct) =>
            Task.FromResult(userId is null ? snapshotEmail : userId == inactiveUser ? null : currentAddress);
    }
}



