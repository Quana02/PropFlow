using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.Authentication.Services;

public sealed class AuthenticationService(ApiClient publicApi, AuthenticatedApiClient privateApi, AuthSession session)
{
    public Task<ApiResult<ChallengeResponse>> RegisterAsync(RegisterRequest request) => publicApi.SendAsync<ChallengeResponse>(HttpMethod.Post, "api/v1/auth/register", request);
    public Task<ApiResult<ChallengeResponse>> ResumeAsync(ResumeRegistrationRequest request) => publicApi.SendAsync<ChallengeResponse>(HttpMethod.Post, "api/v1/auth/registration/resend", request);
    public Task<ApiResult<EmptyResponse>> ActivateAsync(VerifyChallengeRequest request) => publicApi.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/registration/verify", request);
    public Task<ApiResult<ChallengeResponse>> ForgotAsync(ForgotPasswordRequest request) => publicApi.SendAsync<ChallengeResponse>(HttpMethod.Post, "api/v1/auth/password/forgot", request);
    public Task<ApiResult<ResetProofResponse>> VerifyRecoveryAsync(VerifyChallengeRequest request) => publicApi.SendAsync<ResetProofResponse>(HttpMethod.Post, "api/v1/auth/password/verify", request);
    public async Task<ApiResult<EmptyResponse>> ResetAsync(ResetPasswordRequest request)
    {
        var result = await publicApi.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/password/reset", request, csrf: true);
        if (result.IsSuccess) session.Clear("Mật khẩu đã được cập nhật. Vui lòng đăng nhập lại.");
        return result;
    }
    public Task<ApiResult<AccountResponse>> MeAsync() => privateApi.SendAsync<AccountResponse>(HttpMethod.Get, "api/v1/auth/me");
    public async Task<ApiResult<AccountResponse>> UpdateAsync(UpdateProfileRequest request)
    {
        var result = await privateApi.SendAsync<AccountResponse>(HttpMethod.Put, "api/v1/auth/me", request);
        if (result.IsSuccess) session.UpdateUser(result.Data!);
        return result;
    }
    public async Task<ApiResult<EmptyResponse>> ChangePasswordAsync(ChangePasswordRequest request)
    {
        var result = await privateApi.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/password/change", request, csrf: true);
        if (result.IsSuccess) session.Clear("Mật khẩu đã được đổi. Vui lòng đăng nhập lại.");
        return result;
    }
}
