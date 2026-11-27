using System.Numerics;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal static partial class ParkingObjectReconstruction
{
    private static readonly int[] NeighbourX = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] NeighbourY = [-1, -1, 0, 1, 1, 1, 0, -1];

    private static List<Line> TraceOpenLinework(byte[] ink,int width,int height,CancellationToken ct)
    {
        var skeleton = ThinStrokes(ink,width,height,ct);
        var links = new byte[skeleton.Length];
        var seen = new byte[skeleton.Length];
        var steps = NeighbourX.Zip(NeighbourY,(x,y)=>x+y*width).ToArray();
        for (var y = 1; y < height - 1; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = 1; x < width - 1; x++)
            {
                var pixel = y*width+x;
                if (skeleton[pixel] == 0) continue;
                for (var direction = 0; direction < steps.Length; direction++)
                {
                    if (skeleton[pixel+steps[direction]] == 0) continue;
                    // Adjacent axial links already connect this diagonal pixel through the same junction.
                    if ((direction & 1) != 0 && (skeleton[pixel+steps[(direction+7)%8]] | skeleton[pixel+steps[(direction+1)%8]]) != 0) continue;
                    links[pixel] |= (byte)(1<<direction);
                }
            }
        }

        var lines = new List<Line>();
        Scan(closedLoops:false);
        Scan(closedLoops:true);
        return JoinCollinear(lines,ct);

        void Scan(bool closedLoops)
        {
            for (var pixel = 0; pixel < links.Length; pixel++)
            {
                if (pixel % width == 0) ct.ThrowIfCancellationRequested();
                if (links[pixel] == seen[pixel] || (!closedLoops && BitOperations.PopCount(links[pixel]) == 2)) continue;
                var unvisited = links[pixel] & ~seen[pixel];
                while (unvisited != 0)
                {
                    var chain = new List<Point> { At(pixel) };
                    var next = BitOperations.TrailingZeroCount((uint)unvisited);
                    var current = pixel;
                    do
                    {
                        var adjacent = current+steps[next];
                        seen[current] |= (byte)(1<<next);
                        seen[adjacent] |= (byte)(1<<((next+4)%8));
                        chain.Add(At(adjacent));
                        current = adjacent;
                        var remaining = links[current] & ~seen[current];
                        next = remaining == 0 || BitOperations.PopCount(links[current]) != 2 ? 8 : BitOperations.TrailingZeroCount((uint)remaining);
                    } while (next < 8);
                    if (chain.Count >= 6) SimplifyStroke(chain,lines);
                    unvisited = links[pixel] & ~seen[pixel];
                }
            }
        }

        Point At(int pixel) => new(pixel%width,pixel/width);
    }

    private static byte[] ThinStrokes(byte[] ink,int width,int height,CancellationToken ct)
    {
        var pixels = ink.ToArray();
        Array.Clear(pixels,0,width);
        Array.Clear(pixels,pixels.Length-width,width);
        for (var y = 0; y < height; y++) pixels[y*width] = pixels[(y+1)*width-1] = 0;

        var remove = new List<int>();
        for (var pass = 0; pass < 80; pass++)
        {
            var changed = false;
            for (var side = 0; side < 2; side++)
            {
                for (var y = 1; y < height-1; y++)
                {
                    ct.ThrowIfCancellationRequested();
                    for (var x = 1; x < width-1; x++)
                    {
                        var index = y*width+x;
                        if (pixels[index] == 0) continue;
                        var n = pixels[index-width]; var ne = pixels[index-width+1];
                        var e = pixels[index+1]; var se = pixels[index+width+1];
                        var s = pixels[index+width]; var sw = pixels[index+width-1];
                        var w = pixels[index-1]; var nw = pixels[index-width-1];
                        var count = n+ne+e+se+s+sw+w+nw;
                        if (count is <2 or >6) continue;
                        var transitions = (1-n)*ne+(1-ne)*e+(1-e)*se+(1-se)*s+(1-s)*sw+(1-sw)*w+(1-w)*nw+(1-nw)*n;
                        if (transitions != 1) continue;
                        // Alternate the sides removed during each thinning cycle so line junctions stay linked.
                        if (side == 0 ? n*e*s == 0 && e*s*w == 0 : n*e*w == 0 && n*s*w == 0) remove.Add(index);
                    }
                }
                foreach (var index in remove) pixels[index] = 0;
                changed |= remove.Count != 0;
                remove.Clear();
            }
            if (!changed) break;
        }
        return pixels;
    }

    private static void SimplifyStroke(List<Point> chain,List<Line> lines)
    {
        var ranges = new Stack<(int Start,int End)>();
        ranges.Push((0,chain.Count-1));
        while (ranges.TryPop(out var range))
        {
            var a = chain[range.Start];
            var b = chain[range.End];
            var split = -1;
            var deviation = 1.8;
            for (var i = range.Start+1; i < range.End; i++)
            {
                var distance = SegmentDistance(chain[i],a,b);
                if (distance <= deviation) continue;
                deviation = distance;
                split = i;
            }
            if (split >= 0)
            {
                ranges.Push((split,range.End));
                ranges.Push((range.Start,split));
            }
            else if (Distance(a,b) >= 5) lines.Add(new(a,b,1));
        }
    }

    private static List<Line> JoinCollinear(List<Line> segments,CancellationToken ct)
    {
        var joined = new List<Line>();
        foreach (var line in segments.OrderByDescending(segment=>segment.Length))
        {
            ct.ThrowIfCancellationRequested();
            var current = line;
            for (var i = joined.Count-1;i>=0;i--)
            {
                var old = joined[i];
                if (Math.Abs(Cross(old.Direction,current.Direction)) > .09 || LineDistance(current.A,old)>2.5 || LineDistance(current.B,old)>2.5) continue;
                var start = Dot(current.A-old.A,old.Direction);
                var end = Dot(current.B-old.A,old.Direction);
                if (Math.Min(start,end)>old.Length+5 || Math.Max(start,end)<-5) continue;
                current = new(old.A+old.Direction*Math.Min(0,Math.Min(start,end)),old.A+old.Direction*Math.Max(old.Length,Math.Max(start,end)),1);
                joined.RemoveAt(i);
            }
            joined.Add(current);
        }
        return joined.OrderByDescending(line=>line.Length).ToList();
    }
}
