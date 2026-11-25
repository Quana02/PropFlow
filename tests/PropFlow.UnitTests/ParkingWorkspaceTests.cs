using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.PropertyAssets.Application.Parking;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Shared;

namespace PropFlow.UnitTests;

[Trait("Feature","FE-04-Parking-ScopeExtension")]
public sealed class ParkingWorkspaceTests
{
    private static readonly Guid Actor=Guid.NewGuid();
    private static ParkingShape Space(string label="P-001",double x=20,double y=20,double rotation=0)=>new(Guid.NewGuid(),"SPACE",label,x,y,60,80,rotation);
    private static ParkingDrawing Drawing(params ParkingShape[] shapes)=>new(600,400,shapes);
    [Fact]
    public void Free_canvas_accepts_signed_distant_geometry_but_keeps_numeric_safety_and_legacy_validation()
    {
        var drawing=ParkingDrawing.Empty with{Shapes=[Space(x:-20000,y:-15000),Space("P-002",30000,25000)],Scale=new(new(-20000,-15000),new(-19900,-15000),1,"m")};
        Assert.Equal(2,ParkingDrawingRules.Validate(drawing,true).Shapes.Length);
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(drawing with{InfiniteCanvas=false}));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(drawing with{Shapes=[Space(x:double.NaN)]}));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(drawing with{Shapes=[Space(x:2e9)]}));
        var legacy=JsonSerializer.Deserialize<ParkingDrawing>("""{"Width":600,"Height":400,"Shapes":[]}""")!;
        Assert.False(legacy.InfiniteCanvas);Assert.Null(legacy.Scale);Assert.NotNull(ParkingDrawingRules.Validate(legacy));
    }
    [Fact]
    public void Standalone_editor_route_keeps_manager_permission_and_has_its_own_layout()
    {
        var type=typeof(PropFlow.Web.Client.Features.Facility.Pages.ParkingEditor);
        var permission=Assert.Single(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute),true).Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
        Assert.Equal("MANAGER",permission.Roles);Assert.Equal("property-assets.manage",permission.Policy);
        var layout=Assert.Single(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Components.LayoutAttribute),true).Cast<Microsoft.AspNetCore.Components.LayoutAttribute>());
        Assert.Equal(typeof(PropFlow.Web.Client.Features.Facility.Components.ParkingEditorLayout),layout.LayoutType);
    }
    [Theory]
    [InlineData("RECT")][InlineData("SQUARE")][InlineData("DIVIDER")][InlineData("WALL")][InlineData("WALL_SQUARE")][InlineData("WALL_CIRCLE")][InlineData("ENTRANCE")][InlineData("EXIT")][InlineData("GATE")]
    public void Manual_symbols_can_be_published_without_inventing_managed_parking_spaces(string kind)
    {
        var shape=new ParkingShape(Guid.NewGuid(),kind,"",120,120,80,80,35);
        var area=new ParkingArea("Bản vẽ thủ công",Actor,DateTimeOffset.UtcNow);area.Save(area.Name,Drawing(shape),Actor,DateTimeOffset.UtcNow);area.Publish(Actor,DateTimeOffset.UtcNow);
        Assert.Equal(kind,Assert.Single(area.Published!.Shapes).Kind);Assert.Empty(area.Spaces);
    }
    [Fact]
    public void Manual_paths_preserve_control_points_and_reject_invalid_path_data()
    {
        var line=Space() with{Kind="LINE",Label="",ControlPoints=[new(0,.5),new(1,.5)]};
        var curve=line with{Id=Guid.NewGuid(),Kind="CURVE",ControlPoints=[new(0,1),new(.5,0),new(1,1)]};
        var drawing=ParkingDrawingRules.Validate(Drawing(line,curve),true);
        var restored=JsonSerializer.Deserialize<ParkingDrawing>(JsonSerializer.Serialize(drawing))!;
        Assert.Equal(curve.ControlPoints,restored.Shapes[1].ControlPoints);Assert.Equal(line.ControlPoints,restored.Shapes[0].ControlPoints);
        foreach(var invalid in new ParkingPoint[]?[]{null,[new(0,0)],[new(0,0),new(0,0)],[new(double.NaN,0),new(1,1)],[new(-.1,0),new(1,1)],[null!,new(1,1)]})
            Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(line with{ControlPoints=invalid})));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(curve with{ControlPoints=line.ControlPoints})));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(line with{Kind="RECT"})));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(line with{Outline=[new(0,0),new(1,0),new(1,1),new(0,1)]})));
        foreach(var kind in new[]{"SQUARE","WALL_SQUARE","WALL_CIRCLE"})Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(Space() with{Kind=kind})));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(),true));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(Space() with{Kind="LABEL"}),true));
    }
    [Fact]
    public void Draft_changes_do_not_change_published_drawing_and_republishing_preserves_space_state()
    {
        var area=new ParkingArea("Bãi B1",Actor,DateTimeOffset.UtcNow);var shape=Space();
        area.Save(area.Name,Drawing(shape),Actor,DateTimeOffset.UtcNow);area.Publish(Actor,DateTimeOffset.UtcNow);
        area.ChangeSpace(shape.Id,"IN_USE",Actor,DateTimeOffset.UtcNow);
        area.Save(area.Name,Drawing(shape with{X=150,Label="A-01"}),Actor,DateTimeOffset.UtcNow);
        Assert.Equal(20,Assert.Single(area.Published!.Shapes).X);Assert.Equal(150,Assert.Single(area.Draft.Shapes).X);
        area.Publish(Actor,DateTimeOffset.UtcNow);Assert.Equal("IN_USE",Assert.Single(area.Spaces).Status);
    }
    [Theory][InlineData("IN_USE")][InlineData("MAINTENANCE")]
    public void Cannot_remove_or_change_kind_of_used_or_maintenance_space(string status)
    {
        var area=new ParkingArea("B1",Actor,DateTimeOffset.UtcNow);var shape=Space();
        area.Save(area.Name,Drawing(shape),Actor,DateTimeOffset.UtcNow);area.Publish(Actor,DateTimeOffset.UtcNow);area.ChangeSpace(shape.Id,status,Actor,DateTimeOffset.UtcNow);
        area.Save(area.Name,Drawing(shape with{Kind="LANE"},Space("P-002",200)),Actor,DateTimeOffset.UtcNow);
        Assert.Equal(409,Assert.Throws<ParkingFailure>(()=>area.Publish(Actor,DateTimeOffset.UtcNow)).Status);
        area.ChangeSpace(shape.Id,"AVAILABLE",Actor,DateTimeOffset.UtcNow);area.Publish(Actor,DateTimeOffset.UtcNow);
        Assert.True(area.Spaces.Single(x=>x.Id==shape.Id).Archived);
    }
    [Fact]
    public void Publication_checks_duplicate_labels_overlap_and_rotated_geometry()
    {
        var first=Space();
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(first,Space(" p-001 ",200)),true));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(first,Space("P-002",40)),true));
        Assert.NotNull(ParkingDrawingRules.Validate(Drawing(first,Space("P-002",80)),true));
        var rotated=first with{X=100,Y=100,Width=60,Height=60,Rotation=45};
        Assert.NotNull(ParkingDrawingRules.Validate(Drawing(rotated,Space("P-002") with{X=143,Y=143,Width=60,Height=60,Rotation=45}),true));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(rotated,Space("P-002") with{X=120,Y=120,Width=60,Height=60,Rotation=45}),true));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(first with{X=0,Y=0,Rotation=45}),true));
    }
    [Fact]
    public void Bad_numbers_ids_types_and_empty_publication_are_rejected()
    {
        var valid=Space();
        foreach(var invalid in new[]{valid with{X=double.NaN},valid with{Height=double.PositiveInfinity},valid with{Id=Guid.Empty},valid with{Kind="SCRIPT"},valid with{Rotation=360},valid with{Width=-1}})
            Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(invalid)));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(valid,valid)));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(),true));
        Assert.NotNull(ParkingDrawingRules.Validate(Drawing()));
    }
    [Fact]
    public async Task Store_saves_publication_space_state_revision_and_audit_and_rejects_stale_writes()
    {
        var scenario=new Scenario();var area=await scenario.Call(x=>x.CreateAsync(new("B1"),Actor,default));var shape=Space();
        area=await scenario.Call(x=>x.SaveAsync(area.Id,new(area.Version,area.Name,Drawing(shape)),Actor,default));
        var version=area.Version;area=await scenario.Call(x=>x.PublishAsync(area.Id,new(area.Version),Actor,default));
        Assert.Single(area.Spaces);Assert.Equal("AVAILABLE",area.Spaces[0].Status);
        var stale=await Assert.ThrowsAsync<ParkingFailure>(()=>scenario.Call(x=>x.SaveAsync(area.Id,new(version,"old",Drawing()),Actor,default)));
        Assert.Equal(409,stale.Status);
        area=await scenario.Call(x=>x.ChangeSpaceAsync(area.Id,shape.Id,new(area.Version,"MAINTENANCE","Sơn lại"),Actor,default));
        Assert.Equal("MAINTENANCE",Assert.Single(area.Spaces).Status);
        await using var db=scenario.Database();Assert.Equal(1,await db.Set<ParkingRevision>().CountAsync());Assert.Equal(4,await db.Set<ParkingAction>().CountAsync());
        Assert.Equal("B1",(await scenario.Call(x=>x.GetAsync(area.Id,default))).Name);
    }
    [Fact]
    public async Task AI_outage_keeps_existing_draft_and_private_source_without_changing_published_map()
    {
        var scenario=new Scenario();var area=await scenario.Call(x=>x.CreateAsync(new("B1"),Actor,default));var shape=Space();
        area=await scenario.Call(x=>x.SaveAsync(area.Id,new(area.Version,area.Name,Drawing(shape)),Actor,default));
        area=await scenario.Call(x=>x.PublishAsync(area.Id,new(area.Version),Actor,default));
        var result=await scenario.Call(x=>x.ImportAsync(area.Id,area.Version,new MemoryStream(Png),"../bãi.png","image/png",1,Actor,default));
        Assert.False(result.Recognized);Assert.Equal(shape.Id,Assert.Single(result.Area.Draft.Shapes).Id);Assert.Equal(shape.Id,Assert.Single(result.Area.Published!.Shapes).Id);
        Assert.NotNull(result.Area.DraftSource);Assert.Null(result.Area.PublishedSource);
        var file=await scenario.Call(x=>x.SourceAsync(area.Id,result.Area.DraftSource!.Id,default));Assert.Equal(Png,file.Content);Assert.Equal("bãi.png",file.FileName);
        Assert.Equal(404,(await Assert.ThrowsAsync<ParkingFailure>(()=>scenario.Call(x=>x.SourceAsync(Guid.NewGuid(),file.Id,default)))).Status);
        var retry=await scenario.Call(x=>x.RecognizeAsync(area.Id,result.Area.Version,Actor,default));Assert.False(retry.Recognized);
        await using var db=scenario.Database();Assert.Equal(1,await db.Set<ParkingSource>().CountAsync());
    }
    [Fact]
    public async Task Overlapping_AI_result_cannot_replace_the_existing_draft_or_published_map()
    {
        var scenario=new Scenario();var area=await scenario.Call(x=>x.CreateAsync(new("B1"),Actor,default));var shape=Space();
        area=await scenario.Call(x=>x.SaveAsync(area.Id,new(area.Version,area.Name,Drawing(shape)),Actor,default));
        area=await scenario.Call(x=>x.PublishAsync(area.Id,new(area.Version),Actor,default));
        scenario.Recognition.Result=new(Drawing(shape,shape with{Id=Guid.NewGuid(),Label="P-002",X=shape.X+10}),"AI đề xuất bản mới.");
        var imported=await scenario.Call(x=>x.ImportAsync(area.Id,area.Version,new MemoryStream(Png),"source.png","image/png",1,Actor,default));
        Assert.False(imported.Recognized);Assert.Equal(shape,Assert.Single(imported.Area.Draft.Shapes));Assert.Equal(shape,Assert.Single(imported.Area.Published!.Shapes));
        var retry=await scenario.Call(x=>x.RecognizeAsync(area.Id,imported.Area.Version,Actor,default));
        Assert.False(retry.Recognized);Assert.Equal(imported.Area.Version,retry.Area.Version);Assert.Equal(shape,Assert.Single(retry.Area.Draft.Shapes));
        Assert.Contains("giữ bản nháp",retry.Message);
    }
    [Fact]
    public async Task Concurrent_edit_while_AI_is_reading_is_not_overwritten_and_does_not_leave_an_orphan_source()
    {
        var scenario=new Scenario();var area=await scenario.Call(x=>x.CreateAsync(new("B1"),Actor,default));
        scenario.Recognition.Before=async()=>{await scenario.Call(x=>x.SaveAsync(area.Id,new(area.Version,"Newer edit",Drawing(Space())),Actor,default));};
        var failure=await Assert.ThrowsAsync<ParkingFailure>(()=>scenario.Call(x=>x.ImportAsync(area.Id,area.Version,new MemoryStream(Png),"source.png","image/png",1,Actor,default)));
        Assert.Equal(409,failure.Status);await using var db=scenario.Database();Assert.Empty(await db.Set<ParkingSource>().ToArrayAsync());
        Assert.Equal("Newer edit",(await scenario.Call(x=>x.GetAsync(area.Id,default))).Name);
    }
    [Fact]
    public async Task File_validation_rejects_mime_spoofing_and_oversized_input()
    {
        await Assert.ThrowsAsync<ParkingFailure>(()=>ParkingWorkspace.ReadFileAsync(new MemoryStream("<svg>"u8.ToArray()),"image/png",default));
        await Assert.ThrowsAsync<ParkingFailure>(()=>ParkingWorkspace.ReadFileAsync(new MemoryStream(Png),"image/svg+xml",default));
        Assert.Equal(413,(await Assert.ThrowsAsync<ParkingFailure>(()=>ParkingWorkspace.ReadFileAsync(new MemoryStream(new byte[ParkingWorkspace.MaxFileBytes+1]),"image/png",default))).Status);
        Assert.Equal(Png,await ParkingWorkspace.ReadFileAsync(new MemoryStream(Png),"image/png",default));
    }
    private static byte[] Png=>Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAABQAAAAUCAIAAAAC64paAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAfSURBVDhPY/hPAWBAFyAFjGomEYxqJhGMaiYRDFHNAOBEq41AB5EfAAAAAElFTkSuQmCC");
    [Fact]
    public async Task Obsolete_grid_only_AI_output_does_not_overwrite_a_draft()
    {
        var plan=new{isParkingPlan=true,regions=new[]{
            new{kind="SPACE",label="",corners=new[]{new[]{125d,133},new[]{75d,458},new[]{262.5,475d},new[]{312.5,150d}},rows=2,columns=10},
            new{kind="SPACE",label="",corners=new[]{new[]{125d,625},new[]{125d,875},new[]{375d,875},new[]{375d,625}},rows=4,columns=2},
            new{kind="SPACE",label="",corners=new[]{new[]{575d,150},new[]{500d,858},new[]{775d,875},new[]{850d,166}},rows=2,columns=8}},dimensions=Array.Empty<object>()};
        var envelope=new{candidates=new[]{new{finishReason="STOP",content=new{parts=new[]{new{text=JsonSerializer.Serialize(plan)}}}}}};
        var reader=new GeminiParkingPlanRecognition(new(new HttpClient(new CaptureProvider(JsonSerializer.Serialize(envelope))){BaseAddress=new("https://generativelanguage.googleapis.com/v1beta/")},"test-only","test-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance);
        var result=await reader.ReadAsync(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-46.png")),"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Contains("giữ nguyên",result.Message);
    }
    [Fact]
    public void Convex_outlines_use_all_edges_for_overlap_and_reject_crossed_or_nonfinite_vertices()
    {
        var first=Space() with {X=100,Y=100,Width=100,Height=100,Outline=[new(0,0),new(1,.8),new(1,1),new(0,.2)]};
        var second=first with {Id=Guid.NewGuid(),Label="P-002",Y=120};
        Assert.NotNull(ParkingDrawingRules.Validate(Drawing(first,second),true)); // Shared sloping edge.
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(first,second with{Y=119}),true));
        foreach(var outline in new ParkingPoint[][]{[new(0,0),new(1,1),new(1,0),new(0,1)],[new(0,0),new(1,0),new(1,1),new(double.NaN,1)],[new(0,0),new(1,0),new(1,1),new(-.1,1)]})
            Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(first with{Outline=outline})));
        var json=JsonSerializer.Serialize(Drawing(first));
        Assert.Equal(first.Outline,JsonSerializer.Deserialize<ParkingDrawing>(json)!.Shapes[0].Outline);
    }
    [Fact]
    public async Task Obsolete_annotation_output_cannot_bypass_semantic_geometry_validation()
    {
        var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-46.png"));
        var envelope=new{candidates=new[]{new{finishReason="STOP",content=new{parts=new[]{new{text=JsonSerializer.Serialize(new{regions=new[]{new{id="C001",kind="SPACE",labelText="A-01",uncertain=false,x=99999},new{id="not-a-source-id",kind="SPACE",labelText="INVENTED",uncertain=false,x=99999}},dimensions=Array.Empty<object>()})}}}}}};
        var handler=new CaptureProvider(JsonSerializer.Serialize(envelope));var result=await Reader(handler).ReadAsync(bytes,"image/png",1,default);
        Assert.Null(result.Drawing);
        using var request=JsonDocument.Parse(handler.Body!);Assert.Equal("image/png",request.RootElement.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inlineData").GetProperty("mimeType").GetString());
        Assert.True(request.RootElement.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("properties").GetProperty("objects").GetProperty("items").GetProperty("properties").TryGetProperty("points",out _));
    }
    [Theory][InlineData(503)][InlineData(400)]
    public async Task Provider_outage_preserves_existing_draft_instead_of_claiming_pixel_regions_are_bays(int status)
    {
        var handler=new FailedProvider(status);var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-46.png"));
        var result=await Reader(handler).ReadAsync(bytes,"image/png",1,default);
        Assert.Null(result.Drawing);Assert.Equal(status>=500?3:1,handler.Calls);Assert.Contains(status>=500?"Dịch vụ AI tạm thời không sẵn sàng":"AI chưa",result.Message);
        var local=ParkingPlanGeometry.Read(bytes,"image/png",1,default).Drawing;Assert.Equal(46,local.Shapes.Count(x=>x.Kind=="SPACE"));Assert.All(local.Shapes.Where(x=>x.Kind=="SPACE"),x=>Assert.True(x.NeedsReview));
    }
    [Fact]
    public async Task Blank_input_cannot_be_turned_into_a_fake_parking_plan_by_the_provider()
    {var handler=new FailedProvider(200);Assert.Null((await Reader(handler).ReadAsync(Png,"image/png",1,default)).Drawing);Assert.Equal(0,handler.Calls);}
    [Fact]
    public async Task Unconfigured_AI_preserves_draft_and_source_decoder_rejects_truncated_images()
    {
        var handler=new FailedProvider(503);var reader=new GeminiParkingPlanRecognition(new(new HttpClient(handler){BaseAddress=new("https://generativelanguage.googleapis.com/v1beta/")},"","test-model",false),NullLogger<GeminiParkingPlanRecognition>.Instance);
        var bytes=await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-46.png"));
        var result=await reader.ReadAsync(bytes,"image/png",1,default);Assert.Null(result.Drawing);Assert.Equal(0,handler.Calls);Assert.Equal(46,ParkingPlanGeometry.Read(bytes,"image/png",1,default).Drawing.Shapes.Count(x=>x.Kind=="SPACE"));
        Assert.Equal(400,(await Assert.ThrowsAsync<ParkingFailure>(()=>reader.ReadAsync(bytes.Take(24).ToArray(),"image/png",1,default))).Status);
    }
    [Fact]
    public void PDF_geometry_reads_only_the_selected_page()
    {
        using var pdf=new PdfSharp.Pdf.PdfDocument();pdf.AddPage();var second=pdf.AddPage();second.Width=PdfSharp.Drawing.XUnit.FromPoint(600);second.Height=PdfSharp.Drawing.XUnit.FromPoint(300);
        using(var graphics=PdfSharp.Drawing.XGraphics.FromPdfPage(second))for(var i=0;i<3;i++)graphics.DrawRectangle(PdfSharp.Drawing.XPens.Black,50+i*120,50,100,70);
        using var buffer=new MemoryStream();pdf.Save(buffer,false);var bytes=buffer.ToArray();
        Assert.Empty(ParkingPlanGeometry.Read(bytes,"application/pdf",1,default).Drawing.Shapes);
        var result=ParkingPlanGeometry.Read(bytes,"application/pdf",2,default);Assert.Equal(3,result.Drawing.Shapes.Count(x=>x.Kind=="SPACE"));Assert.Equal(2d,result.Drawing.Width/(double)result.Drawing.Height,3);
        Assert.Equal(400,Assert.Throws<ParkingFailure>(()=>ParkingPlanGeometry.Read(bytes,"application/pdf",3,default)).Status);
    }
    [Fact]
    public void Scale_and_trace_validation_reject_invalid_units_coordinates_and_injected_paths()
    {
        var drawing=Drawing(Space());
        Assert.NotNull(ParkingDrawingRules.Validate(drawing with{Scale=new(new(10,10),new(110,10),10,"m")}));
        foreach(var scale in new[]{new ParkingScale(new(10,10),new(10,10),10,"m"),new(new(10,10),new(110,10),0,"m"),new(new(10,10),new(110,10),10,"unknown"),new(new(10,10),new(double.NaN,10),10,"m")})
            Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(drawing with{Scale=scale}));
        var trace=Space() with{Kind="TRACE",Contours=[[new(0,0),new(1,0),new(1,1),new(0,1)]]};
        Assert.NotNull(ParkingDrawingRules.Validate(Drawing(trace)));Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(trace with{Contours=[[new(0,0),new(double.NaN,0),new(1,1)]]})));
        Assert.Throws<ParkingFailure>(()=>ParkingDrawingRules.Validate(Drawing(trace with{Kind="SPACE"})));
    }
    private static GeminiParkingPlanRecognition Reader(HttpMessageHandler handler)=>new(new(new HttpClient(handler){BaseAddress=new("https://generativelanguage.googleapis.com/v1beta/")},"test-only","test-model",true),NullLogger<GeminiParkingPlanRecognition>.Instance);
    [Fact]
    public void Vehicle_symbols_inside_stalls_are_not_reconstructed_as_additional_stalls()
    {
        using var bitmap=new SkiaSharp.SKBitmap(600,250);using var canvas=new SkiaSharp.SKCanvas(bitmap);canvas.Clear(SkiaSharp.SKColors.White);
        using var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,IsStroke=true,StrokeWidth=2};
        for(var i=0;i<3;i++)
        {
            var x=100+i*120;canvas.DrawRect(x,30,120,160,pen);canvas.DrawRoundRect(new SkiaSharp.SKRect(x+25,55,x+95,165),10,10,pen);
            canvas.DrawRect(x+35,70,50,20,pen);canvas.DrawRect(x+35,125,50,20,pen);
        }
        using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var png=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        var drawing=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default).Drawing;
        Assert.Equal(3,drawing.Shapes.Count(s=>s.Kind=="SPACE"));Assert.DoesNotContain(drawing.Shapes,s=>s.Contours is not null||s.Kind=="TRACE");
    }
    [Fact]
    public async Task Reconstruction_contains_independent_stall_objects_and_no_photographic_trace_objects()
    {
        var result=ParkingPlanGeometry.Read(await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory,"Fixtures","parking-46.png")),"image/png",1,default);
        Assert.DoesNotContain(result.Drawing.Shapes,x=>x.Kind=="TRACE"||x.Contours is not null);
        var stalls=result.Drawing.Shapes.Where(x=>x.Kind=="SPACE").ToArray();Assert.Equal(46,stalls.Length);Assert.Equal(46,stalls.Select(x=>x.Id).Distinct().Count());Assert.All(stalls,x=>Assert.Equal(4,x.Outline!.Length));
    }
    [Fact]
    public void Small_stall_banks_with_scan_gaps_are_reconstructed_as_full_cells()
    {
        using var bitmap=new SkiaSharp.SKBitmap(600,300);using var canvas=new SkiaSharp.SKCanvas(bitmap);canvas.Clear(SkiaSharp.SKColors.White);
        using var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,IsStroke=true,StrokeWidth=1};
        for(var i=0;i<4;i++)canvas.DrawRect(60+i*65,25,60,80,pen);
        foreach(var x in new[]{70,130})
        {
            canvas.DrawLine(x,150,x,220,pen);canvas.DrawLine(x+26,150,x+26,220,pen);
            for(var i=0;i<=7;i++)
            {
                var y=150+i*10;canvas.DrawLine(x,y,x+10,y,pen);canvas.DrawLine(x+13,y,x+26,y,pen);
            }
        }
        using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var png=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        var drawing=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default).Drawing;
        var spaces=drawing.Shapes.Where(s=>s.Kind=="SPACE").ToArray();Assert.Equal(18,spaces.Length);
        Assert.Equal(14,spaces.Count(s=>s.Y>drawing.Height*.45&&s.Width>s.Height*1.8));
        Assert.NotNull(ParkingDrawingRules.Validate(drawing with{Shapes=drawing.Shapes.Select(s=>s with{NeedsReview=false}).ToArray()},true));
    }
    [Fact]
    public void A_row_with_small_scan_jitter_gets_consistent_sizes_without_adding_stalls()
    {
        using var bitmap=new SkiaSharp.SKBitmap(500,250);using var canvas=new SkiaSharp.SKCanvas(bitmap);canvas.Clear(SkiaSharp.SKColors.White);
        using var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,IsStroke=true,StrokeWidth=1};
        for(var i=0;i<6;i++){var jitter=i%3-1;canvas.DrawRect(60+i*52,40+jitter,44+jitter*2,90+jitter*2,pen);}
        using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var png=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        var drawing=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default).Drawing;var spaces=drawing.Shapes.Where(s=>s.Kind=="SPACE").OrderBy(s=>s.X).ToArray();
        Assert.Equal(6,spaces.Length);
        Assert.True(spaces.Max(s=>s.Width)-spaces.Min(s=>s.Width)<spaces.Average(s=>s.Width)*.015,"Ô cùng dãy cần có chiều rộng đồng đều sau loại nhiễu quét.");
        Assert.True(spaces.Max(s=>s.Height)-spaces.Min(s=>s.Height)<spaces.Average(s=>s.Height)*.015);
        Assert.All(spaces,s=>Assert.True(s.NeedsReview));
    }
    [Fact]
    public void Row_alignment_preserves_different_size_families_and_real_aisle_gaps()
    {
        using var bitmap=new SkiaSharp.SKBitmap(640,380);using var canvas=new SkiaSharp.SKCanvas(bitmap);canvas.Clear(SkiaSharp.SKColors.White);
        using var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,IsStroke=true,StrokeWidth=1};
        foreach(var x in new[]{30,80,130,350,400,450})canvas.DrawRect(x,40,42,80,pen);
        foreach(var x in new[]{40,150,260})canvas.DrawRect(x,210,76,110,pen);
        using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var png=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        var drawing=ParkingPlanGeometry.Read(png.ToArray(),"image/png",1,default).Drawing;var spaces=drawing.Shapes.Where(s=>s.Kind=="SPACE").ToArray();
        var upper=spaces.Where(s=>s.Y<drawing.Height*.4).ToArray();var lower=spaces.Where(s=>s.Y>drawing.Height*.5).ToArray();
        Assert.Equal(9,spaces.Length);Assert.Equal(6,upper.Length);Assert.Equal(3,lower.Length);
        Assert.True(lower.Average(s=>s.Width)>upper.Average(s=>s.Width)*1.6);
        Assert.DoesNotContain(upper,s=>(s.X+s.Width/2)/drawing.Width>200d/640&&(s.X+s.Width/2)/drawing.Width<330d/640);
        Assert.NotNull(ParkingDrawingRules.Validate(drawing with{Shapes=drawing.Shapes.Select(s=>s with{NeedsReview=false}).ToArray()},true));
    }
    [Fact]
    public void JPEG_orientation_is_applied_before_extracting_source_coordinates()
    {
        using var bitmap=new SkiaSharp.SKBitmap(120,80);using var canvas=new SkiaSharp.SKCanvas(bitmap);canvas.Clear(SkiaSharp.SKColors.White);using var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,IsStroke=true,StrokeWidth=2};
        for(var i=0;i<3;i++)canvas.DrawRect(10+i*30,10,20,50,pen);
        using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg,100);var jpeg=encoded.ToArray();
        byte[] exif=[255,225,0,34,69,120,105,102,0,0,73,73,42,0,8,0,0,0,1,0,18,1,3,0,1,0,0,0,6,0,0,0,0,0,0,0];
        var result=ParkingPlanGeometry.Read([..jpeg.AsSpan(0,2).ToArray(),..exif,..jpeg.AsSpan(2).ToArray()],"image/jpeg",1,default);
        Assert.Equal(80,result.PixelWidth);Assert.Equal(120,result.PixelHeight);Assert.Equal(3,result.Drawing.Shapes.Count(x=>x.Kind=="SPACE"));
    }
    [Fact]
    public void Overlapping_source_lines_and_internal_symbols_do_not_produce_overlapping_space_proposals()
    {
        using var bitmap=new SkiaSharp.SKBitmap(600,300);using var canvas=new SkiaSharp.SKCanvas(bitmap);canvas.Clear(SkiaSharp.SKColors.White);using var pen=new SkiaSharp.SKPaint{Color=SkiaSharp.SKColors.Black,IsStroke=true,StrokeWidth=2};
        for(var row=0;row<2;row++)for(var column=0;column<3;column++)canvas.DrawRect(10+column*75,10+row*100,70,90,pen);
        var random=new Random(17);for(var i=0;i<50;i++)canvas.DrawRect(random.Next(300,530),random.Next(20,240),random.Next(20,60),random.Next(15,50),pen);
        using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);
        var drawing=ParkingPlanGeometry.Read(encoded.ToArray(),"image/png",1,default).Drawing;
        Assert.True(drawing.Shapes.Count(x=>x.Kind=="SPACE")>=6);
        Assert.NotNull(ParkingDrawingRules.Validate(drawing with{Shapes=drawing.Shapes.Select(x=>x with{NeedsReview=false}).ToArray()},true));
    }
    [Fact]
    public async Task Saved_source_contours_scale_and_review_flags_survive_publication_round_trip()
    {
        var scenario=new Scenario();var area=await scenario.Call(x=>x.CreateAsync(new("B1"),Actor,default));
        var shape=Space() with{NeedsReview=true};var trace=Space() with{Id=Guid.NewGuid(),Kind="TRACE",Label="",X=250,Contours=[[new(0,0),new(1,0),new(1,1),new(0,1)]]};
        var drawing=Drawing(shape,trace) with{Scale=new(new(20,20),new(120,20),10,"m")};
        area=await scenario.Call(x=>x.SaveAsync(area.Id,new(area.Version,"B1",drawing),Actor,default));
        await Assert.ThrowsAsync<ParkingFailure>(()=>scenario.Call(x=>x.PublishAsync(area.Id,new(area.Version),Actor,default)));
        area=await scenario.Call(x=>x.SaveAsync(area.Id,new(area.Version,"B1",drawing with{Shapes=[shape with{NeedsReview=false},trace]}),Actor,default));
        area=await scenario.Call(x=>x.PublishAsync(area.Id,new(area.Version),Actor,default));
        Assert.Equal(drawing.Scale,area.Published!.Scale);Assert.Equal(trace.Contours![0],area.Published.Shapes.Single(x=>x.Kind=="TRACE").Contours![0]);Assert.False(area.Published.Shapes.Single(x=>x.Kind=="SPACE").NeedsReview);
    }
    private sealed class Scenario
    {
        private readonly string name=Guid.NewGuid().ToString();public Recognizer Recognition {get;}=new();
        public PropertyAssetsDbContext Database()=>new(new DbContextOptionsBuilder<PropertyAssetsDbContext>().UseInMemoryDatabase(name).Options);
        public async Task<T> Call<T>(Func<ParkingWorkspace,Task<T>> run){await using var db=Database();return await run(new(new EfParkingStore(db),Recognition,TimeProvider.System));}
    }
    private sealed class Recognizer:IParkingPlanRecognition
    {public Func<Task>? Before;public ParkingRecognition Result {get;set;}=new(null,"AI chưa sẵn sàng.");public async Task<ParkingRecognition> ReadAsync(byte[] content,string type,int page,CancellationToken ct){if(Before is not null)await Before();return Result;}}
    private sealed class CaptureProvider(string response):HttpMessageHandler
    {public string? Body;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Body=await request.Content!.ReadAsStringAsync(ct);return new(HttpStatusCode.OK){Content=new StringContent(response,Encoding.UTF8,"application/json")};}}
    private sealed class FailedProvider(int status):HttpMessageHandler
    {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent("{}",Encoding.UTF8,"application/json")});}}
}
