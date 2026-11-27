using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

/// <summary>Recognizes facing angled rows only when the source supports their repeated bay boundaries.</summary>
internal sealed class ParkingAngledRowRecognition(byte[] ink, int width, int height, ParkingDrawing canvas)
{
    private readonly record struct Stroke(int Left, int Right, int Top, int Bottom);
    private readonly record struct Rail(int Left, int Right, int Top, int Bottom);
    private sealed record Row(ParkingPoint[][] Bays, int Handedness);

    public ParkingShape[]? Recognize(CancellationToken ct)
    {
        var strokes = HorizontalStrokes(ct);
        var rails = new List<Rail>();
        for (var i = 0; i < strokes.Count; i++)
        for (var j = i + 1; j < strokes.Count; j++)
        {
            var a = strokes[i];
            var b = strokes[j];
            var gap = b.Top - a.Bottom;
            if (gap > height * .04) break;
            if (gap < 3 || Math.Abs(a.Left - b.Left) > width * .035 || Math.Abs(a.Right - b.Right) > width * .035)
                continue;
            rails.Add(new(Math.Max(a.Left, b.Left), Math.Min(a.Right, b.Right), a.Top, b.Bottom));
        }

        foreach (var top in rails)
        foreach (var bottom in rails)
        {
            ct.ThrowIfCancellationRequested();
            if (bottom.Top - top.Bottom < height * .28 || Math.Abs(top.Left - bottom.Left) > width * .04 || Math.Abs(top.Right - bottom.Right) > width * .04)
                continue;

            var inset = Math.Max(3, (int)Math.Round((bottom.Top - top.Bottom) * .01));
            var upper = Apexes(top, top.Bottom + inset);
            var lower = Apexes(bottom, bottom.Top - inset);
            if (!Regular(upper, out var upperPitch) || !Regular(lower, out var lowerPitch) ||
                Math.Abs(upperPitch - lowerPitch) > .13 * Math.Min(upperPitch, lowerPitch))
                continue;

            var north = Fit(upper, top.Bottom + 1, 1, upperPitch, ct);
            var south = Fit(lower, bottom.Top - 1, -1, lowerPitch, ct);
            if (north is null || south is null || north.Handedness != south.Handedness)
                continue;

            var laneLeft = Math.Max(top.Left, bottom.Left);
            var laneRight = Math.Min(top.Right, bottom.Right);
            var laneTop = north.Bays.SelectMany(bay => bay).Max(point => point.Y);
            var laneBottom = south.Bays.SelectMany(bay => bay).Min(point => point.Y);
            if (laneBottom - laneTop < (laneRight - laneLeft) * .08 || !ClearLane(laneLeft, laneTop, laneRight, laneBottom))
                continue;

            var shapes = new List<ParkingShape>
            {
                Block("WALL", top.Left, top.Top, top.Right, top.Bottom + 1),
                Block("WALL", bottom.Left, bottom.Top, bottom.Right, bottom.Bottom + 1),
                Block("LANE", laneLeft, laneTop, laneRight, laneBottom)
            };
            var number = 1;
            foreach (var bay in north.Bays.Concat(south.Bays))
            {
                var shape = new ParkingShape(Guid.NewGuid(), "SPACE", $"P-{number++:000}", 0, 0, 0, 0, NeedsReview: true);
                shapes.Add(ParkingVisionGeometry.FromCorners(shape, bay.Select(World).ToArray(), canvas));
            }
            try
            {
                return ParkingDrawingRules.ValidateSpaceSeparation(canvas with { Shapes = shapes.ToArray() }).Shapes;
            }
            catch (ParkingFailure)
            {
                // Another pair of reference rails may yield a supported, non-overlapping draft.
            }
        }
        return null;
    }

    private List<Stroke> HorizontalStrokes(CancellationToken ct)
    {
        var strokes = new List<Stroke>();
        for (var y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            var longest = Runs(y, 0, width - 1, 1).DefaultIfEmpty().MaxBy(run => run.Right - run.Left);
            if (longest.Right - longest.Left < width * .46) continue;
            var row = new Stroke(longest.Left, longest.Right, y, y);
            if (strokes.Count != 0 && strokes[^1] is var prior && y == prior.Bottom + 1 &&
                Math.Abs(prior.Left - row.Left) < width * .03 && Math.Abs(prior.Right - row.Right) < width * .03)
                strokes[^1] = new(Math.Min(prior.Left, row.Left), Math.Max(prior.Right, row.Right), prior.Top, y);
            else
                strokes.Add(row);
        }
        return strokes;
    }

    private double[] Apexes(Rail rail, int y)
    {
        var span = rail.Right - rail.Left;
        return Runs(y, rail.Left + 2, rail.Right - 2, 2)
            .Where(run => run.Right - run.Left + 1 >= Math.Max(5, span * .014) && run.Right - run.Left < span * .09)
            .Select(run => (run.Left + run.Right) * .5).ToArray();
    }

