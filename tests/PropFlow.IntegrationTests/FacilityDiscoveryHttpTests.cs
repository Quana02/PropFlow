using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Domain.Notifications;

namespace PropFlow.IntegrationTests;

[Trait("Feature", "FacilityDiscovery-Task2")]
public sealed class FacilityDiscoveryHttpTests : IAsyncLifetime
{
    private readonly Guid resident = Guid.NewGuid(), manager = Guid.NewGuid();
    private Guid facility;
    private string databaseName = "", adminConnection = "";
    private Factory factory = null!;
    public async Task InitializeAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(TestDatabaseConfiguration.GetConnectionString()) { Pooling = false };
        Assert.Equal("propflow_test", connection.Database);
        connection.Database = "postgres"; adminConnection = connection.ConnectionString;
        databaseName = "propflow_facility_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(adminConnection))
        { await admin.OpenAsync(); await TestDatabaseSafety.CreateAsync(admin,databaseName); }
        connection.Database = databaseName; factory = new(connection.ConnectionString, resident);
        using var scope = factory.Services.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Administration.Infrastructure.Persistence.AdministrationDbContext>();
        await audit.Database.ExecuteSqlRawAsync(audit.Database.GenerateCreateScript());
        var db = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        Assert.Equal(databaseName, db.Database.GetDbConnection().Database);
        await db.GetService<IMigrator>().MigrateAsync("20261115183504_AddSharedFacilityUsage");
        // Upgrade an actual old profile rather than testing only an empty schema.
        var entity = new Facility("KIDS-TEST", "Khu vui chơi cuối tuần", DateTimeOffset.UtcNow, locationDescription: "Tầng 2");
        db.Facilities.Add(entity); await db.SaveChangesAsync(); facility = entity.Id;
        var token = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO property_assets.facility_usage_settings
            (facility_id,published,capacity,slot_minutes,rules,hours_json,closed_dates,assigned_staff_ids,token,updated_by,updated_at)
            VALUES ({facility},true,20,60,'Nội quy được giữ lại','[]'::jsonb,ARRAY[]::date[],ARRAY[]::uuid[],{token},{manager},{now})
            """);
        await db.Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>().Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        if (!databaseName.StartsWith("propflow_facility_test_", StringComparison.Ordinal)) return;
        await using var admin = new NpgsqlConnection(adminConnection); await admin.OpenAsync();
        await TestDatabaseSafety.DropAsync(admin,databaseName);
    }
    [Fact]
    public async Task Upgrade_retains_rules_as_draft_and_removes_old_counting_tables()
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        var guide = await db.FacilityGuides.SingleAsync();
        Assert.Equal("Nội quy được giữ lại", guide.Rules); Assert.False(guide.Published); Assert.Equal("", guide.Description);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT to_regclass('property_assets.facility_occupancies') IS NULL AND to_regclass('property_assets.facility_usage_settings') IS NULL";
        await db.Database.OpenConnectionAsync(); Assert.Equal(true, await command.ExecuteScalarAsync());
    }
    [Fact]
    public async Task Weekend_workers_share_a_lease_and_notifications_are_idempotent_and_use_the_right_facility_link()
    {
        using var scope=factory.Services.CreateScope();var connection=scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>().Database.GetConnectionString()!;
        await using(var communication=new CommunicationDbContext(new DbContextOptionsBuilder<CommunicationDbContext>().UseNpgsql(connection,x=>x.MigrationsHistoryTable("__EFMigrationsHistory","communication")).Options))
            await communication.Database.MigrateAsync();
        var assets=scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        foreach(var index in new[]{1,2})
        {
            var asset=new Facility("WEEKEND-"+index,"Thư viện "+index,DateTimeOffset.UtcNow,locationDescription:"Tầng 1");assets.Facilities.Add(asset);
            var guide=new FacilityGuide(asset.Id);guide.Change(new(true,"Không gian đọc sách","GENERAL",null,null,"GUARDIAN_REQUIRED","","Nội quy",[new(6,new(8,0),new(22,0))],[],Staff.Id,"Quầy hỗ trợ",null),manager,DateTimeOffset.UtcNow);
            assets.FacilityGuides.Add(guide);
        }
        await assets.SaveChangesAsync();
        var clock=(Clock)scope.ServiceProvider.GetRequiredService<TimeProvider>();clock.Instant=new(2026,11,21,8,0,0,TimeSpan.Zero);
        var delivery=new WeekendDelivery(connection,resident);
        async Task Dispatch()
        {
            await using var db=new AiRecommendationDbContext(new DbContextOptionsBuilder<AiRecommendationDbContext>().UseNpgsql(connection,x=>x.MigrationsHistoryTable("__EFMigrationsHistory","ai_recommendation")).Options);
            using var sourceScope=factory.Services.CreateScope();var guides=sourceScope.ServiceProvider.GetRequiredService<IFacilityGuideSource>();
            await new FacilityWeekendNotices(db,guides,new Zone(),clock,new(),delivery).DispatchAsync(default);
        }
        await Task.WhenAll(Dispatch(),Dispatch());await Dispatch();
        clock.Instant=clock.Instant.AddHours(4);await Dispatch();
        await using var verify=new CommunicationDbContext(new DbContextOptionsBuilder<CommunicationDbContext>().UseNpgsql(connection).Options);
        var notifications=await verify.Notifications.Where(x=>x.SourceType==FacilityWeekendNotice.SourceType).ToArrayAsync();
        Assert.Equal(2,notifications.Length);Assert.Equal(2,notifications.Select(x=>x.SourceId).Distinct().Count());
        Assert.All(notifications,item=>{Assert.Equal(NotificationType.ANNOUNCEMENT,item.Type);Assert.Equal($"/resident/facilities?facility={item.SourceId:D}",item.ActionPath);Assert.Equal(resident,item.RecipientUserId);});
        using var owner=Client("RESIDENT",resident);
        var opened=await owner.GetFromJsonAsync<FacilityRecommendationView>($"/api/v1/resident/facility-recommendations/today?facilityId={notifications[0].SourceId:D}");
        Assert.Equal(notifications[0].SourceId,Assert.Single(opened!.Suggestions).Facility.Id);Assert.Equal("TODAY",opened.Mode);Assert.NotNull(opened.SpeechId);
    }
    private sealed class WeekendDelivery(string connection,Guid recipient):IFacilityWeekendNoticeDelivery
    {
        public async Task<int> DeliverAsync(FacilityWeekendNotice notice,CancellationToken ct)
        {
            await using var db=new CommunicationDbContext(new DbContextOptionsBuilder<CommunicationDbContext>().UseNpgsql(connection).Options);
            var publisher=new FinanceNoticePublisher(db);
            await publisher.PublishAsync(new(notice.EventId,recipient,null,notice.Title,notice.Message,"",FacilityWeekendNotice.SourceType,notice.FacilityId,notice.ActionPath),ct);
            return 1;
        }
    }
    [Fact]
    public async Task Manager_publishes_real_facts_resident_gets_grounded_suggestions_and_staff_cannot_manage()
    {
        const string guides = "/api/v1/facility-guides";
        const string discover = "/api/v1/resident/facility-recommendations";
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(discover)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(discover+"/today")).StatusCode);
        using var staff = Client("STAFF", Staff.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(guides)).StatusCode);
        using var owner = Client("RESIDENT", resident);
        using var stranger = Client("RESIDENT", Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(discover)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(discover+"/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(discover+"/today")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await staff.PostAsJsonAsync(discover+"/speech",new FacilitySpeechRequest())).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<ResidentFacilityCatalogue>(discover))!.Facilities);
        using var management = Client("MANAGER", manager);
        var draft = (await management.GetFromJsonAsync<FacilityGuidesManagement>(guides))!.Facilities.Single();
        var input = new SaveFacilityGuide(true, "Khu vui chơi và đọc sách cuối tuần cho trẻ em", "CHILDREN", 3, 12, "GUARDIAN_REQUIRED", "", "Có người lớn đi cùng.",
            [new(6,new(8,0),new(17,0)),new(0,new(9,0),new(16,0))], [], Staff.Id, "Lễ tân tầng 1", draft.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync($"{guides}/{facility}",input)).StatusCode);
        var concurrent = await Task.WhenAll(management.PutAsJsonAsync($"{guides}/{facility}",input), management.PutAsJsonAsync($"{guides}/{facility}",input));
        Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(concurrent, x => x.StatusCode == HttpStatusCode.Conflict);
        var body = await (await owner.GetAsync(discover)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("responsibleStaffId", body); Assert.DoesNotContain(Staff.Id.ToString(), body);
        var clarification=await (await owner.PostAsJsonAsync(discover,new FacilityRecommendationRequest("Tôi hơi chán"))).Content.ReadFromJsonAsync<FacilityRecommendationView>();
        Assert.Equal("CLARIFY",clarification!.Mode);Assert.Empty(clarification.Suggestions);Assert.NotNull(clarification.SpeechId);
        Assert.DoesNotContain("bé",clarification.FollowUp);
        var response = await owner.PostAsJsonAsync(discover,new FacilityRecommendationRequest("Khu vực cuối tuần cho trẻ em 6 tuổi thứ 7"));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var view = (await response.Content.ReadFromJsonAsync<FacilityRecommendationView>())!;
        var recommendation = Assert.Single(view.Suggestions);
        Assert.NotNull(view.SpeechId);
        var spoken=await owner.PostAsJsonAsync(discover+"/speech",new FacilitySpeechRequest(view.SpeechId));
        Assert.Equal(HttpStatusCode.OK,spoken.StatusCode);Assert.Equal("audio/wav",(await spoken.Content.ReadFromJsonAsync<FacilitySpeechAudio>())!.ContentType);
        Assert.Equal("GEMINI",view.Mode); Assert.Equal(facility,recommendation.Facility.Id);
        Assert.Equal("Staff phụ trách kiểm thử",recommendation.Facility.ResponsibleName);
        Assert.Equal("Lễ tân tầng 1",recommendation.Facility.ContactDetails);
        Assert.All(recommendation.OpeningTimes, time => { Assert.Equal(DayOfWeek.Saturday,time.Date.DayOfWeek); Assert.Equal(new TimeOnly(8,0),time.OpensAt); Assert.Equal(new TimeOnly(17,0),time.ClosesAt); });
        var childcare = await (await owner.PostAsJsonAsync(discover,new FacilityRecommendationRequest("Bé 6 tuổi, bố mẹ đi làm thứ 7 cần người trông trẻ"))).Content.ReadFromJsonAsync<FacilityRecommendationView>();
        Assert.Empty(childcare!.Suggestions); Assert.Contains("không thay thế dịch vụ trông trẻ",childcare.Message);
        factory.Staff.Available = false;
        Assert.Empty((await owner.GetFromJsonAsync<ResidentFacilityCatalogue>(discover))!.Facilities);
        Assert.Equal(HttpStatusCode.NotFound,(await management.GetAsync("/api/v1/facility-usage")).StatusCode);
    }
    private HttpClient Client(string role,Guid user)
    {
        var client=factory.CreateClient(new WebApplicationFactoryClientOptions{BaseAddress=new("https://localhost")});
        client.DefaultRequestHeaders.Add("X-Test-Role",role);client.DefaultRequestHeaders.Add("X-Test-User",user.ToString());return client;
    }
    private sealed class Factory(string connection,Guid resident):PropFlowApiFactory(connection)
    {
        public Staff Staff {get;}=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>{["FacilityRecommendations:WeekendNotice:Enabled"]="false"}));
            builder.ConfigureTestServices(services=>{
                services.AddAuthentication(o=>{o.DefaultAuthenticateScheme=o.DefaultChallengeScheme=o.DefaultForbidScheme="FacilityTest";})
                    .AddScheme<AuthenticationSchemeOptions,TestAuthentication>("FacilityTest",_=>{});
                services.RemoveAll<IMaintenanceStaffDirectory>();services.AddSingleton<IMaintenanceStaffDirectory>(Staff);
                services.RemoveAll<IFacilityResidentAccess>();services.AddSingleton<IFacilityResidentAccess>(new Access(resident));
                services.RemoveAll<IFacilityNeedAdvisor>();services.AddSingleton<IFacilityNeedAdvisor,Advisor>();
                services.RemoveAll<IFacilitySpeechProvider>();services.AddSingleton<IFacilitySpeechProvider,SpeechProvider>();
                services.RemoveAll<ICurrentBuildingTimeZone>();services.AddSingleton<ICurrentBuildingTimeZone,Zone>();
                services.RemoveAll<TimeProvider>();services.AddSingleton<TimeProvider,Clock>();
            });
        }
    }
    private sealed class Staff:IMaintenanceStaffDirectory
    {
        public static readonly Guid Id=Guid.NewGuid();public bool Available=true;
        public Task<IReadOnlyList<MaintenanceStaffRecord>> GetActiveStaffAsync(CancellationToken ct)=>Task.FromResult<IReadOnlyList<MaintenanceStaffRecord>>(Available?[new(Id,"staff-test","Staff phụ trách kiểm thử")]:[]);
        public Task<MaintenanceStaffRecord?> GetActiveStaffAsync(Guid userId,CancellationToken ct)=>Task.FromResult<MaintenanceStaffRecord?>(Available&&userId==Id?new(Id,"staff-test","Staff phụ trách kiểm thử"):null);
    }
    private sealed class Access(Guid resident):IFacilityResidentAccess{public Task<bool> ActiveAsync(Guid actor,CancellationToken ct)=>Task.FromResult(actor==resident);}
    private sealed class Advisor:IFacilityNeedAdvisor
    {
        public bool Ready=>true;
        public Task<FacilityAdvisorResult?> RecommendAsync(string need,FacilityAdvisorInput[] choices,CancellationToken ct)=>Task.FromResult<FacilityAdvisorResult?>(new([choices[0].Key],true,false,"test-model",12,8));
    }
    private sealed class SpeechProvider:IFacilitySpeechProvider
    {
        public Task<byte[]?> GenerateAsync(string text,CancellationToken ct)=>Task.FromResult<byte[]?>([82,73,70,70]);
    }
    private sealed class Clock:TimeProvider{public DateTimeOffset Instant=new(2026,11,16,10,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Instant;}
    private sealed class Zone:ICurrentBuildingTimeZone{public Task<string> GetAsync(CancellationToken ct)=>Task.FromResult("UTC");}
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Request.Headers.TryGetValue("X-Test-Role",out var role))return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new List<Claim>{new(ClaimTypes.Role,role.ToString()),new("sub",Request.Headers["X-Test-User"].ToString())};
            if(role=="MANAGER")claims.Add(new("permission","MANAGE_OPERATIONS"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }
}
