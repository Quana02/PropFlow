using System.Security.Claims;

namespace PropFlow.Modules.AiChatbot.Contracts;

public static class ResidentChatPolicy
{
    public const string Use = "chatbot.resident";
    public const string RateLimit = "chatbot-ask";
    public static bool Allowed(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true &&
        user.IsInRole("RESIDENT") && user.HasClaim("permission", "USE_RESIDENT_SERVICES");
}
public sealed record AskResidentChat(string Question);
public sealed record ChatCitation(int Number, Guid DocumentId, int Revision, int Chunk, string Title, string Excerpt);
public sealed record ResidentChatReply(string Mode, string Answer, IReadOnlyList<ChatCitation> Sources);
public sealed record ResidentChatStatus(bool AiConfigured);
public sealed record KnowledgeIndexStatus(bool AiConfigured, int Chunks, int EmbeddedChunks);
