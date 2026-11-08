using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.AiChatbot.Application;
using PropFlow.Modules.AiChatbot.Contracts;
using PropFlow.Modules.AiChatbot.Domain;

namespace PropFlow.Modules.AiChatbot.Presentation;

[ApiController, Route("api/v1/knowledge/documents"), Authorize(Policy = KnowledgePolicies.Read)]
public sealed class KnowledgeDocumentsController(IKnowledgeDocuments documents) : ControllerBase
{
    [HttpGet]
    public Task<ActionResult<KnowledgeDocumentPage>> List(string? search, int page = 1, CancellationToken ct = default) =>
        Execute(() => documents.ListAsync(KnowledgePolicies.CanManage(User), search, page, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<KnowledgeDocumentDetail>> Get(Guid id, CancellationToken ct)
    {
        var result = await documents.GetAsync(id, KnowledgePolicies.CanManage(User), ct);
        return result is null ? Failure(404, "knowledge_not_found", "Không tìm thấy tài liệu hoặc tài liệu chưa được xuất bản.") : Ok(result);
    }

    [HttpPost, Authorize(Policy = KnowledgePolicies.Manage), RequestSizeLimit(400000)]
    public Task<ActionResult<Guid>> Create(SaveKnowledgeDocument request, CancellationToken ct) => Save(null, request, ct);

    [HttpGet("{id:guid}/index"), Authorize(Policy = KnowledgePolicies.Manage)]
    public async Task<ActionResult<KnowledgeIndexStatus>> IndexStatus(Guid id, CancellationToken ct)
    {
        var status = await documents.IndexStatusAsync(id, ct);
        return status is null ? Failure(404, "knowledge_not_found", "Không tìm thấy tài liệu.") : Ok(status);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = KnowledgePolicies.Manage), RequestSizeLimit(400000)]
    public Task<ActionResult<Guid>> Update(Guid id, SaveKnowledgeDocument request, CancellationToken ct) => Save(id, request, ct);

    [HttpPost("{id:guid}/publication"), Authorize(Policy = KnowledgePolicies.Manage)]
    public Task<ActionResult<Guid>> Publish(Guid id, ChangeKnowledgePublication request, CancellationToken ct) => Publication(id, request, true, ct);

    [HttpPost("{id:guid}/unpublish"), Authorize(Policy = KnowledgePolicies.Manage)]
    public Task<ActionResult<Guid>> Unpublish(Guid id, ChangeKnowledgePublication request, CancellationToken ct) => Publication(id, request, false, ct);

    private Task<ActionResult<Guid>> Save(Guid? id, SaveKnowledgeDocument request, CancellationToken ct) => Execute(() => documents.SaveAsync(id, request, Actor(), ct));
    private Task<ActionResult<Guid>> Publication(Guid id, ChangeKnowledgePublication request, bool publish, CancellationToken ct) =>
        Execute(async () => { await documents.SetPublicationAsync(id, request.ExpectedToken, publish, Actor(), ct); return id; });
    private Guid Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var actor) ? actor : Guid.Empty;
    private async Task<ActionResult<T>> Execute<T>(Func<Task<T>> operation)
    {
        if (Actor() == Guid.Empty) return Unauthorized();
        try { return Ok(await operation()); }
        catch (KnowledgeValidationException e) { return Failure(400, "knowledge_invalid", e.Message); }
        catch (KnowledgeNotFoundException) { return Failure(404, "knowledge_not_found", "Không tìm thấy tài liệu."); }
        catch (KnowledgeConflictException) { return Failure(409, "knowledge_conflict", "Tài liệu đã thay đổi. Vui lòng tải lại trước khi lưu hoặc xuất bản."); }
    }
    private ObjectResult Failure(int status, string code, string message) => StatusCode(status,
        new ProblemDetails { Status = status, Title = message, Extensions = { ["code"] = code, ["traceId"] = HttpContext.TraceIdentifier } });
}
