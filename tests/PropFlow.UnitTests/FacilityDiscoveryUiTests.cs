using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Web.Client.Features.Facility.Components;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Pages;
using PropFlow.Web.Client.Services.Api;
using ManagementPage = PropFlow.Web.Client.Features.Facility.Pages.FacilityGuidesManagement;

namespace PropFlow.UnitTests;

[Trait("Feature", "FacilityDiscovery-Task2")]
public sealed class FacilityDiscoveryUiTests
{
    private static ResidentFacilityCard Card()
    {
        var guide = FacilityDiscoveryTests.Guide();
        return new(guide.FacilityId,guide.Name,guide.Type,guide.Location,guide.Description,guide.Audience,guide.MinimumAge,guide.MaximumAge,
            guide.ChildPolicy,guide.SupervisionNote,guide.Rules,guide.Hours,guide.ClosedDates,guide.ResponsibleName!,guide.ContactDetails);
    }
    private static FacilityRecommendationView Result() => new([new(Card(),[new(new(2026,11,21),new(8,0),new(17,0))],"Phù hợp hoạt động cuối tuần cho trẻ em.","Ngày 21/11 · khung tiếp theo 08:00–17:00")],
        "GEMINI","Thông tin lấy từ hồ sơ BQL, không phải xác nhận tiếp nhận trẻ.","Bé bao nhiêu tuổi? Có người giám hộ đi cùng không?",DateTimeOffset.UtcNow);
    [Fact]
    public async Task Notification_link_opens_the_requested_facility_without_asking_the_resident_to_describe_a_need()
    {
        using var handler=new Responses();using var http=new HttpClient(handler){BaseAddress=new("https://facility.test/")};var capture=new Capture();
        await using var services=new ServiceCollection().AddLogging().AddSingleton(new AuthenticatedApiClient(http,NullLogger<ApiClient>.Instance))
            .AddSingleton<NavigationManager,FacilityTestNavigation>().AddSingleton<FacilityRecommendationsApi>().AddResidentAssistant().AddSingleton<IJSRuntime,NoJavaScript>().AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var root=await renderer.RenderComponentAsync<ResidentFacilities>(ParameterView.Empty);var page=capture.Resident!;var id=Guid.NewGuid();
            page.SuggestedFacility=id;await Invoke(page,"OnParametersSetAsync");Refresh(page);
            Assert.Equal(id,handler.RequestedFacility);
            var html=WebUtility.HtmlDecode(root.ToHtmlString());Assert.Contains("LỊCH TIỆN ÍCH HÔM NAY",html);Assert.Contains("08:00",html);
            Assert.DoesNotContain("Bạn thích vận động",html);Assert.Null(handler.Need);
        });
    }
    [Fact]
    public async Task Clarification_is_a_conversation_not_an_empty_recommendation_and_later_turns_keep_context()
    {
        using var handler=new Responses {View=new([],"CLARIFY","Cần hiểu thêm","Bạn thích hoạt động gì và muốn đi lúc nào?",DateTimeOffset.UtcNow)};
        using var http=new HttpClient(handler){BaseAddress=new("https://facility.test/")};var capture=new Capture();
        await using var services=new ServiceCollection().AddLogging().AddSingleton(new AuthenticatedApiClient(http,NullLogger<ApiClient>.Instance))
            .AddSingleton<NavigationManager,FacilityTestNavigation>().AddSingleton<FacilityRecommendationsApi>().AddResidentAssistant().AddSingleton<IJSRuntime,NoJavaScript>().AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var root=await renderer.RenderComponentAsync<ResidentFacilities>(ParameterView.Empty);var page=capture.Resident!;
            var welcome=WebUtility.HtmlDecode(root.ToHtmlString());Assert.DoesNotContain("cho bé",welcome);
            Set(page,"need","Tôi hơi chán");await Invoke(page,"Recommend");Refresh(page);
            var html=WebUtility.HtmlDecode(root.ToHtmlString());Assert.Contains("Bạn thích hoạt động gì",html);Assert.DoesNotContain("facility-recommendations",html);
            handler.View=Result() with{FollowUp=null};Set(page,"need","Đọc sách một mình");await Invoke(page,"Recommend");
            Assert.Contains("Tôi hơi chán\nBổ sung: Đọc sách một mình",handler.Need);
            Set(page,"need","Vậy ngày mai");await Invoke(page,"Recommend");
            Assert.Contains("Đọc sách một mình\nBổ sung: Vậy ngày mai",handler.Need);
        });
    }
    [Fact]
    public async Task Cards_display_grounded_hours_contact_child_policy_and_escape_source_html()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var result=Result();var first=result.Suggestions[0];result=result with{Suggestions=[first with{Facility=first.Facility with{Rules="<script>alert(1)</script>"}}]};
            var root=await renderer.RenderComponentAsync<FacilityRecommendationCards>(ParameterView.FromDictionary(new Dictionary<string,object?>{["Data"]=result}));
            var html=WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("08:00",html);Assert.Contains("17:00",html);Assert.Contains("21/11/2026",html);Assert.Contains("khung tiếp theo",html);
            Assert.Contains("Nhân viên phụ trách",html);Assert.Contains("Quầy BQL tầng 1",html);Assert.Contains("không phải dịch vụ trông trẻ",html);
            Assert.DoesNotContain("<script>",root.ToHtmlString());Assert.Contains("&lt;script&gt;",root.ToHtmlString());
        });
    }
    [Fact]
    public async Task Choosing_another_suggestion_changes_visible_hours_and_contact_without_a_new_AI_request()
    {
        var capture=new Capture();await using var services=new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var view=Result();var second=view.Suggestions[0] with{Facility=Card() with{Name="Thư viện thứ hai",ResponsibleName="Người phụ trách thư viện"},OpeningTimes=[new(new(2026,11,21),new(9,0),new(21,0))],Availability="Ngày 21/11 · khung tiếp theo 09:00–21:00"};
            view=view with{Suggestions=[view.Suggestions[0],second]};
            var root=await renderer.RenderComponentAsync<FacilityRecommendationCards>(ParameterView.FromDictionary(new Dictionary<string,object?>{["Data"]=view}));
            var html=WebUtility.HtmlDecode(root.ToHtmlString());Assert.Contains("08:00",html);Assert.DoesNotContain("21:00",html);
            Set(capture.Cards!,"selectedIndex",1);Refresh(capture.Cards!);html=WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("21:00",html);Assert.Contains("Người phụ trách thư viện",html);Assert.DoesNotContain("08:00",html);
        });
    }
    [Fact]
    public async Task Resident_submits_full_need_and_sees_real_recommendations_without_old_occupancy_filters()
    {
        using var handler=new Responses();using var http=new HttpClient(handler){BaseAddress=new("https://facility.test/")};var capture=new Capture();
        await using var services=new ServiceCollection().AddLogging().AddSingleton(new AuthenticatedApiClient(http,NullLogger<ApiClient>.Instance))
            .AddSingleton<NavigationManager,FacilityTestNavigation>().AddSingleton<FacilityRecommendationsApi>().AddResidentAssistant().AddSingleton<IJSRuntime, NoJavaScript>().AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var root=await renderer.RenderComponentAsync<ResidentFacilities>(ParameterView.Empty);var page=capture.Resident!;
            await Export("welcome",root.ToHtmlString());
            const string need="Bé 6 tuổi muốn vui chơi cuối tuần, có phụ huynh đi cùng";
            Set(page,"need",need);await Invoke(page,"Recommend");Refresh(page);
            Assert.Equal(need,handler.Need);var html=WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Góc vui chơi trẻ em",html);Assert.Contains("Nhân viên phụ trách",html);Assert.Contains("08:00",html);
            Assert.DoesNotContain("Số người",html);Assert.DoesNotContain("Mức đông",html);Assert.Contains("Chạm để nói",html);
            Assert.Contains("assistant-orbit",html);Assert.DoesNotContain("discovery-catalogue",html);
            await Export("resident",root.ToHtmlString());
        });
    }
    [Fact]
    public async Task Spoken_need_submits_on_completion_and_followup_keeps_previous_context()
    {
        using var handler=new Responses();using var http=new HttpClient(handler){BaseAddress=new("https://facility.test/")};var capture=new Capture();
        await using var services=new ServiceCollection().AddLogging().AddSingleton(new AuthenticatedApiClient(http,NullLogger<ApiClient>.Instance))
            .AddSingleton<NavigationManager,FacilityTestNavigation>().AddSingleton<FacilityRecommendationsApi>().AddResidentAssistant().AddSingleton<IJSRuntime,NoJavaScript>().AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var root=await renderer.RenderComponentAsync<ResidentFacilities>(ParameterView.Empty);var page=capture.Resident!;
            Set(page,"voiceEpoch",1);Set(page,"listening",true);
            await page.OnVoiceResult(1,"Khu vực cuối tuần cho trẻ em","");
            await page.OnVoiceEnded(1,"");
            Assert.Equal("Khu vực cuối tuần cho trẻ em",handler.Need);
            Set(page,"voiceEpoch",2);Set(page,"listening",true);
            await page.OnVoiceResult(2,"Bé 6 tuổi có phụ huynh đi cùng","");await page.OnVoiceEnded(2,"");
            Assert.Contains("Khu vực cuối tuần cho trẻ em\nBổ sung: Bé 6 tuổi",handler.Need);
            Set(page,"voiceEpoch",3);Set(page,"listening",true);
            await page.OnVoiceResult(2,"Nội dung cũ đến muộn","");
            Assert.Equal("",typeof(ResidentFacilities).GetField("need",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page));
            await page.OnAssistantSpeech(3,"paused");Refresh(page);
            Assert.Contains("Mình ở đây để giúp bạn",WebUtility.HtmlDecode(root.ToHtmlString()));
            var currentEpoch=(int)typeof(ResidentFacilities).GetField("voiceEpoch",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
            await page.OnAssistantSpeech(currentEpoch,"playback-blocked");Refresh(page);
            var unavailable=WebUtility.HtmlDecode(root.ToHtmlString());
            Assert.Contains("Nhấn Nghe lại",unavailable);Assert.Contains("Giọng nói bật",unavailable);Assert.DoesNotContain("Đã tắt tiếng",unavailable);
        });
    }
    [Fact]
    public void Spoken_reply_uses_returned_facts_and_preserves_childcare_confirmation_and_fallback_mode()
    {
        var view=Result();var first=view.Suggestions[0];
        var text=FacilityAssistantReply.Create(view);
        Assert.Contains(first.Facility.Name,text);Assert.Contains(first.Facility.ResponsibleName,text);Assert.Contains("08:00",text);Assert.Contains("17:00",text);
        var supervised=view with{Suggestions=[first with{Facility=first.Facility with{ChildPolicy="SUPERVISED_CONTACT_REQUIRED"}}],Mode="FALLBACK"};
        text=FacilityAssistantReply.Create(supervised);
        Assert.Contains("tra cứu",text);Assert.Contains("chưa phải đăng ký thành công",text);
    }
    [Fact]
    public async Task Manager_edits_published_profile_with_real_contact_and_schedule_not_people_counts()
    {
        using var handler=new Responses();using var http=new HttpClient(handler){BaseAddress=new("https://facility.test/")};var capture=new Capture();
        await using var services=new ServiceCollection().AddLogging().AddSingleton(new AuthenticatedApiClient(http,NullLogger<ApiClient>.Instance))
            .AddSingleton<NavigationManager,FacilityTestNavigation>().AddSingleton<FacilityRecommendationsApi>().AddResidentAssistant().AddSingleton<IJSRuntime,NoJavaScript>().AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var root=await renderer.RenderComponentAsync<ManagementPage>(ParameterView.Empty);var page=capture.Manager!;
            var html=WebUtility.HtmlDecode(root.ToHtmlString());Assert.Contains("Giờ mở và đóng cửa",html);Assert.Contains("Người phụ trách",html);
            Assert.Contains("aria-expanded=\"false\"",html);
            Assert.DoesNotContain("Sức chứa",html);Assert.DoesNotContain("Số người",html);
            Set(page,"description","Mô tả đã chỉnh sửa");await Invoke(page,"Save");Refresh(page);
            Assert.Equal("Mô tả đã chỉnh sửa",handler.Saved!.Description);Assert.Equal(handler.Guide.Token,handler.Saved.ExpectedToken);
            Assert.Equal(handler.Guide.ResponsibleStaffId,handler.Saved.ResponsibleStaffId);Assert.NotEmpty(handler.Saved.Hours);
            await Export("manager",root.ToHtmlString());
        });
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(target,value);
    private static Task Invoke(object target,string name)=>(Task)target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,null)!;
    private static void Refresh(ComponentBase target)=>typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,null);
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string id,object?[]? args)=>throw new NotSupportedException("Static rendering must not open a microphone.");
        public ValueTask<T> InvokeAsync<T>(string id,CancellationToken ct,object?[]? args)=>InvokeAsync<T>(id,args);
    }
    private static Task Export(string role,string html)=>Environment.GetEnvironmentVariable("PROPFLOW_FACILITY_PREVIEW_DIR") is {Length:>0} directory
        ?File.WriteAllTextAsync(Path.Combine(directory,$"propflow-facility-{role}.html"),html):Task.CompletedTask;
    private sealed class Capture:IComponentActivator
    {
        public ResidentFacilities? Resident;public ManagementPage? Manager;public FacilityRecommendationCards? Cards;
        public IComponent CreateInstance(Type type){var item=(IComponent)Activator.CreateInstance(type)!;if(item is ResidentFacilities resident)Resident=resident;if(item is ManagementPage manager)Manager=manager;if(item is FacilityRecommendationCards cards)Cards=cards;return item;}
    }
    private sealed class Responses:HttpMessageHandler
    {
        public readonly FacilityGuideView Guide=FacilityDiscoveryTests.Guide();public string? Need;public SaveFacilityGuide? Saved;public FacilityRecommendationView? View;public Guid? RequestedFacility;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            object payload;
            if(request.Method==HttpMethod.Put){Saved=await request.Content!.ReadFromJsonAsync<SaveFacilityGuide>(cancellationToken:ct);return new(HttpStatusCode.NoContent);}
            if(request.Method==HttpMethod.Post){using var data=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));Need=data.RootElement.GetProperty("need").GetString();payload=View??Result();}
            else if(request.RequestUri!.AbsolutePath.EndsWith("/today")){RequestedFacility=Guid.Parse(request.RequestUri.Query.Split('=')[1]);payload=Result() with{Mode="TODAY",FollowUp=null};}
            else if(request.RequestUri!.AbsolutePath.EndsWith("facility-guides"))payload=new PropFlow.Modules.PropertyAssets.Contracts.FacilityGuidesManagement([Guide],[new(Guide.ResponsibleStaffId!.Value,Guide.ResponsibleName!)],new(2026,11,16));
            else payload=new ResidentFacilityCatalogue([Card()],new(2026,11,16));
            return new(HttpStatusCode.OK){Content=JsonContent.Create(payload,payload.GetType())};
        }
    }
}
