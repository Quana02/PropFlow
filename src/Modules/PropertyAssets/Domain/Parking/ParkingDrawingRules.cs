using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.PropertyAssets.Domain.Parking;

public sealed class ParkingFailure(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public static class ParkingDrawingRules
{
    public const int MaxShapes = 6000;
    public const int MaxObjects = 1000;
    public const int MaxContourPoints = 300000;
    public const double MaxCoordinate = 1_000_000_000;
    public static readonly string[] Kinds = ["SPACE", "LANE", "GATE", "WALL", "LABEL", "TRACE", "LINE", "CURVE", "RECT", "SQUARE", "DIVIDER", "WALL_SQUARE", "WALL_CIRCLE", "ENTRANCE", "EXIT", ..ParkingToolCatalog.Additional.Select(tool=>tool.Kind)];
    public static readonly string[] States = ["AVAILABLE", "IN_USE", "MAINTENANCE"];
    public static ParkingDrawing Validate(ParkingDrawing? drawing, bool publishing = false)
    {
        if (drawing is null || drawing.Shapes is null || drawing.Width < 100 || drawing.Height < 100 || drawing.Width > (drawing.InfiniteCanvas?MaxCoordinate:10000) || drawing.Height > (drawing.InfiniteCanvas?MaxCoordinate:10000) || drawing.Shapes.Length > MaxShapes)
            Fail("Thông tin khung nhìn không hợp lệ; tối đa 1.000 đối tượng quản lý và 6.000 đối tượng gồm nét gốc.");
        if(drawing!.Shapes.Count(x=>x is not null&&x.Kind!="TRACE")>MaxObjects) Fail("Tối đa 1.000 đối tượng quản lý.");
        if(drawing.Scale is {} scale)
        {
            if(scale.Start is null||scale.End is null||!ValidPoint(scale.Start)||!ValidPoint(scale.End)||!double.IsFinite(scale.Length)||scale.Length<0||scale.Length>1e9
                ||(scale.Confirmed&&(scale.Length<=0||scale.Unit is not ("mm" or "cm" or "m")))||(!scale.Confirmed&&scale.Unit is not ("" or "mm" or "cm" or "m"))
                ||Math.Sqrt(Math.Pow(scale.End.X-scale.Start.X,2)+Math.Pow(scale.End.Y-scale.Start.Y,2))<4) Fail("Chọn hai điểm khác nhau, nhập chiều dài và đơn vị kích thước hợp lệ.");
        }
        if(drawing.Digitization is {} info && (info.SourceWidth is <1 or >50000||info.SourceHeight is <1 or >50000||info.Threshold is <0 or >255||info.Candidates is <0 or >1000||!double.IsFinite(info.RetainedInkRatio)||info.RetainedInkRatio is <0 or >1)) Fail("Thông tin đối chiếu nét gốc không hợp lệ.");
        if(drawing.RecognitionReview is {} review && (review.CheckedObjects is <0 or >MaxObjects||review.SourceCheckedObjects<0||review.SourceCheckedObjects>review.CheckedObjects||review.RefinedObjects<0||review.RefinedObjects>review.CheckedObjects||review.UnreadRegions is <0 or >3||review.Issues is null||review.Issues.Length>MaxObjects||review.Issues.Any(i=>i is null||i.Code is null||i.Code.Length is <1 or >40||i.Message is null||i.Message.Length is <1 or >240||i.ShapeIds is null||i.ShapeIds.Length>MaxObjects||i.ShapeIds.Any(id=>id==Guid.Empty))))Fail("Thông tin đối chiếu tự động không hợp lệ.");
        var ids = new HashSet<Guid>(); var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var contourPoints=0;
        var spaces = new List<ParkingShape>();
        foreach (var item in drawing!.Shapes)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || !Kinds.Contains(item.Kind) || item.Label is null || item.Label.Length > 80)
                Fail("Đối tượng không hợp lệ hoặc trùng mã. Nhãn tối đa 80 ký tự.");
            if (!double.IsFinite(item!.X) || !double.IsFinite(item.Y) || !double.IsFinite(item.Width) || !double.IsFinite(item.Height) || !double.IsFinite(item.Rotation)
                || item.Width < 4 || item.Height < 4 || item.Rotation is < 0 or >= 360)
                Fail("Vị trí, kích thước hoặc góc xoay không hợp lệ.");
            if(item.Contours is {} contours)
            {
                if(item.Kind!="TRACE"||contours.Length is <1 or >2000||item.Outline is not null) Fail("Đường nét nguồn không hợp lệ.");
                foreach(var contour in contours)
                {
                    if(contour is null||contour.Length<3||(contourPoints+=contour.Length)>MaxContourPoints||contour.Any(p=>p is null||!double.IsFinite(p.X)||!double.IsFinite(p.Y)||p.X is <0 or >1||p.Y is <0 or >1)) Fail("Nét nguồn có quá nhiều điểm hoặc điểm không hợp lệ.");
                }
            }
            else if(item.Kind=="TRACE") Fail("Nét nguồn cần có đường bao.");
            if (item.Kind is "LINE" or "DASHED_LINE" or "CURVE")
            {
                var required=item.Kind=="CURVE"?3:2;
                if(item.ControlPoints is not {} controls||controls.Length!=required||item.Outline is not null||item.Contours is not null
                    ||controls.Any(p=>p is null||!double.IsFinite(p.X)||!double.IsFinite(p.Y)||p.X is <0 or >1||p.Y is <0 or >1))
                    Fail("Đường vẽ cần các điểm điều khiển hợp lệ bên trong đối tượng.");
                var first=item.ControlPoints![0];var last=item.ControlPoints[^1];
                if(Math.Sqrt(Math.Pow((last.X-first.X)*item.Width,2)+Math.Pow((last.Y-first.Y)*item.Height,2))<4)
                    Fail("Hai đầu đường vẽ phải cách nhau ít nhất 4 đơn vị.");
            }
            else if(item.ControlPoints is not null) Fail("Chỉ đường thẳng và đường cong được có điểm điều khiển.");
            if(ParkingToolCatalog.IsSquare(item.Kind))
                if(Math.Abs(item.Width-item.Height)>.001||item.Outline is not null) Fail("Hình vuông và vật cản tròn phải có chiều rộng bằng chiều cao.");
            if(item.Kind is "DIVIDER" or "ENTRANCE" or "EXIT" || ParkingToolCatalog.Additional.Any(tool=>tool.Kind==item.Kind&&!tool.Outline))
                if(item.Outline is not null) Fail("Ký hiệu sử dụng khung và góc xoay, không dùng đường bao tùy ý.");
            if (item.Outline is { } outline)
            {
                if (outline.Length != 4 || outline.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X is < 0 or > 1 || p.Y is < 0 or > 1))
                    Fail("Đường bao phải có bốn đỉnh hợp lệ trong đối tượng.");
                // Strict convexity rejects crossed/degenerate outlines before the SAT overlap check.
                var turns = Enumerable.Range(0,4).Select(i => {
                    var a=outline[i];var b=outline[(i+1)%4];var c=outline[(i+2)%4];
                    return (b.X-a.X)*(c.Y-b.Y)-(b.Y-a.Y)*(c.X-b.X);
                }).ToArray();
                if (!turns.All(t=>t>1e-9) && !turns.All(t=>t< -1e-9)) Fail("Đường bao không được tự cắt hoặc bị suy biến.");
            }
            var points = Corners(item);
            if (points.Any(p => !double.IsFinite(p.X)||!double.IsFinite(p.Y)|| (drawing.InfiniteCanvas
                ?Math.Abs(p.X)>MaxCoordinate||Math.Abs(p.Y)>MaxCoordinate
                :p.X < -.01 || p.Y < -.01 || p.X > drawing.Width + .01 || p.Y > drawing.Height + .01)))
                Fail(drawing.InfiniteCanvas?"Tọa độ vượt khả năng xử lý. Hãy kiểm tra kích thước và đơn vị m/cm.":"Đối tượng phải nằm bên trong khung bản vẽ.");
            if (item.Kind == "SPACE")
            {
                if (string.IsNullOrWhiteSpace(item.Label) || !labels.Add(item.Label.Trim().Normalize())) Fail("Mỗi ô đỗ cần một tên riêng, không trùng nhau.");
                spaces.Add(item);
            }
        }
        if (publishing)
        {
            if(spaces.Any(x=>x.NeedsReview)) Fail("Cần xác nhận các ô nhận diện chưa chắc chắn trước khi xuất bản.");
            if (!drawing.Shapes.Any(x=>x.Kind is not ("TRACE" or "LABEL"))) Fail("Cần ít nhất một đối tượng sơ đồ trước khi xuất bản.");
            ValidateSpaceSeparation(drawing);
        }
        return drawing with { Shapes = drawing.Shapes.Select(x => x with { Label = x.Label.Trim().Normalize() }).ToArray() };
        bool ValidPoint(ParkingPoint p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y)&&(drawing.InfiniteCanvas
            ?Math.Abs(p.X)<=MaxCoordinate&&Math.Abs(p.Y)<=MaxCoordinate:p.X>=0&&p.Y>=0&&p.X<=drawing.Width&&p.Y<=drawing.Height);
    }
    // Recognition and publishing share the same geometry rule; manual drafts may remain unfinished.
    public static ParkingDrawing ValidateSpaceSeparation(ParkingDrawing drawing)
    {
        foreach(var pair in SpaceOverlaps(drawing))Fail($"Ô đỗ {pair.First.Label} và {pair.Second.Label} đang chồng lên nhau.");
        return drawing;
    }
    public static IEnumerable<(ParkingShape First,ParkingShape Second)> SpaceOverlaps(ParkingDrawing drawing)
    {
        // Sweep broad-phase bounds; run the exact convex SAT only for nearby candidates.
        var spaces=drawing.Shapes.Where(s=>s.Kind=="SPACE").Select(shape=>{var p=Corners(shape);return new{Shape=shape,Left=p.Min(v=>v.X),Right=p.Max(v=>v.X),Top=p.Min(v=>v.Y),Bottom=p.Max(v=>v.Y)};}).OrderBy(s=>s.Left).ToArray();
        for(var i=0;i<spaces.Length;i++)for(var j=i+1;j<spaces.Length&&spaces[j].Left<spaces[i].Right;j++)
            if(spaces[i].Top<spaces[j].Bottom&&spaces[j].Top<spaces[i].Bottom&&Overlap(spaces[i].Shape,spaces[j].Shape))yield return(spaces[i].Shape,spaces[j].Shape);
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message) => throw new ParkingFailure(400, "parking_drawing_invalid", message);
    // Separating axis theorem supports convex outlines and rotated rectangles; touching is allowed.
    private static (double X, double Y)[] Corners(ParkingShape r)
    {
        var angle = r.Rotation * Math.PI / 180; var c = Math.Cos(angle); var s = Math.Sin(angle);
        var local = r.Outline ?? [new(0,0),new(1,0),new(1,1),new(0,1)];
        return local.Select(p => ((p.X-.5)*r.Width,(p.Y-.5)*r.Height))
            .Select(p => (r.X+r.Width/2+p.Item1*c-p.Item2*s, r.Y+r.Height/2+p.Item1*s+p.Item2*c)).ToArray();
    }
    private static bool Overlap(ParkingShape a, ParkingShape b)
    {
        var aa = Corners(a); var bb = Corners(b);
        foreach (var rect in new[] { aa, bb }) for (var i = 0; i < rect.Length; i++)
        {
            var dx = rect[(i+1)%rect.Length].X-rect[i].X; var dy = rect[(i+1)%rect.Length].Y-rect[i].Y;
            var first = aa.Select(p => -dy*p.X+dx*p.Y).ToArray(); var second = bb.Select(p => -dy*p.X+dx*p.Y).ToArray();
            if (first.Max() <= second.Min()+.01 || second.Max() <= first.Min()+.01) return false;
        }
        return true;
    }
}
