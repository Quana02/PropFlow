using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Application.Parking;

public interface IParkingStore
{
    Task<ParkingAreaSummary[]> ListAsync(CancellationToken ct);
    Task<ParkingAreaView?> ReadAsync(Guid id,CancellationToken ct);
    Task CreateAsync(ParkingArea area,ParkingAction action,CancellationToken ct);
    Task<ParkingAreaView> ChangeAsync(Guid id,Guid expectedVersion,Action<ParkingArea> change,ParkingAction action,
        ParkingSource? source,bool revision,CancellationToken ct);
    Task<ParkingSource?> SourceAsync(Guid area,Guid id,CancellationToken ct);
    Task<ParkingActionView[]> HistoryAsync(Guid area,int page,CancellationToken ct);
}
public sealed record ParkingRecognition(ParkingDrawing? Drawing,string Message);
public interface IParkingPlanRecognition
{
    Task<ParkingRecognition> ReadAsync(byte[] content,string contentType,int page,CancellationToken ct);
}
public sealed class ParkingWorkspace(IParkingStore store,IParkingPlanRecognition recognition,TimeProvider clock)
{
    public const int MaxFileBytes=8*1024*1024;
    public Task<ParkingAreaSummary[]> ListAsync(CancellationToken ct)=>store.ListAsync(ct);
    public async Task<ParkingAreaView> GetAsync(Guid id,CancellationToken ct)=>await store.ReadAsync(id,ct)
        ??throw new ParkingFailure(404,"parking_area_not_found","Không tìm thấy bãi đỗ.");
    public async Task<ParkingAreaView> CreateAsync(CreateParkingArea input,Guid actor,CancellationToken ct)
    {
        var area=new ParkingArea(input.Name,actor,clock.GetUtcNow());
        await store.CreateAsync(area,new(area.Id,"CREATED",actor,clock.GetUtcNow()),ct);
        return await GetAsync(area.Id,ct);
    }
    public Task<ParkingAreaView> SaveAsync(Guid id,SaveParkingDrawing input,Guid actor,CancellationToken ct)=>
        store.ChangeAsync(id,input.ExpectedVersion,area=>area.Save(input.Name,input.Drawing,actor,clock.GetUtcNow()),
            new(id,"DRAFT_SAVED",actor,clock.GetUtcNow()),null,false,ct);
    public Task<ParkingAreaView> PublishAsync(Guid id,PublishParkingDrawing input,Guid actor,CancellationToken ct)=>
        store.ChangeAsync(id,input.ExpectedVersion,area=>area.Publish(actor,clock.GetUtcNow()),
            new(id,"PUBLISHED",actor,clock.GetUtcNow()),null,true,ct);
    public Task<ParkingAreaView> ChangeSpaceAsync(Guid area,Guid space,ChangeParkingSpace input,Guid actor,CancellationToken ct)
    {
        if(input.Note is null||input.Note.Length>500) throw new ParkingFailure(400,"parking_note_invalid","Ghi chú tối đa 500 ký tự.");
        return store.ChangeAsync(area,input.ExpectedVersion,x=>x.ChangeSpace(space,input.Status,actor,clock.GetUtcNow()),
            new(area,"SPACE_"+input.Status,actor,clock.GetUtcNow(),space,input.Note.Trim().Normalize()),null,false,ct);
    }
    public async Task<ParkingImportResult> ImportAsync(Guid id,Guid version,Stream stream,string name,string mime,int page,Guid actor,CancellationToken ct)
    {
        var current=await GetAsync(id,ct);
        if(current.Version!=version) throw Changed();
        if(page is <1 or >1000) throw new ParkingFailure(400,"parking_page_invalid","Chọn trang PDF từ 1 đến 1.000.");
        var bytes=await ReadFileAsync(stream,mime,ct);
        if(mime!="application/pdf"&&page!=1) throw new ParkingFailure(400,"parking_page_invalid","Ảnh chỉ có một trang.");
        // Provider work happens before the write transaction. A concurrent save must still win the version check below.
        var result=await recognition.ReadAsync(bytes,mime,page,ct);
        var (valid,message)=ValidateRecognition(result);
        var source=new ParkingSource(id,SafeFileName(name),mime,page,bytes);
        var area=await store.ChangeAsync(id,version,x=>x.Import(source.Id,valid,actor,clock.GetUtcNow()),
            new(id,valid is null?"SOURCE_IMPORTED":"SOURCE_DIGITIZED",actor,clock.GetUtcNow()),source,false,ct);
        return new(area,valid is not null,message);
    }
    public async Task<ParkingImportResult> RecognizeAsync(Guid id,Guid version,Guid actor,CancellationToken ct)
    {
        var current=await GetAsync(id,ct);if(current.Version!=version)throw Changed();
        if(current.DraftSource is null)throw new ParkingFailure(400,"parking_source_required","Tải lên sơ đồ gốc trước khi nhận diện lại.");
        var source=await SourceAsync(id,current.DraftSource.Id,ct);
        var (drawing,message)=ValidateRecognition(await recognition.ReadAsync(source.Content,source.ContentType,source.Page,ct));
        if(drawing is null)return new(await GetAsync(id,ct),false,message);
        var area=await store.ChangeAsync(id,version,x=>x.Import(source.Id,drawing,actor,clock.GetUtcNow()),new(id,"SOURCE_DIGITIZED",actor,clock.GetUtcNow()),null,false,ct);
        return new(area,true,message);
    }
    private static (ParkingDrawing? Drawing,string Message) ValidateRecognition(ParkingRecognition result)
    {
        if(result.Drawing is null)return(null,result.Message);
        try{return(ParkingDrawingRules.ValidateSpaceSeparation(ParkingDrawingRules.Validate(result.Drawing)),result.Message);}
        catch(ParkingFailure){return(null,"AI trả về sơ đồ chưa hợp lệ. Đã giữ bản nháp; bạn có thể đối chiếu tệp và vẽ thủ công.");}
    }
    public async Task<ParkingSource> SourceAsync(Guid area,Guid source,CancellationToken ct)=>await store.SourceAsync(area,source,ct)
        ??throw new ParkingFailure(404,"parking_source_not_found","Không tìm thấy tệp sơ đồ.");
    public async Task<ParkingActionView[]> HistoryAsync(Guid area,int page,CancellationToken ct)
    {await GetAsync(area,ct);return await store.HistoryAsync(area,Math.Clamp(page,1,10000),ct);}
    public static ParkingFailure Changed()=>new(409,"parking_changed","Bãi đỗ đã được cập nhật ở phiên khác. Tải lại dữ liệu trước khi lưu.");
    private static string SafeFileName(string name)
    {
        var normalized=(name??"sơ đồ").Replace('\\','/').Split('/').Last();
        normalized=new string(normalized.Where(x=>!char.IsControl(x)).Take(180).ToArray());
        return string.IsNullOrWhiteSpace(normalized)?"sơ đồ":normalized;
    }
    public static async Task<byte[]> ReadFileAsync(Stream input,string mime,CancellationToken ct)
    {
        if(mime is not ("image/png" or "image/jpeg" or "application/pdf")) throw InvalidFile();
        using var memory=new MemoryStream(); var buffer=new byte[81920]; int count;
        while((count=await input.ReadAsync(buffer,ct))>0)
        {
            if(memory.Length+count>MaxFileBytes) throw new ParkingFailure(413,"parking_file_too_large","Tệp sơ đồ tối đa 8 MB.");
            memory.Write(buffer,0,count);
        }
        var bytes=memory.ToArray();
        var valid=mime switch {
            "image/png"=>bytes.Length>=24&&bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
            "image/jpeg"=>bytes.Length>=4&&bytes[0]==255&&bytes[1]==216&&bytes[2]==255&&bytes[^2]==255&&bytes[^1]==217,
            "application/pdf"=>bytes.Length>=8&&bytes.AsSpan(0,5).SequenceEqual("%PDF-"u8),_=>false};
        if(!valid) throw InvalidFile();
        return bytes;
    }
    private static ParkingFailure InvalidFile()=>new(400,"parking_file_invalid","Chỉ nhận tệp PNG, JPG hoặc PDF đúng định dạng.");
}
