using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Authentication.Services;
using PropFlow.Web.Client.Shared.Feedback;
using PropFlow.Web.Client.Shared.Forms;

namespace PropFlow.Web.Client.Features.Resident.Management.Pages;

public partial class ResidentAccounts : IAsyncDisposable
{
    [Inject] private AuthenticationService Auth { get; set; } = default!;
    [Inject] private ToastService Toasts { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private static readonly PropFlowSelectOption[] ReasonOptions =
    [
        new(nameof(ResidentEmailChangeReason.LostMailbox), "Mất quyền truy cập hộp thư"),
        new(nameof(ResidentEmailChangeReason.IncorrectAddress), "Sửa địa chỉ đã khai báo sai"),
        new(nameof(ResidentEmailChangeReason.ReplacingMailbox), "Đổi hộp thư sử dụng")
    ];

    private readonly ToastLifetime lifetime = new();
    private ResidentAccountPage results = new([], 0, 1, 20);
    private ResidentAccountItem? selected;
    private RequestForm form = new();
    private EditContext? editContext;
    private ApiValidationMessages? validation;
    private IJSObjectReference? dialogModule;
    private ElementReference dialog;
    private string searchInput = "", search = "";
    private string? loadError;
    private int requestedPage = 1;
    private bool loading, saving, openDialog, disposed;

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(results.TotalCount / (double)results.PageSize));

    protected override Task OnInitializedAsync() => LoadAsync(1);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!openDialog || disposed) return;
        openDialog = false;
        if (dialogModule is null)
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./Features/Resident/Management/Pages/ResidentAccounts.razor.js");
            if (disposed)
            {
                await module.DisposeAsync();
                return;
            }
            dialogModule = module;
        }
        await dialogModule.InvokeVoidAsync("open", dialog);
    }

    private Task SearchAsync()
    {
        if (loading) return Task.CompletedTask;
        search = searchInput.Trim();
        return LoadAsync(1);
    }

    private Task ClearSearchAsync()
    {
        searchInput = "";
        return SearchAsync();
    }

    private async Task LoadAsync(int index)
    {
        if (loading || disposed) return;
        requestedPage = index;
        loading = true;
        loadError = null;
        var result = await Auth.ListResidentAccountsAsync(search, index);
        if (disposed) return;
        loading = false;
        if (result.IsSuccess && result.Data is not null) results = result.Data;
        else loadError = result.Message ?? "Không thể tải danh sách.";
    }

    private void Select(ResidentAccountItem account)
    {
        if (selected is not null) return;
        selected = account;
        form = new();
        editContext = new(form);
        validation = new(editContext, ("NewEmail", nameof(form.NewEmail)), ("Reason", nameof(form.Reason)),
            ("IdentityCheckedInPerson", nameof(form.IdentityCheckedInPerson)));
        openDialog = true;
    }

    private async Task RequestAsync()
    {
        if (saving || selected?.CanRequestEmailChange != true || !form.IdentityCheckedInPerson ||
            !Enum.TryParse<ResidentEmailChangeReason>(form.Reason, out var reason)) return;
        var feedback = Toasts.BeginOperation(lifetime);
        saving = true;
        validation?.Clear();
        var result = await Auth.RequestResidentEmailAsync(selected.ResidentId, new(form.NewEmail, reason, true));
        if (disposed) return;
        saving = false;
        if (result.IsSuccess)
        {
            await CloseDetailsAsync();
            feedback.Publish(() => Toasts.Success("Đã gửi yêu cầu đổi email. Cư dân cần tự xác minh mã."));
        }
        else if (feedback.IsCurrent)
        {
            validation?.Apply(result);
            if (editContext?.GetValidationMessages().Any() != true)
                feedback.Publish(() => Toasts.Error(result.Message ?? "Không thể gửi yêu cầu."));
        }
    }

    private async Task CloseDetailsAsync()
    {
        if (saving || selected is null) return;
        if (dialogModule is not null) await dialogModule.InvokeVoidAsync("close", dialog);
        selected = null;
        validation?.Dispose();
        validation = null;
        editContext = null;
    }

    private static string ApartmentLabel(ResidentAccountItem account) =>
        account.Apartments.Count == 0 ? "—" : string.Join(", ", account.Apartments);

    private static string VerificationLabel(ResidentAccountItem account) =>
        account.Username is null ? "—" : account.EmailVerified ? "Email đã xác minh" : "Email chưa xác minh";

    private static string StatusTone(string? status) => status switch
    {
        "ACTIVE" => "positive",
        "PENDING" or "TEMPORARILY_LOCKED" => "caution",
        "LOCKED" or "DISABLED" or "SUSPENDED" => "negative",
        _ => "neutral"
    };

    private static string StatusLabel(string? status) => status switch
    {
        null => "Chưa liên kết", "ACTIVE" => "Đang hoạt động", "LOCKED" => "Đã khóa",
        "TEMPORARILY_LOCKED" => "Khóa tạm thời",
        "DISABLED" => "Vô hiệu hóa", "PENDING" => "Chờ kích hoạt", "SUSPENDED" => "Tạm đình chỉ",
        _ => "Không khả dụng"
    };

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        lifetime.Dispose();
        validation?.Dispose();
        if (dialogModule is not null) await dialogModule.DisposeAsync();
    }

    private sealed class RequestForm
    {
        [Required(ErrorMessage = "Nhập email mới."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(255)]
        public string NewEmail { get; set; } = "";
        [Required(ErrorMessage = "Chọn lý do.")]
        public string Reason { get; set; } = "";
        public bool IdentityCheckedInPerson { get; set; }
    }
}
