using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.AiRecommendation.Application.FacilityDiscovery;

namespace PropFlow.Modules.AiRecommendation.Infrastructure.FacilityDiscovery;

public sealed class GeminiFacilitySpeech(HttpClient http, string apiKey, string model, bool enabled, ILogger<GeminiFacilitySpeech>? logger = null) : IFacilitySpeechProvider
{
    public async Task<byte[]?> GenerateAsync(string transcript, CancellationToken ct)
    {
        if (!enabled || string.IsNullOrWhiteSpace(apiKey)) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(new {
            contents = new[] { new { parts = new[] { new { text = "Đọc nguyên văn phần văn bản dưới đây bằng tiếng Việt chuẩn, rõ ràng, tự nhiên, tốc độ vừa phải. Không dịch sang tiếng Anh, không đọc chỉ dẫn, không thêm nội dung:\n\n" + transcript } } } },
            generationConfig = new { responseModalities = new[] { "AUDIO" },
                speechConfig = new { voiceConfig = new { prebuiltVoiceConfig = new { voiceName = "Kore" } } } }
        });
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            if(json.RootElement.TryGetProperty("usageMetadata",out var usage))
                logger?.LogInformation("Vietnamese speech model {Model}: input tokens {Input}, output tokens {Output}",model,Tokens(usage,"promptTokenCount"),Tokens(usage,"candidatesTokenCount"));
            var candidates = json.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() != 1 || candidates[0].GetProperty("finishReason").GetString() != "STOP") return null;
            var parts = candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray()
                .Where(x => x.TryGetProperty("inlineData", out _)).ToArray();
            if (parts.Length != 1) return null;
            var data = parts[0].GetProperty("inlineData");
            var mime = data.GetProperty("mimeType").GetString()?.Replace(" ", "").ToLowerInvariant();
            if (mime is not ("audio/l16;rate=24000" or "audio/l16;rate=24000;channels=1")) return null;
            var encoded = data.GetProperty("data").GetString();
            if (encoded is null || encoded.Length > 4_000_000) return null;
            var pcm = Convert.FromBase64String(encoded);
            if (pcm.Length < 2 || pcm.Length % 2 != 0) return null;
            var wav = new byte[pcm.Length + 44];
            "RIFF"u8.CopyTo(wav); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
            "WAVEfmt "u8.CopyTo(wav.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
            BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 24000); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 48000);
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
            "data"u8.CopyTo(wav.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), pcm.Length);
            pcm.CopyTo(wav, 44); return wav;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return null; }
    }
    private static int? Tokens(JsonElement value,string name)=>value.TryGetProperty(name,out var tokens)&&tokens.TryGetInt32(out var count)&&count>=0?count:null;
}
