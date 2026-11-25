using System.Text.Json;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Parking;
using SkiaSharp;

namespace PropFlow.UnitTests;

[Trait("Feature","FE-04-Parking-ScopeExtension")]
public sealed class ParkingGeometryQualityTests
{
    [Fact]
    public void Stored_B2_keeps_every_corner_and_tool_and_gets_a_measured_source_review()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-b2");
        var drawing=JsonSerializer.Deserialize<ParkingDrawing>(File.ReadAllText(Path.Combine(root,"b2.json")))!;
        var source=ParkingPlanGeometry.Read(File.ReadAllBytes(Path.Combine(root,"source.jpg")),"image/jpeg",1,default,reconstruct:false);
        var inspected=ParkingSourceGeometry.Inspect(drawing,source);
        Assert.Equal(10,inspected.RecognitionReview!.SourceCheckedObjects);
        Assert.Equal(0,inspected.RecognitionReview.RefinedObjects);
        Assert.Equal(drawing.Shapes,inspected.Shapes);
        ParkingDrawingRules.ValidateSpaceSeparation(inspected);
    }
    [Fact]
    public void Contradicted_orientation_and_unsupported_edges_identify_only_the_bad_bays()
    {
        var source=ParkingPlanGeometry.Read(ParkingVisionRecognitionTests.Source(),"image/png",1,default,reconstruct:false);
        var correct=new ParkingShape(Guid.NewGuid(),"SPACE","A",900,2100,1800,1000,45);
        var mirrored=new ParkingShape(Guid.NewGuid(),"SPACE","B",900,6900,1800,1000,45);
        var floating=new ParkingShape(Guid.NewGuid(),"SPACE","C",4500,4600,600,300);
        var inspected=ParkingSourceGeometry.Inspect(new(10000,10000,[correct,mirrored,floating]),source);
        Assert.Contains(inspected.RecognitionReview!.Issues,i=>i.Code=="orientation"&&i.ShapeIds.Contains(mirrored.Id));
        Assert.Contains(inspected.RecognitionReview.Issues,i=>i.Code=="source_edges"&&i.ShapeIds.Contains(floating.Id));
        Assert.DoesNotContain(inspected.RecognitionReview.Issues,i=>i.ShapeIds.Contains(correct.Id));
        Assert.Equal(mirrored,inspected.Shapes.Single(s=>s.Id==mirrored.Id));
    }
    [Fact]
    public void An_uncertain_nearby_boundary_can_fit_source_lines_without_rescaling_a_row()
    {
        var source=RectSource();
        var seed=new ParkingShape(Guid.NewGuid(),"SPACE","A",2550,2000,2000,4000,NeedsReview:true);
        var inspected=ParkingSourceGeometry.Inspect(new(10000,10000,[seed]),source,uncertain:new HashSet<Guid>{seed.Id});
        Assert.Equal(1,inspected.RecognitionReview!.RefinedObjects);
        var actual=Assert.Single(inspected.Shapes);Assert.Equal(seed.Id,actual.Id);Assert.Equal("SPACE",actual.Kind);
        Assert.InRange(actual.X,2470,2530);Assert.InRange(actual.Width,1970,2030);
        Assert.InRange(actual.Height,3970,4030);
    }
    [Fact]
    public void Large_misplacement_is_reported_instead_of_guessed_or_silently_resized()
    {
        var source=RectSource();var seed=new ParkingShape(Guid.NewGuid(),"SPACE","A",3500,2600,900,1000,NeedsReview:true);
        var result=ParkingSourceGeometry.Inspect(new(10000,10000,[seed]),source,uncertain:new HashSet<Guid>{seed.Id});
        Assert.Equal(0,result.RecognitionReview!.RefinedObjects);Assert.Equal(seed,result.Shapes[0]);Assert.NotEmpty(result.RecognitionReview.Issues);
    }
    [Fact]
    public void Overlap_reports_both_ids_and_inspection_never_shrinks_them_to_make_them_fit()
    {
        var a=new ParkingShape(Guid.NewGuid(),"SPACE","A",2500,2000,2000,4000);
        var b=a with{Id=Guid.NewGuid(),Label="B",X=2600};var drawing=new ParkingDrawing(10000,10000,[a,b]);
        var result=ParkingSourceGeometry.Inspect(drawing,RectSource());
        Assert.Contains(result.RecognitionReview!.Issues,i=>i.Code=="overlap"&&i.ShapeIds.Contains(a.Id)&&i.ShapeIds.Contains(b.Id));
        Assert.Equal(drawing.Shapes,result.Shapes);
    }
    private static ParkingGeometryResult RectSource()
    {
        using var bitmap=new SKBitmap(400,400);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);
        using var pen=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=1,IsAntialias=false};canvas.DrawRect(100,80,80,160,pen);
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        return ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default,reconstruct:false);
    }
    [Theory]
    [InlineData(6,false)][InlineData(8,true)]
    public void A_closed_inner_symbol_requires_a_substantially_larger_source_frame_before_warning(int margin,bool warning)
    {
        using var bitmap=new SKBitmap(400,400);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);
        using var pen=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=1,IsAntialias=false};
        canvas.DrawRect(100,80,80,160,pen);canvas.DrawRect(100+margin,80+margin,80-2*margin,160-2*margin,pen);
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        var source=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default,reconstruct:false);
        var inner=new ParkingShape(Guid.NewGuid(),"SPACE","A",(100+margin)*25,(80+margin)*25,(80-2*margin)*25,(160-2*margin)*25);
        var result=ParkingSourceGeometry.Inspect(new(10000,10000,[inner]),source);
        Assert.Equal(warning,result.RecognitionReview!.Issues.Any(i=>i.Code=="nested_outline"));Assert.Equal(inner,result.Shapes[0]);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void Parallel_lines_only_suggest_the_wall_tool_when_the_enclosed_strip_has_ink(bool hatch)
    {
        using var bitmap=new SKBitmap(400,400);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);
        using var pen=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=1,IsAntialias=false};
        if(hatch){canvas.Save();canvas.ClipRect(new(30,30,330,42));for(var x=18;x<340;x+=3)canvas.DrawLine(x,42,x+12,30,pen);canvas.Restore();}
        canvas.DrawLine(30,30,330,30,pen);canvas.DrawLine(30,42,330,42,pen);
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        var source=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default,reconstruct:false);
        var lines=new[]{new ParkingShape(Guid.NewGuid(),"LINE","",750,748,7500,4,ControlPoints:[new(0,.5),new(1,.5)]),new ParkingShape(Guid.NewGuid(),"LINE","",750,1048,7500,4,ControlPoints:[new(0,.5),new(1,.5)])};
        var result=ParkingSourceGeometry.Inspect(new(10000,10000,lines),source);
        Assert.Equal(hatch,result.RecognitionReview!.Issues.Any(i=>i.Code=="wall_tool"));Assert.Equal(lines,result.Shapes);
    }
    [Fact]
    public void Dark_fill_without_an_actual_boundary_does_not_count_as_supported_edges()
    {
        using var bitmap=new SKBitmap(400,400);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);
        using var pen=new SKPaint{Color=SKColors.Black};canvas.DrawRect(100,80,80,160,pen);
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        var source=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default,reconstruct:false);
        var seed=new ParkingShape(Guid.NewGuid(),"SPACE","A",2800,2500,750,1500);
        var result=ParkingSourceGeometry.Inspect(new(10000,10000,[seed]),source);
        Assert.Contains(result.RecognitionReview!.Issues,i=>i.Code=="source_edges");Assert.Equal(seed,result.Shapes[0]);
    }
}
