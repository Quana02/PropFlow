using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

/// <summary>Reconstructs separate straight-edged objects from structural lines, never pixel contours.</summary>
internal static partial class ParkingObjectReconstruction
{
    private readonly record struct Point(double X,double Y)
    {
        public static Point operator +(Point a,Point b)=>new(a.X+b.X,a.Y+b.Y);
        public static Point operator -(Point a,Point b)=>new(a.X-b.X,a.Y-b.Y);
        public static Point operator *(Point a,double k)=>new(a.X*k,a.Y*k);
    }
    private sealed record Line(Point A,Point B,double Strength)
    {
        public double Length=>Distance(A,B);
        public Point Direction=>(B-A)*(1/Math.Max(.001,Length));
        public Point Center=>(A+B)*.5;
    }
    private sealed record Cell(Point[] Corners,double Area);

    public static ParkingShape[] Build(byte[] ink,int w,int h,int cw,int ch,CancellationToken ct,bool includeLinework=false,byte[]? strongInk=null)
    {
        var lines=DetectLines(ink,w,h,ct,includeLinework);var cells=Combine(FindBandCells(ink,lines,w,h,ct),FindClosedFaces(ink,w,h,ct));
        cells=RegularizeRows(cells,w,h,ct);
        var result=new List<ParkingShape>();
        foreach(var cell in cells.OrderBy(c=>c.Corners.Min(p=>p.Y)).ThenBy(c=>c.Corners.Min(p=>p.X)))
        {
            var x=cell.Corners.Min(p=>p.X);var y=cell.Corners.Min(p=>p.Y);var width=cell.Corners.Max(p=>p.X)-x;var height=cell.Corners.Max(p=>p.Y)-y;
            // The local pass detects outlines without enough semantic evidence to call them parking bays.
            result.Add(new(Guid.NewGuid(),includeLinework?"RECT":"SPACE",includeLinework?"":$"P-{result.Count+1:000}",x*cw/w,y*ch/h,width*cw/w,height*ch/h,0,
                cell.Corners.Select(p=>new ParkingPoint((p.X-x)/width,(p.Y-y)/height)).ToArray(),null,!includeLinework));
        }
        if(includeLinework)
        {
            var sparseInk = ink.Count(pixel => pixel != 0) < ink.Length * .19;
            var traced = sparseInk ? TraceOpenLinework(ink,w,h,ct) : [];
            var hasTrace = traced.Count != 0;
            AddPreviewMarkings(result,hasTrace ? traced : lines,cells,ink,strongInk??ink,w,h,cw,ch,ct,hasTrace);
            return result.Take(ParkingDrawingRules.MaxObjects).ToArray();
        }
        if(cells.Count==0)return [];
        var hull=Hull(cells.SelectMany(c=>c.Corners).ToArray());var distance=Math.Max(4,Math.Min(w,h)*.04);
        var walls=new List<Line>();
        foreach(var line in lines.Where(l=>l.Length>=Math.Max(30,Math.Min(w,h)*.20)).OrderByDescending(l=>l.Length))
        {
            if(cells.Any(c=>Inside(line.Center,c.Corners,1.5)))continue;
            if(Inside(line.Center,hull,-1))continue;
            if(Enumerable.Range(0,hull.Length).Min(i=>SegmentDistance(line.Center,hull[i],hull[(i+1)%hull.Length]))>distance)continue;
            if(walls.Any(old=>Math.Abs(Cross(old.Direction,line.Direction))<.10&&LineDistance(line.Center,old)<3&&OnSegment(line.Center,old,line.Length/2)))continue;
            walls.Add(line);
            var length=line.Length*cw/w;var thickness=Math.Max(4,Math.Min(cw,ch)/800d);var center=new Point(line.Center.X*cw/w,line.Center.Y*ch/h);
            var angle=(Math.Atan2(line.B.Y*ch/h-line.A.Y*ch/h,line.B.X*cw/w-line.A.X*cw/w)*180/Math.PI+360)%360;
            var shape=new ParkingShape(Guid.NewGuid(),"WALL","",center.X-length/2,center.Y-thickness/2,length,thickness,angle);
            try{ParkingDrawingRules.Validate(new(cw,ch,[shape]));result.Add(shape);}catch(ParkingFailure){ }
        }
        if(result.Count>ParkingDrawingRules.MaxObjects)throw new ParkingFailure(400,"parking_plan_complex","Sơ đồ có quá nhiều đối tượng. Hãy cắt riêng mặt bằng.");
        return result.ToArray();
    }

