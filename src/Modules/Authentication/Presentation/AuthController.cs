using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Authentication.Presentation;

[ApiController, Route("api/v1/auth"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), 400), ProducesResponseType(typeof(ProblemDetails), 401)]
[ProducesResponseType(typeof(ProblemDetails), 403), ProducesResponseType(typeof(ProblemDetails), 409)]
[ProducesResponseType(typeof(ProblemDetails), 429), ProducesResponseType(typeof(ProblemDetails), 500)]
public sealed class AuthController(AuthUseCases auth, IAntiforgery antiforgery, AuthPolicy policy) : ControllerBase
{
    public const string RefreshCookie = "__Secure-PropFlow.Refresh";
    private Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);
    private CookieOptions Cookie(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true, Secure = true, SameSite = policy.CrossSiteCookie ? SameSiteMode.None : SameSiteMode.Strict,
        Path = "/api/v1/auth", Expires = expires, IsEssential = true
    };
    private void SetSession(SessionIssue issue)
    {
        HttpContext.Items["AuthAuditUserId"] = issue.Response.User.Id;
        Response.Cookies.Append(RefreshCookie, issue.RefreshToken, Cookie(issue.RefreshExpiresAt));
    }
    private void ClearCookie() => Response.Cookies.Delete(RefreshCookie, Cookie());
    private Task CheckCsrfAsync() => antiforgery.ValidateRequestAsync(HttpContext);

    [HttpGet("csrf"), AllowAnonymous]
    public ActionResult<CsrfResponse> Csrf() => new CsrfResponse(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);

    [HttpPost("register"), AllowAnonymous, EnableRateLimiting("auth-entry")]
    public async Task<ActionResult<ChallengeResponse>> Register(RegisterRequest request, CancellationToken ct)
        => Ok(await auth.RegisterAsync(request, ct));

    [HttpPost("registration/resend"), AllowAnonymous, EnableRateLimiting("auth-entry")]
    public async Task<ActionResult<ChallengeResponse>> Resume(ResumeRegistrationRequest request, CancellationToken ct)
        => Ok(await auth.ResumeRegistrationAsync(request, ct));

    [HttpPost("registration/verify"), AllowAnonymous, EnableRateLimiting("auth-login")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Activate(VerifyChallengeRequest request, CancellationToken ct)
    { await auth.ActivateAsync(request, ct); return NoContent(); }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth-login")]
    public async Task<ActionResult<SessionResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        await CheckCsrfAsync();
        var result = await auth.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        SetSession(result); return Ok(result.Response);
    }

    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("auth-session")]
    public async Task<ActionResult<SessionResponse>> Refresh(CancellationToken ct)
    {
        await CheckCsrfAsync();
        var raw = Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(raw)) throw new AuthFailure(401, "session_expired", "Phiên đăng nhập đã hết hạn.");
        // A rejected stale cookie may race a successful rotation in another tab.
        // Do not overwrite that newer cookie with an expiry response.
        var result = await auth.RefreshAsync(raw, ct); SetSession(result); return Ok(result.Response);
    }

    [HttpPost("logout"), AllowAnonymous, EnableRateLimiting("auth-session"), ProducesResponseType(204)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    { await CheckCsrfAsync(); await auth.LogoutAsync(Request.Cookies[RefreshCookie], ct); ClearCookie(); return NoContent(); }

    [HttpPost("password/forgot"), AllowAnonymous, EnableRateLimiting("auth-entry")]
    public async Task<ActionResult<ChallengeResponse>> Forgot(ForgotPasswordRequest request, CancellationToken ct)
        => Ok(await auth.ForgotAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct));

    [HttpPost("password/verify"), AllowAnonymous, EnableRateLimiting("auth-login")]
    public async Task<ActionResult<ResetProofResponse>> VerifyRecovery(VerifyChallengeRequest request, CancellationToken ct)
        => Ok(await auth.VerifyRecoveryAsync(request, ct));

    [HttpPost("password/reset"), AllowAnonymous, EnableRateLimiting("auth-login"), ProducesResponseType(204)]
    public async Task<IActionResult> Reset(ResetPasswordRequest request, CancellationToken ct)
    { await CheckCsrfAsync(); await auth.ResetPasswordAsync(request, ct); ClearCookie(); return NoContent(); }

    [HttpPost("password/change"), Authorize, EnableRateLimiting("auth-login"), ProducesResponseType(204)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    { await CheckCsrfAsync(); await auth.ChangePasswordAsync(UserId, request, ct); ClearCookie(); return NoContent(); }

    [HttpGet("me"), Authorize]
    public async Task<ActionResult<AccountResponse>> Me(CancellationToken ct) => Ok(await auth.GetAccountAsync(UserId, ct));

    [HttpPut("me"), Authorize, EnableRateLimiting("auth-session")]
    public async Task<ActionResult<AccountResponse>> Update(UpdateProfileRequest request, CancellationToken ct)
        => Ok(await auth.UpdateAccountAsync(UserId, request, ct));
}
