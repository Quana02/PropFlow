using System.Net;
using PropFlow.Web.Client.Features.Resident.Portal.Services;

namespace PropFlow.UnitTests;

public sealed partial class FacilityContinuousConversationTests
{
    [Fact]
    public Task Idle_warning_counts_down_then_closes_voice_before_returning_to_dashboard_once() => WithPage(async f =>
    {
        var clock = new IdleTestClock(); var watch = new ResidentAssistantIdleWatch(clock); f.Set("idleWatch", watch);
        f.Player.LiveSupported = true; await f.InitializeVoice(); var epoch = f.Epoch;
        var version = f.Get<int>("idleGeneration"); var stops = f.Player.Stops;
        clock.Advance(59); await f.Call("CheckIdleTimeout", version);
        Assert.DoesNotContain("assistant-idle-countdown", f.Html());
        clock.Advance(1); await f.Call("CheckIdleTimeout", version);
        Assert.Equal(30, watch.SecondsRemaining);
        Assert.Contains("Bạn còn muốn trò chuyện không?", WebUtility.HtmlDecode(f.Html()));
        Assert.Contains("Tiếp tục trò chuyện", WebUtility.HtmlDecode(f.Html()));
        await f.Export("idle");
        clock.Advance(21); await f.Call("CheckIdleTimeout", version); Assert.Equal(9, watch.SecondsRemaining);
        Assert.Equal(0, f.Navigation.Visits);
        f.Navigation.BeforeNavigate = () => { Assert.False(f.Active); Assert.True(f.Player.Stops > stops); };
        clock.Advance(9); await f.Call("CheckIdleTimeout", version);
        Assert.Equal("https://facility.test/resident", f.Navigation.Uri); Assert.Equal(1, f.Navigation.Visits);
        await f.Call("CheckIdleTimeout", version); await f.Page.OnLiveUserTurn(epoch, 1, "Mình muốn nói tiếp"); await f.Page.OnLiveEnded(epoch);
        Assert.Equal(1, f.Navigation.Visits); Assert.False(f.Active);
    });
    [Fact]
    public Task Speech_continuation_and_the_continue_button_cancel_the_warning_without_creating_a_user_message() => WithPage(async f =>
    {
        var clock = new IdleTestClock(); var watch = new ResidentAssistantIdleWatch(clock); f.Set("idleWatch", watch);
        f.Player.LiveSupported = true; await f.InitializeVoice();
        clock.Advance(70); await f.Call("CheckIdleTimeout", f.Get<int>("idleGeneration"));
        await f.Page.OnLiveUserTurn(f.Epoch, 1, "Mình vẫn đang ở đây");
        Assert.Equal(AssistantIdleState.Listening, watch.State); Assert.DoesNotContain("assistant-idle-countdown", f.Html());
        clock.Advance(60); await f.Call("CheckIdleTimeout", f.Get<int>("idleGeneration"));
        await f.Call("ContinueIdleConversation"); Assert.Equal(AssistantIdleState.Listening, watch.State);
        Assert.Empty(f.Transport.Needs); Assert.True(f.Active); Assert.Equal(0, f.Navigation.Visits);
        clock.Advance(61); await f.Call("CheckIdleTimeout", f.Get<int>("idleGeneration"));
        await f.Page.OnLiveUserTurn(f.Epoch, 1, "Mình đang nhập yêu cầu");
        Assert.Equal(AssistantIdleState.Listening, watch.State); Assert.True(f.Active);
    });
    [Fact]
    public Task AI_work_and_submissions_are_protected_and_stale_timers_cannot_close_a_new_session() => WithPage(async f =>
    {
        var clock = new IdleTestClock(); var watch = new ResidentAssistantIdleWatch(clock); f.Set("idleWatch", watch);
        f.Player.LiveSupported = true; await f.InitializeVoice();
        var version = f.Get<int>("idleGeneration");
        await f.Page.OnLiveState(f.Epoch, "speaking"); clock.Advance(120); await f.Call("CheckIdleTimeout", version);
        Assert.Equal(AssistantIdleState.Listening, watch.State); Assert.Equal(0, f.Navigation.Visits);
        await f.Page.OnLiveState(f.Epoch, "listening"); await f.Call("CheckIdleTimeout", version);
        f.Set("requestSubmitting", true); clock.Advance(120); await f.Call("CheckIdleTimeout", version);
        Assert.True(f.Active); Assert.Equal(0, f.Navigation.Visits);
        f.Set("requestSubmitting", false); await f.Call("CheckIdleTimeout", version);
        await f.ClickMicrophone(); Assert.Equal(AssistantIdleState.Stopped, watch.State);
        await f.ClickMicrophone(); await f.Call("CheckIdleTimeout", version);
        Assert.True(f.Active); Assert.Equal(0, f.Navigation.Visits);
        var current = f.Get<int>("idleGeneration");
        await f.Page.DisposeAsync(); clock.Advance(120); await f.Call("CheckIdleTimeout", current);
        Assert.Equal(AssistantIdleState.Stopped, watch.State); Assert.Equal(0, f.Navigation.Visits);
    });
}
