using PropFlow.Modules.AiChatbot.Contracts;

namespace PropFlow.Modules.AiChatbot.Application;

public interface IKnowledgeDocuments
{
    Task<KnowledgeDocumentPage> ListAsync(bool manage, string? search, int page, CancellationToken ct);
    Task<KnowledgeDocumentDetail?> GetAsync(Guid id, bool manage, CancellationToken ct);
    Task<Guid> SaveAsync(Guid? id, SaveKnowledgeDocument request, Guid actor, CancellationToken ct);
    Task SetPublicationAsync(Guid id, Guid expectedToken, bool publish, Guid actor, CancellationToken ct);
    Task<KnowledgeIndexStatus?> IndexStatusAsync(Guid id, CancellationToken ct);
}

public sealed class KnowledgeConflictException : Exception;
public sealed class KnowledgeNotFoundException : Exception;
