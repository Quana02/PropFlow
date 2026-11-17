using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

namespace PropFlow.UnitTests;

public sealed class FacilitySpeechTests
{
    private static FacilityRecommendationView View()
    {
        var guide=FacilityDiscoveryTests.Guide();
        var card=FacilityNeedMatcher.Card(guide with {ResponsibleName="Private Staff Name",ContactDetails="Private contact 0912345678"});
        return new([new(card,[new(new(2026,11,21),new(8,0),new(17,30))],"Phù hợp")],"GEMINI","Thông tin BQL",null,DateTimeOffset.UtcNow);
    }
    [Fact]
    public void Clarification_speaks_the_question_instead_of_claiming_no_amenities_exist()
    {
        var view=new FacilityRecommendationView([],"CLARIFY","Mình cần hiểu thêm","Bạn thích vận động hay thư giãn? Bạn muốn đi vào lúc nào?",DateTimeOffset.UtcNow);
        Assert.Equal(view.FollowUp,FacilitySpeech.Transcript(view));
    }
    [Fact]
    public void Self_directed_visit_to_a_family_amenity_does_not_start_talking_about_children()
    {
        var view=View();var item=view.Suggestions[0];
        view=view with{Suggestions=[item with{Facility=item.Facility with{Name="Vườn đi bộ",Audience="FAMILY"}}]};
        Assert.DoesNotContain("Trẻ em",FacilitySpeech.Transcript(view));
        Assert.DoesNotContain("phụ huynh",FacilitySpeech.Transcript(view));
    }
    [Fact]
    public void Speech_uses_amenity_facts_and_omits_staff_identity_and_private_contact()
    {
        var text=FacilitySpeech.Transcript(View());
        Assert.Contains("Góc vui chơi",text);Assert.Contains("8 giờ",text);Assert.Contains("17 giờ 30 phút",text);
        Assert.DoesNotContain("Private",text);Assert.DoesNotContain("0912345678",text);
    }
    [Fact]
    public async Task Only_actor_bound_server_tickets_are_read_and_replays_use_cached_audio()
    {
        var provider=new Provider();using var service=new FacilitySpeech(provider,new(),TimeProvider.System,NullLogger<FacilitySpeech>.Instance);
        var actor=Guid.NewGuid();var ticket=service.Prepare(actor,View());
        Assert.Equal(404,(await Assert.ThrowsAsync<FacilityRecommendationFailure>(()=>service.GetAsync(Guid.NewGuid(),new(ticket),default))).Status);
        var result=await service.GetAsync(actor,new(ticket),default);await service.GetAsync(actor,new(ticket),default);
        Assert.Equal("audio/wav",result.ContentType);Assert.Equal(1,provider.Calls);
        Assert.Equal(400,(await Assert.ThrowsAsync<FacilityRecommendationFailure>(()=>service.GetAsync(actor,new(null,"arbitrary text"),default))).Status);
    }
    [Fact]
    public async Task Concurrent_replays_generate_once_and_quota_blocks_a_different_uncached_utterance()
    {
        var provider=new Provider();using var service=new FacilitySpeech(provider,new(){PerResidentHour=1},TimeProvider.System,NullLogger<FacilitySpeech>.Instance);
        var actor=Guid.NewGuid();var ticket=service.Prepare(actor,View());
        await Task.WhenAll(service.GetAsync(actor,new(ticket),default),service.GetAsync(actor,new(ticket),default));
        Assert.Equal(1,provider.Calls);
        Assert.Equal(429,(await Assert.ThrowsAsync<FacilityRecommendationFailure>(()=>service.GetAsync(actor,new(null,"WELCOME"),default))).Status);
    }
    [Fact]
    public async Task Provider_failure_is_a_real_error_and_does_not_cache_fake_audio()
    {
        var provider=new Provider(){Unavailable=true};using var service=new FacilitySpeech(provider,new(),TimeProvider.System,NullLogger<FacilitySpeech>.Instance);
        Assert.Equal(503,(await Assert.ThrowsAsync<FacilityRecommendationFailure>(()=>service.GetAsync(Guid.NewGuid(),new(),default))).Status);
        provider.Unavailable=false;await service.GetAsync(Guid.NewGuid(),new(),default);Assert.Equal(2,provider.Calls);
    }
    [Theory]
    [InlineData("audio/l16; rate=24000; channels=1",true)]
    [InlineData("audio/L16;rate=24000",true)]
    [InlineData("audio/l16;rate=16000",false)]
    [InlineData("text/html",false)]
    public async Task Gemini_returns_playable_mono_WAV_and_accepts_only_documented_PCM_format(string mime,bool valid)
    {
        using var response=new Response(mime);using var http=new HttpClient(response){BaseAddress=new("https://speech.test/")};
        var provider=new GeminiFacilitySpeech(http,"test-only-key","gemini-3.1-flash-tts-preview",true);
        var audio=await provider.GenerateAsync("Chào bạn, mở cửa từ tám giờ.",default);
        Assert.Equal(valid,audio is not null);
        using var request=JsonDocument.Parse(response.Body);
        Assert.Contains("tiếng Việt",request.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Contains("Kore",response.Body);Assert.DoesNotContain("test-only-key",response.Body);
        if(audio is not null){Assert.Equal("RIFF",System.Text.Encoding.ASCII.GetString(audio,0,4));Assert.Equal(24000,BinaryPrimitives.ReadInt32LittleEndian(audio.AsSpan(24)));Assert.Equal(4,BinaryPrimitives.ReadInt32LittleEndian(audio.AsSpan(40)));}
    }
    private sealed class Provider:IFacilitySpeechProvider
    {
        public int Calls;public bool Unavailable;
        public Task<byte[]?> GenerateAsync(string text,CancellationToken ct){Calls++;return Task.FromResult<byte[]?>(Unavailable?null:[82,73,70,70]);}
    }
    private sealed class Response(string mime):HttpMessageHandler
    {
        public string Body="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Body=await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new {candidates=new[]{new {finishReason="STOP",content=new {parts=new[]{new {inlineData=new {mimeType=mime,data=Convert.ToBase64String([0,1,2,3])}}}}}}})};
        }
    }
}
