using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropFlow.Modules.Administration.Infrastructure.SupportContacts;
using PropFlow.Modules.Administration.Contracts;

namespace PropFlow.Modules.Administration.Presentation;

[ApiController, Route("api/v1/administration/system-configuration/support-contacts")]
[Authorize(Roles = "ADMIN", Policy = AdministrationAuthorizationPolicies.ManageSystemConfiguration)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportContactsController(SupportContactSettings settings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SupportContactsState>> Get(CancellationToken ct)
    {
        try { return Ok(await settings.ReadAsync(ct)); }
        catch (SupportContactsUnavailableException) { return Failure(503, "support_contacts_unavailable", "Chưa thể tải cấu hình liên hệ hỗ trợ."); }
    }

    [HttpPut, RequestSizeLimit(4096)]
    public async Task<ActionResult<SupportContactsState>> Update(UpdateSupportContacts input, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("sub"), out var actor) || actor == Guid.Empty) return Unauthorized();
        try { return Ok(await settings.UpdateAsync(input, actor, ct)); }
        catch (ValidationException) { return Failure(400, "support_contacts_invalid", "Thông tin liên hệ hỗ trợ không hợp lệ."); }
        catch (SupportContactsConflictException) { return Failure(409, "support_contacts_changed", "Cấu hình đã được thay đổi. Vui lòng tải lại trước khi lưu."); }
        catch (SupportContactsUnavailableException) { return Failure(503, "support_contacts_unavailable", "Chưa thể tải cấu hình liên hệ hỗ trợ."); }
    }
    private ObjectResult Failure(int status, string code, string title) => StatusCode(status,
        new ProblemDetails { Status = status, Title = title, Extensions = { ["code"] = code, ["traceId"] = HttpContext.TraceIdentifier } });
}

[ApiController, Route("api/v1/public/support-contacts"), AllowAnonymous]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicSupportContactsController(SupportContactSettings settings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PublicSupportContacts>> Get(CancellationToken ct)
    {
        try { return Ok(await settings.PublicAsync(ct)); }
        catch (SupportContactsUnavailableException) { return StatusCode(503, new ProblemDetails { Status = 503,
            Title = "Chưa thể tải thông tin liên hệ hỗ trợ.", Extensions = { ["code"] = "support_contacts_unavailable" } }); }
    }
}
