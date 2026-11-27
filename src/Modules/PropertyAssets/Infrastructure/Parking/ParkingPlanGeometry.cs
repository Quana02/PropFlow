using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using SkiaSharp;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

public sealed record ParkingGeometryResult(ParkingDrawing Drawing, byte[] Preview, byte[] Ink, int PixelWidth, int PixelHeight, byte[]? StrongInk = null);

/// <summary>Decodes the reference and reconstructs clean, independently editable diagram objects.</summary>
public static class ParkingPlanGeometry
{
    private const int MaxSide=2000;
    public static ParkingGeometryResult Read(byte[] content,string mime,int page,CancellationToken ct,bool reconstruct=true)
    {
        ct.ThrowIfCancellationRequested();
        using var bitmap=Rasterize(content,mime,page);
        var w=bitmap.Width;var h=bitmap.Height;var pixels=bitmap.Pixels;
        var ink=new byte[w*h];var strongInk=new byte[w*h];const int threshold=235;
        for(var i=0;i<ink.Length;i++)
        {
            if((i&65535)==0)ct.ThrowIfCancellationRequested();
            var p=pixels[i];var gray=(p.Red*299+p.Green*587+p.Blue*114)/1000;
            var onWhite=gray*p.Alpha/255+255-p.Alpha;
            ink[i]=(byte)(onWhite<threshold?1:0);
            strongInk[i]=(byte)(onWhite<192?1:0);
        }
        var canvasWidth=(int)Math.Round(w*10000d/Math.Max(w,h));var canvasHeight=(int)Math.Round(h*10000d/Math.Max(w,h));
        var drawing=ParkingDrawingRules.Validate(new(canvasWidth,canvasHeight,[],null,new(w,h,threshold,0,0)));
        using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);
        var source = new ParkingGeometryResult(drawing,encoded.ToArray(),ink,w,h,strongInk);
        return reconstruct ? source with { Drawing = Reconstruct(source, ct) } : source;
    }

    internal static ParkingDrawing Reconstruct(ParkingGeometryResult source, CancellationToken ct, bool includeLinework = false)
    {
        var drawing = source.Drawing;
        var (ink, strongInk, width, height) = includeLinework ? PreviewInk(source, ct) : (source.Ink, source.Ink, source.PixelWidth, source.PixelHeight);
        var (colorShapes, cadInk) = includeLinework ? ParkingColorSymbols.Read(source.Preview, drawing, width, height, ct) : (Array.Empty<ParkingShape>(), null);
        if (cadInk is not null) ink = strongInk = cadInk;
        var paintedBays = colorShapes.Count(shape => shape.Kind == "SPACE");
        var angledRows = includeLinework && paintedBays < 4 ? new ParkingAngledRowRecognition(ink, width, height, drawing).Recognize(ct) : null;
        ParkingShape[] shapes;
        if (paintedBays >= 4 && cadInk is null)
            shapes = colorShapes.Take(ParkingDrawingRules.MaxObjects).ToArray();
        else if (angledRows is not null)
            shapes = angledRows;
        else
        {
            var linework = ParkingObjectReconstruction.Build(ink, width, height, drawing.Width, drawing.Height, ct, includeLinework, strongInk);
            if (paintedBays >= 4)
            {
                var bays = colorShapes.Where(shape => shape.Kind == "SPACE").ToArray();
                linework = linework.Where(shape => shape.Kind != "SPACE" &&
                    !bays.Any(bay => shape.X >= bay.X - 5 && shape.Y >= bay.Y - 5 &&
                        shape.X + shape.Width <= bay.X + bay.Width + 5 && shape.Y + shape.Height <= bay.Y + bay.Height + 5)).ToArray();
                shapes = colorShapes.Concat(linework).Take(ParkingDrawingRules.MaxObjects).ToArray();
            }
            else
                shapes = linework.Concat(colorShapes.Where(shape => shape.Kind is not ("SPACE" or "WHEEL_STOP")))
                    .Take(ParkingDrawingRules.MaxObjects).ToArray();
        }
        return ParkingDrawingRules.Validate(drawing with
        {
            Shapes = shapes,
            Digitization = drawing.Digitization is { } info ? info with { Candidates = shapes.Count(shape => shape.Kind == "SPACE") } : null
        });
    }

    internal static ParkingDrawing AddSourceDetails(ParkingDrawing drawing, ParkingGeometryResult source, double[]? planBounds, CancellationToken ct)
    {
        var existingBays = drawing.Shapes.Where(shape => shape.Kind == "SPACE").ToArray();
        if (existingBays.Length < 4) return drawing;
        var ratio = Math.Min(1, 700d / Math.Max(source.PixelWidth, source.PixelHeight));
        var width = (int)Math.Round(source.PixelWidth * ratio);
        var height = (int)Math.Round(source.PixelHeight * ratio);
        var (observed, _) = ParkingColorSymbols.Read(source.Preview, drawing, width, height, ct);
        var observedBays = observed.Where(shape => shape.Kind == "SPACE").ToArray();
        if (observedBays.Length < 4) return drawing;
        var matches = observedBays.Select(bay => (Local: bay, Existing: existingBays.FirstOrDefault(known =>
            SimilarFootprint(known, bay, .65)))).Where(pair => pair.Existing is not null).ToArray();
        if (matches.Length < Math.Max(4, Math.Min(observedBays.Length, existingBays.Length) * .65)) return drawing;

        var result = drawing.Shapes.ToList();
        var labels = existingBays.Select(shape => shape.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reported = drawing.RecognitionReview?.Issues.SelectMany(issue => issue.ShapeIds).ToHashSet() ?? [];
        foreach (var (local, existing) in matches)
        {
            if (existing!.Label.Length != 5 || !existing.Label.StartsWith("P-", StringComparison.Ordinal) ||
                !existing.Label.Skip(2).All(char.IsAsciiDigit) || local.Label.StartsWith("P-", StringComparison.Ordinal) ||
                reported.Contains(existing.Id) || !labels.Add(local.Label)) continue;
            var index = result.FindIndex(shape => shape.Id == existing.Id);
            result[index] = existing with { Label = local.Label };
        }
        foreach (var shape in observed)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count >= ParkingDrawingRules.MaxObjects) break;
            if (shape.Kind is not ("BARRIER" or "GUARD_BOOTH" or "WHEEL_STOP" or "TRAFFIC_SIGN" or "ROUNDABOUT" or "NO_PARKING" or "LABEL" or "DASHED_LINE") ||
                !InsidePlan(shape) || result.Any(old => (old.Kind == shape.Kind || old.Kind is "RECT" or "SQUARE" or "WALL" or "GATE") &&
                    SimilarFootprint(old, shape, .35))) continue;
            result.Add(shape);
        }
        if (drawing.Shapes.SequenceEqual(result)) return drawing;
        var report = drawing.RecognitionReview;
        return ParkingDrawingRules.Validate(drawing with
        {
            Shapes = result.ToArray(),
            RecognitionReview = report is null ? null : report with { CheckedObjects = result.Count }
        });

        bool InsidePlan(ParkingShape shape)
        {
            if (!ParkingSceneRegion.CanTile(source, planBounds)) return true;
            var x = (shape.X + shape.Width / 2) * 1000 / drawing.Width;
            var y = (shape.Y + shape.Height / 2) * 1000 / drawing.Height;
            return y >= planBounds![0] && x >= planBounds[1] && y <= planBounds[2] && x <= planBounds[3];
        }
    }

    private static bool SimilarFootprint(ParkingShape a, ParkingShape b, double threshold)
    {
        var first = Bounds(a);
        var second = Bounds(b);
        var intersection = Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left)) *
            Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return intersection >= threshold * ((first.Right - first.Left) * (first.Bottom - first.Top) +
            (second.Right - second.Left) * (second.Bottom - second.Top) - intersection);

        static (double Left, double Top, double Right, double Bottom) Bounds(ParkingShape shape)
        {
            if (shape.Rotation == 0 && shape.Outline is null)
                return (shape.X, shape.Y, shape.X + shape.Width, shape.Y + shape.Height);
            var corners = ParkingSourceGeometry.Corners(shape);
            return (corners.Min(point => point.X), corners.Min(point => point.Y), corners.Max(point => point.X), corners.Max(point => point.Y));
        }
    }

    private static (byte[] Ink, byte[] StrongInk, int Width, int Height) PreviewInk(ParkingGeometryResult source, CancellationToken ct)
    {
        const int maxPreviewSide = 700;
        var ratio = (double)maxPreviewSide / Math.Max(source.PixelWidth, source.PixelHeight);
        var sourceStrongInk=source.StrongInk??source.Ink;
        if (ratio >= 1) return (source.Ink, sourceStrongInk, source.PixelWidth, source.PixelHeight);

        var width = Math.Max(1, (int)Math.Round(source.PixelWidth * ratio));
        var height = Math.Max(1, (int)Math.Round(source.PixelHeight * ratio));
        var ink = new byte[width * height];
        var strongInk = new byte[ink.Length];
        for (var y = 0; y < height; y++)
        {
            ct.ThrowIfCancellationRequested();
            var row = (int)((y + .5) * source.PixelHeight / height) * source.PixelWidth;
            for (var x = 0; x < width; x++)
            {
                var pixel=row+(int)((x+.5)*source.PixelWidth/width);
                ink[y*width+x]=source.Ink[pixel];
                strongInk[y*width+x]=sourceStrongInk[pixel];
            }
        }
        return (ink,strongInk,width,height);
    }

    private static SKBitmap Rasterize(byte[] content,string mime,int page)
    {
        if(mime=="application/pdf")
        {
            try
            {
                using var input=new MemoryStream(content);using var pdf=PdfReader.Open(input,PdfDocumentOpenMode.Import);
                if(page<1||page>pdf.PageCount)throw new ParkingFailure(400,"parking_page_invalid","Trang PDF được chọn không tồn tại.");
                var p=pdf.Pages[page-1];var box=p.CropBox;var width=box.Width>0?box.Width:p.Width.Point;var height=box.Height>0?box.Height:p.Height.Point;
                if(p.Rotate%180!=0)(width,height)=(height,width);
                CheckAspect(width,height);
                using var selected=new PdfDocument();selected.AddPage(p);using var bytes=new MemoryStream();selected.Save(bytes,false);bytes.Position=0;
                if(OperatingSystem.IsWindows()||OperatingSystem.IsLinux())
                    return PDFtoImage.Conversion.ToImage(bytes,0,options:new PDFtoImage.RenderOptions(Dpi:240,Width:width>=height?MaxSide:null,Height:height>width?MaxSide:null,WithAspectRatio:true));
                throw new ParkingFailure(400,"parking_platform_unsupported","Máy chủ chưa hỗ trợ đọc trang PDF.");
            }
            catch(PdfReaderException){throw new ParkingFailure(400,"parking_pdf_invalid","Không đọc được PDF. Dùng tệp không khóa hoặc xuất trang thành ảnh.");}
        }
        using var data=SKData.CreateCopy(content);using var codec=SKCodec.Create(data);
        if(codec is null)throw new ParkingFailure(400,"parking_image_invalid","Không đọc được ảnh PNG/JPG.");
        CheckAspect(codec.Info.Width,codec.Info.Height);
        if((long)codec.Info.Width*codec.Info.Height>32_000_000)throw new ParkingFailure(400,"parking_image_too_large","Ảnh quá lớn. Hãy xuất riêng sơ đồ dưới 32 triệu điểm ảnh.");
        var size=codec.GetScaledDimensions(Math.Min(1f,(float)MaxSide/Math.Max(codec.Info.Width,codec.Info.Height)));
        using var decoded=new SKBitmap(new SKImageInfo(size.Width,size.Height,SKColorType.Bgra8888,SKAlphaType.Premul));
        if(codec.GetPixels(decoded.Info,decoded.GetPixels())!=SKCodecResult.Success)throw new ParkingFailure(400,"parking_image_invalid","Ảnh bị lỗi hoặc không đầy đủ.");
        var factor=Math.Min(1d,(double)MaxSide/Math.Max(decoded.Width,decoded.Height));var swapped=(int)codec.EncodedOrigin>=5;
        var output=new SKBitmap(Math.Max(1,(int)Math.Round((swapped?decoded.Height:decoded.Width)*factor)),Math.Max(1,(int)Math.Round((swapped?decoded.Width:decoded.Height)*factor)),SKColorType.Bgra8888,SKAlphaType.Premul);
        using(var canvas=new SKCanvas(output))
        {
            canvas.Clear(SKColors.White);canvas.Scale((float)factor);
            switch(codec.EncodedOrigin)
            {
                case SKEncodedOrigin.TopRight:canvas.Translate(decoded.Width,0);canvas.Scale(-1,1);break;
                case SKEncodedOrigin.BottomRight:canvas.Translate(decoded.Width,decoded.Height);canvas.RotateDegrees(180);break;
                case SKEncodedOrigin.BottomLeft:canvas.Translate(0,decoded.Height);canvas.Scale(1,-1);break;
                case SKEncodedOrigin.LeftTop:canvas.RotateDegrees(90);canvas.Scale(1,-1);break;
                case SKEncodedOrigin.RightTop:canvas.Translate(decoded.Height,0);canvas.RotateDegrees(90);break;
                case SKEncodedOrigin.RightBottom:canvas.Translate(decoded.Height,decoded.Width);canvas.Scale(-1,1);canvas.RotateDegrees(-90);break;
                case SKEncodedOrigin.LeftBottom:canvas.Translate(0,decoded.Width);canvas.RotateDegrees(-90);break;
            }
            canvas.DrawBitmap(decoded,0,0,new SKSamplingOptions(SKFilterMode.Linear));
        }
        return output;
    }
    private static void CheckAspect(double w,double h)
    {if(w<=0||h<=0||Math.Max(w,h)/Math.Min(w,h)>100)throw new ParkingFailure(400,"parking_image_invalid","Tỷ lệ trang không phù hợp. Hãy xuất riêng vùng sơ đồ.");}

}
