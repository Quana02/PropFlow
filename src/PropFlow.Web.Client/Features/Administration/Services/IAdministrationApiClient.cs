using PropFlow.Web.Client.Features.Administration.Models;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Administration.Services;

public interface IAdministrationApiClient
{
    Task<ApiResult<AdministrationOverview>> GetOverviewAsync(CancellationToken ct = default);
    Task<ApiResult<InternalAccountSummary>> GetSummaryAsync(CancellationToken ct = default);
    Task<ApiResult<PagedInternalAccounts>> GetAccountsAsync(string role, string? search, string? status, int page, CancellationToken ct = default);
    Task<ApiResult<InternalAccount>> CreateAsync(CreateInternalAccount request, CancellationToken ct = default);
    Task<ApiResult> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default);
    Task<ApiResult> ChangeStatusAsync(Guid userId, string status, CancellationToken ct = default);
    Task<ApiResult<AdministrationRoleAccess[]>> GetRolesAsync(CancellationToken ct = default);
    Task<ApiResult<InternalAccountAccess>> GetEffectiveAccessAsync(Guid userId, CancellationToken ct = default);
    Task<ApiResult<PagedAdministrationActivities>> GetActivityAsync(string? action, int page, CancellationToken ct = default);
    Task<ApiResult<PaymentDestinationState>> GetPaymentDestinationAsync(CancellationToken ct = default);
    Task<ApiResult<PaymentDestinationView[]>> GetPaymentDestinationHistoryAsync(CancellationToken ct = default);
    Task<ApiResult<BankOption[]>> GetPaymentBanksAsync(CancellationToken ct = default);
    Task<ApiResult<PaymentDestinationPreview>> PreviewPaymentDestinationAsync(PaymentDestinationDraft request, CancellationToken ct = default);
    Task<ApiResult<PrivilegedActionChallengeResult>> RequestPaymentDestinationChallengeAsync(PaymentDestinationChallengeRequest request, CancellationToken ct = default);
    Task<ApiResult<PaymentDestinationView>> SavePaymentDestinationAsync(ConfirmPaymentDestinationRequest request, CancellationToken ct = default);
    Task<ApiResult> DeactivatePaymentDestinationAsync(DeactivatePaymentDestinationRequest request, CancellationToken ct = default);
    Task<ApiResult<SePayConnectionState>> GetSePayConnectionAsync(CancellationToken ct = default);
    Task<ApiResult<SePayConnectionState>> CheckSePayConnectionAsync(CancellationToken ct = default);
    Task<ApiResult<SePayBankAccountView[]>> GetSePayAccountsAsync(CancellationToken ct = default);
    Task<ApiResult<PaymentDestinationPreview>> PreviewSePayAccountAsync(SelectSePayAccountRequest request, CancellationToken ct = default);
    Task<ApiResult<PrivilegedActionChallengeResult>> RequestSePayAccountChallengeAsync(SelectSePayAccountRequest request, CancellationToken ct = default);
    Task<ApiResult<PaymentDestinationView>> SaveSePayAccountAsync(ConfirmSePayAccountRequest request, CancellationToken ct = default);
}