    private static void AddPreviewMarkings(List<ParkingShape> result,List<Line> lines,List<Cell> cells,byte[] ink,byte[] strongInk,
        int width,int height,int canvasWidth,int canvasHeight,CancellationToken ct,bool traced)
    {
        var minimumLength = Math.Max(12, Math.Min(width, height) * (traced ? .033 : .055));
        var minimumCoverage = traced ? .78 : .86;
        var directCoverage = traced ? .50 : .62;
        var flankOffset = Math.Max(4, Math.Min(width,height)*.009);
        var edges = cells.SelectMany(cell => cell.Corners.Select((point, index) => new Line(point,cell.Corners[(index+1)%cell.Corners.Length],0))).ToArray();
        var candidates = new List<Line>();
        foreach (var line in lines.Where(line => line.Length >= minimumLength))
        {
            ct.ThrowIfCancellationRequested();
            if (StrokeCoverage(line.A,line.B,ink,width,height) < minimumCoverage || StrokeCoverage(line.A,line.B,ink,width,height,radius:0) < directCoverage) continue;
            if (candidates.Any(old => CoversMarking(old,line,4,.13,.72))) continue;
            var offset = new Point(-line.Direction.Y,line.Direction.X)*flankOffset;
            var leftFlank = StrokeCoverage(line.A+offset,line.B+offset,ink,width,height,radius:0);
            var rightFlank = StrokeCoverage(line.A-offset,line.B-offset,ink,width,height,radius:0);
            if (Math.Min(leftFlank,rightFlank) > .55) continue;
            candidates.Add(line);
            if (candidates.Count == 160) break;
        }
        var layoutMarkings = WithoutTopDimensions(candidates,ink,width,height);
        var markings = layoutMarkings.Where(line=>!edges.Any(edge=>CoversMarking(edge,line,2.5,.07,.85))).ToArray();
        var used = new bool[markings.Length];
        for (var i = 0; i < markings.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (used[i]) continue;
            var stem = markings[i];
            for (var j = 0; j < markings.Length; j++)
            {
                if (i == j || used[j]) continue;
                var bar = markings[j];
                if (Math.Abs(Dot(stem.Direction,bar.Direction)) > .13 || bar.Length > stem.Length * 1.25) continue;
                if (!Intersection(stem,bar,out var join) || Distance(join,bar.Center) > Math.Min(6,bar.Length*.17)) continue;
                if (Distance(join,stem.A) > 6 && Distance(join,stem.B) > 6) continue;
                var tip = Distance(join,stem.A) < Distance(join,stem.B) ? stem.B : stem.A;
                var perpendicular = new Point(-stem.Direction.Y,stem.Direction.X);
                if (Dot(perpendicular,bar.Direction) < 0) perpendicular = perpendicular*-1;
                var start = join-perpendicular*(bar.Length/2);
                var end = join+perpendicular*(bar.Length/2);
                if (Math.Max(Distance(bar.A,start),Distance(bar.B,end)) > 5 || StrokeCoverage(start,end,ink,width,height) < .78) continue;

                if (!Add("DIVIDER","",start,end,tip)) continue;
                used[i] = used[j] = true;
                break;
            }
        }
        for (var i = 0; i < markings.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (used[i]) continue;
            var stroke = markings[i];
            var startCap = traced ? FindEndCap(new(stroke.B,stroke.A,stroke.Strength),ink,width,height) : null;
            var endCap = traced ? FindEndCap(stroke,ink,width,height) : null;
            var divider = endCap ?? startCap;
            if (divider is not null && Add("DIVIDER","",divider.A,divider.B,endCap is null ? stroke.B : stroke.A))
            {
                if (startCap is not null && endCap is not null) Add("LINE","",startCap.A,startCap.B);
            }
            else Add("LINE","",stroke.A,stroke.B);
        }

        if (traced)
        {
            var number = 1;
            var recognized = new List<Point[]>();
            foreach (var corners in ReadOpenBays(markings,layoutMarkings,cells,ink,strongInk,width,height,ct))
            {
                if (!Add("SPACE",$"P-{number:000}",corners)) continue;
                recognized.Add(corners);
                number++;
            }
            if (recognized.Count != 0) result.RemoveAll(shape=>CoveredPreviewBlock(shape,recognized,width,height,canvasWidth,canvasHeight));
        }

        bool Add(string kind, string label, params Point[] points)
        {
            try
            {
                var shape = ParkingVisionGeometry.FromCorners(new(Guid.NewGuid(),kind,label,0,0,0,0,NeedsReview:kind=="SPACE"),
                    points.Select(point => new ParkingPoint(point.X*canvasWidth/width,point.Y*canvasHeight/height)).ToArray(),new(canvasWidth,canvasHeight,[]));
                if (kind=="SPACE") result.Insert(0,shape);
                else result.Add(shape);
                return true;
            }
            catch (ParkingFailure) { return false; }
        }
    }

