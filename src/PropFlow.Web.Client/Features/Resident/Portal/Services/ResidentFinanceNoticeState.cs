using System.Text.Json;
using Microsoft.JSInterop;
using PropFlow.Web.Client.Features.Finance.Services;
using PropFlow.Web.Client.Services.Authentication;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

// Shares the existing notification API between the toolbar, dashboard and billing view.
public sealed class ResidentFinanceNoticeState(FinanceCycleApi api, AuthSession? session = null, IJSRuntime? js = null)
{
    public ResidentNotice[] Items { get; private set; } = [];
    public int UnreadCount => unreadCount ?? Items.Count(x => !x.IsRead);
    public bool HasNewNotices => UnreadCount > 0 &&
        (!inboxAcknowledged || Items.Any(x => !x.IsRead && !acknowledged.Contains(x.Id)));
    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;
    private int generation;
    private int receivedVersion;
    private int? unreadCount;
    private bool reloadPending;
    private readonly HashSet<Guid> acknowledged = [];
    private readonly HashSet<Guid> receivedAfterOpening = [];
    private bool feedLoaded;
    private bool inboxAcknowledged;
    private bool acknowledgeInitialFeed;
    private string? acknowledgementKey;
    private const string Path = "api/v1/notifications";

    public void AcknowledgeInbox()
    {
        inboxAcknowledged = true;
        acknowledgeInitialFeed = !feedLoaded;
        receivedAfterOpening.Clear();
        acknowledged.UnionWith(Items.Select(item => item.Id));
        SaveAcknowledgement();
        Changed?.Invoke();
    }

    public void Receive(ResidentNotice notice)
    {
        receivedVersion++;
        if (acknowledgeInitialFeed) receivedAfterOpening.Add(notice.Id);
        if (unreadCount.HasValue && !notice.IsRead && Items.All(x => x.Id != notice.Id)) unreadCount++;
        Items = Items.Where(x => x.Id != notice.Id).Append(notice).OrderByDescending(x => x.CreatedAt).Take(100).ToArray();
        Error = null;
        Changed?.Invoke();
    }
    public void Reset()
    {
        generation++; Items = []; unreadCount = null; reloadPending = false; Error = null; Loading = false;
        feedLoaded = false;
        acknowledgeInitialFeed = false;
        receivedAfterOpening.Clear();
        acknowledged.Clear();
        inboxAcknowledged = false;
        acknowledgementKey = session?.User is { } user ? $"propflow:notification-inbox:{user.Id:N}" : null;
        if (acknowledgementKey is { } key && js is IJSInProcessRuntime browser)
        {
            try
            {
                var stored = browser.Invoke<string?>("sessionStorage.getItem", key);
                if (stored is not null && JsonSerializer.Deserialize<Guid[]>(stored) is { } ids)
                {
                    acknowledged.UnionWith(ids);
                    inboxAcknowledged = true;
                }
            }
            catch (Exception error) when (error is JSException or JsonException) { /* Storage is optional. */ }
        }
        Changed?.Invoke();
    }

    private void SaveAcknowledgement()
    {
        if (acknowledgementKey is not { } key || js is not IJSInProcessRuntime browser) return;
        try { browser.InvokeVoid("sessionStorage.setItem", key, JsonSerializer.Serialize(acknowledged)); }
        catch (JSException) { /* Keep the indicator usable when browser storage is disabled. */ }
    }
    public async Task ReloadAsync()
    {
        if (Loading) { reloadPending = true; return; }
        var requestGeneration = generation;
        var requestVersion = receivedVersion;
        Loading = true;
        Changed?.Invoke();
        try {
            var result = await api.GetAsync<ResidentNotice[]>(Path);
            var count = await api.GetAsync<UnreadTotal>($"{Path}/unread-count");
            if (requestGeneration == generation) {
                if (result.IsSuccess) {
                    var snapshot = result.Data ?? [];
                    if (acknowledgeInitialFeed)
                    {
                        // The first response completes the opening action. Later realtime arrivals stay new.
                        acknowledged.UnionWith(snapshot.Where(item => !receivedAfterOpening.Contains(item.Id)).Select(item => item.Id));
                        acknowledgeInitialFeed = false;
                        receivedAfterOpening.Clear();
                        SaveAcknowledgement();
                    }
                    feedLoaded = true;
                    Items = requestVersion == receivedVersion ? snapshot : snapshot.Concat(Items.Where(x => !snapshot.Any(y => y.Id == x.Id)))
                        .OrderByDescending(x => x.CreatedAt).Take(100).ToArray();
                }
                if (requestVersion == receivedVersion && count.IsSuccess) unreadCount = count.Data?.Count;
                Error = result.IsSuccess ? null : result.Message;
                reloadPending |= requestVersion != receivedVersion;
            }
        } catch { if (requestGeneration == generation) Error = "Không thể tải thông báo. Vui lòng thử lại."; }
        finally { if (requestGeneration == generation) { Loading = false; Changed?.Invoke(); } }
        if (requestGeneration == generation && reloadPending)
        {
            reloadPending = false;
            await ReloadAsync();
        }
    }
    public async Task<bool> ReadAsync(Guid id)
    {
        var requestGeneration = generation;
        var result = await api.PostAsync<object>($"{Path}/{id}/read", new { });
        if (requestGeneration != generation) return false;
        if (!result.IsSuccess) { Error = result.Message; Changed?.Invoke(); return false; }
        if (Items.Any(x => x.Id == id && !x.IsRead))
        {
            if (unreadCount > 0) unreadCount--;
            Items = Items.Select(x => x.Id == id ? x with { IsRead = true } : x).ToArray();
        }
        await ReloadAsync();
        return true;
    }
    public async Task ReadAllAsync()
    {
        var requestGeneration = generation;
        var result = await api.PostAsync<object>($"{Path}/read-all", new { });
        if (requestGeneration != generation) return;
        if (!result.IsSuccess) { Error = result.Message; Changed?.Invoke(); return; }
        await ReloadAsync();
    }

    private sealed record UnreadTotal(int Count);
}
