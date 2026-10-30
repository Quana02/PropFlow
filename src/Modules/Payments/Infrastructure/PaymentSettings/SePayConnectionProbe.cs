using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed class SePayConnectionProbe(HttpClient http, IOptions<SePayOptions> options,
    SePayOAuth? oauth = null, ISePayBankAccounts? accounts = null) : ISePayConnectionProbe
{
    public async Task<SePayApiProbe> CheckAsync(CancellationToken ct)
    {
        if (oauth?.Enabled == true && accounts is not null)
        {
            try { var list = await accounts.ListAsync(ct); return new(true, "sepay_connected", list.Count == 0
                ? "Đã kết nối. Hãy liên kết ngân hàng trong SePay rồi đồng bộ lại." : "Kết nối SePay thành công.", list.Count); }
            catch (PaymentSettingsFailure e) { return new(false, e.Code, e.Message); }
        }
        var token = options.Value.ApiToken;
        if (string.IsNullOrWhiteSpace(token) || token.Any(char.IsControl))
            return new(false, "sepay_token_missing", "Chưa cấu hình API Token SePay hợp lệ ở máy chủ.");
        try
        {
            // Fixed destination: configuration cannot redirect the API token to another server.
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://userapi.sepay.vn/v2/bank-accounts?page=1&per_page=1");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(false, "sepay_unauthorized", "API Token SePay không hợp lệ hoặc đã hết hiệu lực.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return new(false, "sepay_forbidden", "API Token chưa có quyền đọc tài khoản ngân hàng.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new(false, "sepay_rate_limited", "SePay đang giới hạn số lần truy cập. Vui lòng thử lại sau.");
            if (!response.IsSuccessStatusCode)
                return new(false, "sepay_unavailable", "Chưa thể kết nối SePay. Vui lòng thử lại sau.");
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.String || status.GetString() != "success" ||
                !root.TryGetProperty("data", out var accounts) || accounts.ValueKind != JsonValueKind.Array)
                return new(false, "sepay_invalid_response", "Phản hồi SePay chưa đúng định dạng tài khoản ngân hàng.");
            return new(true, "sepay_connected", accounts.GetArrayLength() == 0
                ? "Kết nối SePay thành công. Chưa có tài khoản ngân hàng trong danh sách."
                : "Kết nối SePay thành công và có thể đọc danh sách tài khoản ngân hàng.", accounts.GetArrayLength());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        { return new(false, "sepay_timeout", "SePay phản hồi quá thời gian chờ. Vui lòng thử lại sau."); }
        catch (HttpRequestException)
        { return new(false, "sepay_unavailable", "Chưa thể kết nối SePay. Vui lòng thử lại sau."); }
        catch (JsonException)
        { return new(false, "sepay_invalid_response", "Phản hồi SePay chưa đúng định dạng tài khoản ngân hàng."); }
    }
}

public sealed class SePayConnectionSettings(IOptions<SePayOptions> options, ISePayConnectionProbe probe,
    IPaymentDestinationStore? destinations = null, PaymentsDbContext? db = null, SePayOAuth? oauth = null)
{
    public const string WebhookPath = "/api/v1/payments/webhooks/sepay";

    public SePayConnectionState Current()
    {
        var settings = options.Value;
        var keyReady = settings.AuthenticationMode == "HMAC" ? settings.WebhookSecret.Length >= 32
            : settings.AuthenticationMode == "API_KEY" && !string.IsNullOrWhiteSpace(settings.WebhookApiKey) && settings.WebhookApiKey.Length >= 32;
        var bankReady = !string.IsNullOrWhiteSpace(settings.Gateway) && !string.IsNullOrWhiteSpace(settings.BankCode);
        string? publicUrl = null;
        if (Uri.TryCreate(settings.WebhookPublicBaseUrl, UriKind.Absolute, out var origin) && origin.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(origin.UserInfo) && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment) &&
            origin.AbsolutePath == "/" && !origin.IsLoopback)
            publicUrl = origin.GetLeftPart(UriPartial.Authority) + WebhookPath;
        return new(!string.IsNullOrWhiteSpace(settings.ApiToken) && !settings.ApiToken.Any(char.IsControl),
            settings.Enabled, keyReady, bankReady, settings.Enabled && keyReady && bankReady && publicUrl is not null,
            WebhookPath, publicUrl);
    }

    public async Task<SePayConnectionState> CurrentAsync(CancellationToken ct)
    {
        var state = Current();
        var delegated = oauth?.Enabled == true ? await oauth.StateAsync(ct) : null;
        if (delegated is not null) state = state with { ApiTokenConfigured = delegated.Connected, OAuthEnabled = true };
        var active = destinations is null ? null : await destinations.ActiveAsync(ct);
        var bankReady = state.ReceivingBankConfigured || active?.SePayAccountId is not null;
        return state with { ReceivingBankConfigured = bankReady,
            WebhookConfigurationReady = state.AutomaticConfirmationEnabled && state.WebhookKeyConfigured && bankReady && state.PublicWebhookUrl is not null && (delegated is null || delegated.WebhookConfigured),
            LastWebhookReceivedAt = db is null ? null : await db.BankTransfers.MaxAsync(x => (DateTimeOffset?)x.ReceivedAt, ct),
            PendingReviewCount = db is null ? 0 : await db.BankTransfers.CountAsync(x => x.Status == "REVIEW", ct) };
    }

    public async Task<SePayConnectionState> CheckAsync(CancellationToken ct) =>
        (await CurrentAsync(ct)) with { ApiCheck = await probe.CheckAsync(ct) };
}
