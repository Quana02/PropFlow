namespace PropFlow.Web.Client.Features.Administration.Models;

public sealed record PaymentDestinationView(
    Guid Id,
    int Version,
    string BankBin,
    string BankCode,
    string BankName,
    string MaskedAccountNumber,
    string AccountName,
    bool IsActive,
    Guid UpdatedBy,
    DateTimeOffset UpdatedAt,
    Guid? SePayAccountId = null,
    Guid? CreatedBy = null,
    DateTimeOffset? CreatedAt = null);

public sealed record PaymentDestinationState(PaymentDestinationView? Destination);
public sealed record BankOption(string Bin, string Code, string ShortName, string Name, string? Logo);
public sealed record PaymentDestinationDraft(
    string BankBin,
    string BankCode,
    string BankName,
    string AccountNumber,
    string AccountName,
    int ExpectedVersion = 0);
public sealed record PaymentDestinationChallengeRequest(string Action, PaymentDestinationDraft? Destination);
public sealed record PrivilegedActionChallengeResult(Guid ChallengeId, DateTimeOffset ExpiresAt, DateTimeOffset ResendAfter, string MaskedEmail);
public sealed record ConfirmPaymentDestinationRequest(PaymentDestinationDraft Destination, Guid ChallengeId, string Code);
public sealed record DeactivatePaymentDestinationRequest(int ExpectedVersion, Guid ChallengeId, string Code);
public sealed record PaymentDestinationPreview(string QrImageUrl, string AccountName, string AccountNumber, string BankName);
public sealed record SePayApiProbe(bool Connected, string Code, string Message, int? AccountsOnFirstPage = null);
public sealed record SePayConnectionState(bool ApiTokenConfigured, bool AutomaticConfirmationEnabled, bool WebhookKeyConfigured,
    bool ReceivingBankConfigured, bool WebhookConfigurationReady, string WebhookPath, string? PublicWebhookUrl, SePayApiProbe? ApiCheck = null,
    DateTimeOffset? LastWebhookReceivedAt = null, int PendingReviewCount = 0, bool OAuthEnabled = false);
public sealed record SePayBankAccountView(Guid Id, string BankCode, string BankName, string MaskedAccountNumber, string AccountName, bool Active);
public sealed record SelectSePayAccountRequest(Guid AccountId, int ExpectedVersion = 0);
public sealed record ConfirmSePayAccountRequest(Guid AccountId, int ExpectedVersion, Guid ChallengeId, string Code);
public sealed record SePayOAuthState(bool Available, bool Connected, bool RequiresReconnect, DateTimeOffset? ConnectedAt, bool WebhookConfigured, string Message);
public sealed record SePayOAuthConfirm(Guid ChallengeId, string Code);
public sealed record SePayOAuthCallbackRequest(string State, string? Code, string? Error);
public sealed record SePayOAuthStart(string AuthorizationUrl);
public sealed record SePaySandboxState(bool Configured, bool Verified, int Version, int AccountCount, DateTimeOffset? CheckedAt, DateTimeOffset? UpdatedAt, bool DemoEnabled = false, bool WebhookKeyConfigured = false, string? WebhookApiKey = null);
public sealed record SePaySandboxDraft(string Action, string? ApiToken, int ExpectedVersion);
public sealed record SePaySandboxConfirm(string Action, string? ApiToken, int ExpectedVersion, Guid ChallengeId, string Code);
public sealed record SePaySandboxProbe(bool Connected, string Code, string Message, int AccountCount = 0);
public sealed record SePaySandboxAccount(string Id, string BankName, string BankCode, string MaskedAccountNumber, string AccountName, bool Active);
