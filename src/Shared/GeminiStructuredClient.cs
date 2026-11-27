using System.Net.Http.Json;
using System.Text.Json;

namespace PropFlow.Shared;

public sealed record GeminiStructuredResponse<T>(T Value, int? InputTokens, int? OutputTokens, int? TotalTokens);
public sealed record GeminiMediaResponse<T>(GeminiStructuredResponse<T>? Result, int? StatusCode, bool DailyQuotaExceeded = false, TimeSpan? RetryAfter = null);

public sealed class GeminiStructuredClient(HttpClient client, string apiKey, string model, bool enabled)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public bool Ready => enabled && !string.IsNullOrWhiteSpace(apiKey);
    public string Model => model;

    public Task<GeminiStructuredResponse<T>?> GenerateAsync<T>(string instructions, object input, object schema, CancellationToken ct) =>
        GenerateCoreAsync<T>(instructions, [new { text = JsonSerializer.Serialize(input, JsonOptions) }], schema, 900, ct);

    public async Task<GeminiMediaResponse<T>> GenerateMediaAsync<T>(string instructions, string caption, string mimeType,
        byte[] content, object schema, int maxOutputTokens, CancellationToken ct, string? thinkingLevel = null)
    {
        int? status = null;var dailyQuota = false;TimeSpan? retryAfter = null;
        var result = await GenerateCoreAsync<T>(instructions, [new { text = caption }, new { inlineData = new { mimeType, data = Convert.ToBase64String(content) } }],
            schema, Math.Clamp(maxOutputTokens, 900, 32768), ct, (code, daily, delay) => { status = code; dailyQuota = daily; retryAfter = delay; }, thinkingLevel);
        return new(result, status, dailyQuota, retryAfter);
    }

    private async Task<GeminiStructuredResponse<T>?> GenerateCoreAsync<T>(string instructions, object[] inputParts, object schema, int maxOutputTokens, CancellationToken ct, Action<int, bool, TimeSpan?>? received = null, string? thinkingLevel = null)
    {
        if (!Ready) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        var generation = new Dictionary<string, object> { ["responseMimeType"] = "application/json", ["responseSchema"] = schema, ["maxOutputTokens"] = maxOutputTokens };
        if (thinkingLevel is not null)
        {
            if (thinkingLevel is not ("LOW" or "MEDIUM" or "HIGH" or "XHIGH")) throw new ArgumentOutOfRangeException(nameof(thinkingLevel));
            generation["thinkingConfig"] = new { thinkingLevel };
        }
        request.Content = JsonContent.Create(new
        {
            systemInstruction = new { parts = new[] { new { text = instructions } } },
            contents = new[] { new { role = "user", parts = inputParts } },
            generationConfig = generation
        });

        try
        {
            using var response = await client.SendAsync(request, ct);
            var retryHeader = response.Headers.RetryAfter;
            var retryAfter = retryHeader?.Delta ?? (retryHeader?.Date is {} date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
            if (retryAfter < TimeSpan.Zero) retryAfter = TimeSpan.Zero;
            received?.Invoke((int)response.StatusCode, false, retryAfter);
            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 429 && received is not null)
                {
                    using var error = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
                    if (error.RootElement.TryGetProperty("error", out var detail) && detail.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                        foreach (var entry in details.EnumerateArray())
                            if (entry.TryGetProperty("violations", out var violations) && violations.ValueKind == JsonValueKind.Array && violations.EnumerateArray().Any(v => v.TryGetProperty("quotaId", out var id) && id.ValueKind == JsonValueKind.String && id.GetString()!.Contains("PerDay", StringComparison.OrdinalIgnoreCase)))
                                received(429, true, retryAfter);
                }
                return null;
            }
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