    private static bool CoveredPreviewBlock(ParkingShape shape,List<Point[]> bays,int width,int height,int canvasWidth,int canvasHeight)
    {
        if (shape.Kind!="RECT" || shape.Outline is not { Length:4 } outline) return false;
        var corners = outline.Select(point=>new Point((shape.X+point.X*shape.Width)*width/canvasWidth,
            (shape.Y+point.Y*shape.Height)*height/canvasHeight)).ToArray();
        var covered = 0;
        for (var x = 1; x <= 3; x++)
        for (var y = 1; y <= 3; y++)
        {
            var upper = corners[0]+(corners[1]-corners[0])*(x/4d);
            var lower = corners[3]+(corners[2]-corners[3])*(x/4d);
            if (bays.Any(bay=>Inside(upper+(lower-upper)*(y/4d),bay,2))) covered++;
        }
        return covered>=6;
    }

    private static Line[] WithoutTopDimensions(List<Line> markings,byte[] ink,int width,int height)
    {
        var borders = markings.Where(line => line.Length > height*.62 && Math.Abs(line.Direction.X) < .06).ToArray();
        var left = borders.FirstOrDefault(line => line.Center.X < width*.13);
        var right = borders.FirstOrDefault(line => line.Center.X > width*.87);
        if (left is null || right is null) return markings.ToArray();

        var startLeft = Math.Min(left.A.Y,left.B.Y);
        var startRight = Math.Min(right.A.Y,right.B.Y);
        if (Math.Abs(startLeft-startRight) > height*.04) return markings.ToArray();
        var firstMarking = Math.Min(startLeft,startRight) - height*.015;
        var dimensionLine = markings.FirstOrDefault(line => line.Length > width*.65 && Math.Abs(line.Direction.Y) < .025 && Math.Max(line.A.Y,line.B.Y) < firstMarking);
        if (dimensionLine is null || !HasDimensionTicks(dimensionLine,ink,width,height)) return markings.ToArray();
        return markings.Where(line => Math.Max(line.A.Y,line.B.Y) >= firstMarking).ToArray();
    }

    private static bool HasDimensionTicks(Line line,byte[] ink,int width,int height)
    {
        var tickDistance = Math.Max(5,(int)Math.Round(height*.012));
        var ticks = 0;
        var previous = -tickDistance;
        var count = (int)line.Length;
        for (var i = 0; i < count; i++)
        {
            var point = line.A+(line.B-line.A)*(i/(double)count);
            var x = (int)Math.Round(point.X);
            var y = (int)Math.Round(point.Y);
            if (x < 0 || x >= width || y < tickDistance || y+tickDistance >= height) continue;
            if ((ink[(y-tickDistance)*width+x] & ink[(y+tickDistance)*width+x]) == 0) continue;
            if (i-previous >= tickDistance) ticks++;
            previous = i;
        }
        return ticks >= 3;
    }

    private static bool CoversMarking(Line edge, Line marking,double tolerance,double angularTolerance,double minimumOverlap)
    {
        if (Math.Abs(Cross(edge.Direction,marking.Direction)) > angularTolerance || LineDistance(marking.Center,edge) > tolerance) return false;
        var a = Dot(edge.A-marking.A,marking.Direction);
        var b = Dot(edge.B-marking.A,marking.Direction);
        return Math.Min(marking.Length,Math.Max(a,b)) - Math.Max(0,Math.Min(a,b)) >= marking.Length*minimumOverlap;
    }

    private static double StrokeCoverage(Point start, Point end, byte[] ink, int width, int height,int radius=1)
    {
        const int samples = 24;
        var hits = 0;
        for (var sample = 1; sample <= samples; sample++)
        {
            var point = start+(end-start)*(sample/(double)(samples+1));
            var x = (int)Math.Round(point.X);
            var y = (int)Math.Round(point.Y);
            var supported = false;
            for (var dy = Math.Max(0,y-radius); dy <= Math.Min(height-1,y+radius) && !supported; dy++)
                for (var dx = Math.Max(0,x-radius); dx <= Math.Min(width-1,x+radius) && !supported; dx++)
                    supported = ink[dy*width+dx]!=0;
            if (supported) hits++;
        }
        return hits/(double)samples;
    }

