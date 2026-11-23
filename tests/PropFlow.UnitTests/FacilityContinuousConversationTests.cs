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
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Pages;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.AiChatbot.Contracts;

namespace PropFlow.UnitTests;

public sealed partial class FacilityContinuousConversationTests
{
    [Fact]
    public Task Live_cards_only_show_the_semantic_selection_and_can_remove_rejected_suggestions() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        var library = f.Transport.Library;
        var gym = library with { Id = Guid.NewGuid(), Name = "Phòng gym người lớn" };
        f.Transport.LookupResult = new([Suggestion(gym), Suggestion(library)], "LOOKUP", "Hồ sơ BQL", null, DateTimeOffset.UtcNow);
        await f.Page.OnLiveTool(f.Epoch, "lookup-1", "Tìm nơi học tập hôm nay");
        Assert.Null(f.Get<FacilityRecommendationView?>("suggestions")); // Candidates must not appear as recommendations.
        var selected = await f.Page.OnLiveSelection(f.Epoch, "lookup-1", [library.Id.ToString()]);
        Assert.NotNull(selected);
        Assert.Equal(library.Id, Assert.Single(f.Get<FacilityRecommendationView>("suggestions").Suggestions).Facility.Id);
        Assert.DoesNotContain("Phòng gym người lớn", f.Html());
        await f.Page.OnLiveSelection(f.Epoch, "lookup-1", []);
        Assert.Empty(f.Get<FacilityRecommendationView>("suggestions").Suggestions);
        Assert.Equal("NO_MATCH", f.Get<FacilityRecommendationView>("suggestions").Mode);
    });

    [Fact]
    public Task Live_selection_rejects_invented_duplicate_oversized_stale_and_cancelled_choices() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice(); var epoch = f.Epoch;
        var library = f.Transport.Library;
        f.Transport.LookupResult = new([Suggestion(library)], "LOOKUP", "Hồ sơ BQL", null, DateTimeOffset.UtcNow);
        await f.Page.OnLiveTool(epoch, "lookup-1", "Tìm nơi học tập");
        foreach (var ids in new[] { new[] { Guid.NewGuid().ToString() }, new[] { "invalid" }, Enumerable.Repeat(library.Id.ToString(), 2).ToArray(), Enumerable.Repeat(library.Id.ToString(), 4).ToArray() })
        {
            await f.Page.OnLiveSelection(epoch, "lookup-1", ids);
            Assert.Null(f.Get<FacilityRecommendationView?>("suggestions"));
        }
        await f.Page.OnLiveTool(epoch, "lookup-2", "Tìm nơi đọc sách");
        await f.Page.OnLiveSelection(epoch, "lookup-1", [library.Id.ToString()]);
        Assert.Null(f.Get<FacilityRecommendationView?>("suggestions"));
        await f.Page.OnLiveToolCancelled(epoch, "lookup-2");
        await f.Page.OnLiveSelection(epoch, "lookup-2", [library.Id.ToString()]);
        Assert.Null(f.Get<FacilityRecommendationView?>("suggestions"));
        await f.Page.OnLiveTool(epoch, "lookup-3", "Tìm nơi đọc sách");
        await f.ClickMicrophone();
        await f.Page.OnLiveSelection(epoch, "lookup-3", [library.Id.ToString()]);
        Assert.Null(f.Get<FacilityRecommendationView?>("suggestions"));
    });
    private static FacilitySuggestion Suggestion(ResidentFacilityCard card) => new(card, [new(new(2026, 11, 23), new(8, 0), new(22, 0))], "Từ hồ sơ BQL");
    [Fact]
    public Task Empty_selection_preserves_clarification_and_starting_a_new_need_invalidates_old_choices() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        await f.Page.OnLiveTool(f.Epoch, "clarify", "Tôi muốn tìm nơi cho trẻ em");
        await f.Page.OnLiveSelection(f.Epoch, "clarify", []);
        Assert.Equal("CLARIFY", f.Get<FacilityRecommendationView>("suggestions").Mode);
        Assert.NotNull(f.Get<FacilityRecommendationView>("suggestions").FollowUp);
        var library = f.Transport.Library;
        f.Transport.LookupResult = new([Suggestion(library)], "LOOKUP", "Hồ sơ BQL", null, DateTimeOffset.UtcNow);
        await f.Page.OnLiveTool(f.Epoch, "lookup", "Tìm nơi đọc sách");
        await f.Call("NewNeed");
        await f.Page.OnLiveSelection(f.Epoch, "lookup", [library.Id.ToString()]);
        Assert.Null(f.Get<FacilityRecommendationView?>("suggestions"));
    });
    [Fact]
    public Task Completed_voice_farewell_stops_the_session_then_returns_to_resident_dashboard_once() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice(); var epoch = f.Epoch;
        await f.Page.OnLiveEnded(epoch); Assert.Equal(0, f.Navigation.Visits);
        await f.Page.OnLiveState(epoch, "ending");
        Assert.Equal(0, f.Navigation.Visits); Assert.True(f.Active);
        await f.Page.OnLiveEnded(epoch);
        Assert.False(f.Active); Assert.False(f.Listening); Assert.Equal("https://facility.test/resident", f.Navigation.Uri);
        Assert.Equal(1, f.Navigation.Visits); Assert.True(f.Player.Stops > 0);
        await f.Page.OnLiveEnded(epoch); Assert.Equal(1, f.Navigation.Visits);
    });
    [Fact]
    public Task Live_conversation_does_not_search_for_smalltalk_and_only_searches_when_model_calls_a_tool() => WithPage(async f =>
    {
        f.Player.LiveSupported = true;
        await f.InitializeVoice(); Assert.True(f.Active); Assert.Single(f.Player.Starts);
        await f.Call("Load"); Assert.Single(f.Player.Starts);
        await f.Page.OnVoiceResult(f.Epoch, "Không được dùng nhận giọng cũ trong Live", "");
        await f.Page.OnVoiceEnded(f.Epoch, ""); Assert.Empty(f.Transport.Needs);
        await f.Page.OnLiveState(f.Epoch, "listening");
        await f.Export("live-listening");
        await f.Page.OnLiveTranscript(f.Epoch, "user", "Alo alo");
        await f.Page.OnLiveTranscript(f.Epoch, "assistant", "Mình nghe bạn đây.");
        await f.Page.OnLiveState(f.Epoch, "speaking"); await f.Export("live-speaking");
        Assert.Empty(f.Transport.Needs); Assert.Equal(0, f.Player.Plays);
        var result = await f.Page.OnLiveTool(f.Epoch, "call-1", "Tôi muốn đọc sách yên tĩnh một mình hôm nay");
        Assert.NotNull(result); Assert.Single(f.Transport.Needs);
        Assert.EndsWith("/live/lookup", f.Transport.Paths.Last());
        var epoch = f.Epoch;
        await f.ClickMicrophone(); Assert.False(f.Active);
        await f.Page.OnLiveTool(epoch, "late-call", "Không được gọi sau khi kết thúc");
        Assert.Single(f.Transport.Needs);
    });

    [Fact]
    public Task Live_interruption_backgrounding_and_late_events_do_not_reactivate_capture() => WithPage(async f =>
    {
        f.Player.LiveSupported = true; await f.InitializeVoice();
        await f.Page.OnLiveState(f.Epoch, "speaking"); Assert.True(f.Speaking);
        await f.Page.OnLiveState(f.Epoch, "listening"); Assert.True(f.Listening);
        var epoch = f.Epoch;
        await f.Page.OnLiveState(epoch, "paused"); Assert.False(f.Active);
        await f.Page.OnLiveState(epoch, "listening"); Assert.False(f.Listening);
    });
    [Fact]
    public Task Resumed_voice_session_automatically_listens_after_each_reply_and_keeps_context() => WithPage(async f =>
    {
        Assert.Empty(f.Player.Starts);
        await f.ClickMicrophone(); Assert.True(f.Active); Assert.True(f.Listening);
        await f.Export("listening");
        await f.Say("Tôi muốn đọc sách một mình hôm nay");
        Assert.Single(f.Transport.Needs); Assert.False(f.Listening); Assert.True(f.Speaking);
        await f.Export("speaking");
        var epoch = f.Epoch;
        await f.Page.OnAssistantSpeech(epoch, "idle");
        Assert.True(f.Listening); Assert.Equal(2, f.Player.Starts.Count);
        await f.Say("Còn buổi tối thì sao");
        Assert.Equal("Tôi muốn đọc sách một mình hôm nay\nBổ sung: Còn buổi tối thì sao", f.Transport.Needs.Last());
        await f.Page.OnAssistantSpeech(f.Epoch, "idle"); Assert.True(f.Listening);
        Assert.Equal(3, f.Player.Starts.Count); Assert.Equal(2, f.Player.Plays);
    });

    [Fact]
    public Task Opening_amenities_starts_conversation_after_greeting_without_a_second_click() => WithPage(async f =>
    {
        await f.InitializeVoice();
        Assert.True(f.Active); Assert.True(f.Speaking); Assert.Empty(f.Player.Starts);
        await f.Page.OnAssistantSpeech(f.Epoch, "idle");
        Assert.True(f.Listening); Assert.Single(f.Player.Starts);
        await f.Say("Tôi muốn tập gym hôm nay");
        await f.Page.OnAssistantSpeech(f.Epoch, "idle");
        Assert.True(f.Listening); Assert.Equal(2, f.Player.Starts.Count);
        await f.ClickMicrophone();
        await f.Call("OnAfterRenderAsync", false);
        Assert.False(f.Active); Assert.Equal(2, f.Player.Starts.Count);
    });

    [Fact]
    public Task Slow_catalogue_still_starts_listening_once_ready_without_recording_the_greeting() => WithPage(async f =>
    {
        f.Set("catalogue", null);
        await f.InitializeVoice(); Assert.False(f.Active); Assert.Empty(f.Player.Starts);
        await f.Call("Load"); Assert.True(f.Active); Assert.Empty(f.Player.Starts);
        await f.Page.OnAssistantSpeech(f.Epoch, "idle"); Assert.True(f.Listening); Assert.Single(f.Player.Starts);
    });

    [Fact]
    public Task Microphone_is_closed_during_speech_generation_and_playback_to_prevent_own_voice_becoming_a_request() => WithPage(async f =>
    {
        await f.ClickMicrophone(); await f.Say("Tôi muốn đi dạo");
        Assert.False(f.Listening); Assert.True(f.Active);
        Assert.Single(f.Player.Starts);
        await f.Page.OnVoiceResult(f.Epoch, "Tiếng trả lời phát từ loa", "");
        Assert.Single(f.Transport.Needs);
        await f.Page.OnAssistantSpeech(f.Epoch, "idle"); Assert.Equal(2, f.Player.Starts.Count);
    });

    [Fact]
    public Task Ending_session_ignores_late_audio_and_recognition_callbacks_and_does_not_restart() => WithPage(async f =>
    {
        await f.ClickMicrophone(); await f.Say("Tìm phòng gym"); var retiredEpoch = f.Epoch;
        await f.ClickMicrophone(); Assert.False(f.Active); Assert.False(f.Listening);
        await f.Page.OnAssistantSpeech(retiredEpoch, "idle");
        await f.Page.OnVoiceResult(retiredEpoch, "Yêu cầu đến muộn", "");
        await f.Page.OnVoiceEnded(retiredEpoch, "");
        Assert.Single(f.Player.Starts); Assert.Single(f.Transport.Needs);
    });

    [Fact]
    public Task Stop_while_waiting_cancels_the_request_and_a_late_result_cannot_speak_or_resume() => WithPage(async f =>
    {
        f.Transport.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.ClickMicrophone(); await f.Page.OnVoiceResult(f.Epoch, "Tôi muốn đi bơi", "");
        var pending = f.Page.OnVoiceEnded(f.Epoch, "");
        await f.Transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(f.Busy); await f.ClickMicrophone();
        f.Transport.Pending.TrySetResult(); await pending;
        Assert.False(f.Active); Assert.False(f.Busy); Assert.Equal(0, f.Player.Plays);
        Assert.Single(f.Player.Starts); Assert.Contains("Tôi muốn đi bơi", f.Draft);
    });

    [Fact]
    public Task Backgrounding_while_AI_is_pending_cancels_processing_and_never_restarts_the_microphone() => WithPage(async f =>
    {
        f.Transport.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await f.ClickMicrophone(); await f.Page.OnVoiceResult(f.Epoch, "Tôi muốn đi dạo", "");
        var pending = f.Page.OnVoiceEnded(f.Epoch, "");
        await f.Transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await f.Page.OnAssistantSpeech(f.Epoch, "paused"); await pending;
        Assert.False(f.Active); Assert.False(f.Busy); Assert.False(f.Listening);
        Assert.Single(f.Player.Starts); Assert.Equal(0, f.Player.Plays);
    });

    [Fact]
    public Task Navigating_away_disposes_capture_and_ignores_late_events() => WithPage(async f =>
    {
        await f.ClickMicrophone(); var epoch = f.Epoch;
        await f.Page.DisposeAsync();
        await f.Page.OnVoiceResult(epoch, "Không được gửi nội dung sau khi rời trang", "");
        await f.Page.OnVoiceEnded(epoch, ""); await f.Page.OnAssistantSpeech(epoch, "idle");
        Assert.Empty(f.Transport.Needs); Assert.Single(f.Player.Starts); Assert.True(f.Player.Disposed);
    });

    [Fact]
    public Task Interrupt_button_cancels_reply_and_opens_a_new_utterance_without_resubmitting_the_previous_one() => WithPage(async f =>
    {
        await f.ClickMicrophone(); await f.Say("Tôi muốn đi bơi"); var old = f.Epoch;
        await f.Call("InterruptAndListen"); Assert.True(f.Active); Assert.True(f.Listening);
        Assert.Empty(f.Draft); Assert.Equal(2, f.Player.Starts.Count);
        await f.Page.OnAssistantSpeech(old, "idle"); Assert.Equal(2, f.Player.Starts.Count);
        await f.Say("Tôi đổi sang tập gym");
        Assert.Equal(2, f.Transport.Needs.Count);
        Assert.EndsWith("Bổ sung: Tôi đổi sang tập gym", f.Transport.Needs.Last());
    });

    [Theory]
    [InlineData("not-allowed")] [InlineData("audio-capture")] [InlineData("network")]
    public Task Recognition_errors_pause_session_without_submitting_partial_text_or_retrying_forever(string failure) => WithPage(async f =>
    {
        await f.ClickMicrophone(); var epoch = f.Epoch;
        await f.Page.OnVoiceResult(epoch, "", "Tôi muốn đi bơi");
        await f.Page.OnVoiceEnded(epoch, failure);
        Assert.False(f.Active); Assert.Empty(f.Transport.Needs); Assert.Single(f.Player.Starts);
        Assert.Contains("Tôi muốn đi bơi", f.Draft);
    });

    [Fact]
    public Task Hidden_page_or_blocked_audio_never_automatically_reopens_microphone() => WithPage(async f =>
    {
        await f.ClickMicrophone(); await f.Page.OnAssistantSpeech(f.Epoch, "paused");
        Assert.False(f.Active); Assert.False(f.Listening);
        await f.ClickMicrophone(); await f.Say("Đi dạo một mình");
        await f.Page.OnAssistantSpeech(f.Epoch, "playback-blocked");
        Assert.False(f.Active); Assert.False(f.Listening); Assert.True(f.Get<bool>("audioEnabled"));
    });

    [Fact]
    public Task Muting_keeps_voice_input_continuous_and_speech_API_failure_keeps_text_available() => WithPage(async f =>
    {
        await f.ClickMicrophone(); await f.Say("Đọc sách hôm nay");
        await f.Call("ToggleAudio"); Assert.True(f.Listening); Assert.False(f.Speaking);
        await f.Say("Ngày mai thì sao"); Assert.True(f.Listening); Assert.Equal(1, f.Player.Plays);
        Assert.Equal(2, f.Transport.Needs.Count);
    });

    [Fact]
    public Task Unavailable_speech_resumes_listening_without_disabling_the_audio_preference() => WithPage(async f =>
    {
        f.Transport.SpeechUnavailable = true;
        await f.ClickMicrophone(); await f.Say("Tôi muốn tập thể dục");
        Assert.True(f.Active); Assert.True(f.Listening); Assert.True(f.Get<bool>("audioEnabled"));
        Assert.Equal(0, f.Player.Plays); Assert.NotNull(f.Get<object?>("suggestions"));
    });

    [Fact]
    public Task Only_interim_transcript_is_preserved_at_turn_end_and_duplicate_end_does_not_send_twice() => WithPage(async f =>
    {
        await f.ClickMicrophone(); var epoch = f.Epoch;
        await f.Page.OnVoiceResult(epoch, "Tôi muốn", "đọc sách hôm nay");
        await f.Page.OnVoiceEnded(epoch, ""); await f.Page.OnVoiceEnded(epoch, "");
        Assert.Equal("Tôi muốn đọc sách hôm nay", Assert.Single(f.Transport.Needs));
    });

    [Fact]
    public Task Silent_fallback_sessions_use_the_same_countdown_instead_of_stopping_before_sixty_seconds() => WithPage(async f =>
    {
        var clock = new IdleTestClock();
        f.Set("idleWatch", new PropFlow.Web.Client.Features.Resident.Portal.Services.ResidentAssistantIdleWatch(clock));
        await f.ClickMicrophone();
        for (var i = 0; i < 3; i++) await f.Page.OnVoiceEnded(f.Epoch, "no-speech");
        Assert.True(f.Active); Assert.True(f.Listening); Assert.Empty(f.Transport.Needs);
        clock.Advance(60); await f.Call("CheckIdleTimeout", f.Get<int>("idleGeneration"));
        Assert.Contains("Bạn còn muốn trò chuyện không?", WebUtility.HtmlDecode(f.Html()));
        clock.Advance(30); await f.Call("CheckIdleTimeout", f.Get<int>("idleGeneration"));
        Assert.False(f.Active); Assert.Equal(1, f.Navigation.Visits);
    });

    private static async Task WithPage(Func<Fixture, Task> action)
    {
        using var transport = new Responses(); using var http = new HttpClient(transport) { BaseAddress = new("https://facility.test/") };
        var capture = new Capture(); var player = new Player(); var navigation = new FacilityTestNavigation();
        await using var services = new ServiceCollection().AddLogging().AddSingleton(new AuthenticatedApiClient(http, NullLogger<ApiClient>.Instance))
            .AddSingleton<NavigationManager>(navigation).AddSingleton<FacilityRecommendationsApi>().AddResidentAssistant().AddSingleton<IJSRuntime>(new Browser(player)).AddSingleton<IComponentActivator>(capture).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<ResidentFacilities>(ParameterView.Empty);
            var f = new Fixture(capture.Page!, player, transport, () => rendered.ToHtmlString(), navigation);
            f.Set("voice", player); f.Set("voiceSupported", true); f.Set("speechSupported", true);
            await action(f);
        });
    }
    private sealed record Fixture(ResidentFacilities Page, Player Player, Responses Transport, Func<string> Html, FacilityTestNavigation Navigation)
    {
        public bool Active => Get<bool>("conversationActive"); public bool Listening => Get<bool>("listening");
        public bool Speaking => Get<bool>("speaking"); public bool Busy => Get<bool>("busy"); public int Epoch => Get<int>("voiceEpoch"); public string Draft => Get<string>("need");
        public T Get<T>(string name) => (T)typeof(ResidentFacilities).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Page)!;
        public void Set(string name, object? value) => typeof(ResidentFacilities).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Page, value);
        public Task Call(string name, params object?[] args) => (Task)typeof(ResidentFacilities).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, args)!;
        public Task CallSync(string name, params object?[] args) { typeof(ResidentFacilities).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page, args); return Task.CompletedTask; }
        public Task InitializeVoice() => Call("OnAfterRenderAsync", true);
        public Task ClickMicrophone() => Call("ToggleMicrophone");
        public async Task Say(string text) { var epoch = Epoch; await Page.OnVoiceResult(epoch, text, ""); await Page.OnVoiceEnded(epoch, ""); }
        public async Task Export(string state)
        {
            var directory = Environment.GetEnvironmentVariable("PROPFLOW_FACILITY_PREVIEW_DIR");
            if (directory is not null) await File.WriteAllTextAsync(Path.Combine(directory, $"propflow-facility-{state}.html"), Html());
        }
    }
    private sealed class Capture : IComponentActivator
    { public ResidentFacilities? Page; public IComponent CreateInstance(Type type) { var component = (IComponent)Activator.CreateInstance(type)!; if (component is ResidentFacilities page) Page = page; return component; } }
    private sealed class Browser(Player player) : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => ValueTask.FromResult((T)(object)new Module(player, args?[0]?.ToString()?.Contains("facility-live-voice") == true));
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args) => InvokeAsync<T>(name, args);
    }
    private sealed class Module(Player player, bool live) : IJSObjectReference
    {
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => ValueTask.FromResult(name == "capabilities"
            ? live ? (T)(object)player.LiveSupported : JsonSerializer.Deserialize<T>("{\"Recognition\":true,\"Speech\":true}")! : (T)(object)player);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args) => InvokeAsync<T>(name, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Player : IJSObjectReference
    {
        public List<int> Starts { get; } = []; public int Plays, Stops; public bool LiveSupported;
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args)
        { if (name == "start") Starts.Add((int)args![args.Length > 1 ? 1 : 0]!); if (name is "play" or "playGreeting") Plays++; if (name == "stop") Stops++; return ValueTask.FromResult(typeof(T) == typeof(bool) ? (T)(object)true : default!); }
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken ct, object?[]? args) => InvokeAsync<T>(name, args);
        public bool Disposed;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class Responses : HttpMessageHandler
    {
        public ResidentServiceRequestDraftReview DraftReview = new(true, []);
        public List<CreateResidentServiceRequestRequest> Submissions { get; } = [];
        public TaskCompletionSource? PendingSubmission;
        public bool RejectSubmission;
        public ResidentServiceRequestCreatedResponse Receipt = new(Guid.NewGuid(), "YC-TEST-CONFIRMED", "SUBMITTED", DateTimeOffset.UtcNow);
        public ResidentChatReply Documents = new("DOCUMENTS_ONLY", "Nguồn BQL", [new(1, Guid.NewGuid(), 2, 0, "Nội quy sử dụng tiện ích", "Không gây tiếng ồn sau 22 giờ. <script>unsafe</script>")]);
        public ResidentFacilityCard Library = new(Guid.NewGuid(), "Thư viện", "Đọc sách", "Tầng 1", "Nơi đọc sách", "ALL", null, null, "GUARDIAN_REQUIRED", "", "", [], [], "BQL", "Quầy BQL");
        public FacilityRecommendationView? LookupResult;
        public List<string> Needs { get; } = []; public List<string> Paths { get; } = []; public bool SpeechUnavailable;
        public TaskCompletionSource? Pending; public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            object payload;
            if (request.RequestUri.AbsolutePath.Contains("/chatbot/sources/"))
                payload = new PublishedKnowledgeSource(Documents.Sources[0].DocumentId, 2, "Nội quy", "Toàn văn nội quy <script>unsafe</script>");
            else if (request.RequestUri.AbsolutePath.EndsWith("/chatbot/lookup")) payload = Documents;
            else if (request.RequestUri.AbsolutePath.EndsWith("/draft-check")) payload = DraftReview;
            else if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath.EndsWith("/service-requests"))
            {
                Submissions.Add((await request.Content!.ReadFromJsonAsync<CreateResidentServiceRequestRequest>(ct))!);
                if (PendingSubmission is not null) await PendingSubmission.Task.WaitAsync(ct);
                if (RejectSubmission) return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { title = "Chưa xác nhận được kết quả gửi" }) };
                payload = Receipt;
            }
            else if (request.Method == HttpMethod.Get)
            {
                payload = new ResidentFacilityCatalogue([Library], new(2026, 11, 22));
            }
            else if (request.RequestUri!.AbsolutePath.EndsWith("/live"))
            {
                payload = new FacilityLiveSession("auth_tokens/test", "wss://generativelanguage.googleapis.com/ws/test", JsonSerializer.SerializeToElement(new { model = "models/test" }), DateTimeOffset.UtcNow.AddMinutes(10));
            }
            else if (request.RequestUri!.AbsolutePath.EndsWith("/speech"))
            {
                if (SpeechUnavailable) return new(HttpStatusCode.ServiceUnavailable) { Content = JsonContent.Create(new { title = "Giọng đọc chưa sẵn sàng" }) };
                payload = new FacilitySpeechAudio("UklGRg==", "audio/wav");
            }
            else
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Needs.Add(json.RootElement.GetProperty("need").GetString()!); Started.TrySetResult();
                if (Pending is not null) await Pending.Task.WaitAsync(ct);
                payload = LookupResult ?? new FacilityRecommendationView([], "CLARIFY", "Cần hiểu thêm", "Bạn thích hoạt động gì?", DateTimeOffset.UtcNow, Guid.NewGuid());
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(payload, payload.GetType()) };
        }
    }
}
