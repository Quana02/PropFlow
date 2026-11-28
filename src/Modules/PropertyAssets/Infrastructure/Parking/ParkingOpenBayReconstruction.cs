using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal static partial class ParkingObjectReconstruction
{
    private sealed record OpenBay(Point[] Corners, double Confidence);
    private sealed record EndCap(Point Center, Point A, Point B);

    private static IEnumerable<Point[]> ReadOpenBays(Line[] markings,Line[] layoutMarkings,List<Cell> closedCells,byte[] ink,byte[] strongInk,int width,int height,CancellationToken ct)
    {
        var shortSide = Math.Min(width,height);
        var proposals = ReadParallelBayRows(layoutMarkings,ink,strongInk,width,height,ct).ToList();
        foreach (var back in BayBacks(markings,ct).Where(line => line.Length >= Math.Max(16,shortSide*.04) && line.Length <= shortSide*.15))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var side in new[] { -1, 1 })
            {
                OpenBay? best = null;
                for (var degrees = -6; degrees <= 6; degrees += 2)
                {
                    var angle = Math.Atan2(back.Direction.Y,back.Direction.X) + degrees*Math.PI/180;
                    var across = new Point(Math.Cos(angle),Math.Sin(angle));
                    var inward = new Point(-across.Y,across.X)*side;
                    var a = back.Center-across*(back.Length/2);
                    var b = back.Center+across*(back.Length/2);
                    var limit = (int)Math.Ceiling(back.Length*2.75)+9;
                    var left = FollowMarking(a,inward,limit,ink,width,height);
                    var right = FollowMarking(b,inward,limit,ink,width,height);
                    var depth = Math.Min(left,right);
                    if (depth < back.Length*1.30 || depth > back.Length*2.65) continue;

                    var c = b+inward*depth;
                    var d = a+inward*depth;
                    if (new[] { a,b,c,d }.Any(point => point.X < 0 || point.X >= width-1 || point.Y < 0 || point.Y >= height-1)) continue;
                    if (StrokeCoverage(c,d,ink,width,height,radius:0) > .37 || HasInteriorInk(a,b,inward,depth,ink,width,height)) continue;
                    // An actual terminal marking anchors the open end. A long crossing of unrelated lines is not enough.
                    var leftCap = FindEndCap(new(a,d,0),ink,width,height);
                    var rightCap = FindEndCap(new(b,c,0),ink,width,height);
                    if (leftCap is null && rightCap is null) continue;
                    if (Math.Abs(left-right) > Math.Max(7,depth*.16) && (left<=right ? leftCap : rightCap) is null) continue;

                    var center = (a+c)*.5;
                    if (closedCells.Any(cell=>cell.Area>=back.Length*depth*.60 && Inside(center,cell.Corners,-1))) continue;

                    var score = Math.Min(StrokeCoverage(a,b,ink,width,height),Math.Min(StrokeCoverage(a,d,ink,width,height),StrokeCoverage(b,c,ink,width,height)));
                    if (score < .87 || (best is not null && score <= best.Confidence)) continue;
                    best = new([a,b,c,d],score);
                }
                if (best is not null) proposals.Add(best);
            }
        }

        var accepted = new List<Point[]>();
        foreach (var candidate in proposals.OrderByDescending(candidate=>candidate.Confidence))
        {
            // Bring each edge slightly inside its stroke so bays sharing the same painted line do not overlap.
            var center = candidate.Corners.Aggregate(new Point(0,0),(sum,point)=>sum+point)*.25;
            var corners = candidate.Corners.Select(point=>point+(center-point)*.045).ToArray();
            if (!accepted.Any(other=>Overlap(other,corners))) accepted.Add(corners);
        }
        var bays = accepted.Where(corners=>accepted.Any(other=>!ReferenceEquals(other,corners)&&BelongsToSameRow(corners,other))).ToArray();
        return InReadingOrder(bays);
    }

    private static IEnumerable<Point[]> InReadingOrder(Point[][] bays)
    {
        if (bays.Length==0) yield break;
        var verticalTolerance = Median(bays.Select(bay=>Math.Min(Distance(bay[0],bay[1]),Distance(bay[0],bay[3]))))*.28;
        var sorted = bays.Select(bay=>(Bay:bay,Center:(bay[0]+bay[2])*.5)).OrderBy(item=>item.Center.Y).ToArray();
        for (var first = 0; first < sorted.Length;)
        {
            var end = first+1;
            while (end<sorted.Length && sorted[end].Center.Y-sorted[first].Center.Y<=verticalTolerance) end++;
            foreach (var item in sorted[first..end].OrderBy(item=>item.Center.X)) yield return item.Bay;
            first=end;
        }
    }

    private static bool BelongsToSameRow(Point[] first, Point[] second)
    {
        var across = first[1]-first[0];
        var along = first[3]-first[0];
        var width = Distance(first[0],first[1]);
        var depth = Distance(first[0],first[3]);
        var otherWidth = Distance(second[0],second[1]);
        var otherDepth = Distance(second[0],second[3]);
        if (Math.Abs(width-otherWidth) > width*.23 || Math.Abs(depth-otherDepth) > depth*.23) return false;
        if (Math.Abs(Cross(Unit(along),Unit(second[3]-second[0]))) > .18) return false;

        var separation = (second[0]+second[2]-first[0]-first[2])*.5;
        var lateral = Math.Abs(Dot(separation,Unit(across)));
        var longitudinal = Math.Abs(Dot(separation,Unit(along)));
        return lateral >= width*.72 && lateral <= width*1.55 && longitudinal <= depth*.25
            || lateral <= width*.30 && longitudinal >= depth*.70 && longitudinal <= depth*1.30;
    }

    private static IEnumerable<Line> BayBacks(Line[] markings,CancellationToken ct)
    {
        foreach (var line in markings)
        {
            ct.ThrowIfCancellationRequested();
            yield return line;
            var junctions = new List<double>();
            foreach (var other in markings)
            {
                if (Math.Abs(Dot(line.Direction,other.Direction)) > .18 || !Intersection(line,other,out var join)) continue;
                if (!OnSegment(join,line,4) || !OnSegment(join,other,4)) continue;
                junctions.Add(Math.Clamp(Dot(join-line.A,line.Direction),0,line.Length));
            }
            junctions.Sort();
            for (var i = 1; i < junctions.Count; i++)
            {
                if (junctions[i]-junctions[i-1] < 12) continue;
                yield return new(line.A+line.Direction*junctions[i-1],line.A+line.Direction*junctions[i],line.Strength);
            }
        }
    }

    private static int FollowMarking(Point origin,Point direction,int limit,byte[] ink,int width,int height)
    {
        var last = 0;
        for (var distance = 1; distance <= limit; distance++)
        {
            if (InkAt(origin+direction*distance,ink,width,height,1)) last=distance;
            else if (distance-last > 5) break;
        }
        return last;
    }

    private static bool HasInteriorInk(Point a,Point b,Point inward,int depth,byte[] ink,int width,int height)
    {
        var hits = 0;
        for (var x = 1; x <= 3; x++)
        for (var y = 1; y <= 3; y++)
            if (InkAt(a+(b-a)*(x/4d)+inward*(depth*y/4d),ink,width,height,0)) hits++;
        return hits > 2;
    }

    private static EndCap? FindEndCap(Line stem,byte[] ink,int width,int height)
    {
        var direction = stem.Direction;
        var perpendicular = new Point(-direction.Y,direction.X);
        var armLimit = Math.Clamp((int)Math.Round(Math.Min(width,height)*.032),8,16);
        EndCap? best = null;
        var bestWidth = 0;
        for (var offset = -3; offset <= 3; offset++)
        {
            var center = stem.B+direction*offset;
            var left = Span(center,perpendicular*-1);
            var right = Span(center,perpendicular);
            var shorter = Math.Min(left,right);
            if (shorter < 3 || Math.Max(left,right) >= armLimit || Math.Max(left,right) > shorter*2+1) continue;
            var inside = center-direction*5;
            if (left+right < Span(inside,perpendicular*-1)+Span(inside,perpendicular)+3) continue;
            if (StrokeCoverage(center+direction*3,center+direction*9,ink,width,height,radius:0) > .45) continue;
            if (left+right <= bestWidth) continue;
            var arm = (left+right)/2d;
            best = new(center,center-perpendicular*arm,center+perpendicular*arm);
            bestWidth = left+right;
        }
        return best;

        int Span(Point point,Point arm)
        {
            var length = 0;
            while (length < armLimit && InkAt(point+arm*(length+1),ink,width,height,0)) length++;
            return length;
        }
    }

    private static bool InkAt(Point point,byte[] ink,int width,int height,int radius)
    {
        var x = (int)Math.Round(point.X);
        var y = (int)Math.Round(point.Y);
        for (var row = Math.Max(0,y-radius); row <= Math.Min(height-1,y+radius); row++)
        for (var column = Math.Max(0,x-radius); column <= Math.Min(width-1,x+radius); column++)
            if (ink[row*width+column]!=0) return true;
        return false;
    }
}
