using System.Text.Json;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.PropertyAssets.Domain.Parking;

public sealed class ParkingArea
{
    private ParkingArea() { }
    public ParkingArea(string name, Guid actor, DateTimeOffset now) { Rename(name); Id=Guid.NewGuid(); Version=Guid.NewGuid(); UpdatedAt=now; UpdatedBy=actor; }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public Guid Version { get; private set; }
    public string DraftJson { get; private set; } = JsonSerializer.Serialize(ParkingDrawing.Empty);
    public string? PublishedJson { get; private set; }
    public Guid? DraftSourceId { get; private set; }
    public Guid? PublishedSourceId { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public List<ParkingSpace> Spaces { get; private set; } = [];
    public ParkingDrawing Draft => JsonSerializer.Deserialize<ParkingDrawing>(DraftJson)!;
    public ParkingDrawing? Published => PublishedJson is null ? null : JsonSerializer.Deserialize<ParkingDrawing>(PublishedJson);
    public void Save(string name, ParkingDrawing drawing, Guid actor, DateTimeOffset now)
    { Rename(name); DraftJson=JsonSerializer.Serialize(ParkingDrawingRules.Validate(drawing)); Touch(actor,now); }
    public void Import(Guid source, ParkingDrawing? drawing, Guid actor, DateTimeOffset now)
    {
        DraftSourceId=source;
        if(drawing is not null) DraftJson=JsonSerializer.Serialize(ParkingDrawingRules.Validate(drawing));
        Touch(actor,now);
    }
    public void Publish(Guid actor, DateTimeOffset now)
    {
        var drawing=ParkingDrawingRules.Validate(Draft,true); var liveIds=drawing.Shapes.Where(x=>x.Kind=="SPACE").Select(x=>x.Id).ToHashSet();
        if(Spaces.Any(x=>!x.Archived&&!liveIds.Contains(x.Id)&&x.Status!="AVAILABLE"))
            throw new ParkingFailure(409,"parking_space_in_use","Không được bỏ ô đang sử dụng hoặc bảo trì. Chuyển ô về trống trước khi bỏ khỏi sơ đồ.");
        foreach(var space in Spaces) space.Archive(!liveIds.Contains(space.Id));
        foreach(var id in liveIds) if(Spaces.All(x=>x.Id!=id)) Spaces.Add(new(id,Id));
        PublishedJson=JsonSerializer.Serialize(drawing); PublishedSourceId=DraftSourceId; PublishedAt=now; Touch(actor,now);
    }
    public void RemovePublishedDrawing(Guid actor, DateTimeOffset now)
    {
        if (PublishedJson is null)
            throw new ParkingFailure(404, "parking_published_drawing_not_found", "Bãi đỗ chưa có sơ đồ đã xuất bản.");
        if (Spaces.Any(space => !space.Archived && space.Status != "AVAILABLE"))
            throw new ParkingFailure(409, "parking_space_in_use", "Chuyển các ô đang sử dụng hoặc bảo trì về Trống trước khi xóa sơ đồ.");

        foreach (var space in Spaces)
            space.Archive(true);

        PublishedJson = null;
        PublishedSourceId = null;
        PublishedAt = null;
        Touch(actor, now);
    }
    public void ChangeSpace(Guid id,string status,Guid actor,DateTimeOffset now)
    {
        PublishedSpace(id).Change(status);
        Touch(actor,now);
    }
    public void UpdateSpaceListing(Guid id, decimal? priceVnd, string listingStatus, Guid actor, DateTimeOffset now)
    {
        PublishedSpace(id).UpdateListing(priceVnd, listingStatus);
        Touch(actor, now);
    }

    private ParkingSpace PublishedSpace(Guid id) =>
        Spaces.SingleOrDefault(space => space.Id == id && !space.Archived)
        ?? throw new ParkingFailure(404, "parking_space_not_found", "Không tìm thấy ô đỗ trên sơ đồ đang sử dụng.");
    private void Rename(string name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>120) throw new ParkingFailure(400,"parking_name_invalid","Tên bãi đỗ cần từ 1 đến 120 ký tự.");
        Name=name.Trim().Normalize();
    }
    private void Touch(Guid actor,DateTimeOffset now) { Version=Guid.NewGuid();UpdatedAt=now;UpdatedBy=actor; }
}
public sealed class ParkingSpace
{
    private ParkingSpace() { }
    public ParkingSpace(Guid id,Guid area) {Id=id;AreaId=area;}
    public Guid Id {get;private set;}
    public Guid AreaId {get;private set;}
    public string Status {get;private set;}="AVAILABLE";
    public decimal? PriceVnd { get; private set; }
    public string ListingStatus { get; private set; } = ParkingListingStatus.Available;
    public bool Archived {get;private set;}
    internal void Archive(bool archived) {Archived=archived;}
    internal void Change(string status)
    {
        if(!ParkingDrawingRules.States.Contains(status)) throw new ParkingFailure(400,"parking_state_invalid","Trạng thái ô đỗ không hợp lệ.");
        Status=status;
    }
    internal void UpdateListing(decimal? priceVnd, string listingStatus)
    {
        if (listingStatus is not (ParkingListingStatus.Available or ParkingListingStatus.Processing or ParkingListingStatus.Sold))
            throw new ParkingFailure(400, "parking_listing_status_invalid", "Trạng thái hiển thị của ô đỗ không hợp lệ.");
        if (priceVnd is { } price && (price < 0 || price > 999_999_999_999_999_999m || price != decimal.Truncate(price)))
            throw new ParkingFailure(400, "parking_price_invalid", "Nhập giá ô đỗ bằng số nguyên VND không âm và tối đa 18 chữ số.");

        PriceVnd = priceVnd;
        ListingStatus = listingStatus;
    }
}
public sealed class ParkingSource
{
    private ParkingSource() { }
    public ParkingSource(Guid area,string name,string mime,int page,byte[] bytes)
    { Id=Guid.NewGuid();AreaId=area;FileName=name;ContentType=mime;Page=page;Content=bytes; }
    public Guid Id {get;private set;}
    public Guid AreaId {get;private set;}
    public string FileName {get;private set;}="";
    public string ContentType {get;private set;}="";
    public int Page {get;private set;}
    public byte[] Content {get;private set;}=[];
}
public sealed class ParkingAction
{
    private ParkingAction() { }
    public ParkingAction(Guid area,string action,Guid actor,DateTimeOffset at,Guid? space=null,string note="")
    {Id=Guid.NewGuid();AreaId=area;Action=action;ActorId=actor;At=at;SpaceId=space;Note=note;}
    public Guid Id {get;private set;}
    public Guid AreaId {get;private set;}
    public string Action {get;private set;}="";
    public Guid ActorId {get;private set;}
    public DateTimeOffset At {get;private set;}
    public Guid? SpaceId {get;private set;}
    public string Note {get;private set;}="";
}
public sealed class ParkingRevision
{
    private ParkingRevision() { }
    public ParkingRevision(ParkingArea area,Guid actor,DateTimeOffset now)
    {Id=Guid.NewGuid();AreaId=area.Id;DrawingJson=area.PublishedJson!;SourceId=area.PublishedSourceId;PublishedBy=actor;PublishedAt=now;}
    public Guid Id {get;private set;}
    public Guid AreaId {get;private set;}
    public string DrawingJson {get;private set;}="";
    public Guid? SourceId {get;private set;}
    public Guid PublishedBy {get;private set;}
    public DateTimeOffset PublishedAt {get;private set;}
}
