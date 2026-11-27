using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure.Parking;
using PropFlow.Shared;
using SkiaSharp;

namespace PropFlow.UnitTests;

[Trait("Feature","FE-04-Parking-ScopeExtension")]
public sealed class ParkingVisionRecognitionTests
{
    // The same uploaded image produced the good B2 and the regressed B3 stored drafts.
    [Theory]
    [InlineData("b2")][InlineData("b3")]
    public async Task Stored_draft_regression_preserves_B2_geometry_and_rejects_B3_overlaps(string name)
    {
        var expected=StoredDrawing(name);using var handler=new VisionResponse(StoredPlan(expected));
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance)
            .ReadAsync(File.ReadAllBytes(Fixture("source.jpg")),"image/jpeg",1,default);
        if(name=="b3")Assert.InRange(handler.Calls,3,4);else Assert.Equal(2,handler.Calls);
        if(name=="b3"){Assert.Null(result.Drawing);Assert.Contains("chồng",result.Message);return;}
        Assert.NotNull(result.Drawing);Assert.Equal(11,result.Drawing.Shapes.Length);
        Assert.Equal(2,result.Drawing.Shapes.Count(s=>s.Kind=="WALL"));Assert.Single(result.Drawing.Shapes.Where(s=>s.Kind=="LANE"));
        foreach(var shape in expected.Shapes)
        {
            var actual=Assert.Single(result.Drawing.Shapes.Where(s=>s.Kind==shape.Kind&&s.Label==shape.Label&&Math.Abs(s.Y-shape.Y)<1));
            var corners=ShapeCorners(actual);
            foreach(var point in ShapeCorners(shape))Assert.True(corners.Min(p=>Math.Sqrt(Math.Pow(p.X-point.X,2)+Math.Pow(p.Y-point.Y,2)))<.01,"Recognition changed a validated B2 corner.");
        }
        ParkingDrawingRules.Validate(result.Drawing with{Shapes=result.Drawing.Shapes.Select(s=>s with{NeedsReview=false}).ToArray()},publishing:true);
    }
    [Fact]
    public async Task Review_repairs_overlap_or_keeps_the_existing_valid_B2_instead_of_accepting_B3()
    {
        foreach(var goodFirst in new[]{false,true})
        {
            using var handler=new VisionResponse(StoredPlan(StoredDrawing(goodFirst?"b2":"b3")),StoredPlan(StoredDrawing(goodFirst?"b3":"b2")));
            using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
            var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(File.ReadAllBytes(Fixture("source.jpg")),"image/jpeg",1,default);
            Assert.NotNull(result.Drawing);Assert.Equal(11,result.Drawing.Shapes.Length);Assert.Equal(2,handler.Calls);
            ParkingDrawingRules.Validate(result.Drawing with{Shapes=result.Drawing.Shapes.Select(s=>s with{NeedsReview=false}).ToArray()},publishing:true);
            Assert.Contains(goodFirst?"chưa hoàn tất":"AI đã đối chiếu",result.Message);
        }
    }
    private static string Fixture(string name)=>Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-b2",name);
    [Theory]
    [InlineData("SPACE")][InlineData("WALL")][InlineData("LANE")]
    public async Task A_review_that_drops_a_whole_tool_category_keeps_the_valid_first_draft(string missing)
    {
        var good=StoredDrawing("b2");var review=good with{Shapes=good.Shapes.Where(s=>s.Kind!=missing).ToArray()};
        using var handler=new VisionResponse(StoredPlan(good),StoredPlan(review));using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(File.ReadAllBytes(Fixture("source.jpg")),"image/jpeg",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(11,result.Drawing.Shapes.Length);Assert.Equal(2,handler.Calls);Assert.Contains("chưa hoàn tất",result.Message);
    }
    private static ParkingDrawing StoredDrawing(string name)=>JsonSerializer.Deserialize<ParkingDrawing>(File.ReadAllText(Fixture(name+".json")))!;
    private static ParkingPoint[] ShapeCorners(ParkingShape s)
    {
        var a=s.Rotation*Math.PI/180;
        return(s.Outline??[new(0,0),new(1,0),new(1,1),new(0,1)]).Select(p=>new ParkingPoint(s.X+s.Width/2+(p.X-.5)*s.Width*Math.Cos(a)-(p.Y-.5)*s.Height*Math.Sin(a),s.Y+s.Height/2+(p.X-.5)*s.Width*Math.Sin(a)+(p.Y-.5)*s.Height*Math.Cos(a))).ToArray();
    }
    private static string StoredPlan(ParkingDrawing drawing)=>JsonSerializer.Serialize(new{isParkingPlan=true,complete=true,planBounds=new[]{0,0,1000,1000},objects=drawing.Shapes.Select(s=>new{kind=s.Kind,label=s.Label,evidence=s.Kind=="SPACE"?"BAY_MARKINGS":"STRUCTURE",points=(s.ControlPoints is {} controls?controls.Select(p=>new ParkingPoint(s.X+p.X*s.Width,s.Y+p.Y*s.Height)).ToArray():ShapeCorners(s)).Select(p=>new[]{p.Y/drawing.Height*1000,p.X/drawing.Width*1000}),uncertain=false}),dimensions=Array.Empty<object>()});
    [Fact]
    public async Task An_empty_region_does_not_erase_overview_bays_or_claim_complete_coverage()
    {
        var overview=JsonNode.Parse(Plan())!;overview["planBounds"]=JsonSerializer.SerializeToNode(new[]{0,0,1000,1000});
        const string empty="""{"isParkingPlan":true,"complete":true,"objects":[],"dimensions":[],"planBounds":[0,0,1000,1000]}""";
        using var handler=new VisionResponse(overview.ToJsonString(),empty,RegionPlan("Cây xanh"),RegionPlan("Mặt nước"));using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);Assert.Contains("2/3 vùng",result.Message);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));
    }
    [Theory]
    [InlineData(-1,"3/3 vùng")][InlineData(-20,"2/3 vùng")]
    public async Task Region_projection_tolerates_only_subpixel_rounding_at_the_crop_border(int coordinate,string coverage)
    {
        var overview=JsonNode.Parse(Plan())!;overview["planBounds"]=JsonSerializer.SerializeToNode(new[]{0,0,1000,1000});var local=JsonNode.Parse(RegionPlan("Đường đi"))!;local["objects"]![0]!["points"]![0]![1]=coordinate;
        using var handler=new VisionResponse(overview.ToJsonString(),local.ToJsonString(),RegionPlan("Cây xanh"),RegionPlan("Mặt nước"));using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);Assert.Contains(coverage,result.Message);
    }
    [Fact]
    public async Task An_overview_retry_leaves_two_detail_regions_and_never_exceeds_four_provider_requests()
    {
        var overview=JsonNode.Parse(Plan())!;overview["planBounds"]=JsonSerializer.SerializeToNode(new[]{0,0,1000,1000});
        using var handler=new VisionResponse(overview.ToJsonString(),overview.ToJsonString(),RegionPlan("Cây xanh"),RegionPlan("Mặt nước")){Statuses=[HttpStatusCode.ServiceUnavailable,HttpStatusCode.OK,HttpStatusCode.OK,HttpStatusCode.OK]};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);Assert.Contains("2/2 vùng",result.Message);
    }
    [Fact]
    public async Task Daily_quota_exhaustion_stops_without_retrying_or_claiming_a_review_timeout()
    {
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.TooManyRequests],DailyQuota=true};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(1,handler.Calls);Assert.Contains("trong ngày",result.Message);Assert.DoesNotContain("lượt đối chiếu",result.Message);
    }
    [Fact]
    public async Task Two_temporary_outages_can_recover_without_changing_model_or_exceeding_four_requests()
    {
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.ServiceUnavailable,HttpStatusCode.ServiceUnavailable,HttpStatusCode.OK]};
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);Assert.Contains("AI đã đối chiếu",result.Message);
    }
    [Fact]
    public async Task Persistent_overload_stops_after_two_retries_and_identifies_503_instead_of_quota()
    {
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.ServiceUnavailable]};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(3,handler.Calls);Assert.Contains("503",result.Message);Assert.DoesNotContain("hết hạn mức",result.Message);
    }
    [Fact]
    public async Task A_server_retry_after_beyond_the_read_budget_stops_without_sending_an_early_retry()
    {
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.ServiceUnavailable],RetryAfter="120"};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(1,handler.Calls);Assert.Contains("120 giây",result.Message);
    }
    [Fact]
    public async Task A_recoverable_retry_waits_for_the_server_retry_after_before_sending_again()
    {
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.ServiceUnavailable,HttpStatusCode.OK],RetryAfter="2"};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(3,handler.Calls);
        Assert.True(System.Diagnostics.Stopwatch.GetElapsedTime(handler.Started[0],handler.Started[1])>=TimeSpan.FromSeconds(2));
    }
    [Theory]
    [InlineData("date")][InlineData("seconds")][InlineData("invalid")][InlineData("past")]
    public async Task Media_transport_reads_retry_after_without_exposing_provider_error_text(string mode)
    {
        var header=mode switch{"date"=>DateTimeOffset.UtcNow.AddMinutes(1).ToString("R"),"past"=>DateTimeOffset.UtcNow.AddMinutes(-1).ToString("R"),"invalid"=>"invalid-value",_=>"60"};
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.ServiceUnavailable],RetryAfter=header};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var response=await new GeminiStructuredClient(http,"fixture-key","fixture-model",true).GenerateMediaAsync<JsonElement>("fixture","fixture","image/png",[1],new{type="OBJECT"},900,default);
        Assert.Null(response.Result);Assert.Equal(503,response.StatusCode);
        if(mode=="invalid")Assert.Null(response.RetryAfter);
        else if(mode=="past")Assert.Equal(TimeSpan.Zero,response.RetryAfter);
        else Assert.InRange(response.RetryAfter!.Value.TotalSeconds,58,60);
    }
    [Theory]
    [InlineData(400)][InlineData(401)][InlineData(403)][InlineData(404)]
    public async Task Configuration_errors_are_not_retried(int status)
    {
        using var handler=new VisionResponse(Plan()){Statuses=[(HttpStatusCode)status]};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task Regional_recognition_preserves_overview_structures_and_projects_each_crop_back_to_the_source()
    {
        var overview=JsonNode.Parse(Plan())!;overview["planBounds"]=JsonSerializer.SerializeToNode(new[]{0,0,1000,1000});
        overview["objects"]!.AsArray().Add(JsonNode.Parse("""{"kind":"RECT","label":"Khu vườn toàn cảnh","evidence":"STRUCTURE","points":[[400,100],[600,300]],"uncertain":false}"""));
        using var handler=new VisionResponse(overview.ToJsonString(),RegionPlan("Đường đi"),RegionPlan("Cây xanh"),RegionPlan("Mặt nước"));using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);Assert.Contains("3/3 vùng",result.Message);
        Assert.Contains(result.Drawing.Shapes,s=>s.Label=="Khu vườn toàn cảnh");foreach(var label in new[]{"Đường đi","Cây xanh","Mặt nước"})Assert.Contains(result.Drawing.Shapes,s=>s.Label==label);
        var bays=result.Drawing.Shapes.Where(s=>s.Kind=="SPACE").OrderBy(s=>s.Y+s.Height/2).ThenBy(s=>s.X).ToArray();Assert.Equal(8,bays.Length);Assert.Equal(8,bays.Select(s=>s.Label).Distinct().Count());
        for(var i=0;i<8;i++){Assert.InRange(bays[i].X+bays[i].Width/2,(180+i%4*190)*10-.01,(180+i%4*190)*10+.01);Assert.InRange(bays[i].Y+bays[i].Height/2,(i<4?2600:7400)-.01,(i<4?2600:7400)+.01);}
    }
    [Fact]
    public async Task A_partial_region_merge_that_overlaps_keeps_the_valid_overview_with_four_calls_maximum()
    {
        var overview=JsonNode.Parse(Plan())!;overview["planBounds"]=JsonSerializer.SerializeToNode(new[]{0,0,1000,1000});
        using var handler=new VisionResponse(overview.ToJsonString(),RegionPlan("Đường đi"),RegionPlan("Cây xanh"),RegionPlan("Mặt nước")){Statuses=[HttpStatusCode.OK,HttpStatusCode.OK,HttpStatusCode.ServiceUnavailable,HttpStatusCode.OK]};
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);Assert.Contains("2/3 vùng",result.Message);Assert.Contains("chưa hoàn tất",result.Message);
        Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));Assert.Contains(result.Drawing.Shapes,s=>s.Kind=="CURVE");
        ParkingDrawingRules.Validate(result.Drawing with{Shapes=result.Drawing.Shapes.Select(s=>s with{NeedsReview=false}).ToArray()},publishing:true);
    }
    private static string RegionPlan(string label)=>JsonSerializer.Serialize(new{isParkingPlan=true,complete=true,planBounds=new[]{0,0,1000,1000},objects=new[]{
        new{kind="SPACE",label="P-001",evidence="BAY_MARKINGS",points=new[]{new[]{100,300},new[]{100,700},new[]{300,700},new[]{300,300}},uncertain=false},
        new{kind="RECT",label,evidence="STRUCTURE",points=new[]{new[]{600,350},new[]{600,650},new[]{800,650},new[]{800,350}},uncertain=false}},dimensions=Array.Empty<object>()});
    [Fact]
    public async Task A_dense_overview_requests_bounded_detail_tiles_instead_of_requiring_every_bay_in_one_answer()
    {
        var (source,expected)=ParkingGeometryDatasetTests.Grid(17);
        const string overview="""{"isParkingPlan":true,"complete":false,"planBounds":[0,0,1000,1000],"objects":[],"dimensions":[]}""";
        using var handler=new VisionResponse(overview,StoredPlan(expected)){LocalizeGlobalPlans=true};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(source.Preview,"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);Assert.Equal(400,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));
        Assert.Equal(0,result.Drawing.RecognitionReview!.UnreadRegions);ParkingDrawingRules.ValidateSpaceSeparation(result.Drawing);
        foreach(var shape in expected.Shapes)
        {
            var actual=Assert.Single(result.Drawing.Shapes.Where(s=>s.Kind=="SPACE"&&s.Label==shape.Label));
            foreach(var p in ShapeCorners(shape))Assert.Contains(ShapeCorners(actual),q=>Math.Abs(p.X-q.X)<.01&&Math.Abs(p.Y-q.Y)<.01);
        }
    }
    [Fact]
    public async Task A_portrait_plan_splits_along_its_long_axis_and_projects_local_coordinates_without_axis_swap()
    {
        using var bitmap=new SKBitmap(400,1000);using(var canvas=new SKCanvas(bitmap)){canvas.Clear(SKColors.White);using var pen=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=2};canvas.DrawRect(10,10,380,980,pen);}
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        const string overview="""{"isParkingPlan":true,"complete":true,"planBounds":[0,0,1000,1000],"objects":[{"kind":"RECT","label":"Khung mặt bằng","evidence":"STRUCTURE","points":[[10,25],[10,975],[990,975],[990,25]],"uncertain":false}],"dimensions":[]}""";
        using var handler=new VisionResponse(overview,RegionPlan("Khu trên"),RegionPlan("Khu giữa"),RegionPlan("Khu dưới"));using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(png.ToArray(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(4,handler.Calls);var bays=result.Drawing.Shapes.Where(s=>s.Kind=="SPACE").OrderBy(s=>s.Y).ToArray();Assert.Equal(3,bays.Length);
        Assert.All(bays,s=>Assert.InRange((s.X+s.Width/2)/result.Drawing.Width,.499,.501));
        Assert.InRange((bays[0].Y+bays[0].Height/2)/result.Drawing.Height,0,.3334);Assert.InRange((bays[1].Y+bays[1].Height/2)/result.Drawing.Height,.3333,.6667);Assert.InRange((bays[2].Y+bays[2].Height/2)/result.Drawing.Height,.6666,1);
    }
    [Fact]
    public async Task A_slightly_tapered_bay_keeps_its_observed_corners_instead_of_becoming_a_resized_rectangle()
    {
        var plan=JsonNode.Parse(Plan())!;var points=plan["objects"]![0]!["points"]!;
        points[0]![1]=points[0]![1]!.GetValue<double>()+8;
        using var handler=new VisionResponse(plan.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);var bay=result.Drawing.Shapes.First(s=>s.Kind=="SPACE");Assert.NotNull(bay.Outline);
        var corners=ShapeCorners(bay);
        foreach(var p in points.AsArray())Assert.Contains(corners,c=>Math.Abs(c.X-p![1]!.GetValue<double>()*10)<.01&&Math.Abs(c.Y-p[0]!.GetValue<double>()*10)<.01);
    }
    [Theory]
    [InlineData(false,false)][InlineData(false,true)][InlineData(true,false)]
    public async Task A_mirrored_row_must_be_reread_by_AI_without_silent_rotation_or_replacing_a_valid_first_pass(bool goodFirst,bool goodReview)
    {
        var plan=JsonNode.Parse(Plan())!;
        for(var i=4;i<8;i++){
            var cx=180+(i%4)*190d;var cy=740d;var angle=Math.PI/4;
            plan["objects"]![i]!["points"]=JsonSerializer.SerializeToNode(new[]{(-90d,-50d),(90d,-50d),(90d,50d),(-90d,50d)}.Select(p=>new[]{cy+p.Item1*Math.Sin(angle)+p.Item2*Math.Cos(angle),cx+p.Item1*Math.Cos(angle)-p.Item2*Math.Sin(angle)}));
        }
        using var handler=new VisionResponse(goodFirst?Plan():plan.ToJsonString(),goodReview?Plan():plan.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        if(!goodFirst&&!goodReview)Assert.InRange(handler.Calls,3,4);else Assert.Equal(2,handler.Calls);
        if(!goodFirst&&!goodReview){Assert.Null(result.Drawing);Assert.Contains("Hướng ô",result.Message);return;}
        Assert.NotNull(result.Drawing);var bays=result.Drawing.Shapes.Where(s=>s.Kind=="SPACE").OrderBy(s=>s.Y+s.Height/2).ToArray();Assert.Equal(8,bays.Length);
        Assert.All(bays.Take(4),s=>Assert.InRange(s.Rotation%180,40,50));Assert.All(bays.Skip(4),s=>Assert.InRange(s.Rotation%180,130,140));
        Assert.Contains(goodReview?"AI đã đối chiếu":"chưa hoàn tất",result.Message);
    }
    [Fact]
    public async Task A_review_that_misclassifies_the_source_does_not_erase_a_valid_first_draft()
    {
        using var handler=new VisionResponse(Plan(),"""{"isParkingPlan":false,"complete":true,"objects":[],"dimensions":[]}""");
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));Assert.Contains("chưa hoàn tất",result.Message);Assert.Equal(2,handler.Calls);
    }
    [Fact]
    public async Task A_targeted_crop_repairs_only_the_mirrored_row_and_keeps_the_source_supported_row()
    {
        var bad=JsonNode.Parse(Plan())!;
        for(var i=4;i<8;i++)
        {
            var cx=180+(i%4)*190d;var cy=740d;var angle=Math.PI/4;
            bad["objects"]![i]!["points"]=JsonSerializer.SerializeToNode(new[]{(-90d,-50d),(90d,-50d),(90d,50d),(-90d,50d)}.Select(p=>new[]{cy+p.Item1*Math.Sin(angle)+p.Item2*Math.Cos(angle),cx+p.Item1*Math.Cos(angle)-p.Item2*Math.Sin(angle)}));
        }
        using var handler=new VisionResponse(bad.ToJsonString(),bad.ToJsonString(),Plan()){LocalizeGlobalPlans=true};
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(3,handler.Calls);
        var bays=result.Drawing.Shapes.Where(s=>s.Kind=="SPACE").OrderBy(s=>s.Y+s.Height/2).ThenBy(s=>s.X).ToArray();Assert.Equal(8,bays.Length);
        Assert.All(bays.Take(4),s=>Assert.InRange(s.Rotation%180,44.9,45.1));Assert.All(bays.Skip(4),s=>Assert.InRange(s.Rotation%180,134.9,135.1));
        for(var i=0;i<4;i++){Assert.InRange(bays[i].X+bays[i].Width/2,(180+i*190)*10-.01,(180+i*190)*10+.01);Assert.InRange(bays[i].Y+bays[i].Height/2,2599.99,2600.01);}
        using var request=JsonDocument.Parse(handler.Body);var caption=JsonNode.Parse(request.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!)!;
        Assert.True(caption["cropBounds"]![0]!.GetValue<double>()>500);Assert.Contains("Hướng ô",caption["validationProblem"]!.GetValue<string>());
        ParkingDrawingRules.ValidateSpaceSeparation(result.Drawing);
    }
    [Fact]
    public async Task Vision_builds_eight_slanted_bays_and_a_curve_without_turning_cars_or_dimensions_into_spaces()
    {
        using var transport=new VisionResponse(Plan());using var http=new HttpClient(transport){BaseAddress=new("https://fixture.invalid/")};
        var recognizer=new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance);
        var result=await recognizer.ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));
        Assert.Contains(result.Drawing.Shapes,s=>s.Kind=="CURVE"&&s.ControlPoints!.Length==3);
        Assert.All(result.Drawing.Shapes.Where(s=>s.Kind=="SPACE"),s=>{Assert.True(s.NeedsReview);Assert.True(s.Rotation is >40 and <50 or >130 and <140);});
        Assert.DoesNotContain(result.Drawing.Shapes,s=>s.Label is "car" or "dimension");
        Assert.Equal(2,transport.Calls);
    }
    [Fact]
    public async Task Vehicle_only_output_does_not_create_bays_from_car_silhouettes()
    {
        var plan=Plan().Replace("BAY_MARKINGS","VEHICLE").Replace("STRUCTURE","DIMENSION");
        using var handler=new VisionResponse(plan);using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Contains("giữ bản nháp",result.Message);
    }
    [Theory]
    [InlineData("incomplete")][InlineData("outside")][InlineData("unknown")][InlineData("crossed")]
    public async Task Invalid_ai_geometry_is_not_replaced_by_unreviewed_pixel_regions(string mode)
    {
        var plan=JsonNode.Parse(Plan())!;
        if(mode=="incomplete")plan["complete"]=false;
        if(mode=="outside")plan["objects"]![0]!["points"]![0]![0]=1200;
        if(mode=="unknown")plan["objects"]![0]!["kind"]="EXECUTE_SCRIPT";
        if(mode=="crossed"){var points=plan["objects"]![0]!["points"]!;var first=points[0]!.DeepClone();points[0]=points[1]!.DeepClone();points[1]=first;}
        using var handler=new VisionResponse(plan.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(2,handler.Calls);
    }
    [Fact]
    public async Task Review_can_repair_a_bad_initial_proposal_and_unconfigured_ai_preserves_the_draft()
    {
        var incomplete=Plan().Replace("\"complete\":true","\"complete\":false");using var handler=new VisionResponse(incomplete,Plan());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));Assert.Equal(2,handler.Calls);
        using var offlineHandler=new VisionResponse(Plan());using var offline=new HttpClient(offlineHandler){BaseAddress=new("https://fixture.invalid/")};
        var unavailable=await new GeminiParkingPlanRecognition(new(offline,"","fixture-model",false),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(unavailable.Drawing);Assert.Equal(0,offlineHandler.Calls);
    }
    [Fact]
    public async Task A_tall_page_reference_preserves_aspect_without_expanding_the_short_side_to_1200_pixels()
    {
        using var image=new SKBitmap(20,2000);using(var canvas=new SKCanvas(image)){canvas.Clear(SKColors.White);using var pen=new SKPaint{Color=SKColors.Black};canvas.DrawRect(4,40,10,1900,pen);}
        using var rendered=SKImage.FromBitmap(image);using var png=rendered.Encode(SKEncodedImageFormat.Png,100);
        using var handler=new VisionResponse(Plan());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(png.ToArray(),"image/png",1,default);
        using var request=JsonDocument.Parse(handler.Body);var data=request.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("data").GetString();
        using var reference=SKBitmap.Decode(Convert.FromBase64String(data!));Assert.Equal(20,reference.Width);Assert.Equal(2000,reference.Height);
    }
    [Fact]
    public async Task A_valid_first_pass_survives_an_unavailable_review_without_claiming_full_review()
    {
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.OK,HttpStatusCode.ServiceUnavailable,HttpStatusCode.ServiceUnavailable]};
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));Assert.Equal(4,handler.Calls);
        Assert.Contains("chưa hoàn tất",result.Message);Assert.DoesNotContain("AI đã đối chiếu",result.Message);
    }
    [Theory]
    [InlineData(429)][InlineData(503)]
    public async Task A_transient_provider_failure_is_retried_once_before_reconstruction(int status)
    {
        using var handler=new VisionResponse(Plan()){Statuses=[(HttpStatusCode)status,HttpStatusCode.OK,HttpStatusCode.OK]};
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(3,handler.Calls);Assert.Contains("AI đã đối chiếu",result.Message);
    }
    [Fact]
    public async Task First_pass_timeout_reports_reading_the_plan_rather_than_a_review_that_never_started()
    {
        using var handler=new VisionResponse(Plan()){TimeoutOnCall=1};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/"),Timeout=TimeSpan.FromMilliseconds(150)};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(1,handler.Calls);Assert.Contains("đọc sơ đồ",result.Message);Assert.DoesNotContain("lượt đối chiếu",result.Message);
    }
    [Fact]
    public async Task Review_timeout_keeps_the_valid_initial_drawing_and_reports_incomplete_review()
    {
        using var handler=new VisionResponse(Plan()){TimeoutOnCall=2};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/"),Timeout=TimeSpan.FromMilliseconds(150)};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));Assert.Equal(2,handler.Calls);Assert.Contains("chưa hoàn tất",result.Message);
    }
    [Fact]
    public async Task Retry_timeout_does_not_erase_the_original_service_unavailable_status()
    {
        using var handler=new VisionResponse(Plan()){Statuses=[HttpStatusCode.ServiceUnavailable,HttpStatusCode.OK],TimeoutOnCall=2};
        using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/"),Timeout=TimeSpan.FromMilliseconds(150)};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(2,handler.Calls);Assert.Contains("Dịch vụ AI tạm thời không sẵn sàng",result.Message);Assert.DoesNotContain("lượt đối chiếu",result.Message);
    }
    [Fact]
    public async Task Invalid_review_geometry_does_not_discard_an_existing_valid_unconfirmed_draft()
    {
        var invalid=JsonNode.Parse(Plan())!;invalid["objects"]![0]!["points"]![0]![0]=1200;
        using var handler=new VisionResponse(Plan(),invalid.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));Assert.All(result.Drawing.Shapes.Where(s=>s.Kind=="SPACE"),s=>Assert.True(s.NeedsReview));
        Assert.Contains("chưa hoàn tất",result.Message);Assert.DoesNotContain("AI đã đối chiếu",result.Message);Assert.Equal(2,handler.Calls);
    }
    [Fact]
    public async Task Configured_read_deadline_is_enforced_and_reported_before_any_review_starts()
    {
        using var handler=new VisionResponse(Plan()){TimeoutOnCall=1};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance,new(0,0,0)).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(1,handler.Calls);Assert.Contains("bước đọc sơ đồ",result.Message);Assert.DoesNotContain("lượt đối chiếu",result.Message);
    }
    [Fact]
    public async Task User_cancellation_is_not_converted_into_a_successful_or_empty_recognition()
    {
        using var handler=new VisionResponse(Plan()){TimeoutOnCall=1};using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};using var canceled=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,canceled.Token));
    }
    [Theory]
    [InlineData("gemini-4-hydrogen")][InlineData("gemini-3.5-flash")][InlineData("gemini-3.1-flash-lite")]
    public async Task Parking_and_text_calls_preserve_the_models_default_reasoning(string model)
    {
        using var handler=new VisionResponse(Plan());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};var provider=new GeminiStructuredClient(http,"fixture-key",model,true);
        var result=await new GeminiParkingPlanRecognition(provider,NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);Assert.NotNull(result.Drawing);
        using(var request=JsonDocument.Parse(handler.Body)){var config=request.RootElement.GetProperty("generationConfig");Assert.False(config.TryGetProperty("thinkingConfig",out _));}
        await provider.GenerateAsync<JsonElement>("fixture",new{},new{type="OBJECT"},default);
        using var textRequest=JsonDocument.Parse(handler.Body);Assert.False(textRequest.RootElement.GetProperty("generationConfig").TryGetProperty("thinkingConfig",out _));
    }
    [Fact]
    public async Task Details_without_specialized_tools_become_labeled_editable_blocks_beside_the_bays()
    {
        var plan=JsonNode.Parse(Plan())!;var objects=plan["objects"]!.AsArray();
        objects.Add(JsonNode.Parse("""{"kind":"OTHER","label":"Cầu đi bộ","evidence":"STRUCTURE","points":[[420,100],[420,400],[440,420],[440,100]],"uncertain":false}"""));
        objects.Add(JsonNode.Parse("""{"kind":"RECT","label":"Mặt nước","evidence":"STRUCTURE","points":[[450,500],[450,700],[550,700],[550,500]],"uncertain":false}"""));
        objects.Add(JsonNode.Parse("""{"kind":"WALL_CIRCLE","label":"Cây xanh","evidence":"STRUCTURE","points":[[450,800],[450,900],[550,900],[550,800]],"uncertain":false}"""));
        objects.Add(JsonNode.Parse("""{"kind":"OTHER","label":"","evidence":"STRUCTURE","points":[[450,100],[550,300]],"uncertain":true}"""));
        using var handler=new VisionResponse(plan.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(8,result.Drawing.Shapes.Count(s=>s.Kind=="SPACE"));
        Assert.Contains(result.Drawing.Shapes,s=>s.Kind=="RECT"&&s.Label=="Cầu đi bộ");Assert.Contains(result.Drawing.Shapes,s=>s.Kind=="RECT"&&s.Label=="Mặt nước");
        Assert.Contains(result.Drawing.Shapes,s=>s.Kind=="WALL_CIRCLE"&&s.Label=="Cây xanh");Assert.Contains(result.Drawing.Shapes,s=>s.Kind=="RECT"&&s.Label=="Chi tiết cần kiểm tra");
        Assert.All(result.Drawing.Shapes,s=>Assert.Contains(s.Kind,ParkingDrawingRules.Kinds));
        Assert.DoesNotContain(result.Drawing.Shapes,s=>s.Label is "car" or "dimension");
    }
    [Fact]
    public async Task Generic_only_plans_keep_labels_and_can_publish_without_creating_parking_spaces()
    {
        var plan=JsonNode.Parse(Plan())!;var objects=plan["objects"]!.AsArray();objects.Clear();
        objects.Add(JsonNode.Parse("""{"kind":"OTHER","label":"Vườn hoa","evidence":"STRUCTURE","points":[[100,100],[300,500]],"uncertain":false}"""));
        foreach(var evidence in new[]{"VEHICLE","DIMENSION","TEXT","UNKNOWN"})objects.Add(JsonNode.Parse($$"""{"kind":"OTHER","label":"Không dựng","evidence":"{{evidence}}","points":[[400,100],[600,500]],"uncertain":false}"""));
        using var handler=new VisionResponse(plan.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);var block=Assert.Single(result.Drawing.Shapes);Assert.Equal("RECT",block.Kind);Assert.Equal("Vườn hoa",block.Label);
        ParkingDrawingRules.Validate(result.Drawing,publishing:true);
        using var request=JsonDocument.Parse(handler.Body);Assert.Contains(request.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("properties").GetProperty("objects").GetProperty("items").GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray(),k=>k.GetString()=="OTHER");
    }
    [Theory]
    [InlineData("outside")][InlineData("crossed")][InlineData("bay")]
    public async Task Generic_blocks_still_require_valid_geometry_and_structure_evidence(string mode)
    {
        var plan=JsonNode.Parse(Plan())!;var item=plan["objects"]![0]!;item["kind"]="OTHER";item["evidence"]="STRUCTURE";
        if(mode=="outside")item["points"]![0]![0]=1200;
        if(mode=="crossed"){var p=item["points"]!;var first=p[0]!.DeepClone();p[0]=p[1]!.DeepClone();p[1]=first;}
        if(mode=="bay")item["evidence"]="BAY_MARKINGS";
        using var handler=new VisionResponse(plan.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(2,handler.Calls);
    }
    [Fact]
    public async Task Other_editor_tools_and_unconfirmed_explicit_dimensions_survive_ai_reconstruction()
    {
        var plan=JsonNode.Parse(Plan())!;var objects=plan["objects"]!.AsArray();objects.Clear();
        objects.Add(JsonNode.Parse("""{"kind":"DIVIDER","label":"","evidence":"STRUCTURE","points":[[100,100],[100,200],[300,150]],"uncertain":false}"""));
        objects.Add(JsonNode.Parse("""{"kind":"LINE","label":"","evidence":"STRUCTURE","points":[[400,100],[400,600]],"uncertain":false}"""));
        objects.Add(JsonNode.Parse("""{"kind":"WALL_CIRCLE","label":"","evidence":"STRUCTURE","points":[[600,300],[600,400],[700,400],[700,300]],"uncertain":false}"""));
        plan["dimensions"]!.AsArray().Add(JsonNode.Parse("""{"length":5,"unit":"m","points":[[400,100],[400,600]]}"""));
        using var handler=new VisionResponse(plan.ToJsonString());using var http=new HttpClient(handler){BaseAddress=new("https://fixture.invalid/")};
        var result=await new GeminiParkingPlanRecognition(new(http,"fixture-key","fixture-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance).ReadAsync(Source(),"image/png",1,default);
        Assert.NotNull(result.Drawing);Assert.Equal(new[]{"DIVIDER","LINE","WALL_CIRCLE"},result.Drawing.Shapes.Select(s=>s.Kind));Assert.False(result.Drawing.Scale!.Confirmed);Assert.Equal(5,result.Drawing.Scale.Length);
    }
    internal static string Plan()=>JsonSerializer.Serialize(new{isParkingPlan=true,complete=true,objects=Enumerable.Range(0,8).Select(i=>{
        var cx=180+(i%4)*190d;var cy=i<4?260:740;var angle=(i<4?45:135)*Math.PI/180;var points=new[]{(-90d,-50d),(90d,-50d),(90d,50d),(-90d,50d)}.Select(p=>new[]{cy+p.Item1*Math.Sin(angle)+p.Item2*Math.Cos(angle),cx+p.Item1*Math.Cos(angle)-p.Item2*Math.Sin(angle)}).ToArray();
        return new{kind="SPACE",label="",evidence="BAY_MARKINGS",points,uncertain=false};
    }).Cast<object>().Concat([new{kind="CURVE",label="",evidence="STRUCTURE",points=new[]{new[]{450d,80},new[]{550d,500},new[]{450d,920}},uncertain=false},new{kind="SPACE",label="car",evidence="VEHICLE",points=new[]{new[]{210d,130},new[]{210d,230},new[]{310d,230},new[]{310d,130}},uncertain=false},new{kind="SPACE",label="dimension",evidence="DIMENSION",points=new[]{new[]{50d,40},new[]{50d,950},new[]{60d,950},new[]{60d,40}},uncertain=false}]),dimensions=Array.Empty<object>()});
    internal static byte[] Source()
    {
        using var bitmap=new SKBitmap(800,800);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);using var pen=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=2,IsAntialias=true};
        for(var i=0;i<8;i++){canvas.Save();canvas.Translate(144+(i%4)*152,i<4?208:592);canvas.RotateDegrees(i<4?45:135);canvas.DrawRect(-72,-40,144,80,pen);canvas.DrawRoundRect(new SKRect(-60,-30,60,30),10,10,pen);canvas.DrawRect(-30,-23,55,46,pen);canvas.DrawLine(-5,-30,-5,30,pen);canvas.Restore();}
        canvas.DrawLine(40,40,760,40,pen);for(var i=0;i<6;i++)canvas.DrawLine(40+i*144,25,40+i*144,55,pen);
        using var path=new SKPath();path.MoveTo(64,360);path.QuadTo(400,440,736,360);canvas.DrawPath(path,pen);using var image=SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SKEncodedImageFormat.Png,100);return encoded.ToArray();
    }
    internal sealed class VisionResponse(params string[] plans):HttpMessageHandler
    {
        internal int Calls;internal string Body="";
        internal int TimeoutOnCall;
        internal bool DailyQuota;
        internal bool LocalizeGlobalPlans;
        internal string? RetryAfter;
        internal List<long> Started=[];
        internal HttpStatusCode[] Statuses=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var number=Interlocked.Increment(ref Calls);var body=await request.Content!.ReadAsStringAsync(ct);Body=body;
            lock(Started)Started.Add(System.Diagnostics.Stopwatch.GetTimestamp());
            if(number==TimeoutOnCall)await Task.Delay(Timeout.InfiniteTimeSpan,ct);
            var status=Statuses.Length==0?HttpStatusCode.OK:Statuses[Math.Min(number-1,Statuses.Length-1)];
            if(status!=HttpStatusCode.OK){var failure=new HttpResponseMessage(status){Content=new StringContent(DailyQuota?"""{"error":{"code":429,"details":[{"violations":[{"quotaId":"GenerateRequestsPerDayPerProjectPerModel-FreeTier"}]}]}}""":"{}")};if(RetryAfter is not null)failure.Headers.TryAddWithoutValidation("Retry-After",RetryAfter);return failure;}
            var plan=plans[Math.Min(number-1,plans.Length-1)];
            if(LocalizeGlobalPlans)
            {
                using var requestJson=JsonDocument.Parse(body);var caption=requestJson.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
                if(caption.StartsWith('{')&&JsonNode.Parse(caption)?["cropBounds"] is JsonArray bounds)
                {
                    var top=bounds[0]!.GetValue<double>();var left=bounds[1]!.GetValue<double>();var bottom=bounds[2]!.GetValue<double>();var right=bounds[3]!.GetValue<double>();
                    var value=JsonNode.Parse(plan)!;var objects=value["objects"]!.AsArray();
                    for(var i=objects.Count-1;i>=0;i--)
                    {
                        var points=objects[i]!["points"]!.AsArray();
                        if(points.Any(p=>p![0]!.GetValue<double>()<top||p[0]!.GetValue<double>()>bottom||p[1]!.GetValue<double>()<left||p[1]!.GetValue<double>()>right)){objects.RemoveAt(i);continue;}
                        foreach(var p in points){p![0]=(p[0]!.GetValue<double>()-top)*1000/(bottom-top);p[1]=(p[1]!.GetValue<double>()-left)*1000/(right-left);}
                    }
                    value["planBounds"]=JsonSerializer.SerializeToNode(new[]{0,0,1000,1000});plan=value.ToJsonString();
                }
            }
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{candidates=new[]{new{finishReason="STOP",content=new{parts=new[]{new{text=plan}}}}}}),Encoding.UTF8,"application/json")};
        }
    }
}
