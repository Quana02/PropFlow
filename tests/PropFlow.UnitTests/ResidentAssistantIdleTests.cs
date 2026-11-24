using PropFlow.Web.Client.Features.Resident.Portal.Services;

namespace PropFlow.UnitTests;

[Trait("Feature", "FacilityDiscovery-Task2-LiveVoice")]
public sealed class ResidentAssistantIdleTests
{
    [Fact]
    public void Warns_at_sixty_counts_down_thirty_and_expires_at_ninety()
    {
        var clock = new IdleTestClock(); var watch = new ResidentAssistantIdleWatch(clock); watch.Start();
        clock.Advance(59); Assert.Equal(AssistantIdleState.Listening, watch.Tick(false));
        clock.Advance(1); Assert.Equal(AssistantIdleState.Warning, watch.Tick(false)); Assert.Equal(30, watch.SecondsRemaining);
        clock.Advance(11); watch.Tick(false); Assert.Equal(19, watch.SecondsRemaining);
        clock.Advance(18); watch.Tick(false); Assert.Equal(1, watch.SecondsRemaining);
        clock.Advance(1); Assert.Equal(AssistantIdleState.Expired, watch.Tick(false));
        Assert.False(watch.Activity()); Assert.Equal(AssistantIdleState.Expired, watch.State);
    }
    [Fact]
    public void Speech_or_continue_resets_countdown_but_a_late_event_cannot_revive_a_timed_out_session()
    {
        var clock = new IdleTestClock(); var watch = new ResidentAssistantIdleWatch(clock); watch.Start();
        clock.Advance(70); watch.Tick(false); Assert.True(watch.Activity());
        Assert.Equal(AssistantIdleState.Listening, watch.State); Assert.Equal(0, watch.SecondsRemaining);
        clock.Advance(59); Assert.Equal(AssistantIdleState.Listening, watch.Tick(false));
        clock.Advance(31); Assert.False(watch.Activity()); Assert.Equal(AssistantIdleState.Expired, watch.State);
    }
    [Fact]
    public void Busy_work_is_protected_and_background_clock_jumps_do_not_extend_the_deadline()
    {
        var clock = new IdleTestClock(); var watch = new ResidentAssistantIdleWatch(clock); watch.Start();
        clock.Advance(120); Assert.Equal(AssistantIdleState.Listening, watch.Tick(true));
        clock.Advance(30); Assert.Equal(AssistantIdleState.Listening, watch.Tick(false));
        clock.Advance(60); Assert.Equal(AssistantIdleState.Warning, watch.Tick(false));
        clock.Advance(180); Assert.Equal(AssistantIdleState.Expired, watch.Tick(false));
        watch.Stop(); Assert.Equal(AssistantIdleState.Stopped, watch.Tick(false));
        watch.Start(); Assert.True(watch.Activity());
    }
}
internal sealed class IdleTestClock : TimeProvider
{
    private long timestamp;
    public override long TimestampFrequency => 1000;
    public override long GetTimestamp() => timestamp;
    public void Advance(double seconds) => timestamp += (long)(seconds * 1000);
}
