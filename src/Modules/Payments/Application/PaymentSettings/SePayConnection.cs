namespace PropFlow.Modules.Payments.Application.PaymentSettings;

public sealed record SePayApiProbe(bool Connected, string Code, string Message, int? AccountsOnFirstPage = null);

public interface ISePayConnectionProbe
{
    Task<SePayApiProbe> CheckAsync(CancellationToken ct);
}

public sealed record SePayConnectionState(
    bool ApiTokenConfigured,
    bool AutomaticConfirmationEnabled,
    bool WebhookKeyConfigured,
    bool ReceivingBankConfigured,
    bool WebhookConfigurationReady,
    string WebhookPath,
    string? PublicWebhookUrl,
    SePayApiProbe? ApiCheck = null,
    DateTimeOffset? LastWebhookReceivedAt = null,
    int PendingReviewCount = 0,
    bool OAuthEnabled = false);
