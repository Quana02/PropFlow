using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

public sealed class FacilityWeekendNoticeTests
{
    [Theory]
    [InlineData(21, 7, 59, null)]
    [InlineData(21, 8, 0, 8)]
    [InlineData(21, 8, 14, 8)]
    [InlineData(21, 8, 15, null)]
    [InlineData(21, 9, 0, null)]
    [InlineData(21, 12, 0, 12)]
    [InlineData(21, 16, 0, 16)]
    [InlineData(21, 20, 0, 20)]
    [InlineData(21, 20, 15, null)]
    [InlineData(22, 8, 0, 8)]
    [InlineData(23, 8, 0, null)]
    public void Weekend_schedule_starts_at_eight_repeats_four_hourly_and_never_replays_missed_slots(int day,int hour,int minute,int? expected)
        => Assert.Equal(expected,FacilityWeekendPlanner.Slot(new(2026,11,day,hour,minute,0),new())?.Hour);

    internal static FacilityGuideView Guide(int index) => FacilityDiscoveryTests.Guide() with {
        FacilityId=Guid.NewGuid(),Name="Tiện ích "+index,Audience="GENERAL",MinimumAge=null,MaximumAge=null,
        Description="Không gian thư giãn cho cư dân",Hours=[new(6,new(7,0),new(22,0)),new(0,new(7,0),new(22,0))] };

    [Fact]
    public async Task One_different_facility_per_slot_no_repeat_in_the_same_weekend_and_restart_is_idempotent()
    {
        await using var scenario=new Scenario();
        foreach(var day in new[]{21,22})foreach(var hour in new[]{8,12,16,20})
        {
            scenario.Time.Local(day,hour);
            await scenario.Service.DispatchAsync(default);
            await scenario.Service.DispatchAsync(default);
        }
        Assert.Equal(8,scenario.Delivery.Notices.Count);
        Assert.Equal(8,scenario.Delivery.Notices.Select(x=>x.FacilityId).Distinct().Count());
        Assert.All(scenario.Delivery.Notices,notice=>{
            Assert.Single(notice.Message.Split("Tiện ích ").Skip(1));
            Assert.Contains("07:00–22:00",notice.Message);
            Assert.Equal($"/resident/facilities?facility={notice.FacilityId:D}",notice.ActionPath);
        });
        scenario.Time.Instant=scenario.Time.Instant.AddDays(6);scenario.Time.Local(28,8);
        await scenario.Service.DispatchAsync(default);
        Assert.Equal(9,scenario.Delivery.Notices.Count);
    }
    [Fact]
    public async Task Building_timezone_not_utc_controls_the_first_eight_am_notification()
    {
        await using var scenario=new Scenario();scenario.Time.Local(21,7,59);
        await scenario.Service.DispatchAsync(default);Assert.Empty(scenario.Delivery.Notices);
        scenario.Time.Local(21,8);await scenario.Service.DispatchAsync(default);
        Assert.Single(scenario.Delivery.Notices);Assert.Equal(1,scenario.Time.GetUtcNow().Hour);
    }
    [Theory]
    [InlineData("closed")]
    [InlineData("inactive")]
    [InlineData("draft")]
    [InlineData("children")]
    [InlineData("ended")]
    [InlineData("staff")]
    public async Task No_invented_activity_when_source_is_unavailable_or_ineligible(string reason)
    {
        await using var scenario=new Scenario();var guide=Guide(1);
        scenario.Source.Value=new([reason switch {
            "closed"=>guide with{ClosedDates=[new(2026,11,21)]},"inactive"=>guide with{Status="INACTIVE"},
            "draft"=>guide with{Published=false},"children"=>guide with{Audience="CHILDREN",MinimumAge=3,MaximumAge=12},
            "ended"=>guide with{Hours=[new(6,new(6,0),new(7,0))]},_=>guide with{ResponsibleName=null}}],"facts");
        await scenario.Service.DispatchAsync(default);
        Assert.Empty(scenario.Delivery.Notices);Assert.Empty(await scenario.Db.FacilityWeekendNoticeRuns.ToArrayAsync());
    }
    [Fact]
    public async Task Only_one_available_facility_is_not_repeated_later_in_the_weekend()
    {
        await using var scenario=new Scenario();scenario.Source.Value=new([Guide(1)],"single");
        await scenario.Service.DispatchAsync(default);
        scenario.Time.Local(21,12);await scenario.Service.DispatchAsync(default);
        scenario.Time.Local(22,8);await scenario.Service.DispatchAsync(default);
        Assert.Single(scenario.Delivery.Notices);
    }
    [Fact]
    public async Task A_failed_delivery_reuses_the_same_event_and_facility_instead_of_creating_a_duplicate()
    {
        await using var scenario=new Scenario();scenario.Delivery.Fail=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>scenario.Service.DispatchAsync(default));
        var run=await scenario.Db.FacilityWeekendNoticeRuns.SingleAsync();var eventId=run.Id;
        scenario.Time.Local(21,8,1);await scenario.Service.DispatchAsync(default);
        Assert.Equal(1,scenario.Delivery.Calls);
        scenario.Delivery.Fail=false;scenario.Time.Local(21,8,5);await scenario.Service.DispatchAsync(default);
        Assert.Equal(2,scenario.Delivery.Calls);
        Assert.Equal(eventId,Assert.Single(scenario.Delivery.Notices).EventId);
        Assert.NotNull((await scenario.Db.FacilityWeekendNoticeRuns.SingleAsync()).CompletedAt);
    }
    private sealed class Clock:TimeProvider
    {
        public DateTimeOffset Instant;
        public Clock()=>Local(21,8);
        public void Local(int day,int hour,int minute=0)=>Instant=new DateTimeOffset(2026,11,day,hour,minute,0,TimeSpan.FromHours(7)).ToUniversalTime();
        public override DateTimeOffset GetUtcNow()=>Instant;
    }
    private sealed class Zone:ICurrentBuildingTimeZone {public Task<string> GetAsync(CancellationToken ct)=>Task.FromResult("Asia/Ho_Chi_Minh");}
    private sealed class Source:IFacilityGuideSource
    {
        public FacilityGuideWindow Value=new(Enumerable.Range(1,8).Select(Guide).ToArray(),"facts");
        public Task<FacilityGuideWindow> ReadAsync(CancellationToken ct)=>Task.FromResult(Value);
    }
    private sealed class Delivery:IFacilityWeekendNoticeDelivery
    {
        public bool Fail;public int Calls;public List<FacilityWeekendNotice> Notices=[];
        public Task<int> DeliverAsync(FacilityWeekendNotice notice,CancellationToken ct)
        {Calls++;if(Fail)throw new InvalidOperationException();Notices.Add(notice);return Task.FromResult(2);}
    }
    private sealed class Scenario:IAsyncDisposable
    {
        public readonly AiRecommendationDbContext Db=new(new DbContextOptionsBuilder<AiRecommendationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Clock Time=new();public readonly Source Source=new();public readonly Delivery Delivery=new();
        public FacilityWeekendNotices Service=>new(Db,Source,new Zone(),Time,new(),Delivery);
        public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
}
