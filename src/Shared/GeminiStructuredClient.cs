using System.Net.Http.Json;
using System.Text.Json;

namespace PropFlow.Shared;

public sealed record GeminiStructuredResponse<T>(T Value, int? InputTokens, int? OutputTokens, int? TotalTokens);

public sealed class GeminiStructuredClient(HttpClient client, string apiKey, string model, bool enabled)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public bool Ready => enabled && !string.IsNullOrWhiteSpace(apiKey);
    public string Model => model;

    public async Task<GeminiStructuredResponse<T>?> GenerateAsync<T>(string instructions, object input, object schema, CancellationToken ct)
    {
        if (!Ready) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = instructions } } },
            contents = new[] { new { role = "user", parts = new[] { new { text = JsonSerializer.Serialize(input, JsonOptions) } } } },
            generationConfig = new { responseMimeType = "application/json", responseSchema = schema, maxOutputTokens = 900 }
        });

        try
        {
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            using var payload = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var root = payload.RootElement;
            var candidates = root.GetProperty("candidates");
            if (candidates.GetArrayLength() != 1 || candidates[0].GetProperty("finishReason").GetString() != "STOP") return null;
            var parts = candidates[0].GetProperty("content").GetProperty("parts").EnumerateArray();
            var text = string.Concat(parts.Where(part => !part.TryGetProperty("thought", out var thought) || thought.ValueKind == JsonValueKind.False)
                .Where(part => part.TryGetProperty("text", out _)).Select(part => part.GetProperty("text").GetString()));
            var value = JsonSerializer.Deserialize<T>(text, JsonOptions);
            root.TryGetProperty("usageMetadata", out var usage);
            return value is null ? null : new(value, ReadTokens(usage, "promptTokenCount"), ReadTokens(usage, "candidatesTokenCount"), ReadTokens(usage, "totalTokenCount"));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return null; }
    }

    private static int? ReadTokens(JsonElement usage, string property) =>
        usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : null;
}
