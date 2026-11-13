using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Authentication.Presentation;

// FE-01.6: self-service profile picture.
[ApiController, Route("api/v1/auth/me/avatar"), Authorize(Roles = "RESIDENT")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountAvatarController(AccountAvatarUseCases avatars) : ControllerBase
{
    public sealed class UploadForm
    {
        [Required] public IFormFile File { get; set; } = null!;
    }
    private Guid Owner => Guid.Parse(User.FindFirstValue("sub")!);

    [HttpPost, EnableRateLimiting("auth-session"), Consumes("multipart/form-data")]
    [RequestSizeLimit(AccountAvatarUseCases.MaxBytes + 65536), RequestFormLimits(MultipartBodyLengthLimit = AccountAvatarUseCases.MaxBytes + 65536)]
    [ProducesResponseType(typeof(AccountResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400), ProducesResponseType(401), ProducesResponseType(403), ProducesResponseType(413), ProducesResponseType(429)]
    public async Task<ActionResult<AccountResponse>> Upload([FromForm] UploadForm request, CancellationToken ct)
    {
        var file = request.File;
        await using var stream = file.OpenReadStream();
        return Ok(await avatars.UploadAsync(Owner, stream, file.Length, file.ContentType, ct));
    }

    [HttpGet, Produces("image/jpeg")]
    [ProducesResponseType(typeof(FileContentResult), 200), ProducesResponseType(401), ProducesResponseType(403), ProducesResponseType(404)]
    public async Task<IActionResult> Read(CancellationToken ct) => File(await avatars.ReadAsync(Owner, ct), "image/jpeg");
}
