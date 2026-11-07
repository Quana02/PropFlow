using System.Text.RegularExpressions;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;

namespace PropFlow.Modules.AiChatbot.Application;

public sealed record RetrievedPassage(Guid DocumentId, int Revision, int Chunk, string Title, string Content);
public sealed record GroundedAnswer(bool Supported, string Answer, int[] Citations);
public enum ChatEmbeddingPurpose { Document, Query }
public interface IChatAi
{
    bool Ready { get; }
    string EmbeddingKey { get; }
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct, ChatEmbeddingPurpose purpose = ChatEmbeddingPurpose.Document);
    Task<GroundedAnswer?> AnswerAsync(string question, IReadOnlyList<RetrievedPassage> passages, CancellationToken ct);
}
public interface IResidentKnowledgeSearch
{
    Task<IReadOnlyList<RetrievedPassage>> SearchAsync(string question, float[]? embedding, string embeddingKey, CancellationToken ct);
    Task<bool> CurrentAsync(IReadOnlyList<RetrievedPassage> passages, CancellationToken ct);
    Task<PublishedKnowledgeSource?> SourceAsync(Guid id, int revision, CancellationToken ct);
}
public interface IChatResidentScope { Task<bool> ActiveAsync(Guid actor, CancellationToken ct); }
public sealed class ChatAccessException : Exception;
public sealed class ChatAiUnavailableException : Exception;

public sealed partial class ResidentChat(IResidentKnowledgeSearch search, IChatAi ai, IChatResidentScope scope)
{
    public async Task<ResidentChatReply> AskAsync(Guid actor, string? question, CancellationToken ct)
    {
        await EnsureAccess(actor, ct);
        question = (question ?? "").Trim();
        if (question.Length is < 2 or > 1000 || question.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
            throw new KnowledgeValidationException("Nhập câu hỏi từ 2 đến 1.000 ký tự.");
        var normalized = KnowledgeText.Search(question);
        if (PersonalFinance().IsMatch(normalized))
            return new("GUIDANCE", "Bạn hãy mở mục Hóa đơn và thanh toán để xem thông tin chính xác của mình. Trợ lý tài liệu chưa tra cứu hóa đơn, công nợ hoặc giao dịch cá nhân.", []);
        var safeQuestion = KnowledgeText.Redact(question);
        float[]? vector = null;
        if (ai.Ready)
        {
            try { vector = (await ai.EmbedAsync([safeQuestion], ct, ChatEmbeddingPurpose.Query)).Single(); }
            catch (ChatAiUnavailableException) { }
        }
        var passages = await search.SearchAsync(safeQuestion, vector, ai.EmbeddingKey, ct);
        if (passages.Count == 0) return new("NO_SOURCES", "Mình chưa tìm thấy tài liệu đã xuất bản phù hợp với câu hỏi này. Bạn hãy thử nêu rõ chủ đề hoặc liên hệ Ban quản lý.", []);
        GroundedAnswer? generated = null;
        var answerUnavailable = false;
        if (ai.Ready)
            try { generated = await ai.AnswerAsync(safeQuestion, passages, ct); }
            catch (ChatAiUnavailableException) { answerUnavailable = true; }
        // Revoke stale passages even if publication changes during an external model request.
        if (!await search.CurrentAsync(passages, ct))
            return new("SOURCE_CHANGED", "Tài liệu vừa được cập nhật hoặc thu hồi. Vui lòng gửi lại câu hỏi để lấy thông tin mới nhất.", []);
        if (generated is { Supported: true, Answer.Length: > 0 and <= 6000, Citations.Length: > 0 } &&
            generated.Citations.All(n => n >= 1 && n <= passages.Count))
            return new("AI", generated.Answer, generated.Citations.Distinct().Select(n => Citation(n, passages[n - 1])).ToArray());
        return new("DOCUMENTS_ONLY", ai.Ready
            ? answerUnavailable
                ? "Dịch vụ AI hiện không khả dụng. Mình đã tìm được tài liệu liên quan; bạn có thể đọc các trích đoạn bên dưới."
                : "Mình chưa tạo được câu trả lời có đủ căn cứ. Bạn có thể đọc các đoạn tài liệu liên quan bên dưới hoặc liên hệ Ban quản lý."
            : "Chức năng trả lời AI chưa được cấu hình. Đây là các đoạn tài liệu tìm được theo câu hỏi của bạn.",
            passages.Select((p, index) => Citation(index + 1, p)).ToArray());
    }
    public async Task<PublishedKnowledgeSource?> SourceAsync(Guid actor, Guid id, int revision, CancellationToken ct)
    { await EnsureAccess(actor, ct); return await search.SourceAsync(id, revision, ct); }
    public async Task EnsureAccess(Guid actor, CancellationToken ct)
    { if (actor == Guid.Empty || !await scope.ActiveAsync(actor, ct)) throw new ChatAccessException(); }
    private static ChatCitation Citation(int number, RetrievedPassage p) => new(number, p.DocumentId, p.Revision, p.Chunk, p.Title, p.Content);
    [GeneratedRegex(@"(?:hoa don|cong no|giao dich|so du).*(?:cua toi|cua minh|da |chua|thang|can ho|bao nhieu)|(?:toi|minh).*(?:no bao nhieu|da thanh toan)")]
    private static partial Regex PersonalFinance();
}
