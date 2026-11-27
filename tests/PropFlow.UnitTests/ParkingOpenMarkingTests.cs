using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Parking;
using SkiaSharp;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-04-Parking-ScopeExtension")]
public sealed class ParkingOpenMarkingTests
{
    [Theory]
    [InlineData(0, true, false, false)]
    [InlineData(-34, true, false, false)]
    [InlineData(0, false, false, false)]
    [InlineData(90, false, false, false)]
    [InlineData(-26, false, true, false)]
    [InlineData(0, false, false, true)]
    public async Task Repeated_markings_create_normal_parking_spaces_after_review(int angle,bool caps,bool staggered,bool twoBanks)
    {
        using var bitmap = new SKBitmap(480, 400);
        using var canvas = new SKCanvas(bitmap);
        using var pen = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, IsAntialias = true };
        canvas.Clear(SKColors.White);
        canvas.Translate(bitmap.Width / 2, bitmap.Height / 2);
        canvas.RotateDegrees(angle);
        if (!staggered) canvas.DrawLine(-52, -45, 52, -45, pen);
        foreach (var x in new[] { -52, 0, 52 })
        {
            var offset = staggered ? x*.36f : 0;
            canvas.DrawLine(x, (twoBanks ? -138 : -45)+offset, x, 48+offset, pen);
            if (caps) canvas.DrawLine(x - 5, 48+offset, x + 5, 48+offset, pen);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var handler = new ParkingVisionRecognitionTests.VisionResponse(ParkingVisionRecognitionTests.Plan()) { Statuses = [HttpStatusCode.ServiceUnavailable] };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var reader = new GeminiParkingPlanRecognition(new(http, "fixture", "fixture-model", true), NullLogger<GeminiParkingPlanRecognition>.Instance);

        var result = await reader.ReadAsync(png.ToArray(), "image/png", 1, default);

        Assert.True(result.LocalPreview);
        Assert.Equal(1, handler.Calls);
        var drawing = Assert.IsType<ParkingDrawing>(result.ReviewDrawing);
        var bays = drawing.Shapes.Where(shape => shape.Kind == "SPACE").ToArray();
        var expectedBays = twoBanks ? 4 : 2;
        Assert.Equal(expectedBays, bays.Length);
        Assert.All(bays, bay => Assert.True(bay.NeedsReview));
        if (caps) Assert.Contains(drawing.Shapes, shape => shape.Kind == "DIVIDER" && shape.Width < 400);
        if (staggered) Assert.DoesNotContain(drawing.Shapes, shape => shape.Kind == "RECT");
        ParkingDrawingRules.ValidateSpaceSeparation(drawing);

        var actor = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var area = new ParkingArea("Bãi đỗ thử nghiệm", actor, now);
        area.Save(area.Name, drawing, actor, now);
        Assert.Throws<ParkingFailure>(() => area.Publish(actor, now));
        area.Save(area.Name, drawing with { Shapes = drawing.Shapes.Select(shape => shape with { NeedsReview = false }).ToArray() }, actor, now);
        area.Publish(actor, now);
        Assert.Equal(expectedBays, area.Spaces.Count);
        area.UpdateSpaceListing(bays[0].Id, 1_000_000m, ParkingListingStatus.Available, actor, now);
        Assert.Equal(1_000_000m, Assert.Single(area.Spaces.Where(space => space.Id == bays[0].Id)).PriceVnd);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    [InlineData(-24)]
    public async Task Open_markings_survive_alongside_closed_shapes_at_different_angles(int angle)
    {
        const int width = 640;
        const int height = 520;
        var radians = angle * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        SKPoint Rotate(float x, float y) => new((float)(width / 2 + x * cos - y * sin), (float)(height / 2 + x * sin + y * cos));
        (SKPoint Start, SKPoint End)[] markings =
        [
            (Rotate(-190, -125), Rotate(-190, 40)),
            (Rotate(-235, -125), Rotate(-145, -125)),
            (Rotate(-130, -30), Rotate(-52, 30)),
            (Rotate(130, -145), Rotate(233, -92)),
            (Rotate(176, 18), Rotate(176, 126)),
            (Rotate(135, 126), Rotate(217, 126))
        ];
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var pen = new SKPaint { Color = SKColors.Black, StrokeWidth = 3, Style = SKPaintStyle.Stroke, IsAntialias = true };
        canvas.Clear(SKColors.White);
        foreach (var (start, end) in markings) canvas.DrawLine(start, end, pen);
        canvas.Save();
        canvas.Translate(width / 2, height / 2);
        canvas.RotateDegrees(angle);
        canvas.DrawRect(-30, 60, 80, 111, pen);
        canvas.Restore();
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using var handler = new ParkingVisionRecognitionTests.VisionResponse(ParkingVisionRecognitionTests.Plan()) { Statuses = [HttpStatusCode.ServiceUnavailable] };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        var reader = new GeminiParkingPlanRecognition(new(http, "fixture", "fixture-model", true), NullLogger<GeminiParkingPlanRecognition>.Instance);

        var result = await reader.ReadAsync(png.ToArray(), "image/png", 1, default);

        Assert.True(result.LocalPreview);
        Assert.Equal(1, handler.Calls);
        var drawing = Assert.IsType<ParkingDrawing>(result.ReviewDrawing);
        Assert.Contains(drawing.Shapes, shape => shape.Kind == "RECT");
        Assert.DoesNotContain(drawing.Shapes, shape => shape.Kind == "SPACE");
        var segments = drawing.Shapes.SelectMany(StrokeSegments).ToArray();
        foreach (var (start, end) in markings)
        {
            var covered = Enumerable.Range(1, 9).Count(step =>
            {
                var x = (start.X + (end.X - start.X) * step / 10) * drawing.Width / width;
                var y = (start.Y + (end.Y - start.Y) * step / 10) * drawing.Height / height;
                return segments.Any(edge => DistanceToSegment(new(x, y), edge) <= 4d * drawing.Width / width);
            });
            Assert.True(covered >= 8, $"Only {covered}/9 points on an open marking were preserved at {angle} degrees.");
        }
        Assert.True(drawing.Shapes.Any(shape => shape.Kind == "DIVIDER"),
            $"No editable T divider at {angle} degrees. Strokes: {string.Join(", ", segments.Select(edge => $"({edge.Start.X:F0},{edge.Start.Y:F0})–({edge.End.X:F0},{edge.End.Y:F0})"))}");
    }

    private static IEnumerable<(ParkingPoint Start, ParkingPoint End)> StrokeSegments(ParkingShape shape)
    {
        ParkingPoint Rotate(double x, double y)
        {
            var cx = shape.X + shape.Width / 2;
            var cy = shape.Y + shape.Height / 2;
            var angle = shape.Rotation * Math.PI / 180;
            return new(cx + (x - cx) * Math.Cos(angle) - (y - cy) * Math.Sin(angle), cy + (x - cx) * Math.Sin(angle) + (y - cy) * Math.Cos(angle));
        }

        if (shape is { Kind: "LINE", ControlPoints.Length: 2 })
            yield return (Rotate(shape.X + shape.ControlPoints[0].X * shape.Width, shape.Y + shape.ControlPoints[0].Y * shape.Height),
                Rotate(shape.X + shape.ControlPoints[1].X * shape.Width, shape.Y + shape.ControlPoints[1].Y * shape.Height));
        if (shape.Kind == "DIVIDER")
        {
            yield return (Rotate(shape.X, shape.Y), Rotate(shape.X + shape.Width, shape.Y));
            yield return (Rotate(shape.X + shape.Width / 2, shape.Y), Rotate(shape.X + shape.Width / 2, shape.Y + shape.Height));
        }
    }

    private static double DistanceToSegment(ParkingPoint point, (ParkingPoint Start, ParkingPoint End) edge)
    {
        var dx = edge.End.X - edge.Start.X;
        var dy = edge.End.Y - edge.Start.Y;
        var t = Math.Clamp(((point.X - edge.Start.X) * dx + (point.Y - edge.Start.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(point.X - edge.Start.X - t * dx, 2) + Math.Pow(point.Y - edge.Start.Y - t * dy, 2));
    }
}