    private static List<Line> DetectLines(byte[] ink,int w,int h,CancellationToken ct,bool preview)
    {
        var points=Enumerable.Range(0,ink.Length).Where(i=>ink[i]!=0).Select(i=>new Point(i%w,i/w)).ToArray();
        if(points.Length<16)return [];
        var densePreview = preview && points.Length > 45_000;
        var diagonal=(int)Math.Ceiling(Math.Sqrt((double)w*w+(double)h*h));var bins=2*diagonal+1;var accumulator=new int[180*bins];
        var cos=Enumerable.Range(0,180).Select(a=>Math.Cos(a*Math.PI/180)).ToArray();var sin=Enumerable.Range(0,180).Select(a=>Math.Sin(a*Math.PI/180)).ToArray();
        for(var angle=0;angle<180;angle++)
        {
            ct.ThrowIfCancellationRequested();var offset=angle*bins;
            foreach(var p in points)accumulator[offset+(int)Math.Round(p.X*cos[angle]+p.Y*sin[angle])+diagonal]++;
        }
        var minimum=Math.Max(12,(int)(Math.Min(w,h)*.025));var peaks=new List<(int Angle,int Rho,int Votes)>();
        for(var angle=0;angle<180;angle++)for(var rho=1;rho<bins-1;rho++)
        {
            var value=accumulator[angle*bins+rho];if(value<minimum)continue;var maximum=true;
            for(var da=-2;da<=2&&maximum;da++)for(var dr=-2;dr<=2;dr++)
            {var a=angle+da;var r=rho+dr;if(a<0||a>=180||r<0||r>=bins)continue;if(accumulator[a*bins+r]>value){maximum=false;break;}}
            if(maximum)peaks.Add((angle,rho-diagonal,value));
        }
        var lines=new List<Line>();var minimumLength=Math.Max(10,Math.Min(w,h)*.023);var gap=Math.Max(2,Math.Max(w,h)*.0025);
        foreach(var peak in peaks.OrderByDescending(p=>p.Votes).Take(densePreview ? 140 : 650))
        {
            ct.ThrowIfCancellationRequested();var normal=new Point(cos[peak.Angle],sin[peak.Angle]);
            var near=points.Where(p=>Math.Abs(Dot(p,normal)-peak.Rho)<=2.5).ToArray();if(near.Length<minimum)continue;
            var center=new Point(near.Average(p=>p.X),near.Average(p=>p.Y));
            var xx=near.Sum(p=>(p.X-center.X)*(p.X-center.X));var yy=near.Sum(p=>(p.Y-center.Y)*(p.Y-center.Y));var xy=near.Sum(p=>(p.X-center.X)*(p.Y-center.Y));
            var theta=.5*Math.Atan2(2*xy,xx-yy);var direction=new Point(Math.Cos(theta),Math.Sin(theta));normal=new(-direction.Y,direction.X);var rho=Dot(center,normal);
            var support=points.Where(p=>Math.Abs(Dot(p,normal)-rho)<=1.4).Select(p=>Dot(p,direction)).Order().ToArray();
            for(var start=0;start<support.Length;)
            {
                var end=start;while(end+1<support.Length&&support[end+1]-support[end]<=gap)end++;
                var length=support[end]-support[start];
                if(length>=minimumLength)
                {
                    var a=normal*rho+direction*support[start];var b=normal*rho+direction*support[end];
                    a=new(Math.Clamp(a.X,0,w-1),Math.Clamp(a.Y,0,h-1));b=new(Math.Clamp(b.X,0,w-1),Math.Clamp(b.Y,0,h-1));
                    var candidate=new Line(a,b,(end-start+1)/Math.Max(1,length));var merged=false;
                    for(var i=0;i<lines.Count;i++)
                    {
                        var old=lines[i];if(Math.Abs(Cross(old.Direction,candidate.Direction))>.045||LineDistance(candidate.Center,old)>2.1||LineDistance(old.Center,candidate)>2.1)continue;
                        var projections=new[]{0d,old.Length,Dot(candidate.A-old.A,old.Direction),Dot(candidate.B-old.A,old.Direction)};
                        var first=Math.Min(projections[2],projections[3]);var last=Math.Max(projections[2],projections[3]);
                        if(first>old.Length+gap||last< -gap)continue;
                        lines[i]=new(old.A+old.Direction*projections.Min(),old.A+old.Direction*projections.Max(),Math.Max(old.Strength,candidate.Strength));merged=true;break;
                    }
                    if(!merged)lines.Add(candidate);
                }
                start=end+1;
            }
        }
        return lines.OrderByDescending(l=>l.Length).Take(densePreview ? 180 : 500).ToList();
    }

