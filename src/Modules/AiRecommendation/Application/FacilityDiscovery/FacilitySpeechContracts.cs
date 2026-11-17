namespace PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;

public sealed record FacilitySpeechRequest(Guid? SpeechId = null, string Kind = "WELCOME");
public sealed record FacilitySpeechAudio(string Audio, string ContentType = "audio/wav");
public interface IFacilitySpeechProvider
{
    Task<byte[]?> GenerateAsync(string transcript, CancellationToken ct);
}
public sealed class FacilitySpeechOptions
{
    public string Model { get; set; } = "gemini-3.1-flash-tts-preview";
    public int PerResidentHour { get; set; } = 20;
    public int PerDay { get; set; } = 200;
}
