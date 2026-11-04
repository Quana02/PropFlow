using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed class SePayOAuthOptions
{
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
}

public sealed record SePayOAuthState(bool Available, bool Connected, bool RequiresReconnect,
    DateTimeOffset? ConnectedAt, bool WebhookConfigured, string Message);
public sealed record SePayOAuthConfirm(Guid ChallengeId, string Code);
public sealed record SePayOAuthCallback(string State, string? Code, string? Error);
public sealed record SePayOAuthStart(string AuthorizationUrl);

public sealed class SePayOAuth(HttpClient http, PaymentsDbContext db, IDataProtectionProvider protection,
    IOptions<SePayOAuthOptions> config, IOptions<SePayOptions> payments, IPrivilegedActionOtp otp, TimeProvider clock,
    IPaymentAccountProtector accountProtector)
{
    public const string Scopes = "bank-account:read webhook:read webhook:write";
    private readonly IDataProtector protector = protection.CreateProtector("PropFlow.SePay.OAuth.v1");
    public bool Enabled => config.Value.Enabled;
    public bool Available => Enabled && !string.IsNullOrWhiteSpace(config.Value.ClientId) &&
        !string.IsNullOrWhiteSpace(config.Value.ClientSecret) && ValidRedirect(config.Value.RedirectUri);

    public static bool ValidRedirect(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/admin/sepay/callback";

    public async Task<SePayOAuthState> StateAsync(CancellationToken ct)
    {
        var row = await db.SePayConnections.AsNoTracking().SingleOrDefaultAsync(ct);
        var connected = row?.ProtectedRefreshToken is not null && !row.RequiresReconnect;
        var active = await db.PaymentDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive, ct);
        return new(Available, connected, row?.RequiresReconnect == true, row?.ConnectedAt,
            connected && row?.WebhookId is not null && active is not null && row.WebhookDestinationId == active.Id,
            !Available ? "Kết nối SePay chưa được thiết lập. Vui lòng liên hệ đơn vị vận hành PropFlow."
            : row?.RequiresReconnect == true ? "Quyền truy cập đã hết hiệu lực. Vui lòng kết nối lại SePay."
            : connected ? "Đã kết nối tài khoản SePay của chung cư. Chọn ngân hàng nhận tiền bên dưới."
            : "Đăng nhập SePay của bạn và cấp quyền cho PropFlow. Không cần nhập API Key.");
    }

    public async Task<PrivilegedActionChallengeResult> ChallengeAsync(Guid actor, string action, CancellationToken ct)
    {
        EnsureAvailable();
        return await otp.RequestAsync(actor, "SEPAY_CONNECTION_CHANGE", await ActionHashAsync(action, ct), ct);
    }

    public async Task<SePayOAuthStart> StartAsync(Guid actor, SePayOAuthConfirm confirm, CancellationToken ct)
    {
        EnsureAvailable();
        await using var tx = await LockAsync(ct);
        await otp.VerifyAsync(actor, confirm.ChallengeId, "SEPAY_CONNECTION_CHANGE", await ActionHashAsync("CONNECT", ct), confirm.Code, ct);
        var row = await RowAsync(ct);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        row.PendingStateHash = Hash(state);
        row.PendingActor = actor;
        row.PendingExpiresAt = clock.GetUtcNow().AddMinutes(10);
        row.PendingRedirectUri = config.Value.RedirectUri;
        row.PendingAttempt = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        var query = new Dictionary<string, string> { ["response_type"] = "code", ["client_id"] = config.Value.ClientId,
            ["redirect_uri"] = config.Value.RedirectUri, ["scope"] = Scopes, ["state"] = state };
        return new("https://my.sepay.vn/oauth/authorize?" + string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}")));
    }

    public async Task CompleteAsync(Guid actor, SePayOAuthCallback callback, CancellationToken ct)
    {
        EnsureAvailable();
        if (callback.State?.Length != 64 || callback.Code?.Length > 2048) throw Failure("sepay_invalid_state", "Phiên kết nối không hợp lệ. Vui lòng bắt đầu lại.");
        Guid attempt, generation;
        string redirect;
        // Consume state before exchanging the one-use code, including denied consent.
        await using (var tx = await LockAsync(ct))
        {
            var row = await RowAsync(ct);
            if (row.PendingActor != actor || row.PendingExpiresAt <= clock.GetUtcNow() || row.PendingStateHash is null ||
                !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(row.PendingStateHash), Encoding.UTF8.GetBytes(Hash(callback.State))))
                throw Failure("sepay_invalid_state", "Phiên kết nối đã hết hạn hoặc không thuộc tài khoản này. Vui lòng kết nối lại.");
            attempt = row.PendingAttempt; generation = row.Generation; redirect = row.PendingRedirectUri!;
            row.PendingStateHash = null;
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        if (!string.IsNullOrEmpty(callback.Error) || string.IsNullOrWhiteSpace(callback.Code))
            throw Failure("sepay_consent_denied", "Bạn chưa cấp quyền kết nối. Cấu hình nhận tiền hiện tại vẫn được giữ.");
        var token = await TokenAsync(new() { ["grant_type"] = "authorization_code", ["code"] = callback.Code, ["redirect_uri"] = redirect }, ct);
        await using (var tx = await LockAsync(ct))
        {
            var row = await RowAsync(ct);
            if (row.PendingAttempt != attempt || row.Generation != generation)
                throw Failure("sepay_connection_changed", "Một phiên kết nối khác đã thay đổi cấu hình. Vui lòng tải lại.");
            // Provisioned old webhooks are left intact for invoices pinned to old bank details.
            row.Generation = Guid.NewGuid();
            SaveToken(row, token);
            row.ConnectedBy = actor; row.ConnectedAt = clock.GetUtcNow();
            row.WebhookId = null; row.WebhookDestinationId = null; row.WebhookConfiguredAt = null;
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
    }

    public async Task<(string Token, Guid Generation)> AccessAsync(CancellationToken ct)
    {
        EnsureAvailable();
        // Serialize refresh-token rotation across API instances.
        await using var tx = await LockAsync(ct);
        var row = await RowAsync(ct);
        if (row.ProtectedRefreshToken is null || row.RequiresReconnect) throw Reconnect();
        if (row.AccessExpiresAt is null || row.ProtectedAccessToken is null || row.AccessExpiresAt <= clock.GetUtcNow().AddMinutes(1))
        {
            try { SaveToken(row, await TokenAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = protector.Unprotect(row.ProtectedRefreshToken) }, ct)); }
            catch (PaymentSettingsFailure e) when (e.Code == "sepay_reconnect_required")
            {
                row.RequiresReconnect = true; await db.SaveChangesAsync(ct);
                if (tx is not null) await tx.CommitAsync(ct);
                throw;
            }
            await db.SaveChangesAsync(ct);
        }
        var result = (protector.Unprotect(row.ProtectedAccessToken!), row.Generation);
        if (tx is not null) await tx.CommitAsync(ct);
        return result;
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        var access = await AccessAsync(ct);
        try { return await ApiAsync(HttpMethod.Get, path, access.Token, null, ct); }
        catch (PaymentSettingsFailure e) when (e.Code == "sepay_token_expired")
        {
            access = await AccessAsync(ct);
            return await ApiAsync(HttpMethod.Get, path, access.Token, null, ct);
        }
    }

    public async Task ConfigureWebhookAsync(Guid actor, SePayOAuthConfirm confirm, CancellationToken ct)
    {
        EnsureAvailable();
        var hash = await ActionHashAsync("WEBHOOK", ct);
        await otp.VerifyAsync(actor, confirm.ChallengeId, "SEPAY_CONNECTION_CHANGE", hash, confirm.Code, ct);
        var access = await AccessAsync(ct);
        await using var tx = await LockAsync(ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('payment-destination'))", ct);
        var row = await RowAsync(ct);
        if (row.Generation != access.Generation || hash != await ActionHashAsync("WEBHOOK", ct))
            throw Failure("sepay_connection_changed", "Kết nối hoặc tài khoản nhận tiền đã thay đổi. Vui lòng xác nhận lại.");
        var destination = await db.PaymentDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive, ct)
            ?? throw Failure("sepay_account_required", "Hãy chọn tài khoản nhận tiền và xác nhận OTP trước.");
        var settings = payments.Value;
        var secret = settings.AuthenticationMode == "HMAC" ? settings.WebhookSecret : settings.WebhookApiKey;
        if (!PublicOrigin(settings.WebhookPublicBaseUrl) || secret.Length < 32 || settings.AuthenticationMode is not ("HMAC" or "API_KEY"))
            throw Failure("sepay_webhook_not_ready", "Đơn vị vận hành cần thiết lập địa chỉ nhận giao dịch và khóa bảo mật trước.");
        // Resolve the provider ID server-side; never trust an ID or bank details supplied by the browser.
        using var accounts = await ApiAsync(HttpMethod.Get, "bank-accounts?limit=100", access.Token, null, ct);
        var providerId = await FindProviderAccountAsync(destination, access.Generation, access.Token, accounts, ct);
        var payload = new Dictionary<string, object?> { ["name"] = "PropFlow " + destination.Id.ToString("N"),
            ["bank_mode"] = "single", ["bank_account_id"] = providerId, ["event_type"] = "In_only",
            ["authen_type"] = settings.AuthenticationMode == "HMAC" ? "HMAC_SHA256" : "Api_Key",
            ["webhook_url"] = settings.WebhookPublicBaseUrl.TrimEnd('/') + SePayConnectionSettings.WebhookPath,
            ["request_content_type"] = "Json", ["is_verify_payment"] = 1, ["skip_if_no_code"] = 0,
            ["active"] = 1, ["only_va"] = 0, ["retry_conditions"] = new[] { "non_2xx_status_code" },
            [settings.AuthenticationMode == "HMAC" ? "secret_key" : "api_key"] = secret };
        // Deterministic name permits safe recovery after an ambiguous network failure.
        var name = (string)payload["name"]!;
        var existing = await FindWebhookAsync(name, access.Token, ct);
        using var response = await ApiAsync(existing is null ? HttpMethod.Post : HttpMethod.Patch,
            existing is null ? "webhooks" : $"webhooks/{existing}", access.Token, payload, ct);
        if (!response.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("id", out var id) || !id.TryGetInt64(out var webhookId))
            throw Failure("sepay_invalid_response", "SePay chưa xác nhận cấu hình nhận giao dịch. Vui lòng thử lại.");
        row.WebhookId = webhookId; row.WebhookDestinationId = destination.Id; row.WebhookConfiguredAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    private async Task<long> FindProviderAccountAsync(PropFlow.Modules.Payments.Domain.PaymentDestinations.PaymentDestination destination, Guid generation, string token, JsonDocument first, CancellationToken ct)
    {
        for (var page = 1; page <= 100; page++)
        {
            using var next = page == 1 ? null : await ApiAsync(HttpMethod.Get, $"bank-accounts?limit=100&page={page}", token, null, ct);
            var root = (next ?? first).RootElement;
            foreach (var item in root.GetProperty("data").EnumerateArray())
            {
                var id = item.GetProperty("id").GetInt64();
                if (AccountId(generation, id) == destination.SePayAccountId)
                {
                    var account = OAuthSePayBankAccounts.Parse(item, generation);
                    if (!account.Active || account.BankBin != destination.BankBin || account.AccountNumber != accountProtector.Unprotect(destination.ProtectedAccountNumber))
                        throw Failure("sepay_account_changed", "Thông tin ngân hàng đã thay đổi trên SePay. Hãy đồng bộ và xác nhận lại tài khoản nhận tiền.");
                    return id;
                }
            }
            if (!HasMore(root, page)) break;
        }
        throw Failure("sepay_account_changed", "Tài khoản nhận tiền không thuộc kết nối SePay hiện tại. Hãy đồng bộ và chọn lại.");
    }

    private async Task<long?> FindWebhookAsync(string name, string token, CancellationToken ct)
    {
        for (var page = 1; page <= 100; page++)
        {
            using var response = await ApiAsync(HttpMethod.Get, $"webhooks?limit=100&page={page}", token, null, ct);
            var root = response.RootElement;
            foreach (var item in root.GetProperty("data").EnumerateArray())
                if (item.GetProperty("name").GetString() == name) return item.GetProperty("id").GetInt64();
            if (!HasMore(root, page)) return null;
        }
        throw Failure("sepay_too_many_webhooks", "Danh sách cấu hình SePay quá lớn. Vui lòng liên hệ hỗ trợ.");
    }

    public static Guid AccountId(Guid generation, long id) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"sepay-oauth:{generation:N}:{id}"))[..16]);
    public static bool HasMore(JsonElement root, int page) => root.TryGetProperty("meta", out var meta) &&
        meta.TryGetProperty("pagination", out var pagination) && pagination.TryGetProperty("last_page", out var last) && last.GetInt32() > page;
    public static bool PublicOrigin(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        !uri.IsLoopback && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private async Task<string> ActionHashAsync(string action, CancellationToken ct)
    {
        if (action is not ("CONNECT" or "WEBHOOK")) throw Failure("invalid_action", "Thao tác không hợp lệ.");
        var row = await db.SePayConnections.AsNoTracking().SingleOrDefaultAsync(ct);
        var active = await db.PaymentDestinations.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive, ct);
        var p = payments.Value;
        return Hash($"{action}|{row?.Generation ?? Guid.Empty}|{active?.Id}|{active?.Version}|{config.Value.ClientId}|{config.Value.RedirectUri}|{p.WebhookPublicBaseUrl}|{p.AuthenticationMode}|{Hash(p.WebhookSecret + p.WebhookApiKey)}");
    }

    private async Task<SePayOAuthConnection> RowAsync(CancellationToken ct)
    {
        var row = await db.SePayConnections.SingleOrDefaultAsync(ct);
        if (row is not null) { await db.Entry(row).ReloadAsync(ct); return row; }
        row = new(); db.SePayConnections.Add(row); return row;
    }

    private async Task<IDbContextTransaction?> LockAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(728405192)", ct);
        return tx;
    }

    private sealed record Tokens(string Access, string Refresh, int Expires);
    private async Task<Tokens> TokenAsync(Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["client_id"] = config.Value.ClientId; fields["client_secret"] = config.Value.ClientSecret;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://my.sepay.vn/oauth/token") { Content = new FormUrlEncodedContent(fields) };
        using var response = await SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            try
            {
                using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (error.RootElement.ValueKind == JsonValueKind.Object && error.RootElement.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.String && code.GetString() is "invalid_client" or "unauthorized_client")
                    throw Failure("sepay_oauth_application_invalid", "Thông tin ứng dụng SePay chưa hợp lệ. Vui lòng liên hệ đơn vị vận hành PropFlow.");
            }
            catch (JsonException) { }
            throw Reconnect();
        }
        if (!response.IsSuccessStatusCode) throw Unavailable();
        try
        {
            using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = data.RootElement;
            var access = root.GetProperty("access_token").GetString();
            var refresh = root.GetProperty("refresh_token").GetString();
            var expires = root.GetProperty("expires_in").GetInt32();
            if (string.IsNullOrWhiteSpace(access) || access.Any(char.IsControl) || string.IsNullOrWhiteSpace(refresh) || expires <= 0 || expires > 31536000) throw Unavailable();
            return new(access, refresh, expires);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { throw Unavailable(); }
    }

    private void SaveToken(SePayOAuthConnection row, Tokens token)
    {
        row.ProtectedAccessToken = protector.Protect(token.Access); row.ProtectedRefreshToken = protector.Protect(token.Refresh);
        row.AccessExpiresAt = clock.GetUtcNow().AddSeconds(token.Expires); row.RequiresReconnect = false;
    }

    private async Task<JsonDocument> ApiAsync(HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "https://my.sepay.vn/api/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // Do not repeat a POST after a provider/network error. Expire only the token used by this request.
            var current = await db.SePayConnections.AsNoTracking().SingleOrDefaultAsync(ct);
            if (current?.ProtectedAccessToken is { } encrypted && protector.Unprotect(encrypted) == token)
            {
                if (db.Database.IsRelational()) await db.SePayConnections.Where(x => x.ProtectedAccessToken == encrypted)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.AccessExpiresAt, clock.GetUtcNow()), ct);
                else { var row = await RowAsync(ct); row.AccessExpiresAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); }
            }
            throw Failure("sepay_token_expired", "Phiên SePay cần làm mới. Vui lòng thử lại thao tác.");
        }
        if (response.StatusCode == HttpStatusCode.Forbidden) throw Failure("sepay_forbidden", "SePay chưa cấp đủ quyền. Hãy kiểm tra quyền tài khoản hoặc kết nối lại.");
        if (!response.IsSuccessStatusCode) throw Unavailable();
        try
        {
            var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "success") { doc.Dispose(); throw Unavailable(); }
            return doc;
        }
        catch (JsonException) { throw Unavailable(); }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try { return await http.SendAsync(request, ct); }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
    }
    private void EnsureAvailable() { if (!Available) throw Failure("sepay_oauth_not_configured", "Đơn vị vận hành cần đăng ký ứng dụng SePay và thiết lập kết nối trước."); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static PaymentSettingsFailure Reconnect() => Failure("sepay_reconnect_required", "Quyền truy cập SePay không còn hiệu lực. Vui lòng kết nối lại.");
    private static PaymentSettingsFailure Unavailable() => new(503, "sepay_unavailable", "SePay chưa phản hồi hợp lệ. Vui lòng thử lại sau.");
    private static PaymentSettingsFailure Failure(string code, string message) => new(409, code, message);
}
