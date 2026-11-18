using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Application.UtilityConsumption;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    private async Task RunIsolatedDelta(Func<AuthenticationFlowTests,Task> scenario)
    {
        var isolated=new AuthDatabaseFixture { ConfigureFixtureServices=services=>services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>() };
        try { await isolated.InitializeAsync();await scenario(new AuthenticationFlowTests(isolated,output)); }
        finally { await isolated.DisposeAsync(); }
    }
    private sealed class DeltaProviders : IFacilityNeedAdvisor, IFacilitySpeechProvider, IUtilityExplanation, IUtilityConsumptionSource
    {
        public bool Ready => true;
        public int NeedCalls, SpeechCalls, ExplainCalls;
        public bool Fail, Malformed;
        public string Need = "", Choices = "", Speech = "", Analysis = "";
        public Guid Apartment, Resident;
        public static readonly DateOnly Month = new(2026, 10, 1);
        public Task<FacilityAdvisorResult?> RecommendAsync(string need, FacilityAdvisorInput[] choices, CancellationToken ct)
        {
            NeedCalls++; Need=need; Choices=JsonSerializer.Serialize(choices);
            return Task.FromResult<FacilityAdvisorResult?>(Fail ? null : new(Malformed ? ["not-in-catalogue"] : [choices[0].Key], false, false, "fixture", 2, 1));
        }
        public Task<byte[]?> GenerateAsync(string transcript, CancellationToken ct)
        { SpeechCalls++; Speech=transcript; return Task.FromResult<byte[]?>(Fail ? null : "RIFF-local-fixture"u8.ToArray()); }
        public Task<UtilityAiResult?> ExplainAsync(UtilityAnalysis analysis, CancellationToken ct)
        { ExplainCalls++; Analysis=JsonSerializer.Serialize(analysis); return Task.FromResult<UtilityAiResult?>(Fail ? null : new("Cần kiểm tra lại mức tiêu thụ.", "fixture", 2, 1)); }
        public Task<IReadOnlyList<BillingApartment>> ApartmentsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BillingApartment>>([new(Apartment,"DELTA-101")]);
        public Task<UtilityConsumptionWindow?> ReadAsync(Guid apartmentId, DateOnly month, CancellationToken ct) => Task.FromResult<UtilityConsumptionWindow?>(apartmentId!=Apartment ? null :
            new(Apartment,"DELTA-101",month,Enumerable.Range(-8,9).Select(i=>new UtilityConsumptionPoint(month.AddMonths(i), i==0?900:300,12,true,true,[Resident],true)).ToArray(),"delta-window"));
        // Intentionally over-broad candidate periods: the production service must still enforce snapshot recipients.
        public Task<IReadOnlyList<UtilityConsumptionPeriod>> ResidentPeriodsAsync(Guid userId,CancellationToken ct) => Task.FromResult<IReadOnlyList<UtilityConsumptionPeriod>>([new(Apartment,Month)]);
    }
    private WebApplicationFactory<Program> DeltaHost(DeltaProviders provider) => database.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        // The worker workflows have their own tests. Authorization snapshots must measure only this HTTP operation.
        foreach(var descriptor in services.Where(item=>item.ServiceType==typeof(Microsoft.Extensions.Hosting.IHostedService)
            && item.ImplementationType?.Name is "UtilityNoticeWorker" or "FacilityWeekendNoticeWorker").ToArray()) services.Remove(descriptor);
        services.AddSingleton<IFacilityNeedAdvisor>(provider); services.AddSingleton<IFacilitySpeechProvider>(provider);
        services.AddSingleton<IUtilityExplanation>(provider); services.AddSingleton<IUtilityConsumptionSource>(provider);
    }));
    private static HttpClient DeltaClient(WebApplicationFactory<Program> host) => host.CreateClient(new WebApplicationFactoryClientOptions {BaseAddress=new("https://localhost"),HandleCookies=true});
    private async Task<(HttpClient Client,SessionResponse Session)> DeltaLogin(WebApplicationFactory<Program> host,string role)
    {
        var account=await SeedRealtimeAccount(role);var client=DeltaClient(host);var session=await SignIn(client,account.Username);
        client.DefaultRequestHeaders.Authorization=new("Bearer",session.AccessToken);return(client,session);
    }
    private async Task DeltaMigrate()
    {
        using var scope=database.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery.FacilityWeekendNoticeOptions>().Enabled=false;
        await scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Communication.Infrastructure.Persistence.CommunicationDbContext>().Database.MigrateAsync();
    }
    private async Task<string> DeltaSnapshot(DeltaProviders provider)
    {
        using var scope=database.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>();
        var assets=scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        return JsonSerializer.Serialize(new { guides=await assets.FacilityGuides.AsNoTracking().OrderBy(x=>x.FacilityId).ToArrayAsync(),
            facilities=await assets.Facilities.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
            runs=await db.FacilityRecommendationRuns.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
            insights=await db.UtilityInsights.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync(),
            reviews=await db.UtilityInsightReviews.CountAsync(), attempts=await db.UtilityAiAttempts.CountAsync(), notices=await db.UtilityNotices.CountAsync(),
            notifications=await scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Communication.Infrastructure.Persistence.CommunicationDbContext>().Notifications.CountAsync(),
            provider.NeedCalls,provider.SpeechCalls,provider.ExplainCalls });
    }
    private async Task DeltaDenied(WebApplicationFactory<Program> host,DeltaProviders provider,HttpMethod method,string path,object? payload,string[] allowed)
    {
        async Task Send(HttpClient client,HttpStatusCode status)
        {
            // Account onboarding/login is the positive fixture setup, not part of the denied business request.
            var before=await DeltaSnapshot(provider);
            using var request=new HttpRequestMessage(method,path);if(payload is not null)request.Content=JsonContent.Create(payload);
            using var response=await client.SendAsync(request);Assert.Equal(status,response.StatusCode);
            Assert.Equal(before,await DeltaSnapshot(provider));
        }
        using(var anonymous=DeltaClient(host))await Send(anonymous,HttpStatusCode.Unauthorized);
        foreach(var role in new[]{"ADMIN","MANAGER","STAFF","ACCOUNTANT","RESIDENT"}.Except(allowed))
        {var (client,_)=await DeltaLogin(host,role);using(client)await Send(client,HttpStatusCode.Forbidden);}
        var (revoked,_session)=await DeltaLogin(host,allowed[0]);using(revoked)
        {
            using var logout=await Post(revoked,"logout",new{},true);Assert.Equal(HttpStatusCode.NoContent,logout.StatusCode);
            await Send(revoked,HttpStatusCode.Unauthorized);
        }
    }
    private async Task DeltaPermissionDenied(WebApplicationFactory<Program> host,DeltaProviders provider,string role,string permission,
        params (HttpMethod Method,string Path,object? Payload)[] operations)
    {
        var(client,_)=await DeltaLogin(host,role);using(client)
        {
            using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AdministrationDbContext>();
            var grant=await db.RolePermissions.Include(x=>x.Role).Include(x=>x.Permission).SingleAsync(x=>x.Role.Code==role&&x.Permission.Code==permission);
            var before=await DeltaSnapshot(provider);db.RolePermissions.Remove(grant);await db.SaveChangesAsync();
            try
            {
                foreach(var operation in operations)
                {
                    using var request=new HttpRequestMessage(operation.Method,operation.Path);
                    if(operation.Payload is not null)request.Content=JsonContent.Create(operation.Payload);
                    using var denied=await client.SendAsync(request);Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
                    Assert.Equal(before,await DeltaSnapshot(provider));
                }
            }
            finally {db.RolePermissions.Add(grant);await db.SaveChangesAsync();}
        }
    }
    private async Task<(Guid Facility,Guid Staff,Guid Control)> DeltaFacility(WebApplicationFactory<Program> host)
    {
        var (staff,session)=await DeltaLogin(host,"STAFF");staff.Dispose();
        using var scope=database.Factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
        var facility=new Facility("DELTA-"+Guid.NewGuid().ToString("N"),"Vườn đọc sách",database.Factory.Clock.GetUtcNow(),locationDescription:"Sảnh công cộng");
        var control=new Facility("CONTROL-"+Guid.NewGuid().ToString("N"),"Tiện ích nháp đối chứng",database.Factory.Clock.GetUtcNow());
        var controlGuide=new PropFlow.Modules.PropertyAssets.Domain.Facilities.FacilityGuide(control.Id);
        controlGuide.Change(DeltaGuide(session.User.Id) with{Published=false,Description="INTERNAL_GUIDE_CANARY"},session.User.Id,database.Factory.Clock.GetUtcNow());
        db.Facilities.AddRange(facility,control);db.FacilityGuides.Add(controlGuide);await db.SaveChangesAsync();return(facility.Id,session.User.Id,control.Id);
    }
    private static SaveFacilityGuide DeltaGuide(Guid staff,Guid? token=null) => new(true,"Không gian đọc sách thư giãn","GENERAL",null,null,"GUARDIAN_REQUIRED","","Nội quy công khai",
        Enumerable.Range(0,7).Select(i=>new FacilityOpening(i,new(0,0),new(23,59))).ToArray(),[],staff,"Quầy hỗ trợ",token);

    [Fact]
    public Task Post_pull_facility_guide_uses_real_login_permissions_tokens_and_preserves_other_assets() =>
        RunIsolatedDelta(owner=>owner.VerifyDeltaGuide());
    private async Task VerifyDeltaGuide()
    {
        await DeltaMigrate();var provider=new DeltaProviders();await using var host=DeltaHost(provider);var (facility,staff,control)=await DeltaFacility(host);
        string controlBefore;
        using(var scope=database.Factory.Services.CreateScope()){controlBefore=JsonSerializer.Serialize(await scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>().FacilityGuides.AsNoTracking().SingleAsync(x=>x.FacilityId==control));}
        var path=$"/api/v1/facility-guides/{facility}";var input=DeltaGuide(staff);
        await DeltaDenied(host,provider,HttpMethod.Put,path,input,["MANAGER"]);
        var (manager,session)=await DeltaLogin(host,"MANAGER");using(manager)
        {
            Assert.Equal(HttpStatusCode.NoContent,(await manager.PutAsJsonAsync(path,input)).StatusCode);
            var list=(await manager.GetFromJsonAsync<FacilityGuidesManagement>("/api/v1/facility-guides"))!;
            var guide=Assert.Single(list.Facilities.Where(x=>x.FacilityId==facility));Assert.True(guide.Published);
            using(var scope=database.Factory.Services.CreateScope())
            {var db=scope.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();Assert.Equal(session.User.Id,(await db.FacilityGuides.SingleAsync(x=>x.FacilityId==facility)).UpdatedBy);}
            var before=await DeltaSnapshot(provider);
            Assert.Equal(HttpStatusCode.Conflict,(await manager.PutAsJsonAsync(path,input)).StatusCode);Assert.Equal(before,await DeltaSnapshot(provider));
            Assert.Equal(HttpStatusCode.NotFound,(await manager.PutAsJsonAsync($"/api/v1/facility-guides/{Guid.NewGuid()}",input)).StatusCode);
            var results=await Task.WhenAll(manager.PutAsJsonAsync(path,input with{ExpectedToken=guide.Token,Description="Nội dung A"}),manager.PutAsJsonAsync(path,input with{ExpectedToken=guide.Token,Description="Nội dung B"}));
            Assert.Equal(new[]{HttpStatusCode.NoContent,HttpStatusCode.Conflict}.Order(),results.Select(x=>x.StatusCode).Order());
            var current=(await manager.GetFromJsonAsync<FacilityGuidesManagement>("/api/v1/facility-guides"))!.Facilities.Single(x=>x.FacilityId==facility);
            Assert.Equal(HttpStatusCode.BadRequest,(await manager.PutAsJsonAsync(path,input with{ExpectedToken=current.Token,ResponsibleStaffId=session.User.Id})).StatusCode);
            var tampered=JsonSerializer.SerializeToNode(input with{ExpectedToken=current.Token})!;
            tampered["facilityId"]=Guid.NewGuid();tampered["updatedBy"]=Guid.NewGuid();tampered["status"]="INACTIVE";
            Assert.Equal(HttpStatusCode.NoContent,(await manager.PutAsJsonAsync(path,tampered)).StatusCode);
            using var verify=database.Factory.Services.CreateScope();var assets=verify.ServiceProvider.GetRequiredService<PropertyAssetsDbContext>();
            Assert.Equal("ACTIVE",(await assets.Facilities.SingleAsync(x=>x.Id==facility)).Status.ToString());
            Assert.Equal(session.User.Id,(await assets.FacilityGuides.SingleAsync(x=>x.FacilityId==facility)).UpdatedBy);
            Assert.Equal(controlBefore,JsonSerializer.Serialize(await assets.FacilityGuides.AsNoTracking().SingleAsync(x=>x.FacilityId==control)));
            await DeltaPermissionDenied(host,provider,"MANAGER","MANAGE_OPERATIONS",(HttpMethod.Put,path,input));
        }
    }

    [Fact]
    public Task Post_pull_facility_recommendation_and_speech_enforce_resident_scope_privacy_and_provider_effects() =>
        RunIsolatedDelta(owner=>owner.VerifyDeltaRecommendation());
    private async Task VerifyDeltaRecommendation()
    {
        await DeltaMigrate();var provider=new DeltaProviders();await using var host=DeltaHost(provider);var(facility,staff,control)=await DeltaFacility(host);
        var(manager,_)=await DeltaLogin(host,"MANAGER");using(manager)Assert.Equal(HttpStatusCode.NoContent,(await manager.PutAsJsonAsync($"/api/v1/facility-guides/{facility}",DeltaGuide(staff))).StatusCode);
        const string path="/api/v1/resident/facility-recommendations";
        var input=new FacilityRecommendationRequest("Tôi muốn đọc sách một mình hôm nay; email canary@example.invalid; password=FACILITY_SECRET; OTP=564321");
        await DeltaDenied(host,provider,HttpMethod.Post,path,input,["RESIDENT"]);
        await DeltaDenied(host,provider,HttpMethod.Get,path+"/today?facilityId="+facility,null,["RESIDENT"]);
        await DeltaDenied(host,provider,HttpMethod.Post,path+"/speech",new FacilitySpeechRequest(null,"WELCOME"),["RESIDENT"]);
        var(owner,session)=await DeltaLogin(host,"RESIDENT");var(other,_other)=await DeltaLogin(host,"RESIDENT");using(owner)using(other)
        {
            var first=await owner.PostAsJsonAsync(path,new{input.Need,input.PreferredDate,userId=_other.User.Id,residentId=Guid.NewGuid(),facilityId=Guid.NewGuid()});Assert.Equal(HttpStatusCode.OK,first.StatusCode);
            var view=(await first.Content.ReadFromJsonAsync<FacilityRecommendationView>())!;Assert.NotEmpty(view.Suggestions);Assert.Equal(1,provider.NeedCalls);
            foreach(var canary in new[]{"canary@example.invalid","FACILITY_SECRET","564321"})Assert.False((provider.Need+provider.Choices).Contains(canary,StringComparison.Ordinal));
            Assert.DoesNotContain(session.User.Id.ToString(),provider.Choices);Assert.DoesNotContain(staff.ToString(),provider.Choices);Assert.DoesNotContain("Quầy hỗ trợ",provider.Choices);
            Assert.DoesNotContain("INTERNAL_GUIDE_CANARY",provider.Choices);Assert.DoesNotContain(control.ToString(),provider.Choices);
            Assert.All(view.Suggestions,item=>Assert.NotEqual(control,item.Facility.Id));
            Assert.Equal(HttpStatusCode.OK,(await owner.PostAsJsonAsync(path,input)).StatusCode);Assert.Equal(1,provider.NeedCalls);
            using(var runScope=database.Factory.Services.CreateScope()){var db=runScope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>();Assert.Single(await db.FacilityRecommendationRuns.Where(x=>x.Actor==session.User.Id).ToArrayAsync());}
            var speech=new FacilitySpeechRequest(view.SpeechId,"RESULT");
            var today=(await owner.GetFromJsonAsync<FacilityRecommendationView>(path+"/today?facilityId="+facility))!;
            Assert.NotNull(today.SpeechId);Assert.All(today.Suggestions,item=>Assert.Equal(facility,item.Facility.Id));
            Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsJsonAsync(path+"/speech",new FacilitySpeechRequest(today.SpeechId,"RESULT"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsJsonAsync(path+"/speech",speech)).StatusCode);Assert.Equal(0,provider.SpeechCalls);
            var audio=await owner.PostAsJsonAsync(path+"/speech",speech);Assert.Equal(HttpStatusCode.OK,audio.StatusCode);Assert.Equal("no-store",audio.Headers.CacheControl?.ToString());
            Assert.Equal("application/json",audio.Content.Headers.ContentType?.MediaType);Assert.Contains("audio/wav",await audio.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK,(await owner.PostAsJsonAsync(path+"/speech",speech)).StatusCode);Assert.Equal(1,provider.SpeechCalls);
            Assert.DoesNotContain("FACILITY_SECRET",provider.Speech);Assert.DoesNotContain("Quầy hỗ trợ",provider.Speech);
            Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path,new{need=new string('x',1201)})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path+"/speech",new{kind="arbitrary",text="private"})).StatusCode);
            provider.Fail=true;
            var fallback=await other.PostAsJsonAsync(path,new FacilityRecommendationRequest("Đọc sách thư giãn một mình vào ngày mai"));
            Assert.Equal(HttpStatusCode.OK,fallback.StatusCode);Assert.Equal("FALLBACK",(await fallback.Content.ReadFromJsonAsync<FacilityRecommendationView>())!.Mode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable,(await other.PostAsJsonAsync(path+"/speech",new FacilitySpeechRequest(null,"NEW_NEED"))).StatusCode);
            provider.Fail=false;provider.Malformed=true;
            var malformed=await other.PostAsJsonAsync(path,new FacilityRecommendationRequest("Đọc sách yên tĩnh cùng bạn vào cuối tuần"));Assert.Equal(HttpStatusCode.OK,malformed.StatusCode);
            Assert.Equal("FALLBACK",(await malformed.Content.ReadFromJsonAsync<FacilityRecommendationView>())!.Mode);
            using var scope=database.Factory.Services.CreateScope();var residents=scope.ServiceProvider.GetRequiredService<PropFlow.Modules.Residents.Infrastructure.Persistence.ResidentsDbContext>();
            var residentId=await residents.Residents.Where(x=>x.UserId==session.User.Id).Select(x=>x.Id).SingleAsync();
            await residents.ResidentApartments.Where(x=>x.ResidentId==residentId).ExecuteUpdateAsync(set=>set
                .SetProperty(x=>x.EndDate,x=>x.StartDate).SetProperty(x=>x.Status,PropFlow.Modules.Residents.Domain.ResidentApartments.ResidencyStatus.ENDED));
            var before=provider.NeedCalls;Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsJsonAsync(path,input)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await owner.PostAsJsonAsync(path+"/speech",speech)).StatusCode);Assert.Equal(before,provider.NeedCalls);
            await DeltaPermissionDenied(host,provider,"RESIDENT","USE_RESIDENT_SERVICES",(HttpMethod.Post,path,input),(HttpMethod.Post,path+"/speech",speech),(HttpMethod.Get,path+"/today?facilityId="+facility,null));
        }
    }

    [Fact]
    public Task Post_pull_utility_operations_check_real_sessions_role_scope_and_persisted_effects() =>
        RunIsolatedDelta(owner=>owner.VerifyDeltaUtility());
    private async Task VerifyDeltaUtility()
    {
        await DeltaMigrate();var provider=new DeltaProviders{Apartment=Guid.NewGuid()};await using var host=DeltaHost(provider);
        var(owner,session)=await DeltaLogin(host,"RESIDENT");provider.Resident=session.User.Id;
        var(manager,managerSession)=await DeltaLogin(host,"MANAGER");var(accountant,_)=await DeltaLogin(host,"ACCOUNTANT");var(other,_other)=await DeltaLogin(host,"RESIDENT");
        using(owner)using(manager)using(accountant)using(other)
        {
            var root="/api/v1/utility-insights";var detail=$"{root}/apartments/{provider.Apartment}?month=2026-10-01";
            await DeltaDenied(host,provider,HttpMethod.Get,root+"?month=2026-10-01",null,["MANAGER","ACCOUNTANT"]);
            await DeltaDenied(host,provider,HttpMethod.Get,detail,null,["MANAGER","ACCOUNTANT"]);
            await DeltaDenied(host,provider,HttpMethod.Get,"/api/v1/resident/utility-insights",null,["RESIDENT"]);
            var view=(await manager.GetFromJsonAsync<UtilityInsightView>(detail))!;Assert.Equal(provider.Apartment,view.ApartmentId);Assert.Equal("PENDING",view.Review);
            Assert.Single((await accountant.GetFromJsonAsync<UtilityInsightPage>(root+"?month=2026-10-01"))!.Items);
            Assert.Single((await owner.GetFromJsonAsync<UtilityInsightView[]>("/api/v1/resident/utility-insights"))!);
            Assert.Empty((await other.GetFromJsonAsync<UtilityInsightView[]>("/api/v1/resident/utility-insights"))!);
            Assert.Equal(0,provider.ExplainCalls);Assert.Equal(HttpStatusCode.NotFound,(await manager.GetAsync($"{root}/apartments/{Guid.NewGuid()}?month=2026-10-01")).StatusCode);
            var explain=$"{root}/apartments/{provider.Apartment}/explain?month=2026-10-01";var review=$"{root}/apartments/{provider.Apartment}/review?month=2026-10-01";
            await DeltaDenied(host,provider,HttpMethod.Post,explain,new UtilityExplainInput(view.Fingerprint),["MANAGER","ACCOUNTANT"]);
            await DeltaDenied(host,provider,HttpMethod.Post,review,new UtilityReviewInput(view.Fingerprint,"CONFIRMED","Kiểm tra"),["MANAGER"]);
            Assert.Equal(HttpStatusCode.OK,(await accountant.PostAsJsonAsync(explain,new UtilityExplainInput(view.Fingerprint))).StatusCode);Assert.Equal(1,provider.ExplainCalls);
            Assert.DoesNotContain(provider.Apartment.ToString(),provider.Analysis);Assert.DoesNotContain(session.User.Id.ToString(),provider.Analysis);Assert.DoesNotContain("DELTA-101",provider.Analysis);
            Assert.Equal(HttpStatusCode.OK,(await manager.PostAsJsonAsync(explain,new UtilityExplainInput(view.Fingerprint))).StatusCode);Assert.Equal(1,provider.ExplainCalls);
            Assert.Equal(HttpStatusCode.Conflict,(await manager.PostAsJsonAsync(review,new UtilityReviewInput("stale","CONFIRMED","Kiểm tra"))).StatusCode);
            var input=new UtilityReviewInput(view.Fingerprint,"CONFIRMED","Đã xác minh");var responses=await Task.WhenAll(manager.PostAsJsonAsync(review,input),manager.PostAsJsonAsync(review,input));
            Assert.All(responses,response=>Assert.Equal(HttpStatusCode.OK,response.StatusCode));
            using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AiRecommendationDbContext>();
            Assert.Single(await db.UtilityInsightReviews.Where(x=>x.InsightId==view.Id).ToArrayAsync());Assert.Single(await db.UtilityNotices.Where(x=>x.SourceId==view.Id).ToArrayAsync());
            var projection=await (await owner.GetAsync("/api/v1/resident/utility-insights")).Content.ReadAsStringAsync();Assert.DoesNotContain("recipients",projection,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("email",projection,StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.BadRequest,(await manager.GetAsync(root+"?month=2099-01-01")).StatusCode);
            await DeltaPermissionDenied(host,provider,"MANAGER","MANAGE_OPERATIONS",
                (HttpMethod.Get,root+"?month=2026-10-01",null),(HttpMethod.Get,detail,null),
                (HttpMethod.Post,explain,new UtilityExplainInput(view.Fingerprint)),(HttpMethod.Post,review,input));
            await DeltaPermissionDenied(host,provider,"RESIDENT","USE_RESIDENT_SERVICES",(HttpMethod.Get,"/api/v1/resident/utility-insights",null));
        }
    }
}
