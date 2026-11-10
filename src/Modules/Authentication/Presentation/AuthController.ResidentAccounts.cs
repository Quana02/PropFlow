using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Authentication.Presentation;

public sealed partial class AuthController
{
    [HttpGet("resident-accounts"), Authorize(Policy = ResidentAccountPolicies.Read)]
    public async Task<ActionResult<ResidentAccountPage>> ListResidentAccounts([FromQuery, StringLength(150)] string? search, int pageIndex = 1,
        int pageSize = 20, CancellationToken ct = default) =>
        Ok(await auth.ListResidentAccountsAsync(search, pageIndex, pageSize, ct));

    [HttpPost("resident-accounts/{residentId:guid}/email-change"), Authorize(Policy = ResidentAccountPolicies.RequestEmailChange)]
    [EnableRateLimiting("auth-login"), ProducesResponseType(204), ProducesResponseType(404)]
    public async Task<IActionResult> RequestResidentEmail(Guid residentId, RequestResidentEmailChange request, CancellationToken ct)
    {
        await CheckCsrfAsync();
        await auth.RequestResidentEmailAsync(UserId, residentId, request, ct);
        return NoContent();
    }

    [HttpGet("resident-email-change"), Authorize(Roles = "RESIDENT")]
    [ProducesResponseType(typeof(ResidentEmailChangePending), 200)]
    public async Task<ActionResult<ResidentEmailChangePending>> PendingResidentEmail(CancellationToken ct)
    {
        var pending = await auth.PendingResidentEmailAsync(UserId, ct);
        return Ok(pending ?? new ResidentEmailChangePending(null, null));
    }

    [HttpPost("resident-email-change/verify"), Authorize(Roles = "RESIDENT")]
    [EnableRateLimiting("auth-login"), ProducesResponseType(204)]
    public async Task<IActionResult> VerifyResidentEmail(VerifyResidentEmailChange request, CancellationToken ct)
    {
        await CheckCsrfAsync();
        await auth.VerifyResidentEmailAsync(UserId, request, ct);
        ClearCookie();
        return NoContent();
    }
}
