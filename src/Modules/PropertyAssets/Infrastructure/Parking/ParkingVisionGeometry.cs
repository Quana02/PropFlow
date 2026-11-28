using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal sealed record ParkingVisionPlan(bool IsParkingPlan,bool Complete,ParkingVisionObject[]? Objects,ParkingVisionDimension[]? Dimensions,double[]? PlanBounds=null);
internal sealed record ParkingVisionObject(string? Kind,string? Label,string? Evidence,double[][]? Points,bool Uncertain);
internal sealed record ParkingVisionDimension(double Length,string? Unit,double[][]? Points);

/// <summary>Converts semantic image objects into exactly the geometry supported by the editor.</summary>
internal static class ParkingVisionGeometry
{
    private static readonly string[] IgnoredEvidence=["VEHICLE","DIMENSION","TEXT","UNKNOWN"];
    internal static readonly string[] EditorKinds = ParkingDrawingRules.Kinds.Where(kind => kind != "TRACE").ToArray();

    public static ParkingDrawing Build(ParkingVisionPlan plan,ParkingGeometryResult source,CancellationToken ct=default,bool strict=true,ISet<Guid>? uncertain=null,bool allowPartial=false)
    {
        if(!plan.IsParkingPlan||(!plan.Complete&&!allowPartial)||plan.Objects is null||plan.Objects.Length>ParkingDrawingRules.MaxShapes)throw Invalid();
        var shapes=new List<ParkingShape>();var labels=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var number=1;
        foreach(var item in plan.Objects)
        {
            ct.ThrowIfCancellationRequested();
            if(item is null){if(allowPartial)continue;throw Invalid();}
            if(IgnoredEvidence.Contains(item.Evidence??"")||item.Kind is "VEHICLE" or "DIMENSION" or "TEXT" or "UNKNOWN")continue;
            if(item.Kind=="SPACE"&&item.Evidence!="BAY_MARKINGS")continue;
            try
            {
                if(item.Evidence is not ("BAY_MARKINGS" or "STRUCTURE")||!EditorKinds.Contains(item.Kind??""))throw Invalid();
                if(shapes.Count>=ParkingDrawingRules.MaxObjects)throw Invalid();
                var points=Points(item.Points,source);
                var label=(item.Label??"").Trim().Normalize();
                if(label.Length>80)throw Invalid();
                var isSpace=item.Kind=="SPACE";
                if(isSpace&&(label.Length==0||labels.Contains(label)))
                    do{label=$"P-{number++:000}";}while(labels.Contains(label));
                var shape=Shape(item.Kind!,label,points,source.Drawing.Width,source.Drawing.Height);
                if(strict)ParkingBayEdgeAlignment.ValidateOrientation(shape,source,ct);
                ParkingDrawingRules.Validate(new(source.Drawing.Width,source.Drawing.Height,[shape]));
                if(isSpace)labels.Add(label);
                if(item.Uncertain)uncertain?.Add(shape.Id);
                shapes.Add(shape);
            }
            catch(ParkingFailure)when(allowPartial){}
        }
        ParkingScale? scale=null;
        foreach(var dimension in plan.Dimensions??[])
        {
            if(dimension is null||dimension.Unit is not ("mm" or "cm" or "m")||!double.IsFinite(dimension.Length)||dimension.Length<=0||dimension.Length>1e9)continue;
            try{var p=Points(dimension.Points,source);if(p.Length!=2||Distance(p[0],p[1])<4)continue;scale=new(p[0],p[1],dimension.Length,dimension.Unit,false);break;}catch(ParkingFailure){}
        }
        var drawing=ParkingDrawingRules.Validate(source.Drawing with{Shapes=shapes.ToArray(),Scale=scale,Digitization=source.Drawing.Digitization is {} info?info with{Candidates=shapes.Count(s=>s.Kind=="SPACE")}:null});
        return strict?ParkingDrawingRules.ValidateSpaceSeparation(drawing):drawing;
    }
    internal static ParkingShape FromCorners(ParkingShape original,ParkingPoint[] corners,ParkingDrawing drawing)
    {
        var shape=Shape(original.Kind,original.Label,corners,drawing.Width,drawing.Height) with{Id=original.Id,NeedsReview=original.NeedsReview};
        ParkingDrawingRules.Validate(drawing with{Shapes=[shape],RecognitionReview=null});return shape;
    }
    private static ParkingPoint[] Points(double[][]? points,ParkingGeometryResult source)
    {
        if(points is null||points.Length is <2 or >4||points.Any(p=>p is not {Length:2}||p.Any(v=>!double.IsFinite(v)||v<0||v>1000)))throw Invalid();
        return points.Select(p=>new ParkingPoint(p[1]*source.Drawing.Width/1000,p[0]*source.Drawing.Height/1000)).ToArray();
    }
    private static ParkingShape Shape(string kind,string label,ParkingPoint[] p,int cw,int ch)
    {
        var id=Guid.NewGuid();
        if(kind is "LINE" or "DASHED_LINE" or "CURVE")
        {
            if(p.Length!=(kind=="CURVE"?3:2))throw Invalid();
            var x=p.Min(v=>v.X);var y=p.Min(v=>v.Y);var w=Math.Max(4,p.Max(v=>v.X)-x);var h=Math.Max(4,p.Max(v=>v.Y)-y);
            x=Math.Clamp(x-(w-(p.Max(v=>v.X)-x))/2,0,cw-w);y=Math.Clamp(y-(h-(p.Max(v=>v.Y)-y))/2,0,ch-h);
            return new(id,kind,label,x,y,w,h,ControlPoints:p.Select(v=>new ParkingPoint((v.X-x)/w,(v.Y-y)/h)).ToArray());
        }
        if(kind=="DIVIDER")
        {
            if(p.Length!=3)throw Invalid();var w=Distance(p[0],p[1]);if(w<4)throw Invalid();var mx=(p[0].X+p[1].X)/2;var my=(p[0].Y+p[1].Y)/2;
            var angle=Math.Atan2(p[1].Y-p[0].Y,p[1].X-p[0].X);var nx=-Math.Sin(angle);var ny=Math.Cos(angle);var projection=(p[2].X-mx)*nx+(p[2].Y-my)*ny;
            if(projection<0){angle+=Math.PI;nx=-nx;ny=-ny;projection=-projection;}if(projection<4)throw Invalid();
            return new(id,kind,label,mx+nx*projection/2-w/2,my+ny*projection/2-projection/2,w,projection,Angle(angle));
        }
        if(p.Length==2)
        {
            if(kind=="SPACE")throw Invalid();
            var x=Math.Min(p[0].X,p[1].X);var y=Math.Min(p[0].Y,p[1].Y);var w=Math.Abs(p[1].X-p[0].X);var h=Math.Abs(p[1].Y-p[0].Y);
            if(ParkingToolCatalog.IsSquare(kind)){var size=(w+h)/2;x+=(w-size)/2;y+=(h-size)/2;w=h=size;}
            return new(id,kind,label,x,y,w,h,NeedsReview:kind=="SPACE");
        }
        if(p.Length!=4)throw Invalid();
        var turns=Enumerable.Range(0,4).Select(i=>{var a=p[i];var b=p[(i+1)%4];var c=p[(i+2)%4];return(b.X-a.X)*(c.Y-b.Y)-(b.Y-a.Y)*(c.X-b.X);}).ToArray();
        if(!turns.All(v=>v>1e-6)&&!turns.All(v=>v< -1e-6))throw Invalid();
        var widths=new[]{Distance(p[0],p[1]),Distance(p[3],p[2])};var heights=new[]{Distance(p[1],p[2]),Distance(p[0],p[3])};
        if(widths.Concat(heights).Any(v=>v<4))throw Invalid();
        var dot=((p[1].X-p[0].X)*(p[2].X-p[1].X)+(p[1].Y-p[0].Y)*(p[2].Y-p[1].Y))/(widths[0]*heights[0]);
        // Only canonicalize actual rectangles. Approximate quadrilaterals retain their four source corners.
        var rectangular=Math.Abs(dot)<.0001&&Math.Abs(widths[0]-widths[1])<widths.Average()*.0001&&Math.Abs(heights[0]-heights[1])<heights.Average()*.0001;
        if(rectangular || (ParkingToolCatalog.Additional.Any(tool=>tool.Kind==kind) && !ParkingToolCatalog.SupportsOutline(kind) && Math.Abs(dot)<.20))
        {
            var w=widths.Average();var h=heights.Average();if(ParkingToolCatalog.IsSquare(kind))w=h=(w+h)/2;
            var angle=Math.Atan2(p[1].Y-p[0].Y+p[2].Y-p[3].Y,p[1].X-p[0].X+p[2].X-p[3].X);
            var shape=new ParkingShape(id,kind,label,p.Average(v=>v.X)-w/2,p.Average(v=>v.Y)-h/2,w,h,Angle(angle),NeedsReview:kind=="SPACE");
            try{ParkingDrawingRules.Validate(new(cw,ch,[shape]));return shape;}catch(ParkingFailure)when(ParkingToolCatalog.SupportsOutline(kind)){}
        }
        if(!ParkingToolCatalog.SupportsOutline(kind))throw Invalid();
        var left=p.Min(v=>v.X);var top=p.Min(v=>v.Y);var width=p.Max(v=>v.X)-left;var height=p.Max(v=>v.Y)-top;
        return new(id,kind,label,left,top,width,height,Outline:p.Select(v=>new ParkingPoint((v.X-left)/width,(v.Y-top)/height)).ToArray(),NeedsReview:kind=="SPACE");
    }
    private static double Distance(ParkingPoint a,ParkingPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    private static double Angle(double radians)=>(radians*180/Math.PI%360+360)%360;
    private static ParkingFailure Invalid()=>new(400,"parking_ai_geometry_invalid","Kết quả nhận diện chưa đủ rõ hoặc có hình học không hợp lệ.");
}
