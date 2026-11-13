using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using PropFlow.Modules.AiChatbot.Domain;
using PropFlow.Modules.ServiceRequests.Domain.ServiceRequests;

namespace PropFlow.Api.Composition;

internal static partial class ServiceRequestAiInput
{
    internal static bool CanReview(ServiceRequestStatus status) => status is ServiceRequestStatus.SUBMITTED
        or ServiceRequestStatus.UNDER_REVIEW or ServiceRequestStatus.ASSIGNED or ServiceRequestStatus.IN_PROGRESS;

    internal static string Hash(params string?[] values) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));

    internal static string Clean(string text, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name)) text = text.Replace(name, "[đã ẩn]", StringComparison.OrdinalIgnoreCase);
        return PrivateReferences().Replace(KnowledgeText.Redact(text), "[đã ẩn]").Trim();
    }

    [GeneratedRegex(@"https?://\S+|\b(?:căn\s*hộ|phòng|số\s*nhà|cccd|cmnd|otp|mật\s*khẩu)\s*[:#-]?\s*[\p{L}\d.-]*\d[\p{L}\d.-]*", RegexOptions.IgnoreCase)]
    private static partial Regex PrivateReferences();
}
