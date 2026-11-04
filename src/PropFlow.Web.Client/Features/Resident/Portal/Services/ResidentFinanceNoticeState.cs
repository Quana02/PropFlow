using PropFlow.Web.Client.Features.Finance.Services;

namespace PropFlow.Web.Client.Features.Resident.Portal.Services;

// Shares the existing notification API between the toolbar, dashboard and billing view.
public sealed class ResidentFinanceNoticeState(FinanceCycleApi api)
{
    public ResidentNotice[] Items { get; private set; } = [];
    public int UnreadCount => Items.Count(x => !x.IsRead);
    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;
    private int generation;
    private int receivedVersion;
    public void Receive(ResidentNotice notice)
    {
        receivedVersion++;
        Items = Items.Where(x => x.Id != notice.Id).Append(notice).OrderByDescending(x => x.CreatedAt).Take(100).ToArray();
        Error = null;
        Changed?.Invoke();
    }
    public void Reset()
    {
        generation++; Items = []; Error = null; Loading = false; Changed?.Invoke();
    }
    public async Task ReloadAsync()
    {
        if (Loading) return;
        var requestGeneration = generation;
        var requestVersion = receivedVersion;
        Loading = true;
        try {
            var result = await api.GetAsync<ResidentNotice[]>("api/v1/resident/notifications");
            if (requestGeneration == generation) {
                if (result.IsSuccess) {
                    var snapshot = result.Data ?? [];
                    Items = requestVersion == receivedVersion ? snapshot : snapshot.Concat(Items.Where(x => !snapshot.Any(y => y.Id == x.Id)))
                        .OrderByDescending(x => x.CreatedAt).Take(100).ToArray();
                }
                Error = result.IsSuccess ? null : result.Message;
            }
        } catch { if (requestGeneration == generation) Error = "Không thể tải thông báo. Vui lòng thử lại."; }
        finally { if (requestGeneration == generation) { Loading = false; Changed?.Invoke(); } }
    }
    public async Task ReadAsync(Guid id)
    {
        var result = await api.PostAsync<object>($"api/v1/resident/notifications/{id}/read", new { });
        if (result.IsSuccess) await ReloadAsync();
        else { Error = result.Message; Changed?.Invoke(); }
    }
    public async Task ReadAllAsync()
    {
        foreach (var notice in Items.Where(x => !x.IsRead).ToArray()) {
            var result = await api.PostAsync<object>($"api/v1/resident/notifications/{notice.Id}/read", new { });
            if (!result.IsSuccess) { Error = result.Message; Changed?.Invoke(); return; }
        }
        await ReloadAsync();
    }
}
