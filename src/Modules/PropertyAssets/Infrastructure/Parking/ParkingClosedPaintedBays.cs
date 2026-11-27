using SkiaSharp;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

internal static partial class ParkingColorSymbols
{
    private static (int Left, int Top, int Right, int Bottom)[] AddFineBays(
        SKBitmap source, (int Left, int Top, int Right, int Bottom)[] bays, int width, int height,
        int left, int top, int right, int bottom, CancellationToken ct)
    {
        var sourceSide = Math.Max(source.Width, source.Height);
        if (bays.Length < 4 || sourceSide < Math.Max(width, height) * 1.45) return bays;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromMilliseconds(750));
        try
        {
            var scale = Math.Min(1, 1150d / sourceSide);
            var fineWidth = (int)Math.Round(source.Width * scale);
            var fineHeight = (int)Math.Round(source.Height * scale);
            using var image = source.Resize(new SKImageInfo(fineWidth, fineHeight), new SKSamplingOptions(SKFilterMode.Linear));
            if (image is null) return bays;
            var pixels = image.Pixels;
            var paint = new byte[pixels.Length];
            var road = new bool[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                if ((i & 16383) == 0) budget.Token.ThrowIfCancellationRequested();
                var p = pixels[i];
                var min = Math.Min(p.Red, Math.Min(p.Green, p.Blue));
                var max = Math.Max(p.Red, Math.Max(p.Green, p.Blue));
                paint[i] = (byte)(min >= 163 && max - min <= 35 ? 1 : 0);
                road[i] = min >= 20 && max <= 170 && max - min <= 34 ||
                    p.Red < 95 && p.Blue >= p.Red + 65 && p.Blue >= p.Green + 27;
            }
            var observed = AddClosedBays([], paint, road, fineWidth, fineHeight, left * fineWidth / width,
                top * fineHeight / height, right * fineWidth / width, bottom * fineHeight / height, budget.Token);
            var result = bays.ToList();
            foreach (var fine in observed)
            {
                var cell = (Left: (int)Math.Round(fine.Left * (double)width / fineWidth),
                    Top: (int)Math.Round(fine.Top * (double)height / fineHeight),
                    Right: (int)Math.Round(fine.Right * (double)width / fineWidth),
                    Bottom: (int)Math.Round(fine.Bottom * (double)height / fineHeight));
                var originalArea = Area(cell);
                foreach (var known in result)
                {
                    if (Intersection(cell, known) == 0) continue;
                    var across = Math.Min(cell.Right, known.Right) - Math.Max(cell.Left, known.Left);
                    var along = Math.Min(cell.Bottom, known.Bottom) - Math.Max(cell.Top, known.Top);
                    if (across <= 2)
                    {
                        if (cell.Left < known.Left) cell.Right = known.Left;
                        else cell.Left = known.Right;
                    }
                    else if (along <= 2)
                    {
                        if (cell.Top < known.Top) cell.Bottom = known.Top;
                        else cell.Top = known.Bottom;
                    }
                }
                if (cell.Right > cell.Left && cell.Bottom > cell.Top && Area(cell) >= originalArea * .84 &&
                    !result.Any(known => Intersection(known, cell) > 0)) result.Add(cell);
            }
            return result.OrderBy(bay => bay.Top).ThenBy(bay => bay.Left).ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return bays;
        }
    }

    private static (int Left, int Top, int Right, int Bottom)[] AddClosedBays(
        (int Left, int Top, int Right, int Bottom)[] rows, byte[] paint, bool[] surface,
        int width, int height, int left, int top, int right, int bottom, CancellationToken ct)
    {
        var free = new byte[paint.Length];
        for (var y = Math.Max(1, top - 2); y < Math.Min(height - 1, bottom + 3); y++)
        {
            ct.ThrowIfCancellationRequested();
            for (var x = Math.Max(1, left - 2); x < Math.Min(width - 1, right + 3); x++)
            {
                var at = y * width + x;
                if (!surface[at] || paint[at] != 0) continue;
                if (paint[at - width] != 0 && paint[at + width] != 0 || paint[at - 1] != 0 && paint[at + 1] != 0) continue;
                free[at] = 1;
            }
        }
        var candidates = new List<Component>();
        foreach (var cell in Components(free, width, height, ct))
        {
            var shortest = Math.Min(cell.Width, cell.Height);
            var longest = Math.Max(cell.Width, cell.Height);
            if (shortest < Math.Max(7, Math.Min(width, height) * .022) || longest < 14 ||
                longest > Math.Min(width, height) * .26 || cell.Fill < .47 || cell.Aspect is < 1.25 or > 3.6) continue;
            if (cell.Left <= left + 1 || cell.Top <= top + 1 || cell.Right >= right - 1 || cell.Bottom >= bottom - 1) continue;
            if (Edge(cell.Left - 1, cell.Top, cell.Left - 1, cell.Bottom) < .65 ||
                Edge(cell.Right + 1, cell.Top, cell.Right + 1, cell.Bottom) < .65 ||
                Edge(cell.Left, cell.Top - 1, cell.Right, cell.Top - 1) < .65 ||
                Edge(cell.Left, cell.Bottom + 1, cell.Right, cell.Bottom + 1) < .65) continue;
            candidates.Add(cell);
        }

        var closed = candidates.Where(cell => candidates.Any(other => Adjacent(cell, other)))
            .Select(cell => (cell.Left, cell.Top, cell.Right + 1, cell.Bottom + 1)).ToList();
        foreach (var cell in IntersectPaintedRows(paint, surface, width, height, left, top, right, bottom, ct))
            if (!closed.Any(other => Intersection(other, cell) > 0)) closed.Add(cell);
        var result = rows.Where(row =>
        {
            var overlapping = closed.Select(cell => Intersection(row, cell)).Where(area => area >= Area(row) * .21).ToArray();
            return overlapping.Length < 2 || overlapping.Sum() < Area(row) * .65;
        }).ToList();
        foreach (var cell in closed)
            if (!result.Any(existing => Intersection(existing, cell) > 0)) result.Add(cell);
        return result.OrderBy(bay => bay.Top).ThenBy(bay => bay.Left).ToArray();

        double Edge(int x0, int y0, int x1, int y1)
        {
            var steps = Math.Max(x1 - x0, y1 - y0);
            var hit = 0;
            for (var at = 1; at < steps; at++)
            {
                var x = x0 + (x1 == x0 ? 0 : at);
                var y = y0 + (y1 == y0 ? 0 : at);
                for (var offset = -2; offset <= 2; offset++)
                {
                    var u = x + (x0 == x1 ? offset : 0);
                    var v = y + (y0 == y1 ? offset : 0);
                    if (u < 0 || u >= width || v < 0 || v >= height) continue;
                    if (paint[v * width + u] == 0) continue;
                    hit++;
                    break;
                }
            }
            return hit / (double)(steps - 1);
        }
    }

    private static IEnumerable<(int Left, int Top, int Right, int Bottom)> IntersectPaintedRows(
        byte[] paint, bool[] surface, int width, int height, int left, int top, int right, int bottom, CancellationToken ct)
    {
        var scale = Math.Min(width, height);
        foreach (var transpose in new[] { false, true })
        {
            int Index(int x, int y) => transpose ? x * width + y : y * width + x;
            var minX = Math.Max(2, transpose ? top : left);
            var maxX = Math.Min((transpose ? height : width) - 3, transpose ? bottom : right);
            var minY = Math.Max(2, transpose ? left : top);
            var maxY = Math.Min((transpose ? width : height) - 3, transpose ? right : bottom);
            var minimum = Math.Max(18, scale * .055);
            var baselines = new List<(int Y, int Start, int End)>();
            for (var y = minY; y <= maxY; y++)
            {
                ct.ThrowIfCancellationRequested();
                var from = minX;
                var last = minX - 4;
                for (var x = minX; x <= maxX + 3; x++)
                {
                    if (x <= maxX && (paint[Index(x, y)] != 0 || paint[Index(x, y + 1)] != 0))
                    {
                        if (x - last > 3) from = x;
                        last = x;
                        continue;
                    }
                    if (x - last == 3 && last - from >= minimum) baselines.Add((y, from, last));
                }
            }
            var merged = new List<(int Y, int Start, int End)>();
            foreach (var line in baselines.OrderByDescending(line => line.End - line.Start))
            {
                if (merged.Any(other => Math.Abs(line.Y - other.Y) <= 3 &&
                    Math.Min(line.End, other.End) - Math.Max(line.Start, other.Start) >= (line.End - line.Start) * .74)) continue;
                merged.Add(line);
                if (merged.Count == 120) break;
            }

            var results = new List<(double Score, (int Left, int Top, int Right, int Bottom) Cell)>();
            var ordered = merged.OrderBy(line => line.Y).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var start = ordered[i];
                for (var j = i + 1; j < ordered.Length; j++)
                {
                    var end = ordered[j];
                    var depth = end.Y - start.Y;
                    if (depth > scale * .27) break;
                    if (depth < Math.Max(12, scale * .038)) continue;
                    var from = Math.Max(minX, Math.Max(start.Start, end.Start) - 2);
                    var to = Math.Min(maxX, Math.Min(start.End, end.End) + 2);
                    if (to - from < depth * .65) continue;
                    var dividers = new List<(int X, int Direct)>();
                    for (var x = from; x <= to; x++)
                    {
                        var near = 0;
                        var direct = 0;
                        for (var y = start.Y + 2; y <= end.Y - 2; y++)
                        {
                            if (paint[Index(x, y)] != 0) direct++;
                            if (paint[Index(x - 1, y)] != 0 || paint[Index(x, y)] != 0 || paint[Index(x + 1, y)] != 0) near++;
                        }
                        if (near < (depth - 3) * .86 || direct < (depth - 3) * .48) continue;
                        if (dividers.Count > 0 && x - dividers[^1].X <= 3)
                        {
                            if (dividers[^1].Direct < direct) dividers[^1] = (x, direct);
                            continue;
                        }
                        dividers.Add((x, direct));
                    }
                    for (var k = 1; k < dividers.Count; k++)
                    {
                        var a = dividers[k - 1];
                        var b = dividers[k];
                        var span = b.X - a.X;
                        var ratio = span / (double)depth;
                        if (span < 7 || ratio is < .24 or > .86) continue;
                        var previous = k >= 2 ? a.X - dividers[k - 2].X : 0;
                        var next = k + 1 < dividers.Count ? dividers[k + 1].X - b.X : 0;
                        if (Math.Min(Math.Abs(previous - span), Math.Abs(next - span)) > Math.Max(2, span * .24)) continue;
                        var paved = 0;
                        var split = false;
                        for (var step = 1; step <= 3; step++)
                        {
                            var y = start.Y + depth * step / 4;
                            if (surface[Index(a.X + 2, y)] || surface[Index(b.X - 2, y)]) paved++;
                        }
                        for (var y = start.Y + depth / 4; y < end.Y - depth / 4; y++)
                        {
                            var ink = 0;
                            for (var x = a.X + 2; x <= b.X - 2; x++)
                                ink += paint[Index(x, y)];
                            if (ink >= (span - 3) * .75) { split = true; break; }
                        }
                        if (paved < 2 || split) continue;
                        var score = (a.Direct + b.Direct) / (2d * (depth - 3)) - Math.Abs(ratio - .50) * .4;
                        var cell = transpose ? (start.Y + 1, a.X + 1, end.Y, b.X) : (a.X + 1, start.Y + 1, b.X, end.Y);
                        results.Add((score, cell));
                    }
                }
            }
            foreach (var candidate in results.OrderByDescending(result => result.Score))
                yield return candidate.Cell;
        }
    }

    private static bool Adjacent(Component first, Component second)
    {
        if (first == second || Math.Abs(first.Width - second.Width) > Math.Max(3, first.Width * .25) ||
            Math.Abs(first.Height - second.Height) > Math.Max(3, first.Height * .25)) return false;
        var xGap = Math.Max(first.Left, second.Left) - Math.Min(first.Right, second.Right);
        var yGap = Math.Max(first.Top, second.Top) - Math.Min(first.Bottom, second.Bottom);
        return xGap is >= 0 and <= 5 && -yGap >= first.Height * .68 ||
            yGap is >= 0 and <= 5 && -xGap >= first.Width * .68;
    }

    private static int Intersection((int Left, int Top, int Right, int Bottom) a, (int Left, int Top, int Right, int Bottom) b) =>
        Math.Max(0, Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left)) *
        Math.Max(0, Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top));

    private static int Area((int Left, int Top, int Right, int Bottom) cell) => (cell.Right - cell.Left) * (cell.Bottom - cell.Top);
}
