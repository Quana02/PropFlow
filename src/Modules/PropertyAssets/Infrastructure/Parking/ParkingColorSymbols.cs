using PropFlow.Modules.PropertyAssets.Contracts;
using SkiaSharp;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

/// <summary>Finds painted bays, planted islands and zebra markings on color site plans.</summary>
internal static partial class ParkingColorSymbols
{
    internal static (ParkingShape[] Shapes, byte[]? CadInk) Read(byte[] preview, ParkingDrawing drawing, int width, int height, CancellationToken ct)
    {
        using var source = SKBitmap.Decode(preview);
        using var image = source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        if (image is null) return ([], null);

        var pixels = image.Pixels;
        var green = new byte[pixels.Length];
        var white = new byte[pixels.Length];
        var red = new byte[pixels.Length];
        var road = new bool[pixels.Length];
        var parkingSurface = new bool[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            if ((i & 65535) == 0) ct.ThrowIfCancellationRequested();
            var p = pixels[i];
            if (p.Alpha < 200) continue;
            var min = Math.Min(p.Red, Math.Min(p.Green, p.Blue));
            var max = Math.Max(p.Red, Math.Max(p.Green, p.Blue));
            green[i] = (byte)(p.Green >= 60 && p.Green - p.Red >= 13 && p.Green - p.Blue >= 18 ? 1 : 0);
            white[i] = (byte)(min >= 185 && max - min <= 32 ? 1 : 0);
            red[i] = (byte)(p.Red >= 135 && p.Red - p.Green >= 58 && p.Red - p.Blue >= 53 ? 1 : 0);
            road[i] = min >= 20 && max <= 166 && max - min <= 34;
            parkingSurface[i] = road[i] || p.Red < 95 && p.Blue >= p.Red + 65 && p.Blue >= p.Green + 27;
        }

        if (green.Count(p => p != 0) < pixels.Length * .008)
        {
            // A dark CAD sheet uses its white structure strokes as ink and excludes colored vehicle
            // drawings and most dimension text. A monochrome print keeps the original dark ink.
            if (pixels.Count(p => Math.Max(p.Red, Math.Max(p.Green, p.Blue)) < 95) < pixels.Length * .72)
                return ([], null);
            for (var i = 0; i < pixels.Length; i++)
            {
                if ((i & 65535) == 0) ct.ThrowIfCancellationRequested();
                var p = pixels[i];
                var min = Math.Min(p.Red, Math.Min(p.Green, p.Blue));
                white[i] = (byte)(min >= 112 && Math.Max(p.Red, Math.Max(p.Green, p.Blue)) - min <= 37 ? 1 : 0);
            }
            var cadShapes = CadObstacles(red, road, width, height, drawing, ct).ToList();
            var cadCells = AddClosedBays([], white, parkingSurface, width, height, 2, 2, width - 3, height - 3, ct);
            for (var i = 0; i < cadCells.Length; i++)
            {
                var cell = cadCells[i];
                cadShapes.Add(Frame("SPACE", cell.Left, cell.Top, cell.Right, cell.Bottom) with
                {
                    Label = $"P-{i + 1:000}", NeedsReview = true
                });
            }
            return (cadShapes.ToArray(), white);
        }
        var planted = Components((byte[])green.Clone(), width, height, ct).ToArray();
        var mainSite = planted.Where(c => c.Area >= pixels.Length * .008).ToArray();
        if (mainSite.Length == 0)
            mainSite = planted.Where(c => c.Area >= pixels.Length * .001 && c.Width >= 10 && c.Height >= 8 && c.Fill >= .64).ToArray();
        if (mainSite.Length == 0) return ([], null);

        // Even a narrow horseshoe of lawn can delimit the entire site. Use its extent here
        // without converting its full bounding rectangle into a planted obstacle.
        var margin = Math.Min(width, height) * .14;
        var siteLeft = mainSite.Min(c => c.Left);
        var siteRight = mainSite.Max(c => c.Right);
        var siteTop = mainSite.Min(c => c.Top);
        var siteBottom = mainSite.Max(c => c.Bottom);
        var left = siteLeft - margin;
        var right = siteRight + margin;
        var top = siteTop - margin;
        var bottom = siteBottom + margin;
        var islands = planted
            .Where(c => c.Area >= pixels.Length * .001 && c.Area < pixels.Length * .25 &&
                c.Width >= 10 && c.Height >= 8 && c.Fill >= .64 &&
                c.Cx >= siteLeft && c.Cx <= siteRight && c.Cy >= siteTop && c.Cy <= siteBottom)
            .OrderByDescending(c => c.Area).Take(32).ToArray();

        var bays = ReadBays(white, parkingSurface, width, height, left, top, right, bottom, ct);
        bays = AddClosedBays(bays, white, parkingSurface, width, height, siteLeft, siteTop, siteRight, siteBottom, ct);
        bays = AddFineBays(source, bays, width, height, siteLeft, siteTop, siteRight, siteBottom, ct);
        var markings = Components((byte[])white.Clone(), width, height, ct, minimumArea: 3).Where(InSite).ToArray();
        var stripes = markings.Where(c =>
            Math.Min(c.Width, c.Height) >= 2 && Math.Max(c.Width, c.Height) >= 6 &&
            Math.Max(c.Width, c.Height) <= Math.Min(width, height) * .18 && c.Aspect is >= 2 and <= 12 &&
            c.Fill >= .72 && InSite(c))
            .OrderBy(c => c.Cy).ThenBy(c => c.Cx).Take(1200).ToArray();

        var shapes = islands.Select(c => Frame("GREEN_ISLAND", c.Left, c.Top, c.Right + 1, c.Bottom + 1)).ToList();
        foreach (var marking in markings)
        {
            ct.ThrowIfCancellationRequested();
            if (TrafficDirection(marking, white, road, width, height) is not { } angle) continue;
            shapes.Add(Orient(Frame("TRAFFIC_ARROW", marking.Left, marking.Top, marking.Right + 1, marking.Bottom + 1), angle));
        }
        var numbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < bays.Length; i++)
        {
            var bay = bays[i];
            var frame = Frame("SPACE", bay.Left, bay.Top, bay.Right, bay.Bottom);
            var label = ParkingPlanText.ReadBay(source, frame, drawing);
            if (label is null || !numbers.Add(label)) label = $"P-{i + 1:000}";
            shapes.Add(frame with { Label = label, NeedsReview = true });
        }
        var stops = bays.Select(bay => markings.FirstOrDefault(mark => IsWheelStop(mark, bay))).Where(mark => mark.Area != 0).ToArray();
        if (stops.Length >= 3)
            shapes.AddRange(stops.Select(mark => Orient(Frame("WHEEL_STOP", mark.Left, mark.Top, mark.Right + 1, mark.Bottom + 1),
                mark.Width > mark.Height ? 0 : 90)));
        var used = new HashSet<Component>();
        foreach (var seed in stripes)
        {
            ct.ThrowIfCancellationRequested();
            if (used.Contains(seed)) continue;
            var horizontal = seed.Width > seed.Height;
            int Across(Component c) => horizontal ? c.Cy : c.Cx;
            int Along(Component c) => horizontal ? c.Cx : c.Cy;
            var thin = Math.Min(seed.Width, seed.Height);
            var length = Math.Max(seed.Width, seed.Height);
            var row = stripes.Where(c => !used.Contains(c) && (c.Width > c.Height) == horizontal &&
                Math.Abs(Along(c) - Along(seed)) <= Math.Max(2, length * .14) &&
                Math.Abs(c.Width - seed.Width) <= Math.Max(1, seed.Width * .25) &&
                Math.Abs(c.Height - seed.Height) <= Math.Max(1, seed.Height * .25))
                .OrderBy(Across).ToArray();
            var group = new List<Component> { seed };
            double pitch = 0;
            foreach (var next in row.SkipWhile(c => Across(c) <= Across(seed)))
            {
                var distance = Across(next) - Across(group[^1]);
                if (distance < thin + 1) continue;
                if (distance > thin * 4.5 || pitch > 0 && Math.Abs(distance - pitch) > Math.Max(1, pitch * .25)) break;
                var gapCenter = (Across(next) + Across(group[^1])) / 2;
                var crossCenter = (Along(next) + Along(group[^1])) / 2;
                var pavement = 0;
                for (var offset = -2; offset <= 2; offset++)
                {
                    var along = Math.Clamp(crossCenter + offset * length / 6, 0, (horizontal ? width : height) - 1);
                    if (road[horizontal ? gapCenter * width + along : along * width + gapCenter]) pavement++;
                }
                if (pavement < 4) break;
                pitch = distance;
                group.Add(next);
            }
            if (group.Count < 4) continue;
            used.UnionWith(group);
            var frame = Frame("CROSSWALK", group.Min(c => c.Left), group.Min(c => c.Top), group.Max(c => c.Right) + 1, group.Max(c => c.Bottom) + 1);
            shapes.Add(Orient(frame, horizontal ? 90 : 0));
        }
        var site = new Component(siteLeft, siteTop, siteRight, siteBottom, 0);
        var roundaboutCenters = Components(ConnectNearby(green, width, height, ct), width, height, ct);
        shapes.AddRange(ReadSiteDetails(source, pixels, red, white, road, site, drawing, width, height, roundaboutCenters, markings, used, bays, ct));
        return (shapes.ToArray(), null);

