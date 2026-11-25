using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

/// <summary>Source-grounded checks. A score measures raster support, not recognition accuracy.</summary>
public static class ParkingSourceGeometry
{
    public static ParkingDrawing Inspect(ParkingDrawing drawing,ParkingGeometryResult source,CancellationToken ct=default,IReadOnlySet<Guid>? uncertain=null)
    {
        ParkingDrawingRules.Validate(drawing);ct.ThrowIfCancellationRequested();
        source=source with{Drawing=drawing with{Shapes=[],RecognitionReview=null}};
        var edges=source with{Ink=EdgeMask(source,ct)};
        var issues=new List<ParkingGeometryIssue>();var shapes=drawing.Shapes.ToArray();var sourceChecked=0;var refined=0;var attempts=0;
        var conflicts=ParkingDrawingRules.SpaceOverlaps(drawing).Take(ParkingDrawingRules.MaxObjects).ToArray();
        var overlapping=conflicts.SelectMany(p=>new[]{p.First.Id,p.Second.Id}).ToHashSet();
        foreach(var pair in conflicts)Add("overlap",$"Ô {Name(pair.First)} và {Name(pair.Second)} chồng nhau; cần đọc lại ranh giới.",pair.First.Id,pair.Second.Id);
        for(var i=0;i<shapes.Length;i++)
        {
            ct.ThrowIfCancellationRequested();var shape=shapes[i];
            if(uncertain?.Contains(shape.Id)==true&&shape.Kind!="SPACE")Add("semantic",$"Cần xác nhận loại khối và đường bao của {Name(shape)}.",shape.Id);
            if(shape.Kind is not ("SPACE" or "WALL" or "LINE" or "CURVE"))continue;
            var boundary=Boundary(shape,drawing,source);if(boundary.Length<2)continue;
            sourceChecked++;
            var scores=Scores(boundary,edges,1);
            if(shape.Kind=="SPACE")
            {
                var contradicted=false;
                try{ParkingBayEdgeAlignment.ValidateOrientation(shape,source,ct);}
                catch(ParkingFailure){contradicted=true;Add("orientation",$"Hướng ô {Name(shape)} mâu thuẫn với đường biên nguồn; đọc lại bốn góc.",shape.Id);}
                if(!contradicted&&!overlapping.Contains(shape.Id)&&uncertain?.Contains(shape.Id)==true&&attempts++<24)
                {
                    var fit=FitBoundary(shape,drawing,edges,ct);
                    if(fit is not null&&!Intersects(fit,shapes,drawing))
                    {shape=shapes[i]=fit;boundary=Boundary(shape,drawing,source);scores=Scores(boundary,edges,1);refined++;}
                }
                if(scores.Min()>.80&&HasOuterFrame(boundary,edges,ct))
                    Add("nested_outline",$"Có đường bao lớn hơn quanh ô {Name(shape)}; kiểm tra có đang bắt nhầm nét xe bên trong ô đỗ.",shape.Id);
            }
            // Blank space (LANE) is deliberately not checked as if it had four inked edges.
            if(scores.Average()<.68||scores.Min()<.35)
                Add("source_edges",$"Đường bao {Name(shape)} chưa bám đủ nét nguồn; kiểm tra vị trí, góc và ranh giới ngoài hình xe.",shape.Id);
        }
        var lines=shapes.Where(s=>s.Kind=="LINE").OrderByDescending(s=>Math.Max(s.Width,s.Height)).Take(96).ToArray();
        for(var i=0;i<lines.Length;i++)for(var j=i+1;j<lines.Length;j++)
        {
            ct.ThrowIfCancellationRequested();
            if(HatchedStrip(Boundary(lines[i],drawing,source),Boundary(lines[j],drawing,source),source))
                Add("wall_tool","Hai nét song song bao vùng có nét tô/gạch chéo; kiểm tra dùng khối tường thay cho hai đường thẳng.",lines[i].Id,lines[j].Id);
        }
        var previous=drawing.RecognitionReview;
        var managedCount=shapes.Count(s=>s.Kind!="TRACE");
        return drawing with{Shapes=shapes,RecognitionReview=new(managedCount,sourceChecked,Math.Min(managedCount,refined+(previous?.RefinedObjects??0)),previous?.UnreadRegions??0,issues.Concat(previous?.Issues.Where(i=>i.Code.StartsWith("region_",StringComparison.Ordinal))??[]).Take(ParkingDrawingRules.MaxObjects).ToArray())};
        void Add(string code,string message,params Guid[] ids){if(issues.Count<ParkingDrawingRules.MaxObjects)issues.Add(new(code,message,ids));}
    }
    private static byte[] EdgeMask(ParkingGeometryResult source,CancellationToken ct)
    {
        var result=new byte[source.Ink.Length];var w=source.PixelWidth;var h=source.PixelHeight;
        for(var y=0;y<h;y++)
        {
            if((y&63)==0)ct.ThrowIfCancellationRequested();
            for(var x=0;x<w;x++)
            {
                var p=y*w+x;if(source.Ink[p]==0)continue;
                if(x==0||y==0||x==w-1||y==h-1||source.Ink[p-1]==0||source.Ink[p+1]==0||source.Ink[p-w]==0||source.Ink[p+w]==0)result[p]=1;
            }
        }
        return result;
    }
    internal static ParkingPoint[] Corners(ParkingShape shape)
    {
        var a=shape.Rotation*Math.PI/180;var c=Math.Cos(a);var s=Math.Sin(a);
        return(shape.Outline??[new(0,0),new(1,0),new(1,1),new(0,1)]).Select(p=>new ParkingPoint(shape.X+shape.Width/2+(p.X-.5)*shape.Width*c-(p.Y-.5)*shape.Height*s,shape.Y+shape.Height/2+(p.X-.5)*shape.Width*s+(p.Y-.5)*shape.Height*c)).ToArray();
    }
    private static ParkingPoint[] Boundary(ParkingShape shape,ParkingDrawing drawing,ParkingGeometryResult source)
    {
        ParkingPoint[] world;
        if(shape.ControlPoints is {} controls)
        {
            var angle=shape.Rotation*Math.PI/180;var c=Math.Cos(angle);var s=Math.Sin(angle);
            var points=controls.Select(p=>new ParkingPoint(shape.X+shape.Width/2+(p.X-.5)*shape.Width*c-(p.Y-.5)*shape.Height*s,shape.Y+shape.Height/2+(p.X-.5)*shape.Width*s+(p.Y-.5)*shape.Height*c)).ToArray();
            world=points.Length==3?Enumerable.Range(0,17).Select(i=>{var t=i/16d;return new ParkingPoint(Math.Pow(1-t,2)*points[0].X+2*t*(1-t)*points[1].X+t*t*points[2].X,Math.Pow(1-t,2)*points[0].Y+2*t*(1-t)*points[1].Y+t*t*points[2].Y);}).ToArray():points;
        }
        else world=Corners(shape);
        return world.Select(p=>new ParkingPoint(p.X*source.PixelWidth/drawing.Width,p.Y*source.PixelHeight/drawing.Height)).ToArray();
    }
    private static double[] Scores(ParkingPoint[] points,ParkingGeometryResult source,int radius)
    {
        var closed=points.Length==4;var count=closed?4:points.Length-1;
        return Enumerable.Range(0,count).Select(i=>Support(points[i],points[(i+1)%points.Length],source,radius)).ToArray();
    }
    private static double Support(ParkingPoint a,ParkingPoint b,ParkingGeometryResult source,int radius)
    {
        var count=Math.Clamp((int)Math.Ceiling(Distance(a,b)),12,96);var hits=0;
        for(var i=0;i<count;i++)
        {
            var t=.06+.88*(i+.5)/count;var x=(int)Math.Round(a.X+(b.X-a.X)*t);var y=(int)Math.Round(a.Y+(b.Y-a.Y)*t);var hit=false;
            for(var yy=Math.Max(0,y-radius);yy<=Math.Min(source.PixelHeight-1,y+radius)&&!hit;yy++)
                for(var xx=Math.Max(0,x-radius);xx<=Math.Min(source.PixelWidth-1,x+radius);xx++)if(source.Ink[yy*source.PixelWidth+xx]!=0){hit=true;break;}
            if(hit)hits++;
        }
        return hits/(double)count;
    }
    // Fit only uncertain, nearby edges. No row template, 90-degree correction or unconstrained rescaling.
    private static ParkingShape? FitBoundary(ParkingShape shape,ParkingDrawing drawing,ParkingGeometryResult source,CancellationToken ct)
    {
        var p=Boundary(shape,drawing,source);if(p.Length!=4)return null;
        var before=Scores(p,source,0);if(before.Min()>=.90)return null;
        var radius=Math.Min(2.5,Enumerable.Range(0,4).Min(i=>Distance(p[i],p[(i+1)%4]))*.03);if(radius<.75)return null;
        var fitted=new (ParkingPoint A,ParkingPoint B)[4];
        for(var edge=0;edge<4;edge++)
        {
            ct.ThrowIfCancellationRequested();var a=p[edge];var b=p[(edge+1)%4];var length=Distance(a,b);var mx=(a.X+b.X)/2;var my=(a.Y+b.Y)/2;
            var baseAngle=Math.Atan2(b.Y-a.Y,b.X-a.X);var best=Support(a,b,source,0);fitted[edge]=(a,b);
            foreach(var degrees in new[]{0d,-.5,.5,-1,1})
            for(var step=-(int)(radius*2);step<=(int)(radius*2);step++)
            {
                var offset=step*.5;var angle=baseAngle+degrees*Math.PI/180;var dx=Math.Cos(angle);var dy=Math.Sin(angle);
                var aa=new ParkingPoint(mx-dy*offset-dx*length/2,my+dx*offset-dy*length/2);var bb=new ParkingPoint(mx-dy*offset+dx*length/2,my+dx*offset+dy*length/2);
                var score=Support(aa,bb,source,0);
                if(score>best+.02){best=score;fitted[edge]=(aa,bb);}
            }
        }
        var corners=new ParkingPoint[4];
        for(var i=0;i<4;i++)
        {
            var intersection=Intersection(fitted[(i+3)%4],fitted[i]);if(intersection is null||Distance(p[i],intersection)>radius)return null;corners[i]=intersection;
        }
        var after=Scores(corners,source,0);var area=Area(corners)/Math.Max(.001,Area(p));
        if(after.Min()<.90||after.Average()<before.Average()+.15||area is <.97 or >1.03)return null;
        var world=corners.Select(v=>new ParkingPoint(v.X*drawing.Width/source.PixelWidth,v.Y*drawing.Height/source.PixelHeight)).ToArray();
        try{return ParkingVisionGeometry.FromCorners(shape,world,drawing);}catch(ParkingFailure){return null;}
    }
    private static bool Intersects(ParkingShape shape,ParkingShape[] others,ParkingDrawing drawing)
    {
        var radius=Math.Sqrt(shape.Width*shape.Width+shape.Height*shape.Height)/2;
        foreach(var other in others.Where(s=>s.Kind=="SPACE"&&s.Id!=shape.Id))
        {
            var distance=Math.Sqrt(Math.Pow(shape.X+shape.Width/2-other.X-other.Width/2,2)+Math.Pow(shape.Y+shape.Height/2-other.Y-other.Height/2,2));
            if(distance>radius+Math.Sqrt(other.Width*other.Width+other.Height*other.Height)/2)continue;
            if(ParkingDrawingRules.SpaceOverlaps(drawing with{Shapes=[shape,other]}).Any())return true;
        }
        return false;
    }
    private static bool HatchedStrip(ParkingPoint[] first,ParkingPoint[] second,ParkingGeometryResult source)
    {
        if(first.Length!=2||second.Length!=2)return false;var a=first[0];var b=first[1];var c=second[0];var d=second[1];
        if(Distance(a,c)+Distance(b,d)>Distance(a,d)+Distance(b,c))(c,d)=(d,c);
        var length=Distance(a,b);var other=Distance(c,d);if(length<30||Math.Abs(length-other)>length*.08)return false;
        var cross=Math.Abs((b.X-a.X)*(d.Y-c.Y)-(b.Y-a.Y)*(d.X-c.X))/(length*other);if(cross>.025)return false;
        var gap=Math.Abs((c.X-a.X)*(b.Y-a.Y)-(c.Y-a.Y)*(b.X-a.X))/length;if(gap<3||gap>Math.Min(24,length*.08))return false;
        if(Distance(a,c)>gap+4||Distance(b,d)>gap+4)return false;
        var hits=0;var total=0;
        for(var i=1;i<32;i++)for(var j=1;j<8;j++)
        {
            var t=i/32d;var u=j/8d;var x=(int)Math.Round((a.X+(b.X-a.X)*t)*(1-u)+(c.X+(d.X-c.X)*t)*u);var y=(int)Math.Round((a.Y+(b.Y-a.Y)*t)*(1-u)+(c.Y+(d.Y-c.Y)*t)*u);
            if(x<0||y<0||x>=source.PixelWidth||y>=source.PixelHeight)continue;total++;if(source.Ink[y*source.PixelWidth+x]!=0)hits++;
        }
        return total>0&&hits/(double)total>.30;
    }
    private static bool HasOuterFrame(ParkingPoint[] p,ParkingGeometryResult source,CancellationToken ct)
    {
        if(p.Length!=4)return false;
        var range=Math.Min(12,Enumerable.Range(0,4).Min(i=>Distance(p[i],p[(i+1)%4]))*.55);if(range<3)return false;
        var cx=p.Average(v=>v.X);var cy=p.Average(v=>v.Y);var options=new List<(ParkingPoint A,ParkingPoint B)>[4];
        for(var edge=0;edge<4;edge++)
        {
            ct.ThrowIfCancellationRequested();var a=p[edge];var b=p[(edge+1)%4];var length=Distance(a,b);var nx=-(b.Y-a.Y)/length;var ny=(b.X-a.X)/length;
            if(nx*((a.X+b.X)/2-cx)+ny*((a.Y+b.Y)/2-cy)<0){nx=-nx;ny=-ny;}
            var candidates=new List<(ParkingPoint A,ParkingPoint B)>();
            for(var offset=2;offset<=range;offset++)
            {
                var aa=new ParkingPoint(a.X+nx*offset,a.Y+ny*offset);var bb=new ParkingPoint(b.X+nx*offset,b.Y+ny*offset);
                if(Support(aa,bb,source,0)>=.90)candidates.Add((aa,bb));
            }
            if(candidates.Count==0)return false;
            options[edge]=new[]{candidates[0],candidates[candidates.Count/2],candidates[^1]}.Distinct().ToList();
        }
        foreach(var a in options[0])foreach(var b in options[1])foreach(var c in options[2])foreach(var d in options[3])
        {
            var lines=new[]{a,b,c,d};var corners=new ParkingPoint[4];var valid=true;
            for(var i=0;i<4;i++){var point=Intersection(lines[(i+3)%4],lines[i]);if(point is null){valid=false;break;}corners[i]=point;}
            if(!valid)continue;var ratio=Area(corners)/Math.Max(.001,Area(p));
            if(ratio is >=1.30 and <=3.5&&Scores(corners,source,0).Min()>=.90)return true;
        }
        return false;
    }
    private static ParkingPoint? Intersection((ParkingPoint A,ParkingPoint B) first,(ParkingPoint A,ParkingPoint B) second)
    {
        var dx=first.B.X-first.A.X;var dy=first.B.Y-first.A.Y;var ex=second.B.X-second.A.X;var ey=second.B.Y-second.A.Y;var determinant=dx*ey-dy*ex;if(Math.Abs(determinant)<1e-6)return null;
        var t=((second.A.X-first.A.X)*ey-(second.A.Y-first.A.Y)*ex)/determinant;return new(first.A.X+t*dx,first.A.Y+t*dy);
    }
    private static double Area(ParkingPoint[] p)=>Math.Abs(Enumerable.Range(0,p.Length).Sum(i=>p[i].X*p[(i+1)%p.Length].Y-p[i].Y*p[(i+1)%p.Length].X))/2;
    private static double Distance(ParkingPoint a,ParkingPoint b)=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    private static string Name(ParkingShape s)=>s.Label.Length>0?s.Label:s.Kind switch{"WALL"=>"tường","LINE"=>"đường thẳng","CURVE"=>"đường cong",_=>"đối tượng"};
}
