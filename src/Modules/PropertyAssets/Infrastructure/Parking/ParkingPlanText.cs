using PropFlow.Modules.PropertyAssets.Contracts;
using SkiaSharp;
using System.Text;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

/// <summary>Reads short printed bay identifiers from high-resolution crops of known stalls.</summary>
internal static class ParkingPlanText
{
    private const int GlyphWidth = 12;
    private const int GlyphHeight = 16;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ỐỔÀÃỖĐ";
    private static readonly string[] AccessNotes = ["LỐI VÀO", "LỐI RA", "CỔNG VÀO", "CỔNG RA", "LỐI VÀO BÃI ĐỖ XE", "LỐI RA BÃI ĐỖ XE"];
    private static readonly Lazy<Glyph[]> Templates = new(BuildTemplates);
    private sealed record Glyph(char Value, double Aspect, float[] Coverage);

    internal static string? ReadBay(SKBitmap source, ParkingShape bay, ParkingDrawing drawing)
    {
        var scaleX = (double)source.Width / drawing.Width;
        var scaleY = (double)source.Height / drawing.Height;
        var x = (int)Math.Ceiling((bay.X + bay.Width * .045) * scaleX);
        var y = (int)Math.Ceiling((bay.Y + bay.Height * .26) * scaleY);
        var right = (int)Math.Floor((bay.X + bay.Width * .955) * scaleX);
        var bottom = (int)Math.Floor((bay.Y + bay.Height * .74) * scaleY);
        if (right - x < 17 || bottom - y < 9) return null;

        return Read(source, x, y, right, bottom, digitsOnly: false);
    }

    internal static string? ReadSignNumber(SKBitmap source, int left, int top, int right, int bottom)
        => Read(source, left, top, right, bottom, digitsOnly: true);

    internal static (string Label, SKRectI Bounds)? ReadAccessNote(SKBitmap image, SKRectI crop, bool dark)
    {
        if (!ReadInk(image, crop, dark, out var ink, out var rows)) return null;
        var best = .60;
        (string Label, SKRectI Bounds)? result = null;
        foreach (var (top, bottom) in TextBands(rows))
        {
            var glyphs = Glyphs(ink, crop.Width, top, bottom, out var left, out var right);
            if (glyphs.Count < 5 || glyphs.Any(glyph => glyph.Aspect is < .13 or > 1.15)) continue;
            foreach (var note in AccessNotes)
            {
                var letters = note.Replace(" ", "", StringComparison.Ordinal);
                if (letters.Length != glyphs.Count) continue;
                var scores = glyphs.Select((glyph, index) => NoteScore(glyph, letters[index])).ToArray();
                var score = scores.Average();
                if (scores.Min() < .42 || score <= best) continue;
                best = score;
                result = (note, new(crop.Left + left, crop.Top + top, crop.Left + right, crop.Top + bottom));
            }
        }
        return result;
    }

    private static string? Read(SKBitmap image, int left, int top, int right, int bottom, bool digitsOnly)
    {
        var crop = new SKRectI(left, top, right, bottom);
        if (!ReadInk(image, crop, digitsOnly, out var ink, out var rows, stripEdge: !digitsOnly)) return null;
        var best = (Text: (string?)null, Score: 0d);
        foreach (var (start, end) in TextBands(rows))
        {
            var candidate = ReadGroup(ink, crop.Width, start, end, digitsOnly);
            if (candidate.Score > best.Score) best = candidate;
        }
        return best.Text;
    }