        bool InSite(Component c) => c.Cx >= left && c.Cx <= right && c.Cy >= top && c.Cy <= bottom;

        ParkingShape Frame(string kind, int x0, int y0, int x1, int y1) => RasterFrame(drawing, width, height, kind, x0, y0, x1, y1);
    }

    private static bool IsWheelStop(Component mark, (int Left, int Top, int Right, int Bottom) bay)
    {
        if (mark.Left <= bay.Left + 1 || mark.Right >= bay.Right - 1 || mark.Top <= bay.Top + 1 || mark.Bottom >= bay.Bottom - 1 ||
            mark.Aspect < 3 || mark.Fill < .73) return false;
        var vertical = bay.Bottom - bay.Top > bay.Right - bay.Left;
        var span = vertical ? bay.Right - bay.Left : bay.Bottom - bay.Top;
        var depth = vertical ? bay.Bottom - bay.Top : bay.Right - bay.Left;
        var size = vertical ? mark.Width : mark.Height;
        var thickness = vertical ? mark.Height : mark.Width;
        var distance = vertical ? Math.Min(mark.Cy - bay.Top, bay.Bottom - mark.Cy) : Math.Min(mark.Cx - bay.Left, bay.Right - mark.Cx);
        var lateralOffset = vertical ? Math.Abs(2 * mark.Cx - bay.Left - bay.Right) : Math.Abs(2 * mark.Cy - bay.Top - bay.Bottom);
        return size >= span * .40 && size <= span * .78 && thickness >= 2 && thickness <= depth * .08 &&
            distance <= depth * .15 && lateralOffset <= span * .20;
    }

    private static ParkingShape Orient(ParkingShape frame, int angle) => angle is 0 or 180 ? frame with { Rotation = angle } :
        frame with { X = frame.X + (frame.Width - frame.Height) / 2, Y = frame.Y + (frame.Height - frame.Width) / 2,
            Width = frame.Height, Height = frame.Width, Rotation = angle };

    private static int? TrafficDirection(Component mark, byte[] paint, bool[] road, int width, int height)
    {
        var horizontal = mark.Width > mark.Height;
        var length = Math.Max(mark.Width, mark.Height);
        var breadth = Math.Min(mark.Width, mark.Height);
        if (breadth < 6 || length < 16 || length > Math.Min(width, height) * .17 ||
            mark.Aspect is < 1.6 or > 4.4 || mark.Fill is < .30 or > .74 ||
            mark.Left < 3 || mark.Top < 3 || mark.Right >= width - 3 || mark.Bottom >= height - 3) return null;

        if (!road[mark.Cy * width + mark.Left - 3] || !road[mark.Cy * width + mark.Right + 3] ||
            !road[(mark.Top - 3) * width + mark.Cx] || !road[(mark.Bottom + 3) * width + mark.Cx]) return null;
        var profile = new int[length];
        for (var at = 0; at < length; at++)
        for (var across = 0; across < breadth; across++)
            profile[at] += paint[(mark.Top + (horizontal ? across : at)) * width + mark.Left + (horizontal ? at : across)];

        if (Matches(profile)) return horizontal ? 0 : 90;
        Array.Reverse(profile);
        return Matches(profile) ? horizontal ? 180 : 270 : null;

        bool Matches(int[] profile)
        {
            var peak = Array.IndexOf(profile, profile.Max());
            var tail = profile.Skip(length / 8).Take(length / 4).Average();
            var tip = profile.Skip(length * 7 / 8).Average();
            return peak >= length * .40 && peak <= length * .82 && profile[peak] >= breadth * .78 &&
                tail >= breadth * .15 && tail <= breadth * .43 && tip <= breadth * .38;
        }
    }

    private static (int Left, int Top, int Right, int Bottom)[] ReadBays(byte[] paint, bool[] road, int width, int height,
        double left, double top, double right, double bottom, CancellationToken ct)
    {
        var bays = new List<(int Left, int Top, int Right, int Bottom)>();
        foreach (var swap in new[] { false, true })
        {
            var across = swap ? height : width;
            var along = swap ? width : height;
            var firstU = Math.Max(4, (int)(swap ? top : left));
            var lastU = Math.Min(across - 4, (int)(swap ? bottom : right));
            var firstV = Math.Max(2, (int)(swap ? left : top));
            var lastV = Math.Min(along - 2, (int)(swap ? right : bottom));
            var minDepth = Math.Max(15, Math.Min(width, height) * .048);
            var maxDepth = Math.Min(width, height) * .28;
            var lines = new List<(int U, int Start, int End)>();
            int Index(int u, int v) => swap ? u * width + v : v * width + u;
            for (var u = firstU; u < lastU; u++)
            {
                ct.ThrowIfCancellationRequested();
                var start = firstV;
                var last = firstV - 3;
                for (var v = firstV; v <= lastV + 3; v++)
                {
                    if (v < lastV && paint[Index(u, v)] != 0)
                    {
                        if (v - last > 3) start = v;
                        last = v;
                        continue;
                    }
                    if (v - last != 3 || last - start < minDepth || last - start > maxDepth) continue;
                    var asphalt = 0;
                    for (var s = start + 2; s < last - 1; s += 2)
                        if (road[Index(u - 3, s)] || road[Index(u + 3, s)]) asphalt++;
                    if (asphalt < (last - start) * .33) continue;
                    var duplicate = false;
                    for (var i = lines.Count - 1; i >= 0 && u - lines[i].U <= 3; i--)
                        if (Math.Abs(start - lines[i].Start) <= 4 && Math.Abs(last - lines[i].End) <= 4) { duplicate = true; break; }
                    if (!duplicate) lines.Add((u, start, last));
                }
            }

            var pairs = new List<(int First, int Second, int Start, int End)>();
            for (var i = 0; i < lines.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var a = lines[i];
                var depth = a.End - a.Start;
                var tolerance = Math.Max(4, depth * .13);
                for (var j = i + 1; j < lines.Count; j++)
                {
                    var b = lines[j];
                    var gap = b.U - a.U;
                    if (gap > depth * .80) break;
                    if (gap < Math.Max(7, depth * .125) || Math.Abs(a.Start - b.Start) > tolerance || Math.Abs(a.End - b.End) > tolerance) continue;
                    var start = (a.Start + b.Start) / 2;
                    var end = (a.End + b.End) / 2;
                    if (Math.Max(PaintedBack(start, a.U, b.U), PaintedBack(end, a.U, b.U)) < .68) continue;
                    var cuts = new List<int> { start };
                    var minimumBayDepth = Math.Max(13, (int)(gap / .82));
                    for (var cut = start + minimumBayDepth; cut <= end - minimumBayDepth; cut++)
                    {
                        if (PaintedBack(cut, a.U, b.U, spread: 0) < .78) continue;
                        var first = cut;
                        while (cut < end - minimumBayDepth && PaintedBack(cut + 1, a.U, b.U, spread: 0) >= .78) cut++;
                        cuts.Add((first + cut) / 2);
                        cut += minimumBayDepth - 1;
                    }
                    cuts.Add(end);
                    var before = pairs.Count;
                    for (var part = 1; part < cuts.Count; part++)
                    {
                        var v0 = cuts[part - 1];
                        var v1 = cuts[part];
                        var bayDepth = v1 - v0;
                        if (gap / (double)bayDepth is < .22 or > .82) continue;
                        var paved = 0;
                        for (var sample = 1; sample <= 3; sample++)
                        {
                            var v = v0 + bayDepth * sample / 4;
                            if (road[Index(a.U + 2, v)] || road[Index(b.U - 2, v)]) paved++;
                        }
                        if (paved >= 2) pairs.Add((i, j, v0, v1));
                    }
                    if (pairs.Count == before) continue;
                    break;
                }
            }
            foreach (var pair in pairs)
            {
                var a = lines[pair.First].U;
                var b = lines[pair.Second].U;
                var neighbour = pairs.Any(other => (other.Second == pair.First || other.First == pair.Second) &&
                    Math.Abs(lines[other.Second].U - lines[other.First].U - (b - a)) <= Math.Max(3, (b - a) * .25) &&
                    Math.Min(other.End, pair.End) - Math.Max(other.Start, pair.Start) >= (pair.End - pair.Start) * .62);
                if (!neighbour) continue;
                var rect = swap ? (pair.Start, a, pair.End, b) : (a, pair.Start, b, pair.End);
                var (x0, y0, x1, y1) = rect;
                if (bays.Any(old => x0 < old.Right && old.Left < x1 && y0 < old.Bottom && old.Top < y1)) continue;
                bays.Add(rect);
            }

            double PaintedBack(int v, int from, int to, int spread = 2)
            {
                var covered = 0;
                for (var u = from + 1; u < to; u++)
                {
                    for (var dv = -spread; dv <= spread; dv++)
                    {
                        if (paint[Index(u, v + dv)] == 0) continue;
                        covered++;
                        break;
                    }
                }
                return (double)covered / (to - from - 1);
            }
        }
        return bays.OrderBy(bay => bay.Top).ThenBy(bay => bay.Left).ToArray();
    }

    private readonly record struct Component(int Left, int Top, int Right, int Bottom, int Area)
    {
        public int Cx => (Left + Right) / 2;
        public int Cy => (Top + Bottom) / 2;
        public int Width => Right - Left + 1;
        public int Height => Bottom - Top + 1;
        public double Fill => (double)Area / (Width * Height);
        public double Aspect => (double)Math.Max(Width, Height) / Math.Min(Width, Height);
    }

    private static IEnumerable<Component> Components(byte[] mask, int width, int height, CancellationToken ct, int minimumArea = 6)
    {
        var queue = new int[mask.Length];
        for (var i = 0; i < mask.Length; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (mask[i] == 0) continue;
            var left = i % width;
            var right = left;
            var top = i / width;
            var bottom = top;
            var tail = 0;
            Add(i);
            for (var head = 0; head < tail; head++)
            {
                if ((head & 4095) == 0) ct.ThrowIfCancellationRequested();
                var index = queue[head];
                var x = index % width;
                var y = index / width;
                left = Math.Min(x, left);
                right = Math.Max(x, right);
                top = Math.Min(y, top);
                bottom = Math.Max(y, bottom);
                if (x > 0) Add(index - 1);
                if (x < width - 1) Add(index + 1);
                if (y > 0) Add(index - width);
                if (y < height - 1) Add(index + width);
            }
            if (tail >= minimumArea) yield return new(left, top, right, bottom, tail);

            void Add(int index)
            {
                if (mask[index] == 0) return;
                mask[index] = 0;
                queue[tail++] = index;
            }
        }
    }
}
