using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using SkiaSharp;

namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

public sealed record ParkingGeometryResult(ParkingDrawing Drawing, byte[] Preview, byte[] Ink, int PixelWidth, int PixelHeight);

/// <summary>Decodes the reference and reconstructs clean, independently editable diagram objects.</summary>
public static class ParkingPlanGeometry
{
    private const int MaxSide=2000;
    public static ParkingGeometryResult Read(byte[] content,string mime,int page,CancellationToken ct,bool reconstruct=true)
    {
        ct.ThrowIfCancellationRequested();
        using var bitmap=Rasterize(content,mime,page);
        var w=bitmap.Width;var h=bitmap.Height;var pixels=bitmap.Pixels;
        var ink=new byte[w*h];const int threshold=235;
        for(var i=0;i<ink.Length;i++)
        {
            if((i&65535)==0)ct.ThrowIfCancellationRequested();
            var p=pixels[i];var gray=(p.Red*299+p.Green*587+p.Blue*114)/1000;
            ink[i]=(byte)((gray*p.Alpha/255+255-p.Alpha)<threshold?1:0);
        }
        var canvasWidth=(int)Math.Round(w*10000d/Math.Max(w,h));var canvasHeight=(int)Math.Round(h*10000d/Math.Max(w,h));
        var shapes=reconstruct?ParkingObjectReconstruction.Build(ink,w,h,canvasWidth,canvasHeight,ct):[];
        var drawing=ParkingDrawingRules.Validate(new(canvasWidth,canvasHeight,shapes,null,new(w,h,threshold,shapes.Count(x=>x.Kind=="SPACE"),0)));
        using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);
        return new(drawing,encoded.ToArray(),ink,w,h);
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