    private static bool ReadInk(SKBitmap image, SKRectI crop, bool dark, out bool[] ink, out int[] rows, bool stripEdge = false)
    {
        ink = [];
        rows = [];
        if (crop.Width < 7 || crop.Height < 7 || crop.Width > 380 || crop.Height > 190 ||
            crop.Left < 0 || crop.Top < 0 || crop.Right > image.Width || crop.Bottom > image.Height) return false;
        ink = new bool[crop.Width * crop.Height];
        rows = new int[crop.Height];
        for (var y = 0; y < crop.Height; y++)
        for (var x = 0; x < crop.Width; x++)
        {
            var pixel = image.GetPixel(crop.Left + x, crop.Top + y);
            var low = Math.Min(pixel.Red, Math.Min(pixel.Green, pixel.Blue));
            var high = Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue));
            var on = high - low < 46 && (dark ? high <= 112 : low >= 165);
            ink[y * crop.Width + x] = on;
            if (on) rows[y]++;
        }
        // A divider can cross the entire crop when a thin line straddles the reduced raster grid.
        // Strip such columns before segmenting letters, which occupy only part of the crop height.
        for (var x = 0; x < crop.Width; x++)
        {
            var coverage = 0;
            for (var y = 0; y < crop.Height; y++)
                if (ink[y * crop.Width + x]) coverage++;
            var edge = stripEdge && (x == 0 || x == crop.Width - 1);
            if (coverage < crop.Height * (edge ? .24 : .79)) continue;
            for (var y = 0; y < crop.Height; y++)
            {
                if (!ink[y * crop.Width + x]) continue;
                ink[y * crop.Width + x] = false;
                rows[y]--;
            }
        }
        return true;
    }

    private static IEnumerable<(int Start, int End)> TextBands(int[] rows)
    {
        for (var start = 0; start < rows.Length; start++)
        {
            if (rows[start] < 2) continue;
            var end = start + 1;
            while (end < rows.Length && (rows[end] >= 2 || end + 1 < rows.Length && rows[end + 1] >= 2)) end++;
            if (end - start >= 7 && end - start < rows.Length * .90) yield return (start, end);
            start = end;
        }
    }

    private static List<Glyph> Glyphs(bool[] ink, int width, int top, int bottom, out int first, out int last)
    {
        var glyphs = new List<Glyph>();
        first = width;
        last = 0;
        for (var left = 0; left < width; left++)
        {
            if (!ColumnHasInk(left)) continue;
            var right = left + 1;
            while (right < width && ColumnHasInk(right)) right++;
            Add(left, right);
            first = Math.Min(first, left);
            last = right;
            left = right;
        }
        return glyphs;

        void Add(int left, int right)
        {
            if (Normalize(ink, width, new(left, top, right, bottom), '\0') is not { } glyph) return;
            if (glyph.Aspect <= 1.13 || right - left < 11)
            {
                glyphs.Add(glyph);
                return;
            }
            var split = 0;
            var best = (bottom - top) * .26;
            var margin = Math.Max(3, (right - left) / 4);
            for (var column = left + margin; column <= right - margin; column++)
            {
                var count = 0;
                for (var y = top; y < bottom; y++)
                    if (ink[y * width + column]) count++;
                if (count >= best) continue;
                best = count;
                split = column;
            }
            if (split == 0) glyphs.Add(glyph);
            else
            {
                Add(left, split);
                Add(split, right);
            }
        }

        bool ColumnHasInk(int x)
        {
            for (var y = top; y < bottom; y++)
                if (ink[y * width + x]) return true;
            return false;
        }
    }

    private static (string? Text, double Score) ReadGroup(bool[] ink, int width, int top, int bottom, bool digitsOnly)
    {
        var glyphs = Glyphs(ink, width, top, bottom, out _, out _);
        if (digitsOnly ? glyphs.Count is < 1 or > 3 : glyphs.Count is < 3 or > 5) return default;
        if (glyphs.Any(g => g.Aspect is < .15 or > 1.12)) return default;

        var best = (Text: (string?)null, Score: 0d);
        var minimumPrefixes = digitsOnly ? 0 : 1;
        var maximumPrefixes = digitsOnly ? 0 : Math.Min(2, glyphs.Count - 2);
        for (var prefixes = minimumPrefixes; prefixes <= maximumPrefixes; prefixes++)
        {
            if (glyphs.Count - prefixes > 3) continue;
            var chars = new char[glyphs.Count];
            var total = 0d;
            for (var i = 0; i < glyphs.Count; i++)
            {
                var (value, score) = Match(glyphs[i], i < prefixes);
                if (score < .52) { total = 0; break; }
                chars[i] = value;
                total += score;
            }
            var average = total / glyphs.Count;
            if (average >= .64 && average > best.Score)
                best = (new string(chars), average);
        }
        return best;

    }

    private static (char Value, double Score) Match(Glyph sample, bool letter)
    {
        var best = (Value: '\0', Score: 0d);
        foreach (var candidate in Templates.Value)
        {
            if (letter ? !char.IsAsciiLetterUpper(candidate.Value) : !char.IsAsciiDigit(candidate.Value)) continue;
            var score = Similarity(sample, candidate);
            if (score > best.Score) best = (candidate.Value, score);
        }
        return best;
    }

    private static double NoteScore(Glyph sample, char letter)
    {
        var normalized = letter.ToString().Normalize(NormalizationForm.FormD)[0];
        var best = 0d;
        foreach (var template in Templates.Value)
        {
            if (template.Value == letter) best = Math.Max(best, Similarity(sample, template));
            else if (template.Value == normalized) best = Math.Max(best, Similarity(sample, template) * .91);
        }
        return best;
    }

    private static double Similarity(Glyph sample, Glyph template)
    {
        var intersection = 0d;
        var union = 0d;
        for (var i = 0; i < template.Coverage.Length; i++)
        {
            intersection += Math.Min(template.Coverage[i], sample.Coverage[i]);
            union += Math.Max(template.Coverage[i], sample.Coverage[i]);
        }
        return intersection / union - Math.Abs(Math.Log(template.Aspect / sample.Aspect)) * .16;
    }

    private static Glyph[] BuildTemplates()
    {
        var result = new List<Glyph>();
        using var bitmap = new SKBitmap(70, 66);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        foreach (var family in new[] { "Arial", "Segoe UI", "DejaVu Sans" })
        foreach (var style in new[] { SKFontStyle.Normal, SKFontStyle.Bold })
        {
            using var face = SKTypeface.FromFamilyName(family, style);
            using var font = new SKFont(face ?? SKTypeface.Default, 50);
            foreach (var value in Alphabet)
            {
                canvas.Clear(SKColors.Black);
                canvas.DrawText(value.ToString(), 8, 53, SKTextAlign.Left, font, paint);
                var ink = bitmap.Pixels.Select(pixel => pixel.Red >= 112).ToArray();
                if (Normalize(ink, bitmap.Width, new SKRectI(0, 0, bitmap.Width, bitmap.Height), value) is { } glyph)
                    result.Add(glyph);
            }
        }
        return result.ToArray();
    }

    private static Glyph? Normalize(bool[] ink, int stride, SKRectI crop, char value)
    {
        var left = crop.Right;
        var right = crop.Left;
        var top = crop.Bottom;
        var bottom = crop.Top;
        for (var y = crop.Top; y < crop.Bottom; y++)
        for (var x = crop.Left; x < crop.Right; x++)
        {
            if (!ink[y * stride + x]) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + 1);
            bottom = Math.Max(bottom, y + 1);
        }
        if (left >= right || top >= bottom) return null;
        var width = right - left;
        var height = bottom - top;
        var coverage = new float[GlyphWidth * GlyphHeight];
        const int samples = 3;
        for (var y = 0; y < GlyphHeight * samples; y++)
        for (var x = 0; x < GlyphWidth * samples; x++)
        {
            var px = left + x * width / (GlyphWidth * samples);
            var py = top + y * height / (GlyphHeight * samples);
            if (ink[py * stride + px])
                coverage[(y / samples) * GlyphWidth + x / samples] += 1f / (samples * samples);
        }
        return new(value, (double)width / height, coverage);
    }
}