    private static List<Cell> FindBandCells(byte[] ink,List<Line> lines,int w,int h,CancellationToken ct)
    {
        var major=lines.Where(l=>l.Length>=Math.Max(24,Math.Max(w,h)*.035)).ToArray();var cells=new List<Cell>();
        for(var i=0;i<major.Length;i++)for(var j=i+1;j<major.Length;j++)
        {
            ct.ThrowIfCancellationRequested();var a=major[i];var b=major[j];var separation=LineDistance(b.Center,a);
            if(Math.Abs(Cross(a.Direction,b.Direction))>.11||separation<8||separation>Math.Min(w,h)*.4)continue;
            var bp=new[]{Dot(b.A-a.A,a.Direction),Dot(b.B-a.A,a.Direction)};var first=Math.Max(0,bp.Min());var last=Math.Min(a.Length,bp.Max());var span=last-first;
            if(span<Math.Max(30,separation*1.4))continue;
            var mid=a.A+a.Direction*((first+last)/2);var normal=new Point(-a.Direction.Y,a.Direction.X);
            if(!Intersection(new(mid,mid+normal,1),b,out var bm))continue;
            if(major.Any(l=>!ReferenceEquals(l,a)&&!ReferenceEquals(l,b)&&Math.Abs(Cross(l.Direction,a.Direction))<.11
                &&Intersection(new(mid,mid+normal,1),l,out var p)&&Dot(p-mid,bm-mid)>4&&Dot(bm-p,bm-mid)>4
                &&Math.Min(Dot(l.A-a.A,a.Direction),Dot(l.B-a.A,a.Direction))<=first+span*.1
                &&Math.Max(Dot(l.A-a.A,a.Direction),Dot(l.B-a.A,a.Direction))>=last-span*.1))continue;
            var hits=new List<(double T,Point A,Point B,double Score)>();
            for(var t=first;t<=last;t+=.75)
            {
                var start=a.A+a.Direction*t;var best=0d;var end=default(Point);
                for(var degrees=-6;degrees<=6;degrees+=3)
                {
                    var angle=degrees*Math.PI/180;var direction=new Point(normal.X*Math.Cos(angle)-normal.Y*Math.Sin(angle),normal.X*Math.Sin(angle)+normal.Y*Math.Cos(angle));
                    if(!Intersection(new(start,start+direction,1),b,out var target)||!OnSegment(target,b,2))continue;
                    var count=Math.Max(4,(int)Math.Ceiling(Distance(start,target)));var supported=0;
                    for(var s=0;s<=count;s++)
                    {
                        var p=start+(target-start)*(s/(double)count);var x=(int)Math.Round(p.X);var y=(int)Math.Round(p.Y);var found=false;
                        for(var yy=Math.Max(0,y-1);yy<=Math.Min(h-1,y+1)&&!found;yy++)for(var xx=Math.Max(0,x-1);xx<=Math.Min(w-1,x+1);xx++)if(ink[yy*w+xx]!=0){found=true;break;}
                        if(found)supported++;
                        if(s+1-supported>(count+1)*.08)break; // Remaining samples cannot meet the support threshold.
                    }
                    var score=supported/(double)(count+1);if(score>best){best=score;end=target;}
                }
                if(best>=.92)hits.Add((t,start,end,best));
            }
            var dividers=new List<(Point A,Point B)>();
            for(var k=0;k<hits.Count;)
            {
                var end=k;while(end+1<hits.Count&&hits[end+1].T-hits[end].T<2)end++;
                var middle=hits[(k+end)/2];dividers.Add((middle.A,middle.B));k=end+1;
            }
            for(var k=0;k+1<dividers.Count;k++)
            {
                var c=dividers[k];var d=dividers[k+1];var quad=new[]{c.A,d.A,d.B,c.B};var area=Area(quad);
                if(area<30||area>w*h*.12||Enumerable.Range(0,4).Min(n=>Distance(quad[n],quad[(n+1)%4]))<4)continue;
                if(quad.Any(p=>p.X<0||p.Y<0||p.X>=w||p.Y>=h))continue;
                var turns=Enumerable.Range(0,4).Select(n=>Cross(quad[(n+1)%4]-quad[n],quad[(n+2)%4]-quad[(n+1)%4])).ToArray();if(!turns.All(v=>v>0)&&!turns.All(v=>v<0))continue;
                var center=quad.Aggregate(new Point(),(p,q)=>p+q)*.25;
                if(cells.Any(old=>Distance(old.Corners.Aggregate(new Point(),(p,q)=>p+q)*.25,center)<3&&Math.Abs(old.Area-area)/area<.25))continue;
                cells.Add(new(quad,area));
            }
        }
        return cells;
    }

