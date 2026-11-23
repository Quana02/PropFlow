using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.UnitTests;

[Trait("Feature", "FacilityDiscovery-Task2-LiveVoice")]
public sealed class FacilityLiveSessionTests
{
    [Fact]
    public async Task Token_is_single_use_short_lived_and_locks_instructions_tools_audio_and_output_budget()
    {
        using var handler = new TokenHandler(); using var http = new HttpClient(handler) { BaseAddress = new("https://provider.test/v1beta/") };
        var options = new FacilityLiveOptions();
        var provider = new GeminiFacilityLive(http, "server-only-key", true, options, NullLogger<GeminiFacilityLive>.Instance);
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        var session = await provider.CreateAsync(new(2026, 11, 22, 9, 30, 0), expires, default);
        Assert.NotNull(session); Assert.Equal("auth_tokens/test-token", session.Token);
        Assert.Equal("server-only-key", handler.Key); Assert.Equal("/v1beta/auth_tokens", handler.Path);
        using var body = JsonDocument.Parse(handler.Body!); var root = body.RootElement;
        Assert.Equal(1, root.GetProperty("uses").GetInt32()); Assert.Equal(expires, root.GetProperty("expireTime").GetDateTimeOffset());
        Assert.Equal(expires.AddMinutes(-9), root.GetProperty("newSessionExpireTime").GetDateTimeOffset());
        var setup = root.GetProperty("bidiGenerateContentSetup");
        Assert.False(root.TryGetProperty("fieldMask", out _)); // The complete setup is locked by the provider.
        Assert.Equal(512, setup.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
        Assert.Equal("models/gemini-3.1-flash-live-preview", setup.GetProperty("model").GetString());
        Assert.False(setup.GetProperty("generationConfig").TryGetProperty("thinkingConfig", out _)); // 3.1 Live defaults to minimal thinking.
        Assert.Equal(400, setup.GetProperty("realtimeInputConfig").GetProperty("automaticActivityDetection").GetProperty("silenceDurationMs").GetInt32());
        Assert.Equal("find_facilities", setup.GetProperty("tools")[0].GetProperty("functionDeclarations")[0].GetProperty("name").GetString());
        Assert.Equal("end_conversation", setup.GetProperty("tools")[0].GetProperty("functionDeclarations")[1].GetProperty("name").GetString());
        var selection = setup.GetProperty("tools")[0].GetProperty("functionDeclarations")[2];
        Assert.Equal("show_facilities", selection.GetProperty("name").GetString());
        Assert.Equal(new[] { "lookupId", "facilityIds" }, selection.GetProperty("parameters").GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        var tools = setup.GetProperty("tools")[0].GetProperty("functionDeclarations").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToArray();
        Assert.Contains("find_documents", tools); Assert.Contains("prepare_request", tools); Assert.Contains("submit_request", tools);
        Assert.Contains("2026-11-22 09:30:00", setup.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.DoesNotContain("server-only-key", JsonSerializer.Serialize(session));
        Assert.DoesNotContain("resident", handler.Body!, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task Condominium_scope_is_part_of_the_server_locked_live_setup_and_preserves_greetings_and_legitimate_amenity_needs()
    {
        using var handler = new TokenHandler(); using var http = new HttpClient(handler) { BaseAddress = new("https://provider.test/v1beta/") };
        var provider = new GeminiFacilityLive(http, "server-only-key", true, new(), NullLogger<GeminiFacilityLive>.Instance);
        var session = await provider.CreateAsync(DateTime.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), default);
        Assert.NotNull(session);
        using var body = JsonDocument.Parse(handler.Body!);
        var locked = body.RootElement.GetProperty("bidiGenerateContentSetup");
        Assert.False(body.RootElement.TryGetProperty("fieldMask", out _));
        Assert.Equal(session.Setup.GetRawText(), locked.GetRawText());
        var instructions = locked.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("PHẠM VI BẮT BUỘC", instructions);
        Assert.Contains("không theo từ khóa", instructions);
        Assert.Contains("Không trả lời, giải bài", instructions);
        Assert.Contains("Không gọi find_facilities để xử lý câu hỏi ngoài phạm vi", instructions);
        Assert.Contains("Chào hỏi, thử micro, cảm ơn, tạm biệt", instructions);
        Assert.Contains("có thể tìm khu đọc sách yên tĩnh để học toán", instructions);
        Assert.Contains("chỉ xử lý phần liên quan chung cư", instructions);
        Assert.Contains("gọi end_conversation trước", instructions);
        Assert.Contains("không đủ ý định kết thúc", instructions);
        Assert.Contains("find_facilities chỉ trả ứng viên", instructions);
        Assert.Contains("bắt buộc gọi show_facilities", instructions);
        Assert.Contains("Không chỉ xin lỗi bằng lời rồi giữ các thẻ sai", instructions);
    }
    [Fact]
    public async Task Unavailable_or_invalid_token_response_never_exposes_provider_errors()
    {
        using var handler = new TokenHandler { Status = HttpStatusCode.TooManyRequests };
        using var http = new HttpClient(handler) { BaseAddress = new("https://provider.test/v1beta/") };
        var provider = new GeminiFacilityLive(http, "key", true, new(), NullLogger<GeminiFacilityLive>.Instance);
        Assert.Null(await provider.CreateAsync(DateTime.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), default));
        handler.Status = HttpStatusCode.OK; handler.Token = "not-an-ephemeral-token";
        Assert.Null(await provider.CreateAsync(DateTime.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), default));
    }
    [Fact]
    public async Task Disabled_provider_and_inactive_resident_cannot_create_sessions()
    {
        await using var f = new Fixture(); f.Access.Active = false;
        var failure = await Assert.ThrowsAsync<FacilityRecommendationFailure>(() => f.Service.CreateAsync(Guid.NewGuid(), default));
        Assert.Equal(403, failure.Status); Assert.Equal(0, f.Provider.Calls);
        f.Access.Active = true; f.Provider.Ready = false;
        failure = await Assert.ThrowsAsync<FacilityRecommendationFailure>(() => f.Service.CreateAsync(Guid.NewGuid(), default));
        Assert.Equal(503, failure.Status); Assert.Empty(f.Db.FacilityRecommendationRuns);
    }
    [Fact]
    public async Task Session_quota_uses_existing_ledger_and_prevents_reconnect_bursts()
    {
        await using var f = new Fixture(); var actor = Guid.NewGuid();
        var session = await f.Service.CreateAsync(actor, default);
        Assert.Equal(f.Clock.GetUtcNow().AddMinutes(10), session.ExpiresAt);
        Assert.Equal(9, f.Provider.Local.Hour); // UTC+7 current-building clock, not server clock.
        var failure = await Assert.ThrowsAsync<FacilityRecommendationFailure>(() => f.Service.CreateAsync(actor, default));
        Assert.Equal(429, failure.Status); Assert.Equal(1, f.Provider.Calls);
        f.Quota.AiRequestsPerDay = 1;
        failure = await Assert.ThrowsAsync<FacilityRecommendationFailure>(() => f.Service.CreateAsync(Guid.NewGuid(), default));
        Assert.Equal(429, failure.Status);
    }
    [Fact]
    public async Task Access_is_checked_again_before_returning_a_connection_credential()
    {
        await using var f = new Fixture(); f.Provider.During = () => f.Access.Active = false;
        var failure = await Assert.ThrowsAsync<FacilityRecommendationFailure>(() => f.Service.CreateAsync(Guid.NewGuid(), default));
        Assert.Equal(403, failure.Status);
    }
    private sealed class TokenHandler : HttpMessageHandler
    {
        public string? Body, Path, Key; public string Token = "auth_tokens/test-token"; public HttpStatusCode Status = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct); Path = request.RequestUri!.AbsolutePath; Key = request.Headers.GetValues("x-goog-api-key").Single();
            return new(Status) { Content = JsonContent.Create(new { name = Token }) };
        }
    }
    private sealed class Access : IFacilityResidentAccess { public bool Active = true; public Task<bool> ActiveAsync(Guid actor, CancellationToken ct) => Task.FromResult(Active); }
    private sealed class Zone : ICurrentBuildingTimeZone { public Task<string> GetAsync(CancellationToken ct) => Task.FromResult("Asia/Ho_Chi_Minh"); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 22, 2, 0, 0, TimeSpan.Zero); }
    private sealed class Provider : IFacilityLiveProvider
    {
        public bool Ready { get; set; } = true; public int Calls; public DateTime Local; public Action? During;
        public Task<FacilityLiveSession?> CreateAsync(DateTime local, DateTimeOffset expires, CancellationToken ct)
        { Calls++; Local = local; During?.Invoke(); return Task.FromResult<FacilityLiveSession?>(new("auth_tokens/test", "wss://provider.test/", JsonSerializer.SerializeToElement(new { }), expires)); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly AiRecommendationDbContext Db = new(new DbContextOptionsBuilder<AiRecommendationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Access Access = new(); public readonly Provider Provider = new(); public readonly Clock Clock = new(); public readonly FacilityRecommendationOptions Quota = new();
        public FacilityLiveSessions Service => new(Db, Access, new Zone(), Clock, Provider, new(), Quota, NullLogger<FacilityLiveSessions>.Instance);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
