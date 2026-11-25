using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using SkiaSharp;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal sealed record ParkingSceneRegion(int Index,double Top,double Left,double Bottom,double Right,double CoreLeft,double CoreRight,byte[] Image,double CoreTop=0,double CoreBottom=1000,Guid[]? RepairIds=null,string? Problem=null)
{
    public bool Owns(ParkingShape shape,ParkingDrawing drawing)
    {
        var x=(shape.X+shape.Width/2)*1000/drawing.Width;var y=(shape.Y+shape.Height/2)*1000/drawing.Height;
        return x>=CoreLeft&&x<CoreRight+1e-6&&y>=CoreTop&&y<CoreBottom+1e-6;
    }
    public ParkingVisionPlan Project(ParkingVisionPlan plan)
    {
        double[][]? Map(double[][]? points)
        {
            if(points is null||points.Any(p=>p is not {Length:2}||p.Any(v=>!double.IsFinite(v)||v< -2||v>1002)))throw new ParkingFailure(400,"parking_ai_region_invalid","Tọa độ vùng nhận diện không hợp lệ.");
            return points.Select(p=>new[]{Top+(Bottom-Top)*Math.Clamp(p[0],0,1000)/1000,Left+(Right-Left)*Math.Clamp(p[1],0,1000)/1000}).ToArray();
        }
        return plan with{Objects=plan.Objects?.Select(o=>o is null?null!:o.Evidence is "STRUCTURE" or "BAY_MARKINGS"?o with{Points=Map(o.Points)}:o).ToArray(),Dimensions=plan.Dimensions?.Where(d=>d is not null&&d.Points is {Length:2}&&d.Points.All(p=>p is {Length:2}&&p.All(v=>double.IsFinite(v)&&v>=0&&v<=1000))).Select(d=>d with{Points=Map(d.Points)}).ToArray(),PlanBounds=null};
    }
    public static ParkingSceneRegion[] Create(ParkingGeometryResult source,double[]? bounds,int count,ParkingDrawing? overview=null)
    {
        if(bounds is null||!CanTile(source,bounds)||count is <1 or >3)return [];
        var horizontal=(bounds[3]-bounds[1])*source.PixelWidth>=(bounds[2]-bounds[0])*source.PixelHeight;
        var regions=new List<ParkingSceneRegion>();var span=horizontal?bounds[3]-bounds[1]:bounds[2]-bounds[0];
        var bayExtent=overview?.Shapes.Where(s=>s.Kind=="SPACE").Select(s=>{var p=ParkingSourceGeometry.Corners(s);return horizontal?(p.Max(v=>v.X)-p.Min(v=>v.X))*1000/source.Drawing.Width:(p.Max(v=>v.Y)-p.Min(v=>v.Y))*1000/source.Drawing.Height;}).DefaultIfEmpty(0).Max()??0;
        for(var i=0;i<count;i++)
        {
            var start=(horizontal?bounds[1]:bounds[0])+span*i/count;var end=start+span/count;var overlap=Math.Max(span*.07,Math.Min(span*.3,bayExtent*.6));
            var core=new[]{horizontal?bounds[0]:start,horizontal?start:bounds[1],horizontal?bounds[2]:end,horizontal?end:bounds[3]};
            var cropBounds=new[]{horizontal?bounds[0]:Math.Max(bounds[0],start-overlap),horizontal?Math.Max(bounds[1],start-overlap):bounds[1],horizontal?bounds[2]:Math.Min(bounds[2],end+overlap),horizontal?Math.Min(bounds[3],end+overlap):bounds[3]};
            if(Crop(source,i,cropBounds,core) is not {} region)return [];regions.Add(region);
        }
        return regions.ToArray();
    }
    public static bool CanTile(ParkingGeometryResult source,double[]? bounds)=>bounds is {Length:4}&&bounds.All(v=>double.IsFinite(v)&&v>=0&&v<=1000)&&bounds[2]-bounds[0]>=100&&bounds[3]-bounds[1]>=100&&(bounds[2]-bounds[0])*source.PixelHeight/1000>=16&&(bounds[3]-bounds[1])*source.PixelWidth/1000>=16;
    public static ParkingSceneRegion[] Targets(ParkingGeometryResult source,ParkingDrawing drawing,int maximum,IReadOnlySet<Guid>? uncertain=null)
    {
        if(maximum<=0)return [];
        var boxes=drawing.Shapes.ToDictionary(s=>s.Id,s=>{var p=ParkingSourceGeometry.Corners(s);return(p.Min(v=>v.X),p.Min(v=>v.Y),p.Max(v=>v.X),p.Max(v=>v.Y));});
        var cached=new Dictionary<HashSet<Guid>,(double Left,double Top,double Right,double Bottom)>();
        var issues=(drawing.RecognitionReview?.Issues??[]).Where(i=>i.Code is "overlap" or "orientation" or "wall_tool"||(i.Code is "source_edges" or "nested_outline")&&i.ShapeIds.Any(id=>uncertain?.Contains(id)==true)).ToArray();
        var groups=new List<HashSet<Guid>>();
        foreach(var issue in issues)
        {
            var ids=issue.ShapeIds.Where(boxes.ContainsKey).ToHashSet();if(ids.Count==0)continue;
            bool expanded;
            do
            {
                var touching=groups.Where(g=>g.Overlaps(ids)||Near(g,ids)).ToArray();expanded=touching.Length>0;
                foreach(var group in touching){ids.UnionWith(group);groups.Remove(group);cached.Remove(group);}cached.Remove(ids);
            }while(expanded);
            groups.Add(ids);
        }
        var result=new List<ParkingSceneRegion>();
        foreach(var ids in groups.Take(Math.Clamp(maximum,0,3)))
        {
            var corners=drawing.Shapes.Where(s=>ids.Contains(s.Id)).SelectMany(ParkingSourceGeometry.Corners).ToArray();
            var top=corners.Min(p=>p.Y)*1000/drawing.Height;var bottom=corners.Max(p=>p.Y)*1000/drawing.Height;var left=corners.Min(p=>p.X)*1000/drawing.Width;var right=corners.Max(p=>p.X)*1000/drawing.Width;
            var padx=Math.Max(1000d*10/source.PixelWidth,(right-left)*.2);var pady=Math.Max(1000d*10/source.PixelHeight,(bottom-top)*.2);
            var bounds=new[]{Math.Max(0,top-pady),Math.Max(0,left-padx),Math.Min(1000,bottom+pady),Math.Min(1000,right+padx)};
            if(Crop(source,result.Count,bounds,bounds) is {} region)result.Add(region with{RepairIds=ids.ToArray(),Problem=string.Join(" ",issues.Where(i=>i.ShapeIds.Any(ids.Contains)).Take(3).Select(i=>i.Message))});
        }
        return result.ToArray();
        bool Near(HashSet<Guid> first,HashSet<Guid> second)
        {
            var a=Box(first);var b=Box(second);var dx=Math.Max(a.Right-a.Left,b.Right-b.Left)*.2;var dy=Math.Max(a.Bottom-a.Top,b.Bottom-b.Top)*.2;
            return a.Left<=b.Right+dx&&b.Left<=a.Right+dx&&a.Top<=b.Bottom+dy&&b.Top<=a.Bottom+dy;
        }
        (double Left,double Top,double Right,double Bottom) Box(HashSet<Guid> ids)
        {
            if(cached.TryGetValue(ids,out var box))return box;
            var values=ids.Select(id=>boxes[id]).ToArray();box=(values.Min(p=>p.Item1),values.Min(p=>p.Item2),values.Max(p=>p.Item3),values.Max(p=>p.Item4));cached[ids]=box;return box;
        }
    }
    private static ParkingSceneRegion? Crop(ParkingGeometryResult source,int index,double[] bounds,double[] core)
    {
        using var bitmap=SKBitmap.Decode(source.Preview);
        var left=Math.Max(0,(int)Math.Floor(bounds[1]*bitmap.Width/1000));var right=Math.Min(bitmap.Width,(int)Math.Ceiling(bounds[3]*bitmap.Width/1000));
        var top=Math.Max(0,(int)Math.Floor(bounds[0]*bitmap.Height/1000));var bottom=Math.Min(bitmap.Height,(int)Math.Ceiling(bounds[2]*bitmap.Height/1000));
        if(right-left<16||bottom-top<16)return null;
        using var crop=new SKBitmap(right-left,bottom-top);using(var canvas=new SKCanvas(crop)){canvas.Clear(SKColors.White);canvas.DrawBitmap(bitmap,new SKRect(left,top,right,bottom),new SKRect(0,0,crop.Width,crop.Height),new SKSamplingOptions(SKFilterMode.Linear));}
        using var image=SKImage.FromBitmap(crop);using var data=image.Encode(SKEncodedImageFormat.Png,100);
        return new(index,top*1000d/bitmap.Height,left*1000d/bitmap.Width,bottom*1000d/bitmap.Height,right*1000d/bitmap.Width,core[1],core[3],data.ToArray(),core[0],core[2]);
    }
}
