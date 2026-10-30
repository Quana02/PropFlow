using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Infrastructure.Persistence;

namespace PropFlow.Modules.Payments.Infrastructure.PaymentSettings;

public sealed record SePaySandboxState(bool Configured, bool Verified, int Version, int AccountCount,
    DateTimeOffset? CheckedAt, DateTimeOffset? UpdatedAt, bool DemoEnabled = false, bool WebhookKeyConfigured = false, string? WebhookApiKey = null);
public sealed record SePaySandboxDraft(string Action, string? ApiToken, int ExpectedVersion);
public sealed record SePaySandboxConfirm(string Action, string? ApiToken, int ExpectedVersion, Guid ChallengeId, string Code);
public sealed record SePaySandboxAccount(string Id, string BankName, string BankCode, string MaskedAccountNumber,
    string AccountName, bool Active);
public sealed record SePaySandboxProbe(bool Connected, string Code, string Message, int AccountCount = 0);
public sealed record SandboxReceivingAccount(string Id, string BankName, string BankCode, string AccountNumber, string AccountName, bool Active, string ShortName);

// Setup only: this client cannot select a live payment destination, collect funds or receive bank webhooks.
public sealed class SePaySandbox(HttpClient http, PaymentsDbContext db, IDataProtectionProvider protection,
    IPrivilegedActionOtp otp, TimeProvider clock)
{
    public const string ApiOrigin = "https://userapi-sandbox.sepay.vn";
    public const string WebhookKeyPurpose = "PropFlow.SePay.Sandbox.WebhookKey.v1";
    public const string WebhookPath = "/api/v1/payments/webhooks/sepay/sandbox";
    private const string Purpose = "SEPAY_SANDBOX_CONNECTION_CHANGE";
    private readonly IDataProtector protector = protection.CreateProtector("PropFlow.SePay.Sandbox.ApiToken.v1");
    private readonly IDataProtector hookProtector = protection.CreateProtector(WebhookKeyPurpose);

    public async Task<SePaySandboxState> StateAsync(CancellationToken ct) => View(await db.SePaySandboxConnections.AsNoTracking().SingleOrDefaultAsync(ct));

    public async Task<SePaySandboxProbe> PreviewAsync(SePaySandboxDraft request, CancellationToken ct)
    {
        var token = Token(request.ApiToken);
        try { return Success((await ReadAccountsAsync(token, ct)).Count); }
        catch (PaymentSettingsFailure e) { return new(false, e.Code, e.Message); }
    }

    public async Task<PrivilegedActionChallengeResult> ChallengeAsync(Guid actor, SePaySandboxDraft request, CancellationToken ct)
    {
        var action = Action(request.Action);
        var row = await db.SePaySandboxConnections.AsNoTracking().SingleOrDefaultAsync(ct);
        EnsureVersion(row, request.ExpectedVersion);
        if (action is not ("CONNECT" or "DEMO_OFF") && row?.ProtectedApiToken is null) throw Missing();
        if (action == "DEMO_ON" && row?.LastCheckSucceeded != true) throw Unverified();
        var token = action == "CONNECT" ? Token(request.ApiToken) : "";
        if (action == "CONNECT") await ReadAccountsAsync(token, ct);
        return await otp.RequestAsync(actor, Purpose, Hash(action, token, request.ExpectedVersion), ct);
    }

    public async Task<SePaySandboxState> ConfirmAsync(Guid actor, SePaySandboxConfirm request, CancellationToken ct)
    {
        var action = Action(request.Action);
        var token = action == "CONNECT" ? Token(request.ApiToken) : "";
        await using var tx = await LockAsync(ct);
        var row = await db.SePaySandboxConnections.SingleOrDefaultAsync(ct);
        if (row is not null) await db.Entry(row).ReloadAsync(ct);
        EnsureVersion(row, request.ExpectedVersion);
        if (action is not ("CONNECT" or "DEMO_OFF") && row?.ProtectedApiToken is null) throw Missing();
        if (action == "DEMO_ON" && row?.LastCheckSucceeded != true) throw Unverified();
        await otp.VerifyAsync(actor, request.ChallengeId, Purpose, Hash(action, token, request.ExpectedVersion), request.Code, ct);
        var accounts = action == "CONNECT" ? await ReadAccountsAsync(token, ct) : null;
        if (row is null) { row = new(); db.SePaySandboxConnections.Add(row); }
        if (action == "CONNECT")
        {
            row.ProtectedApiToken = protector.Protect(token);
            row.LastCheckSucceeded = true; row.AccountCount = accounts!.Count; row.CheckedAt = clock.GetUtcNow();
        }
        else if (action == "DISCONNECT")
        {
            row.ProtectedApiToken = null;
            row.LastCheckSucceeded = false; row.AccountCount = 0; row.CheckedAt = null;
        }
        else if (action == "WEBHOOK_KEY") row.ProtectedWebhookKey ??= hookProtector.Protect(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        else row.DemoEnabled = action == "DEMO_ON";
        row.Version++; row.UpdatedBy = actor; row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return action == "WEBHOOK_KEY" ? View(row) with { WebhookApiKey = hookProtector.Unprotect(row.ProtectedWebhookKey!) } : View(row);
    }

    public async Task<SePaySandboxProbe> CheckAsync(CancellationToken ct)
    {
        var accounts = await AccountsAsync(ct);
        return Success(accounts.Count);
    }

    public async Task<IReadOnlyList<SePaySandboxAccount>> AccountsAsync(CancellationToken ct)
    {
        // Keep a concurrent token replacement from being overwritten by a delayed connection check.
        await using var tx = await LockAsync(ct);
        var row = await db.SePaySandboxConnections.SingleOrDefaultAsync(ct) ?? throw Missing();
        await db.Entry(row).ReloadAsync(ct);
        if (row.ProtectedApiToken is null) throw Missing();
        try
        {
            var accounts = await ReadAccountsAsync(protector.Unprotect(row.ProtectedApiToken), ct);
            row.CheckedAt = clock.GetUtcNow(); row.LastCheckSucceeded = true; row.AccountCount = accounts.Count;
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return accounts.Select(x => new SePaySandboxAccount(x.Id, x.BankName, x.BankCode, "******" + x.AccountNumber[^4..], x.AccountName, x.Active)).ToArray();
        }
        catch (PaymentSettingsFailure e) when (e.Code is "sepay_sandbox_unauthorized" or "sepay_sandbox_forbidden")
        {
            row.CheckedAt = clock.GetUtcNow(); row.LastCheckSucceeded = false;
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            throw;
        }
        catch (CryptographicException) { throw new PaymentSettingsFailure(409, "sepay_sandbox_key_unavailable", "Không thể đọc khóa Sandbox đã lưu. Vui lòng nhập lại token và xác nhận OTP."); }
    }

    public async Task<SandboxReceivingAccount> ReceivingAccountAsync(CancellationToken ct)
    {
        var row = await db.SePaySandboxConnections.AsNoTracking().SingleOrDefaultAsync(ct);
        if (row?.DemoEnabled != true || row.ProtectedApiToken is null || !row.LastCheckSucceeded || row.ProtectedWebhookKey is null) throw Unverified();
        return (await ReadAccountsAsync(protector.Unprotect(row.ProtectedApiToken), ct)).Where(x => x.Active).OrderBy(x => x.Id).FirstOrDefault()
            ?? throw new PaymentSettingsFailure(409, "sandbox_bank_missing", "Hãy tạo tài khoản ngân hàng trong SePay Test mode rồi đồng bộ lại.");
    }

    private async Task<IReadOnlyList<SandboxReceivingAccount>> ReadAccountsAsync(string token, CancellationToken ct)
    {
        var accounts = new List<SandboxReceivingAccount>();
        try
        {
            for (var page = 1; page <= 100; page++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiOrigin}/v2/bank-accounts?page={page}&per_page=100");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized) throw new PaymentSettingsFailure(409, "sepay_sandbox_unauthorized", "Token Sandbox không hợp lệ hoặc đã bị thu hồi. Hãy lấy token trong Test mode → API Access.");
                if (response.StatusCode == HttpStatusCode.Forbidden) throw new PaymentSettingsFailure(409, "sepay_sandbox_forbidden", "Token chưa được phép truy cập tài khoản thử nghiệm. Hãy kiểm tra trên SePay.");
                if (!response.IsSuccessStatusCode) throw Unavailable();
                using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = data.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("status", out var status) ||
                    status.ValueKind != JsonValueKind.String || status.GetString() != "success" ||
                    !root.TryGetProperty("data", out var items) || items.ValueKind != JsonValueKind.Array) throw Unavailable();
                foreach (var item in items.EnumerateArray()) accounts.Add(Parse(item));
                if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
                    meta.TryGetProperty("pagination", out var pagination) && pagination.ValueKind == JsonValueKind.Object)
                {
                    if (pagination.TryGetProperty("last_page", out var last) && last.TryGetInt32(out var lastPage) && page >= lastPage) return accounts.DistinctBy(x => x.Id).ToArray();
                    if (pagination.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.False) return accounts.DistinctBy(x => x.Id).ToArray();
                }
                else if (items.GetArrayLength() < 100) return accounts.DistinctBy(x => x.Id).ToArray();
            }
            throw Unavailable();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        catch (HttpRequestException) { throw Unavailable(); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException) { throw Unavailable(); }
    }

    private static SandboxReceivingAccount Parse(JsonElement item)
    {
        static string Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()!.Trim() : "";
        var id = Text(item, "id");
        if (string.IsNullOrWhiteSpace(id)) throw Unavailable();
        var number = Text(item, "account_number");
        if (number.Length < 4) throw Unavailable();
        var active = item.TryGetProperty("active", out var a) && (a.ValueKind == JsonValueKind.True || a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var n) && n == 1 || a.ValueKind == JsonValueKind.String && a.GetString() is "1" or "true");
        return new(id, Text(item, "bank_full_name"), Text(item, "bank_code"), number, Text(item, "account_holder_name"), active, Text(item, "bank_short_name"));
    }

    private async Task<IDbContextTransaction?> LockAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return null;
        var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(728405193)", ct);
        return tx;
    }
    private static string Token(string? token)
    {
        token = token?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsWhiteSpace) || token.Any(char.IsControl))
            throw new PaymentSettingsFailure(400, "sepay_sandbox_token_invalid", "Vui lòng nhập API Token từ mục API Access trong Test mode của SePay.");
        return token;
    }
    private static string Action(string action) => action?.Trim().ToUpperInvariant() switch
    { "CONNECT" => "CONNECT", "DISCONNECT" => "DISCONNECT", "DEMO_ON" => "DEMO_ON", "DEMO_OFF" => "DEMO_OFF", "WEBHOOK_KEY" => "WEBHOOK_KEY", _ => throw new PaymentSettingsFailure(400, "invalid_action", "Thao tác Sandbox không hợp lệ.") };
    private static string Hash(string action, string token, int version) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"SEPAY_SANDBOX|{action}|{version}|{token}")));
    private static void EnsureVersion(SePaySandboxConnection? row, int expected)
    { if (expected < 0 || (row?.Version ?? 0) != expected) throw new PaymentSettingsFailure(409, "sepay_sandbox_changed", "Cấu hình Sandbox đã thay đổi. Vui lòng tải lại trước khi xác nhận."); }
    private static SePaySandboxState View(SePaySandboxConnection? row) => new(row?.ProtectedApiToken is not null,
        row?.ProtectedApiToken is not null && row.LastCheckSucceeded, row?.Version ?? 0, row?.AccountCount ?? 0, row?.CheckedAt, row?.UpdatedAt, row?.DemoEnabled == true, row?.ProtectedWebhookKey is not null);
    private static SePaySandboxProbe Success(int count) => new(true, "sepay_sandbox_connected", count == 0
        ? "Kết nối Sandbox thành công. Hãy tạo tài khoản ngân hàng trong Test mode rồi đồng bộ lại."
        : "Kết nối Sandbox thành công. Đã đọc được tài khoản ngân hàng thử nghiệm.", count);
    private static PaymentSettingsFailure Missing() => new(409, "sepay_sandbox_missing", "Chưa lưu kết nối Sandbox. Hãy nhập API Token và xác nhận OTP email.");
    private static PaymentSettingsFailure Unverified() => new(409, "sepay_sandbox_unverified", "Hãy kiểm tra kết nối Sandbox thành công trước khi bật mô phỏng FE-05.");
    private static PaymentSettingsFailure Unavailable() => new(503, "sepay_sandbox_unavailable", "Chưa thể đọc dữ liệu Sandbox từ SePay. Vui lòng thử lại sau.");
}
