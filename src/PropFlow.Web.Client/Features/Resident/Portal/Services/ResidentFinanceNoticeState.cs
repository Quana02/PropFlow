using PropFlow.Web.Client.Features.Finance.Services;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

// Shares the existing notification API between the toolbar, dashboard and billing view.
public sealed class ResidentFinanceNoticeState(FinanceCycleApi api)
{
    public ResidentNotice[] Items { get; private set; } = [];
    public int UnreadCount => unreadCount ?? Items.Count(x => !x.IsRead);
    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;
    private int generation;
    private int receivedVersion;
    private int? unreadCount;
    private bool reloadPending;
    private const string Path = "api/v1/notifications";
    public void Receive(ResidentNotice notice)
    {
        receivedVersion++;
        if (unreadCount.HasValue && !notice.IsRead && Items.All(x => x.Id != notice.Id)) unreadCount++;
        Items = Items.Where(x => x.Id != notice.Id).Append(notice).OrderByDescending(x => x.CreatedAt).Take(100).ToArray();
        Error = null;
        Changed?.Invoke();
    }
    public void Reset()
    {
        generation++; Items = []; unreadCount = null; reloadPending = false; Error = null; Loading = false; Changed?.Invoke();
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
