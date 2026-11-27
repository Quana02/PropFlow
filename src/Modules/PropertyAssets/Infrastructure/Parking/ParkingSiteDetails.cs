using PropFlow.Modules.PropertyAssets.Contracts;
using SkiaSharp;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal static partial class ParkingColorSymbols
{
    private static IEnumerable<ParkingShape> ReadSiteDetails(
        SKBitmap source, SKColor[] pixels, byte[] red, byte[] paint, bool[] road, Component site,
        ParkingDrawing drawing, int width, int height, IEnumerable<Component> islands, Component[] markings,
        HashSet<Component> crossingStripes, (int Left, int Top, int Right, int Bottom)[] bays, CancellationToken ct)
    {
        var redMarks = Components((byte[])red.Clone(), width, height, ct).Where(c =>
            c.Cx >= site.Left - 4 && c.Cx <= site.Right + 4 && c.Cy >= site.Top - 4 && c.Cy <= site.Bottom + 4).ToArray();
        foreach (var (left, top, right, bottom) in Perimeter(pixels, site, width, height, ct))
            yield return RasterStroke(drawing, width, height, "LINE", left, top, right, bottom);

        var dashes = markings.Where(mark => !crossingStripes.Contains(mark) && !bays.Any(bay =>
            mark.Cx >= bay.Left - 2 && mark.Cx <= bay.Right + 2 && mark.Cy >= bay.Top - 2 && mark.Cy <= bay.Bottom + 2)).ToArray();
        foreach (var run in PaintedRuns(dashes, road, width, height, ct))
            yield return RasterStroke(drawing, width, height, "DASHED_LINE", run.Left, run.Top, run.Right, run.Bottom);

        var barriers = BarrierArms(redMarks, paint, road, width, height, ct).ToArray();
        foreach (var arm in barriers)
            yield return RasterFrame(drawing, width, height, "BARRIER", arm.Left, arm.Top, arm.Right + 1, arm.Bottom + 1);

        foreach (var booth in GuardBooths(barriers, pixels, width, height, ct))
            yield return RasterFrame(drawing, width, height, "GUARD_BOOTH", booth.Left, booth.Top, booth.Right + 1, booth.Bottom + 1);
        foreach (var access in AccessDetails(source, pixels, site, drawing, width, height, barriers, ct))
            yield return access;
        foreach (var zone in RestrictedAreas(pixels, site, width, height, ct))
            yield return RasterFrame(drawing, width, height, "NO_PARKING", zone.Left, zone.Top, zone.Right + 1, zone.Bottom + 1);

        foreach (var sign in redMarks)
        {
            ct.ThrowIfCancellationRequested();
            if (!CircularSign(sign, red, paint, width, height)) continue;
            var pad = Math.Max(2, sign.Width / 5);
            var label = ParkingPlanText.ReadSignNumber(source, (sign.Left + pad) * source.Width / width,
                (sign.Top + pad) * source.Height / height, (sign.Right - pad + 1) * source.Width / width,
                (sign.Bottom - pad + 1) * source.Height / height);
            if (label is null && paint[sign.Cy * width + sign.Cx] != 0 && red[(sign.Top + pad) * width + sign.Cx] != 0)
                label = "Cấm vào";
            var size = Math.Max(sign.Width, sign.Height);
            var side = size * (double)drawing.Width / width;
            var x = sign.Cx * (double)drawing.Width / width - side / 2;
            var y = sign.Cy * (double)drawing.Height / height - side / 2;
            if (x >= 0 && y >= 0 && x + side <= drawing.Width && y + side <= drawing.Height)
                yield return new(Guid.NewGuid(), "TRAFFIC_SIGN", label ?? "", x, y, side, side);
        }

        var roundabouts = new List<ParkingShape>();
        foreach (var island in islands.OrderByDescending(c => c.Area))
        {
            ct.ThrowIfCancellationRequested();
            if (RoundaboutRadius(island, paint, road, width, height) is not { } radius) continue;
            var side = 2 * radius * (double)drawing.Width / width;
            var x = island.Cx * (double)drawing.Width / width - side / 2;
            var y = island.Cy * (double)drawing.Height / height - side / 2;
            if (x < 0 || y < 0 || x + side > drawing.Width || y + side > drawing.Height ||
                roundabouts.Any(ring => Math.Abs(ring.X + ring.Width / 2 - x - side / 2) < side * .25 &&
                    Math.Abs(ring.Y + ring.Height / 2 - y - side / 2) < side * .25)) continue;
            var shape = new ParkingShape(Guid.NewGuid(), "ROUNDABOUT", "", x, y, side, side);
            roundabouts.Add(shape);
            yield return shape;
        }
    }

    private static IEnumerable<Component> RestrictedAreas(SKColor[] pixels, Component site, int width, int height, CancellationToken ct)
    {
        var yellow = new byte[pixels.Length];
        for (var y = site.Top; y <= site.Bottom; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = site.Left; x <= site.Right; x++)
            {
                var p = pixels[y * width + x];
                yellow[y * width + x] = (byte)(p.Red >= 165 && p.Green >= 117 && p.Red - p.Green < 90 &&
                    p.Blue <= 130 && p.Green - p.Blue >= 55 ? 1 : 0);
            }
        }
        foreach (var connected in Components(ConnectNearby(yellow, width, height, ct, radius: 1), width, height, ct))
        {
            var zone = new Component(connected.Left + 1, connected.Top + 1, connected.Right - 1, connected.Bottom - 1, 0);
            var shortest = Math.Min(zone.Width, zone.Height);
            var longest = Math.Max(zone.Width, zone.Height);
            if (shortest < 5 || longest < 13 || longest > Math.Min(width, height) * .13 ||
                zone.Aspect > 4.8 || connected.Fill < .62) continue;
            var count = 0;
            for (var y = zone.Top; y <= zone.Bottom; y++)
            for (var x = zone.Left; x <= zone.Right; x++)
                count += yellow[y * width + x];
            var fill = count / (double)(zone.Width * zone.Height);
            if (fill is < .19 or > .70 || !RepeatedDiagonals(zone, yellow, width)) continue;
            yield return zone;
        }
    }

    private static bool RepeatedDiagonals(Component box, byte[] mask, int width)
    {
        var minimum = Math.Max(3, Math.Min(box.Width, box.Height) * .60);
        foreach (var slope in new[] { -1, 1 })
        {
            var previous = int.MinValue / 2;
            var runs = 0;
            for (var offset = -box.Width; offset <= box.Width + box.Height; offset++)
            {
                var run = 0;
                var longest = 0;
                for (var x = 0; x < box.Width; x++)
                {
                    var y = offset + slope * x;
                    if (y < 0 || y >= box.Height || mask[(box.Top + y) * width + box.Left + x] == 0)
                    {
                        run = 0;
                        continue;
                    }
                    longest = Math.Max(longest, ++run);
                }
                if (longest < minimum) continue;
                if (offset - previous >= 3) runs++;
                previous = offset;
            }
            if (runs >= 2) return true;
        }
        return false;
    }

    private static IEnumerable<ParkingShape> AccessDetails(SKBitmap source, SKColor[] pixels, Component site,
        ParkingDrawing drawing, int width, int height, Component[] barriers, CancellationToken ct)
    {
        var margin = Math.Min(width, height) * .15;
        var pigment = new byte[pixels.Length];
        var grey = new bool[pixels.Length];
        var exterior = new byte[pixels.Length];
        var foundNotes = new List<SKRectI>();
        for (var y = Math.Max(0, site.Top - (int)margin); y <= Math.Min(height - 1, site.Bottom + (int)margin); y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = Math.Max(0, site.Left - (int)margin); x <= Math.Min(width - 1, site.Right + (int)margin); x++)
            {
                var p = pixels[y * width + x];
                var min = Math.Min(p.Red, Math.Min(p.Green, p.Blue));
                var max = Math.Max(p.Red, Math.Max(p.Green, p.Blue));
                grey[y * width + x] = min >= 164 && max <= 247 && max - min < 25;
                if (x >= site.Left && x <= site.Right && y >= site.Top && y <= site.Bottom) continue;
                if (grey[y * width + x]) exterior[y * width + x] = 1;
                pigment[y * width + x] = (byte)(p.Red > p.Green + 55 && p.Red > p.Blue + 55 ||
                    p.Green > p.Red + 22 && p.Green > p.Blue + 47 ? 1 : 0);
            }
        }
        foreach (var mark in Components((byte[])pigment.Clone(), width, height, ct))
        {
            if (TrafficDirection(mark, pigment, grey, width, height) is not { } rotation) continue;
            var cx = source.Width * mark.Cx / width;
            var cy = source.Height * mark.Cy / height;
            var span = source.Width * mark.Width / width;
            var thickness = source.Height * mark.Height / height;
            var readingBox = Clip(source, cx - span * 3 / 4, cy - thickness * 2, cx + span * 3 / 4, cy + thickness * 2);
            if (ParkingPlanText.ReadAccessNote(source, readingBox, dark: true) is not { } note) continue;
            var entry = note.Label.Contains("VÀO", StringComparison.Ordinal);
            yield return Orient(RasterFrame(drawing, width, height, entry ? "ENTRANCE" : "EXIT",
                mark.Left, mark.Top, mark.Right + 1, mark.Bottom + 1), entry ? rotation : (rotation + 180) % 360);
            if (Remember(note.Bounds)) yield return NoteShape(note);
        }
        foreach (var zone in Components(ConnectNearby(exterior, width, height, ct, radius: 1), width, height, ct))
        {
            if (zone.Width < 16 || zone.Height < 12 || zone.Aspect > 4 ||
                zone.Width > margin * 1.7 || zone.Height > margin * 1.7) continue;
            var readingBox = Clip(source, (zone.Left + 1) * source.Width / width, (zone.Top + 2) * source.Height / height,
                zone.Right * source.Width / width, (zone.Bottom - 1) * source.Height / height);
            if (ParkingPlanText.ReadAccessNote(source, readingBox, dark: true) is { } note && Remember(note.Bounds))
                yield return NoteShape(note);
        }
        foreach (var arm in barriers)
        {
            var cx = source.Width * arm.Cx / width;
            var cy = source.Height * arm.Cy / height;
            var span = source.Width * arm.Width / width;
            var readingBox = Clip(source, cx - span * 3 / 4, cy + span / 4, cx + span * 3 / 4, cy + span * 5 / 4);
            if (ParkingPlanText.ReadAccessNote(source, readingBox, dark: false) is { } note && Remember(note.Bounds))
                yield return NoteShape(note);
        }

        bool Remember(SKRectI box)
        {
            if (foundNotes.Any(old => old.Left < box.Right && old.Right > box.Left && old.Top < box.Bottom && old.Bottom > box.Top))
                return false;
            foundNotes.Add(box);
            return true;
        }

        ParkingShape NoteShape((string Label, SKRectI Bounds) note) =>
            RasterFrame(drawing, source.Width, source.Height, "LABEL", note.Bounds.Left, note.Bounds.Top,
                note.Bounds.Right, note.Bounds.Bottom) with { Label = note.Label };

        static SKRectI Clip(SKBitmap image, int x0, int y0, int x1, int y1) =>
            new(Math.Max(0, x0), Math.Max(0, y0), Math.Min(image.Width, x1), Math.Min(image.Height, y1));
    }

    private static (int Left, int Top, int Right, int Bottom)[] Perimeter(
        SKColor[] pixels, Component site, int width, int height, CancellationToken ct)
    {
        var result = new List<(int Left, int Top, int Right, int Bottom)>();
        foreach (var vertical in new[] { false, true })
        {
            var breadth = vertical ? width : height;
            var depth = vertical ? height : width;
            var from = vertical ? site.Left : site.Top;
            var to = vertical ? site.Right : site.Bottom;
            var edgeBand = (int)(Math.Min(width, height) * .15);
            var alongFrom = Math.Max(0, (vertical ? site.Top : site.Left) - edgeBand);
            var alongTo = Math.Min(depth, (vertical ? site.Bottom : site.Right) + edgeBand + 1);
            var minLength = Math.Max(28, Math.Min(width, height) * .095);
            var ranges = new List<(int Cross, int Start, int End)>();
            int Index(int cross, int along) => vertical ? along * width + cross : cross * width + along;
            for (var cross = Math.Max(6, from - edgeBand); cross < Math.Min(breadth - 6, to + edgeBand + 1); cross++)
            {
                ct.ThrowIfCancellationRequested();
                if (Math.Min(Math.Abs(cross - from), Math.Abs(cross - to)) > edgeBand) continue;
                var start = alongFrom;
                var last = alongFrom - 4;
                for (var along = alongFrom; along <= alongTo + 3; along++)
                {
                    if (along < alongTo && Dark(pixels[Index(cross, along)]))
                    {
                        if (along - last > 3) start = along;
                        last = along;
                        continue;
                    }
                    if (along - last != 3 || last - start < minLength) continue;
                    var forward = 0;
                    var back = 0;
                    var samples = 0;
                    for (var p = start + 3; p <= last - 3; p += 3)
                    {
                        if (SiteSurface(pixels[Index(cross + 5, p)])) forward++;
                        if (SiteSurface(pixels[Index(cross - 5, p)])) back++;
                        samples++;
                    }
                    if (Math.Max(forward, back) >= samples * .57 && Math.Min(forward, back) <= samples * .37)
                        ranges.Add((cross, start, last));
                }
            }

            var kept = new List<(int Cross, int Start, int End)>();
            foreach (var run in ranges.OrderByDescending(line => line.End - line.Start))
            {
                if (kept.Any(other => Math.Abs(other.Cross - run.Cross) <= 4 &&
                    Math.Min(run.End, other.End) - Math.Max(run.Start, other.Start) >= (run.End - run.Start) * .70)) continue;
                kept.Add(run);
                result.Add(vertical ? (run.Cross, run.Start, run.Cross, run.End) : (run.Start, run.Cross, run.End, run.Cross));
                if (kept.Count >= 18) break;
            }
        }
        return result.ToArray();

        static bool Dark(SKColor p) => Math.Max(p.Red, Math.Max(p.Green, p.Blue)) <= 48;
        static bool SiteSurface(SKColor p) => Math.Min(p.Red, Math.Min(p.Green, p.Blue)) >= 38 &&
            Math.Min(p.Red, Math.Min(p.Green, p.Blue)) < 185;
    }

    private static IEnumerable<(int Left, int Top, int Right, int Bottom)> PaintedRuns(
        Component[] marks, bool[] road, int width, int height, CancellationToken ct)
    {
        foreach (var vertical in new[] { false, true })
        {
            int Along(Component c) => vertical ? c.Cy : c.Cx;
            int Across(Component c) => vertical ? c.Cx : c.Cy;
            int Length(Component c) => vertical ? c.Height : c.Width;
            int Thick(Component c) => vertical ? c.Width : c.Height;
            var candidates = marks.Where(c => Length(c) >= 4 && Length(c) <= Math.Min(width, height) * .14 &&
                c.Aspect >= 2.8 && c.Fill >= .72 && Length(c) > Thick(c)).OrderBy(Along).Take(600).ToArray();
            var used = new HashSet<Component>();
            foreach (var seed in candidates)
            {
                ct.ThrowIfCancellationRequested();
                if (used.Contains(seed)) continue;
                var run = new List<Component> { seed };
                var pitch = 0;
                foreach (var next in candidates)
                {
                    if (used.Contains(next) || Math.Abs(Across(next) - Across(seed)) > Math.Max(1, Thick(seed)) ||
                        Math.Abs(Length(next) - Length(seed)) > Math.Max(2, Length(seed) * .35)) continue;
                    var distance = Along(next) - Along(run[^1]);
                    if (distance <= Length(seed)) continue;
                    if (distance > Length(seed) * 3.1 || pitch > 0 && Math.Abs(distance - pitch) > Math.Max(2, pitch * .32)) break;
                    var middle = (Along(next) + Along(run[^1])) / 2;
                    var across = (Across(next) + Across(run[^1])) / 2;
                    if (!road[vertical ? middle * width + across : across * width + middle]) break;
                    pitch = distance;
                    run.Add(next);
                }
                if (run.Count < 3) continue;
                used.UnionWith(run);
                var axis = (int)Math.Round(run.Average(Across));
                yield return vertical ? (axis, run[0].Top, axis, run[^1].Bottom) : (run[0].Left, axis, run[^1].Right, axis);
            }
        }
    }

    private static IEnumerable<Component> BarrierArms(Component[] red, byte[] white, bool[] road, int width, int height, CancellationToken ct)
    {
        var shortBands = red.Where(c => c.Width >= 3 && c.Height <= 5 && c.Width >= 1.65 * c.Height &&
            c.Width < Math.Min(width, height) * .08 && c.Fill >= .58).OrderBy(c => c.Cx).ToArray();
        var used = new HashSet<Component>();
        foreach (var seed in shortBands)
        {
            if (used.Contains(seed)) continue;
            ct.ThrowIfCancellationRequested();
            var run = new List<Component> { seed };
            foreach (var next in shortBands)
            {
                var prior = run[^1];
                var gap = next.Left - prior.Right - 1;
                if (gap < 1 || used.Contains(next) || Math.Abs(next.Cy - seed.Cy) > 2 ||
                    Math.Abs(next.Width - seed.Width) > Math.Max(2, seed.Width * .5)) continue;
                if (gap > seed.Width * 1.65) break;
                var y = (next.Cy + prior.Cy) / 2;
                var whiteCount = 0;
                var asphalt = 0;
                for (var x = prior.Right + 1; x < next.Left; x++)
                {
                    if (WhiteNear(white, width, height, x, y, 1)) whiteCount++;
                    if (y >= 4 && y < height - 4 && (road[(y - 4) * width + x] || road[(y + 4) * width + x])) asphalt++;
                }
                if (whiteCount < gap * .55 || asphalt < gap * .5) break;
                run.Add(next);
            }
            if (run.Count < 3) continue;
            used.UnionWith(run);
            var center = (int)Math.Round(run.Average(c => c.Cy));
            yield return new(run[0].Left, Math.Max(0, center - 2), run[^1].Right, Math.Min(height - 1, center + 2), 0);
        }
    }

    private static IEnumerable<Component> GuardBooths(Component[] arms, SKColor[] pixels, int width, int height, CancellationToken ct)
    {
        if (arms.Length < 2) yield break;
        var blue = new byte[pixels.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            if ((i & 65535) == 0) ct.ThrowIfCancellationRequested();
            var p = pixels[i];
            blue[i] = (byte)(p.Blue >= 135 && p.Red < p.Blue - 50 && p.Green >= p.Red + 30 ? 1 : 0);
        }
        var windows = Components(blue, width, height, ct).Where(c => c.Width >= 4 && c.Height >= 5 && c.Fill >= .64).ToArray();
        for (var i = 1; i < arms.Length; i++)
        {
            var first = arms[i - 1];
            var second = arms[i];
            var gap = second.Left - first.Right;
            if (Math.Abs(first.Cy - second.Cy) > 4 || gap < 10 || gap > Math.Min(first.Width, second.Width) * 2.45) continue;
            var visible = windows.Where(w => w.Left > first.Right && w.Right < second.Left &&
                Math.Abs(w.Cy - first.Cy) < gap * .45).ToArray();
            if (visible.Length == 0) continue;
            var left = visible.Min(c => c.Left);
            var right = visible.Max(c => c.Right);
            var top = visible.Min(c => c.Top);
            var bottom = visible.Max(c => c.Bottom);
            var surround = Math.Max(2, (right - left) / 4);
            yield return new(Math.Max(first.Right + 1, left - surround), Math.Max(0, top - surround),
                Math.Min(second.Left - 1, right + surround), Math.Min(height - 1, bottom + surround), 0);
        }
    }

    private static bool CircularSign(Component c, byte[] red, byte[] white, int width, int height)
    {
        if (c.Width < 8 || c.Height < 8 || c.Aspect > 1.27 || c.Fill is < .14 or > .76 ||
            c.Width > Math.Min(width, height) * .105) return false;
        var radius = (Math.Min(c.Width, c.Height) - 1) / 2d;
        var ring = 0;
        for (var i = 0; i < 12; i++)
        {
            var angle = i * Math.PI / 6;
            var x = c.Cx + (int)Math.Round(Math.Cos(angle) * radius * .85);
            var y = c.Cy + (int)Math.Round(Math.Sin(angle) * radius * .85);
            if (WhiteNear(red, width, height, x, y, 1)) ring++;
        }
        if (ring < 10) return false;
        var interior = 0;
        var pad = Math.Max(2, Math.Min(c.Width, c.Height) / 4);
        for (var y = c.Top + pad; y <= c.Bottom - pad; y++)
        for (var x = c.Left + pad; x <= c.Right - pad; x++)
            interior += white[y * width + x];
        return interior >= (c.Width - 2 * pad) * (c.Height - 2 * pad) * .28;
    }

    private static int? RoundaboutRadius(Component island, byte[] white, bool[] road, int width, int height)
    {
        if (island.Aspect > 1.33 || island.Width < 12 || island.Fill < .35) return null;
        int? best = null;
        var support = 0d;
        for (var radius = (int)(island.Width * .65); radius <= island.Width * 1.15; radius++)
        {
            if (island.Cx - radius - 4 < 0 || island.Cx + radius + 4 >= width ||
                island.Cy - radius - 4 < 0 || island.Cy + radius + 4 >= height) continue;
            const int segments = 96;
            var mark = 0;
            var pavement = 0;
            var transitions = 0;
            var prior = false;
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * 2 * Math.PI / segments;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                var hit = false;
                for (var shift = -1; shift <= 1; shift++)
                {
                    var x = island.Cx + (int)Math.Round(cos * (radius + shift));
                    var y = island.Cy + (int)Math.Round(sin * (radius + shift));
                    hit |= white[y * width + x] != 0;
                }
                var edgeX = island.Cx + (int)Math.Round(cos * (radius + 4));
                var edgeY = island.Cy + (int)Math.Round(sin * (radius + 4));
                if (road[edgeY * width + edgeX]) pavement++;
                if (hit) mark++;
                if (i != 0 && hit != prior) transitions++;
                prior = hit;
            }
            var fraction = mark / (double)segments;
            if (pavement >= segments * .70 && fraction is >= .33 and <= .87 && transitions >= 12 && fraction > support)
            {
                best = radius;
                support = fraction;
            }
        }
        return best;
    }

    private static ParkingShape[] CadObstacles(byte[] red, bool[] road, int width, int height, ParkingDrawing drawing, CancellationToken ct) =>
        Components(ConnectNearby(red, width, height, ct, radius: 1), width, height, ct).Where(c => c.Width >= 8 && c.Height >= 5 && c.Aspect <= 4 &&
            c.Fill >= .61 && c.Width <= width * .08 && c.Height <= height * .08 &&
            c.Top >= 3 && c.Bottom < height - 3 && road[(c.Top - 3) * width + c.Cx] && road[(c.Bottom + 3) * width + c.Cx])
            .Select(c => RasterFrame(drawing, width, height, "PILLAR", c.Left, c.Top, c.Right + 1, c.Bottom + 1)).Take(32).ToArray();

    private static byte[] ConnectNearby(byte[] mask, int width, int height, CancellationToken ct, int radius = 2)
    {
        var horizontal = new byte[mask.Length];
        var expanded = new byte[mask.Length];
        for (var y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                var at = y * width + x;
                if (mask[at] == 0) continue;
                for (var u = Math.Max(0, x - radius); u <= Math.Min(width - 1, x + radius); u++)
                    horizontal[y * width + u] = 1;
            }
        }
        for (var y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
            {
                if (horizontal[y * width + x] == 0) continue;
                for (var v = Math.Max(0, y - radius); v <= Math.Min(height - 1, y + radius); v++)
                    expanded[v * width + x] = 1;
            }
        }
        return expanded;
    }

    private static bool WhiteNear(byte[] mask, int width, int height, int x, int y, int radius)
    {
        for (var v = Math.Max(0, y - radius); v <= Math.Min(height - 1, y + radius); v++)
        for (var u = Math.Max(0, x - radius); u <= Math.Min(width - 1, x + radius); u++)
            if (mask[v * width + u] != 0) return true;
        return false;
    }

    private static ParkingShape RasterFrame(ParkingDrawing drawing, int width, int height, string kind, int x0, int y0, int x1, int y1) =>
        new(Guid.NewGuid(), kind, "", x0 * (double)drawing.Width / width, y0 * (double)drawing.Height / height,
            (x1 - x0) * (double)drawing.Width / width, (y1 - y0) * (double)drawing.Height / height);

    private static ParkingShape RasterStroke(ParkingDrawing drawing, int width, int height, string kind, int x0, int y0, int x1, int y1)
    {
        var points = new[] { new ParkingPoint(x0 * (double)drawing.Width / width, y0 * (double)drawing.Height / height),
            new ParkingPoint(x1 * (double)drawing.Width / width, y1 * (double)drawing.Height / height) };
        return ParkingVisionGeometry.FromCorners(new(Guid.NewGuid(), kind, "", 0, 0, 4, 4), points, drawing);
    }
}