    private static List<Cell> FindClosedFaces(byte[] original,int w,int h,CancellationToken ct)
    {
        var ink=(byte[])original.Clone();for(var y=1;y<h-1;y++)for(var x=1;x<w-1;x++){var p=y*w+x;if(ink[p]==0&&((original[p-1]!=0&&original[p+1]!=0)||(original[p-w]!=0&&original[p+w]!=0)))ink[p]=1;}
        var visited=new bool[ink.Length];var queue=new int[ink.Length];var faces=new List<Cell>();
        for(var start=0;start<ink.Length;start++)
        {
            if((start&16383)==0)ct.ThrowIfCancellationRequested();if(visited[start]||ink[start]!=0)continue;
            var head=0;var tail=1;queue[0]=start;visited[start]=true;var edge=false;var boundary=new List<Point>();
            while(head<tail)
            {
                var p=queue[head++];var x=p%w;var y=p/w;if(x==0||y==0||x==w-1||y==h-1)edge=true;
                Visit(x-1,y);Visit(x+1,y);Visit(x,y-1);Visit(x,y+1);
                if((x>0&&ink[p-1]!=0)||(x+1<w&&ink[p+1]!=0)||(y>0&&ink[p-w]!=0)||(y+1<h&&ink[p+w]!=0))boundary.Add(new(x,y));
            }
            if(edge||tail<15||tail>w*h*.15||boundary.Count<4)continue;
            var hull=Hull(boundary.ToArray());if(hull.Length<4)continue;var quad=hull.ToList();
            while(quad.Count>4){var index=Enumerable.Range(0,quad.Count).MinBy(n=>LineDistance(quad[n],new(quad[(n+quad.Count-1)%quad.Count],quad[(n+1)%quad.Count],1)));quad.RemoveAt(index);}
            var sides=Enumerable.Range(0,4).Select(n=>Distance(quad[n],quad[(n+1)%4])).ToArray();var area=Area(quad.ToArray());
            if(sides.Min()<3||sides.Max()/sides.Min()>15||tail/area<.2)continue;
            if(hull.Max(p=>Enumerable.Range(0,4).Min(n=>LineDistance(p,new(quad[n],quad[(n+1)%4],1))))>Math.Max(1,sides.Min()*.1))continue;
            faces.Add(new(quad.ToArray(),area));if(faces.Count>4000)throw new ParkingFailure(400,"parking_plan_complex","Sơ đồ quá nhiều chi tiết. Hãy cắt riêng mặt bằng.");
            void Visit(int x,int y){if(x<0||y<0||x>=w||y>=h)return;var p=y*w+x;if(visited[p]||ink[p]!=0)return;visited[p]=true;queue[tail++]=p;}
        }
        (double W,double H) Size(Cell cell){var s=Enumerable.Range(0,4).Select(n=>Distance(cell.Corners[n],cell.Corners[(n+1)%4])).ToArray();return(s.Min(),s.Max());}
        var grouped=faces.Where(c=>faces.Count(other=>!ReferenceEquals(c,other)&&Math.Abs(Size(c).W-Size(other).W)<Math.Max(2,Size(c).W*.3)&&Math.Abs(Size(c).H-Size(other).H)<Math.Max(2,Size(c).H*.3))>=2).ToArray();
        return grouped.Where(c=>!grouped.Any(other=>other.Area>c.Area*1.3&&c.Corners.All(p=>Inside(p,other.Corners,0)))).ToList();
    }

    private static List<Cell> Combine(List<Cell> structural,List<Cell> faces)
    {
        var preferred=new List<Cell>();
        foreach(var parent in structural.OrderByDescending(c=>c.Area))
        {
            var children=faces.Where(f=>f.Area<parent.Area*.7&&f.Corners.All(p=>Inside(p,parent.Corners,1))).ToArray();
            var matching=faces.Any(f=>f.Area>=parent.Area*.7&&(Inside(f.Corners.Aggregate(new Point(),(p,q)=>p+q)*.25,parent.Corners,1)
                ||Inside(parent.Corners.Aggregate(new Point(),(p,q)=>p+q)*.25,f.Corners,1)));
            if(!matching&&!(children.Length>=3&&children.Sum(c=>c.Area)>parent.Area*.5))preferred.Add(parent);
        }
        var proposed=preferred.Concat(faces.Where(f=>!preferred.Any(parent=>f.Area<parent.Area*.8&&Inside(f.Corners.Aggregate(new Point(),(p,q)=>p+q)*.25,parent.Corners,1)))).ToArray();
        var accepted=new List<Cell>();
        foreach(var raw in proposed.OrderByDescending(c=>c.Area))
        {
            var center=raw.Corners.Aggregate(new Point(),(p,q)=>p+q)*.25;var side=Enumerable.Range(0,4).Min(n=>Distance(raw.Corners[n],raw.Corners[(n+1)%4]));
            var quad=raw.Corners.Select(p=>center+(p-center)*Math.Max(.75,1-2.4/side)).ToArray();var cell=new Cell(quad,Area(quad));if(cell.Area<18)continue;
            if(!accepted.Any(other=>Overlap(cell.Corners,other.Corners)))accepted.Add(cell);
        }
        if(accepted.Count>ParkingDrawingRules.MaxObjects)throw new ParkingFailure(400,"parking_plan_complex","Sơ đồ có quá nhiều ô. Hãy cắt riêng mặt bằng.");
        if(accepted.Count<20)return accepted;
        var keep=new List<Cell>();var seen=new HashSet<int>();
        for(var i=0;i<accepted.Count;i++)
        {
            if(!seen.Add(i))continue;var group=new List<int>{i};
            for(var index=0;index<group.Count;index++)for(var j=0;j<accepted.Count;j++)
            {
                if(seen.Contains(j))continue;var a=accepted[group[index]];var b=accepted[j];
                var sideA=Enumerable.Range(0,4).Max(k=>Distance(a.Corners[k],a.Corners[(k+1)%4]));var sideB=Enumerable.Range(0,4).Max(k=>Distance(b.Corners[k],b.Corners[(k+1)%4]));
                var gap=a.Corners.Min(p=>b.Corners.Min(q=>Distance(p,q)));
                if(gap<=Math.Max(10,Math.Min(sideA,sideB)*6)){seen.Add(j);group.Add(j);}
            }
            if(group.Count>=3)keep.AddRange(group.Select(index=>accepted[index]));
        }
        return keep;
    }