    private IEnumerable<(int Left, int Right)> Runs(int y, int left, int right, int bridge)
    {
        var start = -1;
        var end = -1;
        for (var x = left; x <= right; x++)
        {
            if (ink[y * width + x] == 0) continue;
            if (start >= 0 && x - end > bridge + 1)
            {
                yield return (start, end);
                start = -1;
            }
            if (start < 0) start = x;
            end = x;
        }
        if (start >= 0) yield return (start, end);
    }

    private static bool Regular(double[] seeds, out double pitch)
    {
        pitch = 0;
        if (seeds.Length is < 3 or > 14) return false;
        pitch = (seeds[^1] - seeds[0]) / (seeds.Length - 1);
        if (pitch < 15) return false;
        for (var i = 1; i < seeds.Length; i++)
            if (Math.Abs(seeds[i] - seeds[i - 1] - pitch) > pitch * .10) return false;
        return true;
    }

    private Row? Fit(double[] seeds, int y, int inward, double pitch, CancellationToken ct)
    {
        Row? best = null;
        var bestScore = 0d;
        var minSpacing = seeds.Zip(seeds.Skip(1), (before, after) => after - before).Min();
        foreach (var horizontal in new[] { 1, -1 })
        for (var degrees = 32; degrees <= 58; degrees++)
        {
            ct.ThrowIfCancellationRequested();
            var radians = degrees * Math.PI / 180;
            var sin = Math.Sin(radians);
            var cos = Math.Cos(radians);
            var maxWidth = minSpacing * sin - .3;
            for (var depth = 1.18; depth <= 1.68; depth += .035)
            for (var fraction = .84; fraction <= 1.005; fraction += .04)
            {
                var length = pitch * depth;
                var breadth = maxWidth * fraction;
                var bays = new ParkingPoint[seeds.Length][];
                var total = 0d;
                var weakest = 1d;
                for (var index = 0; index < seeds.Length; index++)
                {
                    var x = seeds[index];
                    var dx = length * cos * horizontal;
                    var dy = length * sin * inward;
                    var sx = breadth * sin * horizontal;
                    var sy = breadth * cos * inward;
                    var bay = new ParkingPoint[] { new(x, y), new(x + dx, y + dy), new(x + dx - sx, y + dy + sy), new(x - sx, y + sy) };
                    if (bay.Any(p => p.X < 1 || p.X > width - 2 || p.Y < 1 || p.Y > height - 2)) { weakest = 0; break; }
                    bays[index] = bay;
                    for (var edge = 0; edge < 4; edge++)
                    {
                        var support = EdgeSupport(bay[edge], bay[(edge + 1) % 4]);
                        total += support;
                        weakest = Math.Min(weakest, support);
                    }
                }
                var average = total / (seeds.Length * 4);
                var score = average + .12 * weakest + .11 * (length + breadth) / pitch;
                if (weakest < .64 || average < .85 || score <= bestScore) continue;
                best = new(bays, horizontal);
                bestScore = score;
            }
        }
        return best;
    }

    private double EdgeSupport(ParkingPoint a, ParkingPoint b)
    {
        const int samples = 24;
        var hits = 0d;
        for (var i = 1; i <= samples; i++)
        {
            var x = (int)Math.Round(a.X + (b.X - a.X) * i / (samples + 1));
            var y = (int)Math.Round(a.Y + (b.Y - a.Y) * i / (samples + 1));
            if (ink[y * width + x] != 0) { hits++; continue; }
            for (var dy = -1; dy <= 1; dy++)
            {
                var offset = (y + dy) * width + x;
                if ((ink[offset - 1] | ink[offset] | ink[offset + 1]) == 0) continue;
                hits += .6;
                break;
            }
        }
        return hits / samples;
    }

    private bool ClearLane(double left, double top, double right, double bottom)
    {
        var hits = 0;
        const int samples = 20;
        for (var y = 1; y < samples; y++)
        for (var x = 1; x < samples; x++)
        {
            var px = (int)(left + (right - left) * x / samples);
            var py = (int)(top + (bottom - top) * y / samples);
            hits += ink[py * width + px];
        }
        return hits < (samples - 1) * (samples - 1) * .12;
    }

    private ParkingPoint World(ParkingPoint point) => new(point.X * canvas.Width / width, point.Y * canvas.Height / height);

    private ParkingShape Block(string kind, double left, double top, double right, double bottom)
    {
        var origin = World(new(left, top));
        var end = World(new(right, bottom));
        return new(Guid.NewGuid(), kind, "", origin.X, origin.Y, end.X - origin.X, end.Y - origin.Y);
    }
}
