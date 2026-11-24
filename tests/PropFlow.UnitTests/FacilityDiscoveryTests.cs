using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Facilities;

namespace PropFlow.UnitTests;

[Trait("Feature", "FacilityDiscovery-Task2")]
public sealed class FacilityDiscoveryTests
{
    [Theory]
    [InlineData("Tôi muốn tìm nơi yên tĩnh để học tập một mình hôm nay")]
    [InlineData("Tôi muốn tìm khu vực học bài trong chung cư hôm nay")]
    public async Task Live_study_lookup_only_displays_relevant_facilities_instead_of_filling_three_cards(string need)
    {
        await using var scenario = new Scenario();
        var hours = new FacilityOpening(1, new(5, 30), new(22, 0));
        var library = Library(hours) with { Name = "Thư viện và phòng đọc", Description = "Không gian yên tĩnh để đọc sách, học tập và làm việc." };
        var gym = library with { FacilityId = Guid.NewGuid(), Name = "Phòng gym người lớn", Type = "Phòng gym", Description = "Khu tập thể hình, rèn luyện sức khỏe dành cho cư dân người lớn." };
        var court = library with { FacilityId = Guid.NewGuid(), Name = "Sân cầu lông", Type = "Sân thể thao", Description = "Sân cầu lông dành cho cư dân tập luyện và giao lưu thể thao." };
        scenario.Source.Current = new([gym, library, court], "study-relevance");

        var result = await scenario.Service.LookupAsync(Guid.NewGuid(), new(need), default);

        Assert.Equal(library.FacilityId, Assert.Single(result.Suggestions).Facility.Id);
        Assert.Equal(0, scenario.Advisor.Calls);
    }
    [Fact]
    public async Task Live_study_lookup_uses_the_librarys_next_day_instead_of_an_unrelated_open_gym()
    {
        await using var scenario = new Scenario(); scenario.Options.MaxCandidates = 1;
        var library = Library(new FacilityOpening(2, new(8, 0), new(22, 0)));
        var gym = library with { FacilityId = Guid.NewGuid(), Name = "Phòng gym", Type = "Gym", Description = "Tập luyện thể hình", Hours = [new(1, new(8, 0), new(22, 0))] };
        scenario.Source.Current = new([gym, library], "next-relevant");
        var result = await scenario.Service.LookupAsync(Guid.NewGuid(), new("Tìm nơi học tập hôm nay"), default);
        var chosen = Assert.Single(result.Suggestions);
        Assert.Equal(library.FacilityId, chosen.Facility.Id);
        Assert.Equal(new DateOnly(2026, 11, 17), chosen.OpeningTimes[0].Date);
        Assert.Contains("Lịch thay thế", chosen.Availability);
        scenario.Source.Current = new([gym], "no-study-space");
        result = await scenario.Service.LookupAsync(Guid.NewGuid(), new("Tìm nơi học tập hôm nay"), default);
        Assert.Empty(result.Suggestions); Assert.Equal("NO_MATCH", result.Mode);
    }
    [Fact]
    public async Task Live_lookup_provides_candidates_beyond_three_for_semantic_selection_while_regular_suggestions_stay_bounded()
    {
        await using var scenario = new Scenario();
        scenario.Source.Current = new(Enumerable.Range(1, 7).Select(i => Library(new FacilityOpening(1, new(8, 0), new(22, 0))) with { Name = $"Thư viện {i}" }).ToArray(), "many-libraries");
        var actor = Guid.NewGuid(); var need = new FacilityRecommendationRequest("Tìm nơi đọc sách hôm nay");
        Assert.Equal(7, (await scenario.Service.LookupAsync(actor, need, default)).Suggestions.Length);
        scenario.Options.MaxCandidates = 4;
        Assert.Equal(4, (await scenario.Service.LookupAsync(actor, need, default)).Suggestions.Length);
        scenario.Advisor.Available = false;
        Assert.Equal(3, (await scenario.Service.RecommendAsync(actor, need, default)).Suggestions.Length);
    }
    [Fact]
    public async Task Live_lookup_returns_verified_facts_without_calling_another_model_or_recording_an_AI_run()
    {
        await using var scenario=new Scenario();
        var view=await scenario.Service.LookupAsync(Guid.NewGuid(),new("Khu vực cuối tuần cho trẻ em 6 tuổi thứ 7"),default);
        Assert.Equal("LOOKUP",view.Mode);Assert.NotEmpty(view.Suggestions);
        Assert.Equal(0,scenario.Advisor.Calls);Assert.Empty(scenario.Db.FacilityRecommendationRuns);
        Assert.All(view.Suggestions, suggestion=>Assert.NotEmpty(suggestion.OpeningTimes));
        scenario.Access.Active=false;
        Assert.Equal(403,(await Assert.ThrowsAsync<FacilityRecommendationFailure>(()=>scenario.Service.LookupAsync(Guid.NewGuid(),new("Khu vực cuối tuần cho trẻ em 6 tuổi"),default))).Status);
    }
    private static readonly DateTime Now = new(2026, 11, 16, 10, 0, 0);
    internal static FacilityGuideView Guide() => new(Guid.NewGuid(), "Góc vui chơi trẻ em", "Khu vui chơi", "Tầng 2", "ACTIVE", true,
        "Trò chơi và góc đọc sách cuối tuần cho trẻ em", "CHILDREN", 3, 12, "GUARDIAN_REQUIRED", "", "Có người lớn đi cùng.",
        [new(6, new(8, 0), new(17, 0)), new(0, new(9, 0), new(16, 0))], [], Guid.NewGuid(), "Nhân viên phụ trách", "Quầy BQL tầng 1", Guid.NewGuid(), new(Now, TimeSpan.Zero));
    private static SaveFacilityGuide Input() => new(true, "Khu vui chơi có thật", "CHILDREN", 3, 12, "GUARDIAN_REQUIRED", "", "Nội quy",
        [new(6, new(8, 0), new(17, 0))], [], Guid.NewGuid(), "Lễ tân tầng 1", null);
    [Fact]
    public async Task Vague_mood_asks_about_interests_without_suggesting_a_childrens_playground()
    {
        await using var scenario = new Scenario();
        var response = await scenario.Service.RecommendAsync(Guid.NewGuid(), new("Tôi hơi chán"), default);
        Assert.Equal("CLARIFY", response.Mode);
        Assert.Empty(response.Suggestions);
        Assert.NotNull(response.FollowUp);
        Assert.DoesNotContain("bé", response.FollowUp);
        Assert.Equal(0, scenario.Advisor.Calls);
    }
    [Fact]
    public void Self_directed_activity_does_not_include_children_only_facilities()
    {
        var query = "Tôi muốn đọc sách yên tĩnh";
        Assert.Empty(FacilityNeedMatcher.Match(new([Guide()], "v1"), FacilityNeedMatcher.Interpret(query), query, null, Now));
    }
    private static FacilityGuideView Library(params FacilityOpening[] hours)=>Guide() with {
        FacilityId=Guid.NewGuid(),Name="Thư viện",Type="Thư viện",Description="Đọc sách và thư giãn yên tĩnh",Audience="GENERAL",
        MinimumAge=null,MaximumAge=null,ChildPolicy="GUARDIAN_REQUIRED",Hours=hours };
    [Theory]
    [InlineData(null)]
    [InlineData("INTEREST")]
    [InlineData("PARTY")]
    [InlineData("TIME")]
    public async Task Chill_alone_today_after_a_vague_opening_does_not_repeat_answered_questions(string? question)
    {
        await using var scenario=new Scenario();
        scenario.Source.Current=new([Library(new FacilityOpening(1,new(8,0),new(22,0)))],"library");
        scenario.Advisor.Question=question;
        var result=await scenario.Service.RecommendAsync(Guid.NewGuid(),new("Tôi hơi chán\nBổ sung: tôi muốn chill 1 mình trong hôm nay"),default);
        Assert.NotEqual("CLARIFY",result.Mode);
        Assert.Null(result.FollowUp);
        Assert.Equal("Thư viện",Assert.Single(result.Suggestions).Facility.Name);
    }
    [Fact]
    public void Clarification_only_asks_for_the_missing_interest_when_companions_and_day_are_known()
    {
        var question=FacilityNeedMatcher.Clarification("Tôi hơi chán, đi một mình hôm nay");
        Assert.NotNull(question);
        Assert.DoesNotContain("cùng ai",question);
        Assert.DoesNotContain("lúc nào",question);
        Assert.DoesNotContain("bây giờ",question);
    }
    [Theory]
    [InlineData("tôi muốn chill 1 mình trong hôm nay")]
    [InlineData("Tôi muốn thư giãn một mình hôm nay")]
    [InlineData("Tôi muốn relax, đi riêng, ngày mai")]
    public void Clear_relaxation_need_records_all_answered_dimensions(string need)
    {
        var context=FacilityNeedMatcher.Context(need);
        Assert.True(context.ActivityKnown);Assert.True(context.CompanionsKnown);Assert.True(context.TimeKnown);
        Assert.Null(FacilityNeedMatcher.Clarification(need));
        Assert.Null(FacilityNeedMatcher.Question("INTEREST",need));
        Assert.Null(FacilityNeedMatcher.Question("PARTY",need));
        Assert.Null(FacilityNeedMatcher.Question("TIME",need));
    }
    [Fact]
    public void A_new_colloquial_answer_reaches_semantic_analysis_instead_of_replaying_the_vague_opening()
    {
        Assert.Null(FacilityNeedMatcher.Clarification("Tôi hơi chán\nBổ sung: muốn đổi gió một mình trong hôm nay"));
    }
    [Fact]
    public async Task Conversation_collects_interest_and_never_invents_children_from_a_model_flag()
    {
        await using var scenario=new Scenario();scenario.Source.Current=new([Guide(),Library(new FacilityOpening(1,new(8,0),new(22,0)))],"mixed");
        var actor=Guid.NewGuid();
        var question=await scenario.Service.RecommendAsync(actor,new("Tôi hơi chán"),default);
        Assert.Equal("CLARIFY",question.Mode);
        var answer=await scenario.Service.RecommendAsync(actor,new("Tôi hơi chán\nBổ sung: Tôi thích đọc sách, đi một mình bây giờ"),default);
        var item=Assert.Single(answer.Suggestions);
        Assert.Equal("Thư viện",item.Facility.Name);Assert.True(item.IsOpenNow);
        Assert.Contains("Đang trong giờ mở cửa",item.Availability);
        Assert.DoesNotContain(scenario.Advisor.Choices,x=>x.Audience=="CHILDREN");
        Assert.All(scenario.Advisor.Choices,x=>{Assert.Equal(Now,x.LocalNow);Assert.NotEmpty(x.OpeningTimes!);});
    }
    [Fact]
    public async Task Child_request_collects_age_before_recommending_an_age_limited_facility()
    {
        await using var scenario=new Scenario();
        var result=await scenario.Service.RecommendAsync(Guid.NewGuid(),new("Tìm khu vui chơi cho bé cuối tuần"),default);
        Assert.Equal("CLARIFY",result.Mode);Assert.Empty(result.Suggestions);Assert.Contains("bao nhiêu tuổi",result.FollowUp);
        Assert.Equal(0,scenario.Advisor.Calls);
    }
    [Fact]
    public async Task Closed_today_returns_a_labelled_next_day_alternative_in_building_local_time()
    {
        await using var scenario=new Scenario();scenario.ZoneId="Asia/Ho_Chi_Minh";
        scenario.Source.Current=new([Library(new FacilityOpening(1,new(8,0),new(16,0)),new(2,new(8,0),new(20,0)))],"library");
        var result=await scenario.Service.RecommendAsync(Guid.NewGuid(),new("Tôi muốn đọc sách bây giờ"),default);
        var item=Assert.Single(result.Suggestions);Assert.False(item.IsOpenNow);
        Assert.Equal(new DateOnly(2026,11,17),item.OpeningTimes[0].Date);
        Assert.Contains("Lịch thay thế",item.Availability);Assert.Contains("Ngày mai",item.Availability);
        Assert.Contains("lịch thay thế",result.Message);
        Assert.Equal(17,Assert.Single(scenario.Advisor.Choices).LocalNow!.Value.Hour);
    }
    [Fact]
    public void Current_opening_is_preferred_and_closed_dates_and_expired_slots_are_excluded()
    {
        var closed=Library(new FacilityOpening(1,new(6,0),new(9,0)),new(2,new(7,0),new(18,0))) with {ClosedDates=[new(2026,11,17)]};
        var later=Library(new FacilityOpening(1,new(12,0),new(22,0))) with {Name="A - Thư viện mở sau"};
        var open=Library(new FacilityOpening(1,new(8,0),new(20,0))) with {Name="Z - Thư viện đang mở"};
        var query="Tôi muốn đọc sách";
        var matches=FacilityNeedMatcher.Match(new([closed,later,open],"hours"),FacilityNeedMatcher.Interpret(query),query,null,Now);
        Assert.Equal(open.FacilityId,matches[0].Guide.FacilityId);
        Assert.All(matches,match=>Assert.All(match.Times,time=>Assert.True(time.Date.ToDateTime(time.ClosesAt)>Now)));
        Assert.DoesNotContain(matches.Single(x=>x.Guide.FacilityId==closed.FacilityId).Times,x=>x.Date==new DateOnly(2026,11,17));
    }
    [Theory]
    [InlineData("Tôi muốn đọc sách 7 giờ",7)]
    [InlineData("Tôi muốn đọc sách 7 giờ tối",19)]
    [InlineData("Tôi muốn đọc sách 18:30",18)]
    public void Requested_hour_is_not_confused_with_the_vietnamese_pronoun(string need,int hour)=>Assert.Equal(hour,FacilityNeedMatcher.Interpret(need).At!.Value.Hour);
    [Fact]
    public void Latest_requested_day_overrides_the_previous_turn()
    {
        Assert.Equal(new DateOnly(2026,11,17),FacilityNeedMatcher.Date("Tôi đọc sách hôm nay\nBổ sung: vậy ngày mai thì sao",new(2026,11,16)));
        Assert.Equal(new DateOnly(2026,11,16),FacilityNeedMatcher.Date("Tôi đọc sách ngày mai\nBổ sung: bây giờ có gì",new(2026,11,16)));
        Assert.Null(FacilityNeedMatcher.Date("Tôi đọc sách hôm nay\nBổ sung: thứ 7 thì sao",new(2026,11,16)));
        Assert.Empty(FacilityNeedMatcher.Interpret("Tôi đọc sách cuối tuần\nBổ sung: ngày mai thì sao").Weekdays);
        Assert.Null(FacilityNeedMatcher.Interpret("Tôi đọc sách 18 giờ\nBổ sung: bây giờ thì sao").At);
    }
    [Fact]
    public async Task Cached_recommendation_is_recomputed_when_a_facility_opens()
    {
        await using var scenario=new Scenario();scenario.Source.Current=new([Library(new FacilityOpening(1,new(10,5),new(22,0)))],"library");
        var actor=Guid.NewGuid();var need=new FacilityRecommendationRequest("Tôi muốn đọc sách");
        var before=await scenario.Service.RecommendAsync(actor,need,default);
        Assert.False(Assert.Single(before.Suggestions).IsOpenNow);
        scenario.Time.Instant=scenario.Time.Instant.AddMinutes(6);
        var after=await scenario.Service.RecommendAsync(actor,need,default);
        Assert.True(Assert.Single(after.Suggestions).IsOpenNow);Assert.Equal(2,scenario.Advisor.Calls);
    }
    [Fact]
    public async Task Provider_can_request_clarification_without_generating_fake_suggestions()
    {
        await using var scenario=new Scenario();scenario.Source.Current=new([Library(new FacilityOpening(1,new(8,0),new(20,0)))],"library");
        scenario.Advisor.Question="PARTY";
        var result=await scenario.Service.RecommendAsync(Guid.NewGuid(),new("Tôi muốn đọc sách"),default);
        Assert.Equal("CLARIFY",result.Mode);Assert.Empty(result.Suggestions);Assert.Contains("cùng ai",result.FollowUp);
    }
    [Theory]
    [InlineData("Khu vực cuối tuần cho trẻ em", true, false, null)]
    [InlineData("Bé 6 tuổi; hai vợ chồng phải lên công ty thứ 7", true, true, 6)]
    [InlineData("Tìm khu vui chơi cho bé 5 tuổi, phụ huynh đi cùng", true, false, 5)]
    [InlineData("Nơi đọc sách yên tĩnh", false, false, null)]
    [InlineData("Hai vợ chồng đi làm, muốn thư giãn",false,false,null)]
    public void Need_interpretation_preserves_child_and_supervision_constraints(string query, bool children, bool supervision, int? age)
    {
        var intent = FacilityNeedMatcher.Interpret(query);
        Assert.Equal(children, intent.ForChildren); Assert.Equal(supervision, intent.NeedsSupervision); Assert.Equal(age, intent.Age);
    }
    [Fact]
    public void Weekend_query_returns_only_real_open_dates_and_explicit_saturday_takes_precedence()
    {
        var guide = Guide(); var window = new FacilityGuideWindow([guide], "v1");
        var query = "Khu vực cuối tuần cho trẻ em 6 tuổi thứ 7";
        var match = Assert.Single(FacilityNeedMatcher.Match(window, FacilityNeedMatcher.Interpret(query), query, null, Now));
        Assert.All(match.Times, time => { Assert.Equal(DayOfWeek.Saturday, time.Date.DayOfWeek); Assert.Equal(new TimeOnly(8, 0), time.OpensAt); Assert.Equal(new TimeOnly(17, 0), time.ClosesAt); });
        Assert.Equal(guide.FacilityId, match.Guide.FacilityId);
    }
    [Theory]
    [InlineData("age")]
    [InlineData("inactive")]
    [InlineData("draft")]
    [InlineData("closed")]
    [InlineData("adult")]
    [InlineData("supervision")]
    public void Ineligible_facility_is_excluded_instead_of_filling_results(string reason)
    {
        var guide = Guide(); var query = "Khu vui chơi cho bé 6 tuổi thứ 7";
        guide = reason switch {
            "age" => guide with { MinimumAge = 10 }, "inactive" => guide with { Status = "INACTIVE" },
            "draft" => guide with { Published = false }, "closed" => guide with { ClosedDates = [new(2026, 11, 21)] },
            "adult" => guide with { Audience = "ADULTS", ChildPolicy = "NOT_FOR_CHILDREN" }, _ => guide };
        if (reason == "supervision") query += "; hai vợ chồng phải đi làm, cần người trông";
        Assert.Empty(FacilityNeedMatcher.Match(new([guide], "v1"), FacilityNeedMatcher.Interpret(query), query, new(2026, 11, 21), Now));
    }
    [Fact]
    public void Supervised_program_is_a_contact_lead_not_an_automatic_childcare_booking()
    {
        var guide = Guide() with { ChildPolicy = "SUPERVISED_CONTACT_REQUIRED", SupervisionNote = "Chương trình sáng thứ Bảy, gọi BQL xác nhận tiếp nhận." };
        var query = "Tìm hoạt động cho bé 6 tuổi; bố mẹ đi làm thứ 7";
        var match = Assert.Single(FacilityNeedMatcher.Match(new([guide], "v1"), FacilityNeedMatcher.Interpret(query), query, null, Now));
        Assert.Equal(guide.SupervisionNote, FacilityNeedMatcher.Card(match.Guide).SupervisionNote);
    }
    [Theory]
    [InlineData("hours")]
    [InlineData("person")]
    [InlineData("contact")]
    [InlineData("age")]
    [InlineData("supervision")]
    public void Publishing_requires_complete_valid_facts(string invalid)
    {
        var input = Input();
        input = invalid switch {
            "hours" => input with { Hours = [] }, "person" => input with { ResponsibleStaffId = null },
            "contact" => input with { ContactDetails = "" }, "age" => input with { MinimumAge = 13, MaximumAge = 6 },
            _ => input with { ChildPolicy = "SUPERVISED_CONTACT_REQUIRED", SupervisionNote = "" } };
        Assert.Equal(400, Assert.Throws<FacilityGuideFailure>(() => new FacilityGuide(Guid.NewGuid()).Change(input, Guid.NewGuid(), DateTimeOffset.UtcNow)).Status);
    }
    [Fact]
    public async Task Unconfigured_ai_uses_honest_lookup_and_unknown_need_returns_no_match()
    {
        await using var scenario = new Scenario(); scenario.Advisor.Available = false;
        var response = await scenario.Service.RecommendAsync(Guid.NewGuid(), new("Khu vực cuối tuần cho trẻ em 6 tuổi"), default);
        Assert.Equal("CATALOGUE", response.Mode); Assert.Single(response.Suggestions); Assert.Contains("AI chưa sẵn sàng", response.Message);
        response = await scenario.Service.RecommendAsync(Guid.NewGuid(), new("Tôi cần sửa máy tính"), default);
        Assert.Empty(response.Suggestions); Assert.Equal(0, scenario.Advisor.Calls);
        Assert.Empty(await scenario.Db.FacilityRecommendationRuns.ToArrayAsync());
    }
    [Fact]
    public async Task Gemini_selects_only_existing_facts_and_never_receives_staff_contact_or_personal_details()
    {
        await using var scenario = new Scenario(); var actor = Guid.NewGuid();
        var response = await scenario.Service.RecommendAsync(actor, new("Khu vực cuối tuần cho trẻ em 6 tuổi. Email abc@example.com, điện thoại 0912345678"), default);
        var suggestion = Assert.Single(response.Suggestions); var guide = scenario.Source.Current.Guides.Single();
        Assert.Equal("GEMINI", response.Mode); Assert.Equal(guide.ResponsibleName, suggestion.Facility.ResponsibleName);
        Assert.Equal(guide.ContactDetails, suggestion.Facility.ContactDetails);
        Assert.DoesNotContain("abc@example.com", scenario.Advisor.Need); Assert.DoesNotContain("0912345678", scenario.Advisor.Need);
        var payload = JsonSerializer.Serialize(scenario.Advisor.Choices);
        Assert.DoesNotContain(guide.ResponsibleName!, payload); Assert.DoesNotContain(guide.ContactDetails, payload);
        Assert.DoesNotContain(actor.ToString(), payload); Assert.DoesNotContain(guide.FacilityId.ToString(), payload);
        Assert.Contains("GUARDIAN_REQUIRED", payload);
    }
    [Fact]
    public async Task AI_can_tighten_supervision_but_cannot_recommend_a_playground_as_childcare()
    {
        await using var scenario = new Scenario(); scenario.Advisor.Supervision = true;
        var response = await scenario.Service.RecommendAsync(Guid.NewGuid(), new("Khu vực cuối tuần cho trẻ em 6 tuổi"), default);
        Assert.Empty(response.Suggestions); Assert.Contains("không thay thế dịch vụ trông trẻ", response.Message);
    }
    [Fact]
    public async Task Cache_is_per_actor_and_source_changes_invalidate_it_while_quota_limits_provider_calls()
    {
        await using var scenario = new Scenario(); var actor = Guid.NewGuid(); var input = new FacilityRecommendationRequest("Khu vực cuối tuần cho trẻ em 6 tuổi");
        await scenario.Service.RecommendAsync(actor, input, default); await scenario.Service.RecommendAsync(actor, input, default);
        Assert.Equal(1, scenario.Advisor.Calls);
        await scenario.Service.RecommendAsync(Guid.NewGuid(), input, default); Assert.Equal(2, scenario.Advisor.Calls);
        scenario.Options.AiRequestsPerUserDay = 1; scenario.Source.Current = scenario.Source.Current with { Fingerprint = "changed" };
        Assert.Equal(429, (await Assert.ThrowsAsync<FacilityRecommendationFailure>(() => scenario.Service.RecommendAsync(actor, input, default))).Status);
        Assert.Equal(2, scenario.Advisor.Calls);
    }
    [Theory]
    [InlineData(false, 409)]
    [InlineData(true, 403)]
    public async Task Changed_source_or_residency_during_AI_call_does_not_cache_or_return_stale_result(bool residency, int status)
    {
        await using var scenario = new Scenario();
        scenario.Advisor.DuringCall = () => { if (residency) scenario.Access.Active = false; else scenario.Source.Current = scenario.Source.Current with { Fingerprint = "changed" }; };
        Assert.Equal(status, (await Assert.ThrowsAsync<FacilityRecommendationFailure>(() => scenario.Service.RecommendAsync(Guid.NewGuid(), new("Khu vực cuối tuần cho trẻ em 6 tuổi"), default))).Status);
        Assert.Null((await scenario.Db.FacilityRecommendationRuns.SingleAsync()).ResultJson);
    }
    [Theory]
    [InlineData("[\"0\"]", true)]
    [InlineData("[]", true)]
    [InlineData("[\"unknown\"]", false)]
    [InlineData("[\"0\",\"0\"]", false)]
    public async Task Provider_output_validates_allowed_unique_keys_including_legitimate_empty_result(string keys, bool valid)
    {
        using var handler = new GeminiResponse(keys); using var http = new HttpClient(handler) { BaseAddress = new("https://test.invalid/") };
        var advisor = new GeminiFacilityNeedAdvisor(new PropFlow.Shared.GeminiStructuredClient(http, "test-only-key", "test-model", true));
        var response = await advisor.RecommendAsync("Nơi vui chơi cho trẻ em", [new("0", "Khu vui chơi", "Khu vui chơi", "Trẻ em đi cùng phụ huynh", "CHILDREN", 3, 12, "GUARDIAN_REQUIRED")], default);
        Assert.Equal(valid, response is not null);
    }
    private sealed class GeminiResponse(string keys) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = "{\"keys\":" + keys + ",\"forChildren\":true,\"needsSupervision\":false}" } } } } }, usageMetadata = new { promptTokenCount = 12, candidatesTokenCount = 8 } }), Encoding.UTF8, "application/json") });
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Instant=new(Now, TimeSpan.Zero);public override DateTimeOffset GetUtcNow() => Instant; }
    private sealed class Zone(string id) : ICurrentBuildingTimeZone { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult(id); }
    private sealed class Access : IFacilityResidentAccess { public bool Active = true; public Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => Task.FromResult(Active); }
    private sealed class Source : IFacilityGuideSource { public FacilityGuideWindow Current = new([Guide()], "v1"); public Task<FacilityGuideWindow> ReadAsync(CancellationToken ct) => Task.FromResult(Current); }
    private sealed class Advisor : IFacilityNeedAdvisor
    {
        public bool Available = true, Supervision; public string? Question; public int Calls; public string Need = ""; public FacilityAdvisorInput[] Choices = []; public Action? DuringCall;
        public bool Ready => Available;
        public Task<FacilityAdvisorResult?> RecommendAsync(string need, FacilityAdvisorInput[] choices, CancellationToken ct) { Calls++; Need = need; Choices = choices; DuringCall?.Invoke(); return Task.FromResult<FacilityAdvisorResult?>(new(Question is null?[choices[0].Key]:[], true, Supervision, "test-model", 12, 8,Question)); }
    }
    private sealed class Scenario : IAsyncDisposable
    {
        public readonly AiRecommendationDbContext Db = new(new DbContextOptionsBuilder<AiRecommendationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Source Source = new(); public readonly Access Access = new(); public readonly Advisor Advisor = new(); public readonly FacilityRecommendationOptions Options = new();
        public string ZoneId="UTC";
        public readonly Clock Time=new();
        public FacilityRecommendations Service => new(Db, Source, Access, new Zone(ZoneId), Time, Advisor, Options, NullLogger<FacilityRecommendations>.Instance);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
