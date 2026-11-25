using System.Diagnostics;
using System.Text.Json;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Parking;
using SkiaSharp;

namespace PropFlow.UnitTests;

[Trait("Feature","FE-04-Parking-ScopeExtension")]
public sealed class ParkingGeometryDatasetTests
{
    [Theory]
    [InlineData(0)][InlineData(17)][InlineData(45)][InlineData(135)]
    public void Four_hundred_known_bays_keep_their_observed_geometry_across_held_out_angles(int angle)
    {
        var (source,drawing)=Grid(angle);var watch=Stopwatch.StartNew();
        var result=ParkingSourceGeometry.Inspect(drawing,source);ParkingDrawingRules.ValidateSpaceSeparation(result);watch.Stop();
        Assert.Equal(400,result.RecognitionReview!.SourceCheckedObjects);Assert.Empty(result.RecognitionReview.Issues);Assert.Equal(drawing.Shapes,result.Shapes);
        // A generous guard catches accidental unbounded searches; the report carries the actual timing.
        Assert.True(watch.Elapsed<TimeSpan.FromSeconds(8),$"Inspection took {watch.Elapsed}.");
        Export($"synthetic-400-{angle}",new{kind="synthetic-ground-truth",angle,objects=400,checkedObjects=result.RecognitionReview.SourceCheckedObjects,issues=result.RecognitionReview.Issues.Length,movedCorners=0,milliseconds=watch.Elapsed.TotalMilliseconds,liveAI=false});
    }
    [Theory]
    [InlineData("b2",0,0)][InlineData("b3",3,1)]
    public void Real_diagonal_regressions_measure_missing_tool_types_and_overlaps(string name,int missingInfrastructure,int minimumOverlaps)
    {
        var root=Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-b2");
        var reference=JsonSerializer.Deserialize<ParkingDrawing>(File.ReadAllText(Path.Combine(root,"b2.json")))!;
        var drawing=JsonSerializer.Deserialize<ParkingDrawing>(File.ReadAllText(Path.Combine(root,name+".json")))!;
        var source=ParkingPlanGeometry.Read(File.ReadAllBytes(Path.Combine(root,"source.jpg")),"image/jpeg",1,default,reconstruct:false);
        var result=ParkingSourceGeometry.Inspect(drawing,source);var overlap=ParkingDrawingRules.SpaceOverlaps(drawing).Count();
        var missing=new[]{"WALL","LANE"}.Sum(kind=>Math.Max(0,reference.Shapes.Count(s=>s.Kind==kind)-drawing.Shapes.Count(s=>s.Kind==kind)));
        var errors=reference.Shapes.Where(s=>s.Kind=="SPACE").Select(s=>
        {
            var actual=drawing.Shapes.Single(x=>x.Label==s.Label&&x.Kind==s.Kind);
            var a=Corners(s);var b=Corners(actual);
            return a.Average(p=>b.Min(q=>Math.Sqrt(Math.Pow(p.X-q.X,2)+Math.Pow(p.Y-q.Y,2))))/Math.Max(reference.Width,reference.Height);
        }).ToArray();
        Assert.Equal(missingInfrastructure,missing);Assert.True(overlap>=minimumOverlaps);
        if(name=="b3"){Assert.True(errors.Average()>.01);Assert.Contains(result.RecognitionReview!.Issues,i=>i.Code=="overlap");}else Assert.Equal(0,errors.Average());
        Export(name,new{kind="stored-draft-comparison",sourceHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(root,"source.jpg")))) ,missingInfrastructure=missing,overlappingPairs=overlap,meanCornerDistanceNormalized=errors.Average(),sourceChecks=result.RecognitionReview!.SourceCheckedObjects,issues=result.RecognitionReview.Issues,liveAI=false});
    }
    [Fact]
    public void Complex_source_fixture_retains_both_manually_identified_water_regions_without_claiming_full_annotations()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-quality");var annotation=JsonSerializer.Deserialize<Landmarks>(File.ReadAllText(Path.Combine(root,"complex-landmarks.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var source=ParkingPlanGeometry.Read(File.ReadAllBytes(Path.Combine(root,"complex-source.jpg")),"image/jpeg",1,default,reconstruct:false);
        Assert.Equal(annotation.SourceWidth,source.PixelWidth);Assert.Equal(annotation.SourceHeight,source.PixelHeight);
        var shapes=annotation.Regions.Select((r,i)=>new ParkingShape(Guid.NewGuid(),"RECT",$"Mặt nước {i+1}",r[0]*source.Drawing.Width/source.PixelWidth,r[1]*source.Drawing.Height/source.PixelHeight,r[2]*source.Drawing.Width/source.PixelWidth,r[3]*source.Drawing.Height/source.PixelHeight)).ToArray();
        var drawing=source.Drawing with{Shapes=shapes};var result=ParkingSourceGeometry.Inspect(drawing,source);
        Assert.Equal(drawing.Shapes,result.Shapes);Assert.Equal(0,result.RecognitionReview!.SourceCheckedObjects);
        // Generic filled areas are not spuriously certified as four inked boundary lines.
        Assert.Empty(result.RecognitionReview.Issues);Assert.Equal("partial-landmarks",annotation.Scope);
        Export("complex-landmarks",new{kind=annotation.Scope,sourceWidth=source.PixelWidth,sourceHeight=source.PixelHeight,knownLandmarks=shapes.Length,checkedInkBoundaries=0,fullCoverageEvaluated=false,liveAI=false});
    }
    [Fact]
    public void Quality_metadata_is_optional_validated_and_does_not_change_old_manual_drafts()
    {
        var drawing=ParkingDrawing.Empty;Assert.Null(drawing.RecognitionReview);ParkingDrawingRules.Validate(drawing);
        var invalid=drawing with{RecognitionReview=new(0,1,0,0,[])};
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(invalid));
        invalid=drawing with{RecognitionReview=new(0,0,0,4,[])};Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(invalid));
        invalid=drawing with{RecognitionReview=new(0,0,0,0,[new("bad","message",[Guid.Empty])])};Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(invalid));
    }
    internal static (ParkingGeometryResult,ParkingDrawing) Grid(int angle)
    {
        using var bitmap=new SKBitmap(1600,1200);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);
        using var pen=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=1.5f,IsAntialias=false};var shapes=new List<ParkingShape>();
        for(var row=0;row<20;row++)for(var col=0;col<20;col++)
        {
            var x=70+col*74;var y=60+row*54;canvas.Save();canvas.Translate(x,y);canvas.RotateDegrees(angle);canvas.DrawRect(-20,-10,40,20,pen);
            // Deliberately different inner vehicle symbols must not become additional spaces.
            canvas.DrawRoundRect(new SKRect(-14,-6,14,6),3,3,pen);canvas.Restore();
            shapes.Add(new(Guid.NewGuid(),"SPACE",$"P-{shapes.Count+1:000}",(x-20)*6.25,(y-10)*6.25,250,125,angle));
        }
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        var source=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default,reconstruct:false);
        return(source,source.Drawing with{Shapes=shapes.ToArray()});
    }
    private static ParkingPoint[] Corners(ParkingShape s)
    {
        var a=s.Rotation*Math.PI/180;return(s.Outline??[new(0,0),new(1,0),new(1,1),new(0,1)]).Select(p=>new ParkingPoint(s.X+s.Width/2+(p.X-.5)*s.Width*Math.Cos(a)-(p.Y-.5)*s.Height*Math.Sin(a),s.Y+s.Height/2+(p.X-.5)*s.Width*Math.Sin(a)+(p.Y-.5)*s.Height*Math.Cos(a))).ToArray();
    }
    private static void Export(string name,object value)
    {
        if(Environment.GetEnvironmentVariable("PROPFLOW_PARKING_EVALUATION_DIR") is not {Length:>0} directory)return;
        Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,name+".json"),JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
    }
    private sealed record Landmarks(string Scope,int SourceWidth,int SourceHeight,double[][] Regions);
}
