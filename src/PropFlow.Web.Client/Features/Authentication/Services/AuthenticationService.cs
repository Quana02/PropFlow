using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Services.Authentication;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.Web.Client.Features.Authentication.Services;

public sealed class AuthenticationService(ApiClient publicApi, AuthenticatedApiClient privateApi, AuthSession session, ToastService toasts)
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
        if (result.IsSuccess && result.Data is { } account) session.UpdateUser(account);
        if (result.Code == "phone_conflict" && result.ValidationErrors is null)
            return result with
            {
                ValidationErrors = new Dictionary<string, string[]>
                {
                    [nameof(UpdateProfileRequest.PhoneNumber)] = [result.Message ?? "Không thể sử dụng số điện thoại này."]
                }
            };
        return result;
    }
    public async Task<ApiResult<EmptyResponse>> ChangePasswordAsync(ChangePasswordRequest request, ToastOperation? feedback = null)
    {
        var operationFeedback = feedback ?? toasts.BeginOperation();

        var result = await privateApi.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/password/change", request, csrf: true);
        if (result.IsSuccess)
        {
            operationFeedback.Publish(() => toasts.SuccessAfterNavigation("Đổi mật khẩu thành công", "Vui lòng đăng nhập lại bằng mật khẩu mới.", "login"));
            session.Clear();
        }
        return result;
    }
    public async Task<ApiResult<ChallengeResponse>> RequestEmailChangeAsync(ChangeEmailRequest request)
    {
        var result = await privateApi.SendAsync<ChallengeResponse>(HttpMethod.Post, "api/v1/auth/email/change", request, csrf: true);
        return WithEmailChangeFieldError(result, result.Code switch
        {
            "invalid_email" or "email_unchanged" or "email_conflict" => nameof(ChangeEmailRequest.NewEmail),
            "email_change_failed" => nameof(ChangeEmailRequest.CurrentPassword),
            _ => null
        });
    }

    public async Task<ApiResult<EmptyResponse>> VerifyEmailChangeAsync(VerifyEmailChangeRequest request, ToastOperation? feedback = null)
    {
        var operationFeedback = feedback ?? toasts.BeginOperation();

        var result = await privateApi.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/email/verify", request, csrf: true);
        if (result.IsSuccess)
        {
            operationFeedback.Publish(() => toasts.SuccessAfterNavigation("Đổi email thành công", "Vui lòng đăng nhập lại bằng tên đăng nhập hiện tại.", "login"));
            session.Clear();
        }
        return WithEmailChangeFieldError(result, result.Code == "invalid_challenge" ? nameof(VerifyEmailChangeRequest.Code) : null);
    }

    private static ApiResult<T> WithEmailChangeFieldError<T>(ApiResult<T> result, string? field) =>
        field is null || result.ValidationErrors is { Count: > 0 } ? result : result with
        {
            ValidationErrors = new Dictionary<string, string[]>
            {
                [field] = [result.Message ?? "Vui lòng kiểm tra lại thông tin đã nhập."]
            }
        };

    public Task<ApiResult<ResidentAccountPage>> ListResidentAccountsAsync(string search, int page) =>
        privateApi.SendAsync<ResidentAccountPage>(HttpMethod.Get,
            $"api/v1/auth/resident-accounts?pageIndex={page}&search={Uri.EscapeDataString(search)}");

    public async Task<ApiResult<EmptyResponse>> RequestResidentEmailAsync(Guid residentId, RequestResidentEmailChange request)
    {
        var result = await privateApi.SendAsync<EmptyResponse>(HttpMethod.Post,
            $"api/v1/auth/resident-accounts/{residentId}/email-change", request, csrf: true);
        return WithEmailChangeFieldError(result, result.Code switch
        {
            "invalid_email" or "email_unchanged" or "email_conflict" => nameof(request.NewEmail),
            "identity_check_required" => nameof(request.IdentityCheckedInPerson),
            _ => null
        });
    }

    public Task<ApiResult<ResidentEmailChangePending>> PendingResidentEmailAsync() =>
        privateApi.SendAsync<ResidentEmailChangePending>(HttpMethod.Get, "api/v1/auth/resident-email-change");

    public async Task<ApiResult<EmptyResponse>> VerifyResidentEmailAsync(VerifyResidentEmailChange request, ToastOperation feedback)
    {
        var userId = session.User?.Id;
        var result = await privateApi.SendAsync<EmptyResponse>(HttpMethod.Post, "api/v1/auth/resident-email-change/verify", request, csrf: true);
        if (result.IsSuccess && session.User?.Id == userId)
        {
            feedback.Publish(() => toasts.SuccessAfterNavigation("Đổi email thành công", "Vui lòng đăng nhập lại.", "login"));
            session.Clear();
        }
        return WithEmailChangeFieldError(result, result.Code switch
        {
            "invalid_challenge" => nameof(request.Code),
            "invalid_email" or "email_conflict" => nameof(request.NewEmail),
            _ => null
        });
    }
}
