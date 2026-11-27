using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Web.Client.Features.Facility.Components;
using PropFlow.Web.Client.Features.Facility.Models;
using PropFlow.Web.Client.Features.Facility.Services;
using PropFlow.Web.Client.Services.Api;
using PropFlow.Web.Client.Shared.Forms;

namespace PropFlow.Web.Client.Features.Facility.Components;

public partial class ParkingWorkspace
{
    [Parameter]public Guid? EditorAreaId {get;set;}
    [Parameter]public Guid? SelectedAreaId {get;set;}
    private bool StandaloneEditor=>EditorAreaId.HasValue;
    [Inject]private ParkingApi Api {get;set;}=default!;
    [Inject]private IJSRuntime JS {get;set;}=default!;
    [Inject]private IBuildingApiClient Buildings {get;set;}=default!;
    [Inject]private NavigationManager Navigation {get;set;}=default!;
    private TimeZoneInfo? buildingZone;
    private readonly CancellationTokenSource lifetime=new();
    private ParkingAreaSummary[] areas=[];private ParkingAreaView? area;private ParkingActionView[] history=[];
    private ParkingDrawing working=ParkingDrawing.Empty;private ParkingShape? selectedShape;
    private Guid[] selectedIds=[];private int clipboardCount;private bool smartGuidesEnabled=true;
    private string spacingAxis="x",spacingUnit="m",spacingSummary="";private double spacingGap=.3,knownWidthMetres=2.5,groupRotationDegrees=180;
    private ParkingCanvas? canvas;private IBrowserFile? pendingFile;private IJSObjectReference? sourceModule;
    private string? sourceUrl,error,message;private string newName="",editName="",tool="select",selectedState="AVAILABLE",stateNote="";
    private string shapeLabel="",shapeKind="SPACE";private double shapeX,shapeY,shapeWidth,shapeHeight,shapeRotation;
    private bool editing,busy,loading,dirty,disposed,confirmPublish,compareOriginal,confirmCandidates,shapeReviewed,confirmClear;private string busyLabel="Đang tải…";
    private bool recognitionBusy,recognitionSucceeded;
    private bool confirmRemovePublished;
    private ParkingDrawing? recognitionReviewDrawing;
    private bool reviewingUnsavedRecognition;
    private string listingPrice = "";
    private string listingStatus = ParkingListingStatus.Available;
    private string? recognitionNotice;
    private double scaleLength;private string scaleUnit="";
    private int pdfPage=1,historyPage=1;
    private Func<Task>? discardAction;
    private object CanvasKey=Guid.NewGuid();
    private Guid uploadKey=Guid.NewGuid();
    private bool inspectorOpen;
    private static readonly (string Name,string[] Tools)[] ToolGroups=[
        ("Điều khiển",["select","region","pan"]),("Vẽ hình",["LINE","CURVE","SQUARE","RECT","LABEL"]),
        ("Bãi đỗ",["SPACE","DIVIDER","LANE"]),("Vật cản",["WALL","WALL_SQUARE","WALL_CIRCLE"]),("Ký hiệu",["ENTRANCE","EXIT","GATE"]),
        .. ParkingToolCatalog.Additional.GroupBy(tool => tool.Group).Select(group => (group.Key, group.Select(tool => tool.Kind).ToArray()))];
    private static string ToolIcon(string key)=>key switch{
        "select"=>"M5 3l14 9-7 1-4 7Z","pan"=>"M6 12V7a2 2 0 014 0V4a2 2 0 014 0v2a2 2 0 014 0v4a2 2 0 014 0v5c0 5-3 7-7 7h-2c-3 0-5-3-7-6l-2-3a2 2 0 013-2l2 2",
        "region"=>"M8 3H3V8M16 3H21V8M21 16V21H16M8 21H3V16M8 8H16V16H8Z","LINE"=>"M4 20L20 4","CURVE"=>"M3 19Q12 0 21 15","SQUARE"=>"M4 4H20V20H4Z","RECT"=>"M2 6H22V18H2Z","DIVIDER"=>"M3 4H21M12 4V21",
        "WALL"=>"M2 5H22V19H2ZM3 11L9 5M4 19L18 5M13 19L22 10","WALL_SQUARE"=>"M4 4H20V20H4ZM4 11L11 4M4 18L18 4M10 20L20 10M17 20L20 17",
        "WALL_CIRCLE"=>"M21 12a9 9 0 11-18 0 9 9 0 0118 0ZM4 9L9 4M4 16L16 4M8 20L20 8M15 20L20 15",
        "ENTRANCE"=>"M2 8H14V3L22 12 14 21V16H2Z","EXIT"=>"M22 8H10V3L2 12 10 21V16H22Z","GATE"=>"M3 21V8H6V21M6 8L21 4 22 7 6 11M19 21V15H22V21",
        "SPACE"=>"M4 21V3H20V21M10 17V7h3a3 3 0 010 6h-3","LANE"=>"M4 2V22M20 2V22M12 3V7M12 10V14M12 17V21","LABEL"=>"M4 5H20M12 5V21M8 21H16",
        "CROSSWALK"=>"M3 5V19M7.5 5V19M12 5V19M16.5 5V19M21 5V19","SIDEWALK"=>"M3 4H21V20H3ZM3 13H21M9 4V13M15 13V20",
        "TRAFFIC_ARROW"=>"M3 12H21M14 5L21 12L14 19","DASHED_LINE"=>"M2 12H6M10 12H14M18 12H22",
        "TRAFFIC_SIGN"=>"M19 10A7 7 0 1 1 5 10A7 7 0 0 1 19 10ZM12 8V12M12 17V22","ROUNDABOUT"=>"M5 7A8 8 0 0 1 19 7M19 17A8 8 0 0 1 5 17M19 3V7H15M5 21V17H9",
        "NO_PARKING"=>"M3 4H21V20H3ZM3 20L19 4M8 20L21 7","WHEEL_STOP"=>"M3 9H21V15H3ZM7 9V15M17 9V15","PILLAR"=>"M7 4H17V20H7ZM7 4L17 20M17 4L7 20",
        "GREEN_ISLAND"=>"M3 8Q4 4 8 4H16Q21 4 21 9V15Q21 20 16 20H8Q3 20 3 15Z","TREE"=>"M20 10A8 8 0 1 1 4 10A8 8 0 0 1 20 10ZM12 8V22",
        "BARRIER"=>"M3 21V9H6V21M6 9H22V14H6M10 9L8 14M16 9L14 14M22 9L20 14","GUARD_BOOTH"=>"M3 21V4H21V21ZM8 21V11H16V21",
        "RAMP"=>"M3 4H21V20H3ZM7 15L12 9L17 15","STAIRS"=>"M3 20H8V15H13V10H18V5H22","ELEVATOR"=>"M4 3H20V21H4ZM12 3V21M7 13L9 10M17 10L15 13","LIGHT"=>"M12 2V6M12 18V22M2 12H6M18 12H22M16 12A4 4 0 1 1 8 12A4 4 0 0 1 16 12Z",
        _=>"M4 4H20V20H4Z"};
    private static readonly Dictionary<string,string> ToolOptions=BuildToolOptions();
    private static Dictionary<string,string> BuildToolOptions()
    {
        var options = new Dictionary<string,string>{{"select","Chọn"},{"region","Chọn vùng"},{"pan","Kéo khung"},{"LINE","Đường thẳng"},{"CURVE","Đường cong"},{"SQUARE","Hình vuông"},{"RECT","Chữ nhật"},{"DIVIDER","Phân cách chữ T"},{"WALL","Vật cản chữ nhật"},{"WALL_SQUARE","Vật cản vuông"},{"WALL_CIRCLE","Vật cản tròn"},{"ENTRANCE","Lối vào"},{"EXIT","Lối ra"},{"GATE","Cổng"},{"SPACE","Ô đỗ"},{"LANE","Lối đi"},{"LABEL","Chú thích"}};
        foreach (var tool in ParkingToolCatalog.Additional)
            options.Add(tool.Kind, tool.Label);
        return options;
    }
    private static readonly PropFlowSelectOption[] StateOptions=[new("AVAILABLE","Trống"),new("IN_USE","Đang sử dụng"),new("MAINTENANCE","Bảo trì")];
    private static readonly PropFlowSelectOption[] ListingOptions =
        [.. new[] { ParkingListingStatus.Available, ParkingListingStatus.Processing, ParkingListingStatus.Sold }
            .Select(status => new PropFlowSelectOption(status, ParkingListingPresentation.Label(status)))];
    private static readonly PropFlowSelectOption[] ScaleUnits=[new("","Chọn đơn vị trên bản gốc"),new("mm","Milimét (mm)"),new("cm","Centimét (cm)"),new("m","Mét (m)")];
    private static readonly PropFlowSelectOption[] ObjectKinds=ToolOptions.Where(x=>x.Key is not ("select" or "region" or "pan")).Select(x=>new PropFlowSelectOption(x.Key,x.Value)).ToArray();
    private string ToolHint=>tool switch{"region"=>"Kéo khung để chọn nhóm hình. Ctrl+C/V để sao chép/dán, Ctrl+D nhân bản, Delete xóa; Esc bỏ chọn.","LINE"=>"Kéo từ điểm đầu đến điểm cuối. Chọn đường để kéo lại hai đầu.","CURVE"=>"Kéo để tạo đường cong. Chọn đường rồi kéo điểm giữa để chỉnh độ cong.","DIVIDER"=>"Kéo tạo chữ T. Chọn lại: kéo điểm ở đuôi để tăng chiều dài; kéo tay nắm phía trên để xoay 360°.",_=>"Shift + nhấp để chọn nhiều hình; kéo một hình đã chọn để di chuyển cả nhóm. Ctrl + lăn chuột để zoom; chuột giữa để kéo khung nhìn."};
    protected override async Task OnInitializedAsync()
    {
        loading=true;await Run(async()=>{
            var building=await Buildings.GetCurrentBuildingOverviewAsync(lifetime.Token);
            if(building.IsSuccess&&building.Data is not null)try{buildingZone=TimeZoneInfo.FindSystemTimeZoneById(building.Data.TimeZoneId);}catch(TimeZoneNotFoundException){}
            if(StandaloneEditor){editing=true;await Open(EditorAreaId!.Value);}
            else{await LoadAreas();if(SelectedAreaId.HasValue)await Open(SelectedAreaId.Value);else if(areas.Length>0)await Open(areas[0].Id);}
        });loading=false;
    }
    private async Task Run(Func<Task> operation,string label="Đang cập nhật…",bool recognition=false)
    {
        if(busy||disposed)return;busy=true;busyLabel=label;error=null;
        if(recognition){recognitionBusy=true;recognitionNotice=null;recognitionReviewDrawing=null;recognitionSucceeded=false;message=null;}
        try{await operation();}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(JSException){error="Chưa tải được công cụ sơ đồ. Bạn thử tải lại trang.";}
        finally
        {
            busy=false;
            if(recognition)
            {
                recognitionBusy=false;
                if(!disposed&&!lifetime.IsCancellationRequested)
                {
                    if(error is not null){recognitionNotice=error;recognitionReviewDrawing=null;recognitionSucceeded=false;error=null;}
                    if(!reviewingUnsavedRecognition)
                        recognitionNotice??="Chưa nhận được kết quả nhận diện. Bạn có thể thử lại hoặc tiếp tục vẽ thủ công.";
                }
            }
        }
    }
    private void RecognitionResult(ParkingImportResult result)
    {
        if(result.LocalPreview&&result.ReviewDrawing is { } local)
        {
            ApplyRecognitionReview(local);
            message=result.Message;
            return;
        }
        recognitionSucceeded=result.Recognized;
        recognitionNotice=result.Message;
        recognitionReviewDrawing=result.ReviewDrawing;
    }
    private void CloseRecognitionNotice()
    {
        if (recognitionBusy) return;
        recognitionNotice = null;
        recognitionReviewDrawing = null;
    }
    private void OpenRecognitionReview()
    {
        if (recognitionBusy || recognitionReviewDrawing is not { } drawing) return;

        ApplyRecognitionReview(drawing);
        CloseRecognitionNotice();
    }
    private void ApplyRecognitionReview(ParkingDrawing drawing)
    {
        working = drawing with { InfiniteCanvas = true };
        scaleLength = working.Scale?.Length ?? 0;
        scaleUnit = working.Scale?.Unit ?? "";
        dirty = true;
        reviewingUnsavedRecognition = true;
        inspectorOpen = true;
        compareOriginal = sourceUrl is not null && area?.DraftSource?.ContentType != "application/pdf";
        CanvasKey = Guid.NewGuid();
    }
    private void DiscardRecognitionReview()
    {
        if (busy || area is null) return;

        UseArea(area);
        compareOriginal = false;
        error = null;
        message = "Đã mở lại bản nháp đã lưu.";
    }
    private bool Accept(ApiResult result){if(result.IsSuccess)return true;error=result.Message??"Thao tác chưa thành công. Vui lòng thử lại.";return false;}
    private async Task LoadAreas(){var result=await Api.ListAsync(lifetime.Token);if(Accept(result))areas=result.Data!;}
    private void UseArea(ParkingAreaView value)
    {area=value;working=value.Draft with{Shapes=value.Draft.Shapes.Where(x=>x.Kind!="TRACE").ToArray(),InfiniteCanvas=true};scaleLength=working.Scale?.Length??0;scaleUnit=working.Scale?.Unit??"";editName=value.Name;dirty=false;reviewingUnsavedRecognition=false;selectedShape=null;selectedIds=[];clipboardCount=0;spacingAxis="x";spacingUnit="m";spacingGap=.3;spacingSummary="";tool="select";CanvasKey=Guid.NewGuid();pendingFile=null;uploadKey=Guid.NewGuid();}
    private async Task Open(Guid id)
    {var result=await Api.GetAsync(id,lifetime.Token);if(Accept(result)){UseArea(result.Data!);historyPage=1;history=[];pendingFile=null;if(editing)await LoadSource();}}
    private Task SelectArea(Guid id)=>Guard(()=>Run(()=>Open(id)));
    private Task Reload()=>Guard(()=>Run(async()=>{if(area is not null)await Open(area.Id);await LoadAreas();}));
    private Task Guard(Func<Task> next){if(dirty){discardAction=next;return Task.CompletedTask;}return next();}
    private async Task Discard(){var next=discardAction;discardAction=null;dirty=false;if(next is not null)await next();}
    private void CancelDiscard()=>discardAction=null;
    private async Task BeforeNavigate(LocationChangingContext context)
    {
        if(!dirty)return;context.PreventNavigation();
        discardAction=()=>{Navigation.NavigateTo(context.TargetLocation);return Task.CompletedTask;};
        await InvokeAsync(StateHasChanged);
    }
    private string ManagementUrl=>area is null?"/operations/parking":$"/operations/parking?area={area.Id}";
    private Task ToggleMode()=>Guard(()=>{if(area is not null)Navigation.NavigateTo(StandaloneEditor?ManagementUrl:$"/operations/parking/{area.Id}/editor");return Task.CompletedTask;});
    private Task Create()=>Guard(()=>Run(async()=>{var result=await Api.CreateAsync(newName,lifetime.Token);if(!Accept(result))return;Navigation.NavigateTo($"/operations/parking/{result.Data!.Id}/editor");}));
    private void MarkDirty()=>dirty=true;
    private void ClearDrawing(){confirmClear=false;working=ParkingDrawing.Empty;dirty=true;selectedShape=null;selectedIds=[];clipboardCount=0;spacingAxis="x";spacingUnit="m";spacingGap=.3;spacingSummary="";tool="select";compareOriginal=false;CanvasKey=Guid.NewGuid();message="Bản vẽ trống đã sẵn sàng. Sơ đồ đang quản lý chưa thay đổi.";}
    private void DrawingChanged(ParkingDrawing value){working=value;dirty=true;if(value.Scale is {} scale){scaleLength=scale.Length;scaleUnit=scale.Unit;}tool="select";}
    private void CanvasFailure(string value)=>error=value;
    private void SelectionChanged(Guid[] ids){selectedIds=ids;if(ids.Length!=1)selectedShape=null;}
    private int CountListing(string status) => area?.Spaces.Count(space => space.ListingStatus == status) ?? 0;
    private ParkingSpaceView? SelectedSpace => area?.Spaces.SingleOrDefault(space => space.Id == selectedShape?.Id);
    private Task CanvasAction(string action,object? value=null)=>busy?Task.CompletedTask:canvas?.ActionAsync(action,value)??Task.CompletedTask;
    private async Task SetTool(string value){tool=value;await CanvasAction("tool",value);}
    private void SelectShape(Guid? id)
    {
        selectedShape=(editing?working:area?.Published)?.Shapes.SingleOrDefault(x=>x.Id==id);
        if(selectedShape is null)return;
        var factor=HasPhysicalScale?MetresPerPoint:1;
        shapeKind=selectedShape.Kind;shapeLabel=selectedShape.Label;shapeX=selectedShape.X*factor;shapeY=selectedShape.Y*factor;shapeWidth=selectedShape.Width*factor;shapeHeight=selectedShape.Height*factor;shapeRotation=selectedShape.Rotation;shapeReviewed=!selectedShape.NeedsReview;
        var space = SelectedSpace;
        selectedState=space?.Status??"AVAILABLE";stateNote="";
        listingPrice = space?.PriceVnd?.ToString("0", CultureInfo.InvariantCulture) ?? "";
        listingStatus = space?.ListingStatus ?? ParkingListingStatus.Available;
    }
    private double MetresPerPoint=>working.Scale is {Confirmed:true,Length:>0} s?s.Length*(s.Unit switch{"mm"=>.001,"cm"=>.01,_=>1})/Math.Sqrt(Math.Pow(s.End.X-s.Start.X,2)+Math.Pow(s.End.Y-s.Start.Y,2)):double.NaN;
    private bool HasPhysicalScale=>double.IsFinite(MetresPerPoint)&&MetresPerPoint>0;
    private string SpacingUnit=>spacingUnit;
    private Task SetSpacingAxis(string axis){spacingAxis=axis;return CanvasAction("measure-axis",axis);}
    private Task RotateSelection(double degrees)=>CanvasAction("rotate-group",degrees);
    private Task SelectReviewIssue(ParkingGeometryIssue issue)=>CanvasAction("select-ids",issue.ShapeIds);
    private Task SetSpacingUnit(string unit){if(unit==spacingUnit)return Task.CompletedTask;spacingGap*=unit=="cm"?100:.01;spacingUnit=unit;return CanvasAction("measure-unit",unit);}
    private Task ArrangeSelection(string mode)=>CanvasAction("arrange",new{axis=spacingAxis,mode,gap=spacingGap});
    private Task AlignSelection(string axis){spacingAxis=axis=="y"?"x":"y";return CanvasAction("arrange",new{axis,mode="align",gap=spacingGap});}
    private Task ApplyShape(){if(selectedShape is null)return Task.CompletedTask;var f=HasPhysicalScale?MetresPerPoint:1;return CanvasAction("update",new{kind=shapeKind,label=shapeLabel,x=shapeX/f,y=shapeY/f,width=shapeWidth/f,height=shapeHeight/f,rotation=shapeRotation,needsReview=!shapeReviewed});}
    private Task SetKnownWidth()=>CanvasAction("calibrate-shape",knownWidthMetres);
    private Task ApplyScale()=>CanvasAction("scale",new{length=scaleLength,unit=scaleUnit});
    private async Task ConfirmCandidates(){confirmCandidates=false;await CanvasAction("confirm-reviewed");}
    private string PhysicalSize()
    {
        if(selectedShape is null||working.Scale is not{Confirmed:true} scale)return "Chưa hiệu chuẩn kích thước thực";
        var factor=scale.Length/Math.Sqrt(Math.Pow(scale.End.X-scale.Start.X,2)+Math.Pow(scale.End.Y-scale.Start.Y,2));
        var outline=selectedShape.Outline??[new(0,0),new(1,0),new(1,1),new(0,1)];
        double Edge(int a,int b)=>Math.Sqrt(Math.Pow((outline[a].X-outline[b].X)*selectedShape.Width,2)+Math.Pow((outline[a].Y-outline[b].Y)*selectedShape.Height,2))*factor;
        return $"Kích thước theo tỷ lệ đã xác nhận: {(Edge(0,1)+Edge(2,3))/2:N2} × {(Edge(1,2)+Edge(3,0))/2:N2} {scale.Unit}";
    }
    private async Task<bool> SaveCore()
    {
        if(area is null)return false;
        var result=await Api.SaveAsync(area.Id,new(area.Version,editName,working),lifetime.Token);
        if(!Accept(result))return false;
        var selectedId=selectedShape?.Id;area=result.Data!;working=area.Draft;dirty=false;reviewingUnsavedRecognition=false;SelectShape(selectedId);return true;
    }
    private Task Save()=>Run(async()=>{if(await SaveCore()){message="Đã lưu bản nháp. Sơ đồ quản lý chưa thay đổi.";await LoadAreas();}});
    private void RequestPublish()
    {
        if (reviewingUnsavedRecognition)
        {
            error = "Chỉnh các ô cần đối chiếu và lưu nháp trước khi xuất bản bản AI này.";
            return;
        }
        confirmPublish=true;
    }
    private void CancelPublish()=>confirmPublish=false;
    private Task Publish()=>Run(async()=>
    {
        if(!await SaveCore())return;
        var result=await Api.PublishAsync(area!.Id,area.Version,lifetime.Token);
        if(!Accept(result))return;UseArea(result.Data!);confirmPublish=false;Navigation.NavigateTo(ManagementUrl);
    },"Đang kiểm tra và xuất bản sơ đồ…");
    private Task RemovePublishedDrawing() => Run(async () =>
    {
        if (area?.Published is null) return;

        message = null;
        var result = await Api.RemovePublishedDrawingAsync(area.Id, area.Version, lifetime.Token);
        confirmRemovePublished = false;
        if (!Accept(result)) return;

        UseArea(result.Data!);
        message = "Đã xóa sơ đồ đã xuất bản. Bạn có thể mở lại bản nháp trong trình vẽ.";
        historyPage = 1;
        history = [];
        await LoadAreas();
        await LoadHistory();
    }, "Đang xóa sơ đồ đã xuất bản…");
    private Task SaveState()=>Run(async()=>
    {
        if(area is null||selectedShape is null)return;var id=selectedShape.Id;
        var result=await Api.StateAsync(area.Id,id,new(area.Version,selectedState,stateNote),lifetime.Token);
        if(!Accept(result))return;area=result.Data!;SelectShape(id);message="Đã cập nhật trạng thái ô đỗ.";stateNote="";await LoadAreas();history=[];
    });
    private Task SaveListing() => Run(async () =>
    {
        if (area is null || SelectedSpace is not { } space) return;

        message = null;
        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(listingPrice))
        {
            if (!decimal.TryParse(listingPrice.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
                amount > 999_999_999_999_999_999m)
            {
                error = "Nhập giá ô đỗ bằng số nguyên VND không âm và tối đa 18 chữ số.";
                return;
            }
            price = amount;
        }

        var result = await Api.UpdateListingAsync(area.Id, space.Id, new(area.Version, price, listingStatus), lifetime.Token);
        if (!Accept(result)) return;
        area = result.Data!;
        SelectShape(space.Id);
        message = "Đã lưu giá và trạng thái hiển thị của ô đỗ.";
        historyPage = 1;
        await LoadHistory();
    }, "Đang lưu thông tin ô đỗ…");
    private void ChooseFile(InputFileChangeEventArgs args){pendingFile=args.File;pdfPage=1;message=null;}
    private Task Import()=>Guard(()=>Run(async()=>
    {
        if(area is null||pendingFile is null)return;
        if(pendingFile.Size>8*1024*1024){error="Tệp sơ đồ tối đa 8 MB.";return;}
        using var content=new MultipartFormDataContent();await using var stream=pendingFile.OpenReadStream(8*1024*1024,lifetime.Token);
        using var fileContent=new StreamContent(stream);fileContent.Headers.ContentType=new MediaTypeHeaderValue(pendingFile.ContentType);
        content.Add(fileContent,"file",pendingFile.Name);content.Add(new StringContent(area.Version.ToString()),"expectedVersion");content.Add(new StringContent(pdfPage.ToString(System.Globalization.CultureInfo.InvariantCulture)),"page");
        var result=await Api.ImportAsync(area.Id,content,lifetime.Token);if(!Accept(result))return;
        UseArea(result.Data!.Area);pendingFile=null;await LoadSource();RecognitionResult(result.Data);await LoadAreas();
    },"AI đang đọc sơ đồ, dựng hình và đối chiếu vạch ô đỗ với hình xe. Vui lòng chờ…",recognition:true));
    private async Task LoadSource()
    {
        if(sourceUrl is not null&&sourceModule is not null){await sourceModule.InvokeVoidAsync("releaseUrl",sourceUrl);sourceUrl=null;}
        if(area?.DraftSource is not {} source)return;
        var result=await Api.SourceAsync(area.Id,source.Id,lifetime.Token);if(!Accept(result))return;
            sourceModule??=await JS.InvokeAsync<IJSObjectReference>("import","./js/parking-editor.js?v=20261127-notes");
        if(!disposed)sourceUrl=await sourceModule.InvokeAsync<string>("sourceUrl",result.Data!.Bytes,source.ContentType);
    }
    private Task RecognizeAgain()=>Guard(()=>Run(async()=>
    {
        if(area is null)return;var result=await Api.RecognizeAsync(area.Id,area.Version,lifetime.Token);if(!Accept(result))return;
        UseArea(result.Data!.Area);RecognitionResult(result.Data);await LoadAreas();
        },"AI đang dựng lại và đối chiếu các đối tượng với bản gốc…",recognition:true));
    private async Task LoadHistory()
    {
        if(area is null||disposed)return;var id=area.Id;var result=await Api.HistoryAsync(id,historyPage,lifetime.Token);
        if(!disposed&&area?.Id==id&&Accept(result))history=result.Data!;
    }
    private async Task PreviousHistory(){if(historyPage>1){historyPage--;await LoadHistory();}}
    private async Task NextHistory(){historyPage++;await LoadHistory();}
    private static string ActionText(string action)=>action switch{"CREATED"=>"Tạo bãi đỗ","DRAFT_SAVED"=>"Lưu bản nháp","PUBLISHED"=>"Xuất bản sơ đồ","PUBLISHED_DRAWING_REMOVED"=>"Xóa sơ đồ đã xuất bản","SPACE_LISTING_UPDATED"=>"Cập nhật giá và trạng thái hiển thị của ô đỗ","SOURCE_IMPORTED"=>"Tải lên bản gốc","AI_DRAFT_IMPORTED"=>"AI dựng bản nháp","SOURCE_DIGITIZED"=>"Số hóa sơ đồ gốc","SPACE_AVAILABLE"=>"Chuyển ô về trống","SPACE_IN_USE"=>"Đánh dấu ô đang sử dụng","SPACE_MAINTENANCE"=>"Đánh dấu ô bảo trì",_=>"Cập nhật bãi đỗ"};
    private string FormatTime(DateTimeOffset at)=>buildingZone is null?at.UtcDateTime.ToString("dd/MM/yyyy HH:mm")+" UTC":TimeZoneInfo.ConvertTime(at,buildingZone).ToString("dd/MM/yyyy HH:mm");
    public async ValueTask DisposeAsync()
    {
        if(disposed)return;disposed=true;lifetime.Cancel();
        try{if(sourceModule is not null){if(sourceUrl is not null)await sourceModule.InvokeVoidAsync("releaseUrl",sourceUrl);await sourceModule.DisposeAsync();}}
        catch(Exception ex)when(ex is JSException or JSDisconnectedException or ObjectDisposedException){}
        lifetime.Dispose();
    }
}
