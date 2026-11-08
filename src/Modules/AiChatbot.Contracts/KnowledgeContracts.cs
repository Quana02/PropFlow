using System.Security.Claims;

namespace PropFlow.Modules.AiChatbot.Contracts;

public static class KnowledgePolicies
{
    public const string Read = "knowledge.read";
    public const string Manage = "knowledge.manage";
    public static bool CanManage(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true &&
        ((user.IsInRole("ADMIN") && user.HasClaim("permission", "MANAGE_INTERNAL_ACCOUNTS")) ||
         (user.IsInRole("MANAGER") && user.HasClaim("permission", "MANAGE_OPERATIONS")));
    public static bool CanRead(ClaimsPrincipal user) => CanManage(user) ||
        (user.Identity?.IsAuthenticated == true && user.IsInRole("STAFF") &&
         user.HasClaim("permission", "PERFORM_ASSIGNED_OPERATIONS"));
}

public sealed record SaveKnowledgeDocument(string Title, string Content, string Audience, Guid? ExpectedToken);
public sealed record ChangeKnowledgePublication(Guid ExpectedToken);
public sealed record KnowledgeDocumentSummary(Guid Id, string Title, string Audience, int Revision,
    int? PublishedRevision, Guid Token, DateTimeOffset UpdatedAt);
public sealed record KnowledgeDocumentPage(IReadOnlyList<KnowledgeDocumentSummary> Items, int Total, int Page, int PageSize);
public sealed record KnowledgeRevisionView(int Number, string Title, string Content, string Audience, DateTimeOffset CreatedAt);
public sealed record KnowledgeAuditView(string Action, int Revision, DateTimeOffset CreatedAt);
public sealed record KnowledgeDocumentDetail(KnowledgeDocumentSummary Document, KnowledgeRevisionView Revision,
    IReadOnlyList<KnowledgeRevisionView> History, IReadOnlyList<KnowledgeAuditView> Activity);

// Future RAG ingestion must use this port, never an unrestricted document query.
public sealed record PublishedKnowledgeSource(Guid DocumentId, int Revision, string Title, string Content);
public interface IPublishedKnowledgeSource
{
    Task<PublishedKnowledgeSource?> FindResidentSourceAsync(Guid documentId, CancellationToken ct);
}