    private sealed record Frame(Point Center, Point U, Point V, double Width, double Height);

    // Only regularize repeated, neighboring cells. Never add missing stalls or alter isolated shapes.
    private static List<Cell> RegularizeRows(List<Cell> cells, int width, int height, CancellationToken ct)
    {
        var frames = cells.Select(FrameOf).ToArray();
        var used = new HashSet<int>();
        var result = cells.ToArray();
        for (var seed = 0; seed < cells.Count; seed++)
        {
            ct.ThrowIfCancellationRequested();
            if (used.Contains(seed)) continue;
            var frame = frames[seed];
            foreach (var alongU in new[] { true, false })
            {
                var axis = alongU ? frame.U : frame.V;
                var normal = new Point(-axis.Y, axis.X);
                var alongSize = alongU ? frame.Width : frame.Height;
                var crossSize = alongU ? frame.Height : frame.Width;
                var group = new List<int> { seed };
                for (var k = 0; k < group.Count; k++)
                for (var j = 0; j < cells.Count; j++)
                {
                    if (used.Contains(j) || group.Contains(j)) continue;
                    var other = frames[j];
                    if (Math.Abs(Cross(frame.U, other.U)) > .12 || Dot(frame.U, other.U) < 0
                        || Math.Abs(other.Width - frame.Width) > Math.Max(2.5, frame.Width * .20)
                        || Math.Abs(other.Height - frame.Height) > Math.Max(2.5, frame.Height * .20)) continue;
                    var offset = other.Center - frames[group[k]].Center;
                    if (Math.Abs(Dot(offset, normal)) <= Math.Max(2, crossSize * .16)
                        && Math.Abs(Dot(offset, axis)) <= alongSize * 1.65) group.Add(j);
                }
                if (group.Count < 3) continue;
                var sorted = group.OrderBy(i => Dot(frames[i].Center, axis)).ToArray();
                var center = group.Aggregate(new Point(), (sum, i) => sum + frames[i].Center) * (1d / group.Count);
                var positions = sorted.Select(i => Dot(frames[i].Center - center, axis)).ToArray();
                var offsets = sorted.Select(i => Dot(frames[i].Center - center, normal)).ToArray();
                var slope = positions.Zip(offsets, (x, y) => x * y).Sum() / Math.Max(.001, positions.Sum(x => x * x));
                if (Math.Abs(slope) > .12) continue;
                var u = Unit(group.Aggregate(new Point(), (sum, i) => sum + frames[i].U));
                var v = Unit(group.Aggregate(new Point(), (sum, i) => sum + frames[i].V));
                var commonWidth = Median(group.Select(i => frames[i].Width));
                var commonHeight = Median(group.Select(i => frames[i].Height));
                var spacing = Median(positions.Skip(1).Select((x, i) => x - positions[i]));
                var regularSpacing = positions.Skip(1).Select((x, i) => Math.Abs((x - positions[i]) / spacing - 1)).All(x => x < .16);
                var replacements = new List<(int Index, Cell Cell)>();
                for (var k = 0; k < sorted.Length; k++)
                {
                    var index = sorted[k];
                    var t = regularSpacing ? (k - (sorted.Length - 1) / 2d) * spacing : positions[k];
                    var target = center + axis * t + normal * (t * slope);
                    var quad = new[] { target - u * (commonWidth / 2) - v * (commonHeight / 2),
                        target + u * (commonWidth / 2) - v * (commonHeight / 2),
                        target + u * (commonWidth / 2) + v * (commonHeight / 2),
                        target - u * (commonWidth / 2) + v * (commonHeight / 2) };
                    // Keep adjustment local to the source, and fall back to the original whole row on conflict.
                    if (Distance(target, frames[index].Center) > Math.Max(1.5, Math.Min(commonWidth, commonHeight) * .20)
                        || quad.Any(p => p.X < 0 || p.Y < 0 || p.X >= width || p.Y >= height)) break;
                    replacements.Add((index, new(quad, Area(quad))));
                }
                if (replacements.Count != sorted.Length) continue;
                if (replacements.Any(a => replacements.Any(b => a.Index != b.Index && Overlap(a.Cell.Corners, b.Cell.Corners))
                    || result.Where((_, i) => !group.Contains(i)).Any(other => Overlap(a.Cell.Corners, other.Corners)))) continue;
                foreach (var replacement in replacements) { result[replacement.Index] = replacement.Cell; used.Add(replacement.Index); }
                break;
            }
        }
        return result.ToList();
    }

