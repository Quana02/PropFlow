namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal static partial class ParkingObjectReconstruction
{
    private sealed record DividerPair(Line Left, Line Right, Point[] Corners, double Gap, double Depth, double Support);

    private static IEnumerable<OpenBay> ReadParallelBayRows(Line[] markings,byte[] ink,byte[] strongInk,int width,int height,CancellationToken ct)
    {
        var imageSide = Math.Min(width,height);
        var dividers = SplitAtSharedBacks(markings,ct).Where(line=>line.Length>=Math.Max(26,imageSide*.09) && line.Length<=imageSide*.42)
            .Select(line=>(Math.Abs(line.Direction.Y)<.15 ? line.B.X<line.A.X : line.B.Y<line.A.Y)
                ? new Line(line.B,line.A,line.Strength) : line).ToArray();
        var pairs = new List<DividerPair>();
        foreach (var left in dividers)
        {
            ct.ThrowIfCancellationRequested();
            DividerPair? closest = null;
            foreach (var right in dividers)
            {
                if (ReferenceEquals(left,right) || Math.Abs(Cross(left.Direction,right.Direction))>.12 || Dot(left.Direction,right.Direction)<0) continue;
                var gap = Cross(left.Direction,right.Center-left.Center);
                if (gap<Math.Max(12,imageSide*.028) || gap>imageSide*.16 || (closest is not null && gap>=closest.Gap)) continue;
                var depth = Math.Min(left.Length,right.Length);
                if (gap/depth is <.25 or >.76 || Math.Abs(left.Length-right.Length)>depth*.25) continue;
                var stagger = Math.Max(6,gap*.80);
                if (Math.Abs(Dot(right.A-left.A,left.Direction))>stagger || Math.Abs(Dot(right.B-left.B,left.Direction))>stagger) continue;
                if (Math.Abs(LineDistance(right.A,left)-LineDistance(right.B,left))>gap*.30) continue;

                var support = Math.Min(StrokeCoverage(left.A,left.B,ink,width,height),StrokeCoverage(right.A,right.B,ink,width,height));
                if (support<.82 || !HasOpenMouth(left,right,ink,strongInk,width,height)) continue;
                var corners = AlignedDividerPair(left,right,depth);
                closest = new(left,right,corners,gap,depth,support);
            }
            if (closest is not null) pairs.Add(closest);
        }

        foreach (var pair in pairs)
        {
            var neighbour = pairs.Any(other=>!ReferenceEquals(pair,other) &&
                (ReferenceEquals(pair.Left,other.Right) || ReferenceEquals(pair.Right,other.Left)) &&
                Math.Abs(pair.Gap-other.Gap)<=pair.Gap*.24 && Math.Abs(pair.Depth-other.Depth)<=pair.Depth*.24);
            if (neighbour) yield return new(pair.Corners,1+pair.Support);
        }
    }

    private static IEnumerable<Line> SplitAtSharedBacks(Line[] markings,CancellationToken ct)
    {
        var cuts = new Dictionary<Line,List<double>>();
        foreach (var back in markings)
        {
            ct.ThrowIfCancellationRequested();
            var crossings = new List<(Line Divider,double At)>();
            foreach (var divider in markings)
            {
                if (Math.Abs(Dot(back.Direction,divider.Direction))>.13 || !Intersection(back,divider,out var join) || !OnSegment(join,back,3)) continue;
                var at = Dot(join-divider.A,divider.Direction);
                if (at<divider.Length*.22 || at>divider.Length*.78) continue;
                crossings.Add((divider,at));
            }
            if (crossings.Count<3) continue;
            foreach (var (divider,at) in crossings)
            {
                if (!cuts.TryGetValue(divider,out var positions)) cuts[divider]=positions=[];
                if (!positions.Any(previous=>Math.Abs(previous-at)<5)) positions.Add(at);
            }
        }
        foreach (var line in markings)
        {
            if (!cuts.TryGetValue(line,out var positions)) { yield return line;continue; }
            var start = 0d;
            foreach (var end in positions.Append(line.Length).Order())
            {
                yield return new(line.A+line.Direction*start,line.A+line.Direction*end,line.Strength);
                start=end;
            }
        }
    }

    private static Point[] AlignedDividerPair(Line left,Line right,double depth)
    {
        var along = Unit(left.Direction+right.Direction);
        if (Math.Abs(along.X)<.09) along=new(0,Math.Sign(along.Y));
        else if (Math.Abs(along.Y)<.09) along=new(Math.Sign(along.X),0);
        var across = new Point(-along.Y,along.X);
        var leftOffset = Dot(left.Center,across);
        var rightOffset = Dot(right.Center,across);
        var leftStart = Dot(left.A,along);
        var rightStart = Dot(right.A,along);
        var leftEnd = Dot(left.B,along);
        var rightEnd = Dot(right.B,along);
        if (Math.Abs(leftStart-rightStart)<depth*.12) leftStart=rightStart=(leftStart+rightStart)*.5;
        if (Math.Abs(leftEnd-rightEnd)<depth*.12) leftEnd=rightEnd=(leftEnd+rightEnd)*.5;
        return [along*leftStart+across*leftOffset,along*rightStart+across*rightOffset,along*rightEnd+across*rightOffset,along*leftEnd+across*leftOffset];
    }

    private static bool HasOpenMouth(Line left,Line right,byte[] ink,byte[] strongInk,int width,int height)
    {
        // The fourth side is a display boundary. It may only be inferred where there is an open end.
        // End caps and JPEG halos near the dividers do not close the middle of a bay.
        var hasDarkDividers = Math.Min(StrokeCoverage(left.A,left.B,strongInk,width,height),StrokeCoverage(right.A,right.B,strongInk,width,height))>.60;
        var evidence = hasDarkDividers ? strongInk : ink;
        if (Math.Min(MiddleCoverage(left.A,right.A),MiddleCoverage(left.B,right.B))>=.83) return false;
        // A continuous cross stroke through the middle is evidence of another bay boundary, not one long bay.
        foreach (var t in new[] { .35,.50,.65 })
            if (StrokeCoverage(left.A+(left.B-left.A)*t,right.A+(right.B-right.A)*t,ink,width,height,radius:0)>.64) return false;
        return true;

        double MiddleCoverage(Point first,Point second)
        {
            var start = first+(second-first)*.25;
            var end = first+(second-first)*.75;
            var shift = Unit(left.Direction+right.Direction);
            var coverage = 0d;
            for(var offset=-3;offset<=3;offset++)
                coverage=Math.Max(coverage,StrokeCoverage(start+shift*offset,end+shift*offset,evidence,width,height));
            return coverage;
        }
    }
}
