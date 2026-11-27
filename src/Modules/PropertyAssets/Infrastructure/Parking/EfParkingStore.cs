using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PropFlow.Modules.PropertyAssets.Application.Parking;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

public sealed class EfParkingStore(PropertyAssetsDbContext db):IParkingStore
{
    public Task<ParkingAreaSummary[]> ListAsync(CancellationToken ct)=>db.Set<ParkingArea>().AsNoTracking().OrderBy(x=>x.Name).Take(200)
        .Select(x=>new ParkingAreaSummary(x.Id,x.Name,x.PublishedJson!=null,x.Spaces.Count(s=>!s.Archived),
            x.Spaces.Count(s=>!s.Archived&&s.Status=="AVAILABLE"),x.Spaces.Count(s=>!s.Archived&&s.Status=="IN_USE"),
            x.Spaces.Count(s=>!s.Archived&&s.Status=="MAINTENANCE"))).ToArrayAsync(ct);
    public async Task<ParkingAreaView?> ReadAsync(Guid id,CancellationToken ct)
    {
        var area=await db.Set<ParkingArea>().AsNoTracking().Include(x=>x.Spaces).SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(area is null)return null;
        var sources=await db.Set<ParkingSource>().AsNoTracking().Where(x=>x.AreaId==id&&(x.Id==area.DraftSourceId||x.Id==area.PublishedSourceId))
            .Select(x=>new ParkingSourceView(x.Id,x.FileName,x.ContentType,x.Page)).ToArrayAsync(ct);
        var published = area.Published;
        var spaces = area.Spaces.Where(x => !x.Archived)
            .Select(x => new ParkingSpaceView(x.Id, x.Status, 0, x.PriceVnd, x.ListingStatus));
        return new(area.Id,area.Name,area.Version,area.Draft,published,sources.SingleOrDefault(x=>x.Id==area.DraftSourceId),
            sources.SingleOrDefault(x=>x.Id==area.PublishedSourceId),NumberSpaces(published, spaces),area.PublishedAt);
    }

    public Task<PublishedParkingAreaSummary[]> ListPublishedAsync(CancellationToken ct) =>
        db.Set<ParkingArea>().AsNoTracking().Where(area => area.PublishedJson != null)
            .OrderBy(area => area.Name).ThenBy(area => area.Id).Take(200)
            .Select(area => new PublishedParkingAreaSummary(area.Id, area.Name)).ToArrayAsync(ct);

    public async Task<PublishedParkingArea?> ReadPublishedAsync(Guid id, CancellationToken ct)
    {
        var area = await db.Set<ParkingArea>().AsNoTracking()
            .Where(area => area.Id == id && area.PublishedJson != null)
            .Select(area => new
            {
                area.Id,
                area.Name,
                area.PublishedJson,
                Spaces = area.Spaces.Where(space => !space.Archived)
                    .Select(space => new ParkingSpaceView(space.Id, "", 0, space.PriceVnd, space.ListingStatus)).ToArray()
            }).SingleOrDefaultAsync(ct);
        if (area is null) return null;

        var drawing = JsonSerializer.Deserialize<ParkingDrawing>(area.PublishedJson!)!;
        var spaces = NumberSpaces(drawing, area.Spaces)
            .Select(space => new PublishedParkingSpace(space.Id, space.Number,
                space.ListingStatus == ParkingListingStatus.Sold ? null : space.PriceVnd, space.ListingStatus)).ToArray();
        var residentDrawing = new ParkingDrawing(drawing.Width, drawing.Height,
            drawing.Shapes.Where(shape => shape.Kind != "TRACE").ToArray(), InfiniteCanvas: drawing.InfiniteCanvas);
        return new(area.Id, area.Name, residentDrawing, spaces);
    }

    private static ParkingSpaceView[] NumberSpaces(ParkingDrawing? drawing, IEnumerable<ParkingSpaceView> spaces)
    {
        if (drawing is null) return [];
        var active = spaces.ToDictionary(space => space.Id);
        var numbered = new List<ParkingSpaceView>();
        foreach (var shape in drawing.Shapes.Where(shape => shape.Kind == "SPACE"))
        {
            if (active.TryGetValue(shape.Id, out var space))
                numbered.Add(space with { Number = numbered.Count + 1 });
        }
        return numbered.ToArray();
    }
    public async Task CreateAsync(ParkingArea area,ParkingAction action,CancellationToken ct)
    {db.Add(area);db.Add(action);await db.SaveChangesAsync(ct);}
    public async Task<ParkingAreaView> ChangeAsync(Guid id,Guid expectedVersion,Action<ParkingArea> change,ParkingAction action,ParkingSource? source,bool revision,CancellationToken ct)
    {
        await using var transaction=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(ct):null;
        if(db.Database.IsRelational()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"parking-area:"+id}, 8))",ct);
        var area=await db.Set<ParkingArea>().Include(x=>x.Spaces).SingleOrDefaultAsync(x=>x.Id==id,ct)
            ??throw new ParkingFailure(404,"parking_area_not_found","Không tìm thấy bãi đỗ.");
        if(area.Version!=expectedVersion) throw ParkingWorkspace.Changed();
        change(area);if(source is not null)db.Add(source);db.Add(action);
        if(revision)db.Add(new ParkingRevision(area,action.ActorId,action.At));
        try {await db.SaveChangesAsync(ct);if(transaction is not null)await transaction.CommitAsync(ct);}
        catch(DbUpdateConcurrencyException){throw ParkingWorkspace.Changed();}
        catch(DbUpdateException ex) when(ex.InnerException is Npgsql.PostgresException pg&&pg.SqlState=="23505")
        {throw new ParkingFailure(409,"parking_shape_id_conflict","Mã đối tượng đã tồn tại trong bãi khác. Sao chép đối tượng để tạo mã mới.");}
        return (await ReadAsync(id,ct))!;
    }
    public Task<ParkingSource?> SourceAsync(Guid area,Guid id,CancellationToken ct)=>db.Set<ParkingSource>().AsNoTracking().SingleOrDefaultAsync(x=>x.AreaId==area&&x.Id==id,ct);
    public Task<ParkingActionView[]> HistoryAsync(Guid area,int page,CancellationToken ct)=>db.Set<ParkingAction>().AsNoTracking().Where(x=>x.AreaId==area)
        .OrderByDescending(x=>x.At).ThenByDescending(x=>x.Id).Skip((page-1)*20).Take(20)
        .Select(x=>new ParkingActionView(x.Id,x.Action,x.ActorId,x.At,x.SpaceId,x.Note)).ToArrayAsync(ct);
}