    private static Frame FrameOf(Cell cell)
    {
        var q = cell.Corners;
        var a = (q[1] - q[0]) + (q[2] - q[3]);
        var b = (q[3] - q[0]) + (q[2] - q[1]);
        if (Math.Abs(Unit(a).X) < Math.Abs(Unit(b).X)) (a, b) = (b, a);
        if (a.X < 0) a = a * -1;
        if (b.Y < 0) b = b * -1;
        return new(q.Aggregate(new Point(), (sum, p) => sum + p) * .25, Unit(a), Unit(b), Distance(new(), a) / 2, Distance(new(), b) / 2);
    }

    private static Point Unit(Point p) => p * (1 / Math.Max(.001, Math.Sqrt(Dot(p, p))));
    private static double Median(IEnumerable<double> numbers)
    {
        var values = numbers.Order().ToArray();
        return (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
    }

    private static bool Intersection(Line a, Line b, out Point point)
    {
        var denominator = Cross(a.Direction, b.Direction);
        if (Math.Abs(denominator) < .02)
        {
            point = default;
            return false;
        }

        point = a.A + a.Direction * (Cross(b.A - a.A, b.Direction) / denominator);
        return true;
    }

    private static bool OnSegment(Point point, Line line, double tolerance)
    {
        var projection = Dot(point - line.A, line.Direction);
        return projection >= -tolerance && projection <= line.Length + tolerance;
    }

    private static double LineDistance(Point p,Line line)=>Math.Abs(Cross(p-line.A,line.Direction));
    private static double SegmentDistance(Point p,Point a,Point b){var direction=b-a;var t=Math.Clamp(Dot(p-a,direction)/Math.Max(.001,Dot(direction,direction)),0,1);return Distance(p,a+direction*t);}
    private static bool Inside(Point p,Point[] polygon,double tolerance)
    {var distances=Enumerable.Range(0,polygon.Length).Select(i=>Cross(polygon[(i+1)%polygon.Length]-polygon[i],p-polygon[i])/Math.Max(.001,Distance(polygon[i],polygon[(i+1)%polygon.Length]))).ToArray();return distances.All(x=>x>=-tolerance)||distances.All(x=>x<=tolerance);}
    private static bool Overlap(Point[] a,Point[] b)
    {foreach(var polygon in new[]{a,b})for(var i=0;i<polygon.Length;i++){var edge=polygon[(i+1)%polygon.Length]-polygon[i];var axis=new Point(-edge.Y,edge.X);var aa=a.Select(p=>Dot(p,axis)).ToArray();var bb=b.Select(p=>Dot(p,axis)).ToArray();if(aa.Max()<=bb.Min()+.01||bb.Max()<=aa.Min()+.01)return false;}return true;}
    private static Point[] Hull(Point[] points)
    {var sorted=points.Distinct().OrderBy(p=>p.X).ThenBy(p=>p.Y).ToArray();var hull=new List<Point>();foreach(var p in sorted){while(hull.Count>=2&&Cross(hull[^1]-hull[^2],p-hull[^1])<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}var count=hull.Count;for(var i=sorted.Length-2;i>=0;i--){var p=sorted[i];while(hull.Count>count&&Cross(hull[^1]-hull[^2],p-hull[^1])<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}if(hull.Count>1)hull.RemoveAt(hull.Count-1);return hull.ToArray();}
    private static double Area(Point[] points)=>Math.Abs(Enumerable.Range(0,points.Length).Sum(i=>Cross(points[i],points[(i+1)%points.Length])))/2;
    private static double Cross(Point a,Point b)=>a.X*b.Y-a.Y*b.X;
    private static double Dot(Point a,Point b)=>a.X*b.X+a.Y*b.Y;
    private static double Distance(Point a,Point b)=>Math.Sqrt(Dot(a-b,a-b));
}
