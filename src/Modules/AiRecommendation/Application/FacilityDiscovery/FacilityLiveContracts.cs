using System.Text.Json;

namespace PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;

// User-approved resident voice assistance; FE-05 still owns submission after explicit resident consent.
public sealed record FacilityLiveSession(string Token, string WebSocketUrl, JsonElement Setup, DateTimeOffset ExpiresAt);
public interface IFacilityLiveProvider
{
    bool Ready { get; }
    Task<FacilityLiveSession?> CreateAsync(DateTime localNow, DateTimeOffset expiresAt, CancellationToken ct);
}
public sealed class FacilityLiveOptions
{
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "gemini-3.1-flash-live-preview";
    public string WebSocketUrl { get; set; } = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContentConstrained";
    public int SessionMinutes { get; set; } = 10;
    public int MaxOutputTokens { get; set; } = 512;
    public int EndOfSpeechSilenceMs { get; set; } = 400;
    public int? ThinkingBudget { get; set; } = 0;
}
