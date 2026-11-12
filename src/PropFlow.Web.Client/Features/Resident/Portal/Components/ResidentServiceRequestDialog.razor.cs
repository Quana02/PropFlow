using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Web.Client.Features.Resident.Portal.Models;
using PropFlow.Web.Client.Features.Resident.Portal.Services;
using PropFlow.Web.Client.Features.ServiceRequests.Models;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Feedback;

namespace PropFlow.Web.Client.Features.Resident.Portal.Components;

public partial class ResidentServiceRequestDialog
{
    [Inject] private IResidentServiceRequestApiClient Api { get; set; } = null!;
    [Inject] private ToastService Toasts { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [CascadingParameter] private ResidentPortalProfileContext ProfileContext { get; set; } = ResidentPortalProfileContext.Loading;
    [Parameter] public EventCallback<ResidentServiceRequestCreatedResponse> RequestCreated { get; set; }

    private readonly ToastLifetime toastLifetime = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SubmitServiceRequestForm _form = new();
    private ApiResult<ResidentServiceRequestCreatedResponse>? _result;
    private ResidentServiceRequestCreatedResponse? _created;
    private IReadOnlyList<IBrowserFile> _selectedFiles = [];
    private string? _attachmentError, _voiceNotice, _interimText, _checkNotice, _checkedDescription;
    private string _voiceBase = "", _popupDescription = "";
    private ResidentServiceRequestDraftReview? _review;
    private bool _open, _submitting, _checking, _listening, _voiceSupported, _voiceInitialized, _disposed;
    private bool _showReviewPopup, _popupSendIntent;
    private int _voiceEpoch, _draftRevision;
    private ElementReference _descriptionInput;
    private IJSObjectReference? _voiceModule, _voiceSession;
    private DotNetObjectReference<ResidentServiceRequestDialog>? _voiceReference;
    private CancellationTokenSource? _checkCancellation;

    private string ReviewMessage => _review?.Available == true
        ? "Bạn cần nói hoặc nhập rõ thêm: " + string.Join("; ", _review.MissingInformation.Select(item => item.Message)) +
          ". Nội dung đã nhập vẫn được giữ lại. Bấm micro để nói bổ sung hoặc nhập trực tiếp."
        : _review?.Notice ?? "AI chưa kiểm tra được nội dung. Bạn có thể tự bổ sung thông tin và gửi Ban quản lý.";
    private string ScheduleLabel => string.Join(" · ", new[]
        { _form.PreferredDate?.ToString("dd/MM/yyyy"), _form.PreferredTimeCode is null ? null : ServiceRequestAreaText.TimeSlot(_form.PreferredTimeCode) }.Where(x => x is not null));

    public void Open(string categoryCode, string title)
    {
        _checkCancellation?.Cancel();
        _voiceEpoch++;
        _listening = false;
        _result = null;
        _created = null;
        _selectedFiles = [];
        _attachmentError = _interimText = null;
        if (_voiceSupported || !_voiceInitialized) _voiceNotice = null;
        _form.Description = "";
        InvalidateReview();
        _open = true;
        StateHasChanged();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_open || _voiceInitialized || _disposed) return;
        _voiceInitialized = true;
        try
        {
            _voiceModule = await JS.InvokeAsync<IJSObjectReference>("import", "./js/resident-request-voice.js?v=20261112-1");
            if (_disposed) { await _voiceModule.DisposeAsync(); return; }
            _voiceSupported = await _voiceModule.InvokeAsync<bool>("isSupported");
            if (_disposed) return;
            if (_voiceSupported)
            {
                _voiceReference = DotNetObjectReference.Create(this);
                _voiceSession = await _voiceModule.InvokeAsync<IJSObjectReference>("create", _voiceReference);
            }
            else _voiceNotice = "Trình duyệt này chưa hỗ trợ nhập giọng nói. Bạn có thể nhập bằng bàn phím hoặc thử Chrome/Edge với HTTPS hoặc localhost.";
        }
        catch (JSException) { _voiceNotice = "Chưa khởi tạo được micro. Bạn vẫn có thể nhập yêu cầu bằng bàn phím."; }
        if (!_disposed) await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleVoiceAsync()
    {
        if (_voiceSession is null || _submitting || _checking || _disposed) return;
        if (_listening) { await _voiceSession.InvokeVoidAsync("stop"); return; }
        InvalidateReview();
        _voiceBase = _form.Description.TrimEnd();
        _voiceNotice = _interimText = null;
        _listening = true;
        var epoch = ++_voiceEpoch;
        try
        {
            if (!await _voiceSession.InvokeAsync<bool>("start", epoch))
            {
                _listening = false;
                _voiceNotice = "Không bắt đầu được nhận diện giọng nói. Hãy kiểm tra quyền micro hoặc nhập bằng bàn phím.";
            }
        }
        catch (JSException) { _listening = false; _voiceNotice = "Không mở được micro. Bạn vẫn có thể nhập nội dung bằng bàn phím."; }
    }

    [JSInvokable]
    public Task OnVoiceResult(int epoch, string finalText, string interimText) => InvokeAsync(async () =>
    {
        if (_disposed || !_open || epoch != _voiceEpoch || !_listening) return;
        var combined = string.IsNullOrWhiteSpace(finalText) ? _voiceBase : string.Join(" ", new[] { _voiceBase, finalText.Trim() }.Where(x => x.Length > 0));
        if (combined.Length <= 3500) _form.Description = combined;
        else
        {
            _voiceNotice = "Mô tả đã đạt giới hạn 3500 ký tự. Hãy rút gọn nội dung trước khi nói thêm.";
            if (_voiceSession is not null) await _voiceSession.InvokeVoidAsync("stop");
        }
        _interimText = interimText;
        StateHasChanged();
    });

    [JSInvokable]
    public Task OnVoiceEnded(int epoch, string error) => InvokeAsync(async () =>
    {
        if (_disposed || !_open || epoch != _voiceEpoch) return;
        _listening = false;
        _interimText = null;
        if (!string.IsNullOrEmpty(error))
            _voiceNotice = error switch
            {
                "not-allowed" or "service-not-allowed" => "Micro chưa được cấp quyền. Hãy cho phép micro trong trình duyệt hoặc nhập bằng bàn phím.",
                "audio-capture" => "Không tìm thấy micro hoạt động. Hãy kiểm tra thiết bị.",
                "no-speech" => "Chưa nghe rõ nội dung. Bạn có thể bấm micro để nói lại.",
                "network" => "Nhận diện giọng nói bị gián đoạn kết nối. Phần chữ đã nhận vẫn được giữ lại.",
                _ => "Nhận diện đã dừng. Bạn có thể nhập tiếp hoặc bấm micro để thử lại."
            };
        StateHasChanged();
        if (string.IsNullOrEmpty(error) && _form.Description.Trim() != _voiceBase.Trim() && _form.Description.Trim().Length >= 3)
            await CheckDraftAsync(submitIntent: false);
        else if (string.IsNullOrEmpty(error) && string.IsNullOrWhiteSpace(_voiceNotice)) _voiceNotice = "Chưa nhận được nội dung mới. Bạn có thể nói lại hoặc nhập trực tiếp.";
        if (!_disposed) StateHasChanged();
    });

    private void InvalidateReview()
    {
        _draftRevision++;
        _review = null;
        _checkedDescription = _checkNotice = null;
        _form.PreferredDate = null;
        _form.PreferredTimeCode = null;
        _showReviewPopup = false;
    }

    private async Task CheckManuallyAsync() => await CheckDraftAsync(submitIntent: false, force: true);

    private async Task<bool> CheckDraftAsync(bool submitIntent, bool force = false)
    {
        if (_disposed || !_open || _checking || _listening || _submitting) return false;
        var description = _form.Description.Trim();
        if (description.Length is < 3 or > 3500) return false;
        if (force || _review is null || _checkedDescription != description)
        {
            _checking = true;
            _checkNotice = null;
            StateHasChanged();
            var revision = _draftRevision;
            using var check = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            _checkCancellation = check;
            try
            {
                var result = await Api.CheckDraftAsync(new(description), check.Token);
                if (_disposed || !_open || revision != _draftRevision || description != _form.Description.Trim()) return false;
                _review = result.IsSuccess && result.Data is not null ? result.Data :
                    new(false, [], Notice: result.Message ?? "AI tạm thời chưa kiểm tra được nội dung. Bạn vẫn có thể tự kiểm tra và gửi yêu cầu.");
                _checkedDescription = description;
                _form.PreferredDate = _review.Available ? _review.PreferredDate : null;
                _form.PreferredTimeCode = _review.Available ? _review.PreferredTimeCode : null;
            }
            catch (OperationCanceledException) when (check.IsCancellationRequested) { return false; }
            finally
            {
                _checking = false;
                if (ReferenceEquals(_checkCancellation, check)) _checkCancellation = null;
            }
        }
        if (_review is null) return false;
        if (!_review.Available || _review.MissingInformation.Count > 0)
        {
            _popupDescription = description;
            _popupSendIntent = submitIntent;
            _showReviewPopup = true;
            _checkNotice = _review.Available ? $"AI đề nghị bổ sung {_review.MissingInformation.Count} thông tin." : "AI chưa kiểm tra được nội dung.";
            return false;
        }
        _checkNotice = "AI chưa thấy thiếu thông tin. Bạn hãy đọc lại nội dung trước khi gửi.";
        return true;
    }

    private async Task SubmitAsync()
    {
        if (await CheckDraftAsync(submitIntent: true)) await SendAsync();
    }

    private async Task SendFromPopupAsync()
    {
        if (!_popupSendIntent) { await DismissReviewAsync(); return; }
        _showReviewPopup = false;
        if (_popupDescription != _form.Description.Trim()) { await SubmitAsync(); return; }
        await SendAsync();
    }

    private async Task DismissReviewAsync()
    {
        _showReviewPopup = false;
        if (_open && !_disposed) await _descriptionInput.FocusAsync();
    }

    private async Task SendAsync()
    {
        if (_submitting || _checking || _listening || _disposed || !_open) return;
        var operationFeedback = Toasts.BeginOperation(toastLifetime);
        _submitting = true;
        _result = null;
        try
        {
            _result = await Api.CreateAsync(new(null, null, _form.Description.Trim(), null, null,
                _form.PreferredDate, _form.PreferredTimeCode), lifetime.Token);
            if (_disposed) return;
            if (_result.IsSuccess && _result.Data is not null)
            {
                _created = _result.Data;
                if (operationFeedback.IsCurrent) await RequestCreated.InvokeAsync(_created);
            }
            else if (_result.ValidationErrors?.Count is not > 0)
                operationFeedback.Publish(() => Toasts.Error(_result.Message));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { _submitting = false; }
    }

    private void SelectAttachments(InputFileChangeEventArgs args)
    {
        _attachmentError = null;
        if (args.FileCount > 5) { _selectedFiles = []; _attachmentError = "Chỉ được chọn tối đa 5 tệp."; return; }
        var files = args.GetMultipleFiles(5);
        if (files.Any(file => file.Size > 25 * 1024 * 1024)) { _selectedFiles = []; _attachmentError = "Mỗi tệp phải có dung lượng không quá 25MB."; return; }
        _selectedFiles = files;
    }

    private async Task Close()
    {
        if (_submitting) return;
        _open = false;
        _voiceEpoch++;
        _listening = false;
        _checkCancellation?.Cancel();
        InvalidateReview();
        if (_voiceSession is not null) await _voiceSession.InvokeVoidAsync("cancel");
        _result = null;
        _created = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        lifetime.Cancel();
        toastLifetime.Dispose();
        try
        {
            if (_voiceSession is not null) { await _voiceSession.InvokeVoidAsync("dispose"); await _voiceSession.DisposeAsync(); }
            if (_voiceModule is not null) await _voiceModule.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        finally { _voiceReference?.Dispose(); lifetime.Dispose(); }
    }
}
