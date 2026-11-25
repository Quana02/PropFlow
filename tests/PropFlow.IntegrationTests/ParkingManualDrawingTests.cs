using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.IntegrationTests;

public sealed partial class ParkingHttpTests
{
    [Fact]
    public async Task Free_canvas_preserves_negative_and_distant_positions_and_physical_scale()
    {
        await using var app=await Start();using var client=app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Role","MANAGER");client.DefaultRequestHeaders.Add("X-Permission","MANAGE_OPERATIONS");
        const string root="/api/v1/parking-areas";
        var area=(await(await client.PostAsJsonAsync(root,new CreateParkingArea("Không gian tự do"))).Content.ReadFromJsonAsync<ParkingAreaView>())!;
        Assert.True(area.Draft.InfiniteCanvas);Assert.Equal("m",area.Draft.Scale!.Unit);
        var drawing=ParkingDrawing.Empty with{Shapes=[new(Guid.NewGuid(),"DIVIDER","",-25000,-18000,100,300),new(Guid.NewGuid(),"WALL","",30000,20000,200,100)]};
        var save=await client.PutAsJsonAsync($"{root}/{area.Id}/draft",new SaveParkingDrawing(area.Version,area.Name,drawing));Assert.Equal(HttpStatusCode.OK,save.StatusCode);area=(await save.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"{root}/{area.Id}/publish",new PublishParkingDrawing(area.Version))).StatusCode);
        var restored=(await client.GetFromJsonAsync<ParkingAreaView>($"{root}/{area.Id}"))!;Assert.True(restored.Published!.InfiniteCanvas);Assert.Equal(-25000,restored.Published.Shapes[0].X);Assert.Equal(30000,restored.Published.Shapes[1].X);Assert.Equal(drawing.Scale,restored.Published.Scale);
    }
    [Fact]
    public async Task Manual_drawing_paths_and_symbols_survive_save_publish_and_reload()
    {
        await using var app=await Start();using var client=app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Role","MANAGER");client.DefaultRequestHeaders.Add("X-Permission","MANAGE_OPERATIONS");
        const string root="/api/v1/parking-areas";
        var area=(await(await client.PostAsJsonAsync(root,new CreateParkingArea("Thủ công"))).Content.ReadFromJsonAsync<ParkingAreaView>())!;
        var line=new ParkingShape(Guid.NewGuid(),"LINE","",30,30,100,4,0,ControlPoints:[new(0,.5),new(1,.5)]);
        var curve=new ParkingShape(Guid.NewGuid(),"CURVE","",180,40,100,80,0,ControlPoints:[new(0,1),new(.5,0),new(1,1)]);
        var divider=new ParkingShape(Guid.NewGuid(),"DIVIDER","",350,100,80,140,275);
        var wall=new ParkingShape(Guid.NewGuid(),"WALL_CIRCLE","",150,210,80,80);
        var drawing=new ParkingDrawing(600,400,[line,curve,divider,wall]);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync($"{root}/{area.Id}/draft",new SaveParkingDrawing(area.Version,area.Name,drawing with{Shapes=[line with{ControlPoints=[new(0,0),new(2,1)]}]}))).StatusCode);
        var saved=await client.PutAsJsonAsync($"{root}/{area.Id}/draft",new SaveParkingDrawing(area.Version,area.Name,drawing));Assert.Equal(HttpStatusCode.OK,saved.StatusCode);
        area=(await saved.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        var published=await client.PostAsJsonAsync($"{root}/{area.Id}/publish",new PublishParkingDrawing(area.Version));Assert.Equal(HttpStatusCode.OK,published.StatusCode);
        var restored=(await client.GetFromJsonAsync<ParkingAreaView>($"{root}/{area.Id}"))!;
        Assert.Empty(restored.Spaces);Assert.Equal(4,restored.Published!.Shapes.Length);
        Assert.Equal(curve.ControlPoints,restored.Published.Shapes.Single(s=>s.Id==curve.Id).ControlPoints);
        Assert.Equal(275,restored.Published.Shapes.Single(s=>s.Id==divider.Id).Rotation);
    }
}
