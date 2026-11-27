using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.PropertyAssets.Application.Parking;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Parking;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Presentation.Controllers;

namespace PropFlow.IntegrationTests;

[Trait("Feature","FE-04-Parking-ScopeExtension")]
public sealed partial class ParkingHttpTests
{
    [Fact]
    public async Task Manager_can_import_review_publish_and_update_status_other_roles_cannot_read_sources_or_write()
    {
        await using var app=await Start();using var client=app.GetTestClient();const string root="/api/v1/parking-areas";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(root)).StatusCode);
        foreach(var role in new[]{"STAFF","RESIDENT","ACCOUNTANT","ADMIN"})
        {client.DefaultRequestHeaders.Remove("X-Role");client.DefaultRequestHeaders.Add("X-Role",role);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(root)).StatusCode);}
        client.DefaultRequestHeaders.Remove("X-Role");client.DefaultRequestHeaders.Add("X-Role","MANAGER");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(root)).StatusCode);client.DefaultRequestHeaders.Add("X-Permission","MANAGE_OPERATIONS");
        var area=(await(await client.PostAsJsonAsync(root,new CreateParkingArea("B1"))).Content.ReadFromJsonAsync<ParkingAreaView>())!;
        using var content=new MultipartFormDataContent();var file=new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/l9sAAAAASUVORK5CYII="));
        file.Headers.ContentType=new("image/png");content.Add(file,"file","plan.png");content.Add(new StringContent(area.Version.ToString()),"expectedVersion");content.Add(new StringContent("1"),"page");
        var imported=await client.PostAsync($"{root}/{area.Id}/import",content);Assert.Equal(HttpStatusCode.OK,imported.StatusCode);
        var result=(await imported.Content.ReadFromJsonAsync<ParkingImportResult>())!;Assert.True(result.Recognized);Assert.Null(result.Area.Published);area=result.Area;
        Assert.NotNull(area.Draft.RecognitionReview);Assert.Equal(1,area.Draft.RecognitionReview.SourceCheckedObjects);Assert.Contains(area.Draft.Shapes[0].Id,Assert.Single(area.Draft.RecognitionReview.Issues).ShapeIds);
        Assert.Equal(4,Assert.Single(area.Draft.Shapes).Outline!.Length);
        var trace=new ParkingShape(Guid.NewGuid(),"TRACE","",250,200,200,30,0,null,[[new(0,0),new(1,0),new(1,1),new(0,1)]]);
        var scale=new ParkingScale(new(10,10),new(110,10),10,"m");
        var reviewed=area.Draft with{Shapes=[area.Draft.Shapes[0] with{NeedsReview=true},trace],Scale=scale};
        area=(await(await client.PutAsJsonAsync($"{root}/{area.Id}/draft",new SaveParkingDrawing(area.Version,"B1",reviewed))).Content.ReadFromJsonAsync<ParkingAreaView>())!;
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"{root}/{area.Id}/publish",new PublishParkingDrawing(area.Version))).StatusCode);
        reviewed=reviewed with{Shapes=reviewed.Shapes.Select(x=>x with{NeedsReview=false}).ToArray()};
        area=(await(await client.PutAsJsonAsync($"{root}/{area.Id}/draft",new SaveParkingDrawing(area.Version,"B1",reviewed))).Content.ReadFromJsonAsync<ParkingAreaView>())!;
        var source=await client.GetAsync($"{root}/{area.Id}/sources/{area.DraftSource!.Id}");Assert.Equal(HttpStatusCode.OK,source.StatusCode);Assert.True(source.Headers.CacheControl!.NoStore);
        var published=await client.PostAsJsonAsync($"{root}/{area.Id}/publish",new PublishParkingDrawing(area.Version));Assert.Equal(HttpStatusCode.OK,published.StatusCode);
        area=(await published.Content.ReadFromJsonAsync<ParkingAreaView>())!;var stale=area.Version;var space=Assert.Single(area.Spaces);
        Assert.Equal(result.Area.Draft.Shapes[0].Outline,area.Published!.Shapes[0].Outline);
        Assert.Equal(scale,area.Published.Scale);Assert.Equal(trace.Contours![0],area.Published.Shapes.Single(x=>x.Kind=="TRACE").Contours![0]);
        var changed=await client.PutAsJsonAsync($"{root}/{area.Id}/spaces/{space.Id}",new ChangeParkingSpace(area.Version,"IN_USE","Kiểm tra thủ công"));Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PutAsJsonAsync($"{root}/{area.Id}/draft",new SaveParkingDrawing(stale,"B1",ParkingDrawing.Empty))).StatusCode);
        area=(await changed.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        area=(await(await client.PutAsJsonAsync($"{root}/{area.Id}/draft",new SaveParkingDrawing(area.Version,"B1",ParkingDrawing.Empty))).Content.ReadFromJsonAsync<ParkingAreaView>())!;
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"{root}/{area.Id}/publish",new PublishParkingDrawing(area.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"{root}/{area.Id}/history")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Role");client.DefaultRequestHeaders.Add("X-Role","RESIDENT");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"{root}/{area.Id}/sources/{result.Area.DraftSource!.Id}")).StatusCode);
        await app.StopAsync();
    }
    private static async Task<WebApplication> Start()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});builder.WebHost.UseTestServer();
        builder.Services.AddLogging(x=>x.ClearProviders());builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions,Auth>("test",_=>{});
        builder.Services.AddAuthorization(x=>x.AddPolicy(PropertyAssetsAuthorizationPolicies.Manage,p=>p.RequireRole("MANAGER").RequireClaim("permission","MANAGE_OPERATIONS")));
        builder.Services.AddRateLimiter(x=>x.AddFixedWindowLimiter("parking-recognition",o=>{o.PermitLimit=4;o.Window=TimeSpan.FromMinutes(1);}));
        var name=Guid.NewGuid().ToString();builder.Services.AddDbContext<PropertyAssetsDbContext>(x=>x.UseInMemoryDatabase(name));
        builder.Services.AddScoped<IParkingStore,EfParkingStore>();builder.Services.AddScoped<ParkingWorkspace>();builder.Services.AddSingleton(TimeProvider.System);builder.Services.AddSingleton<IParkingPlanRecognition,Reader>();
        builder.Services.AddControllers().AddApplicationPart(typeof(ParkingController).Assembly);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options=>options.CustomSchemaIds(type=>type.FullName?.Replace('+','.')??type.Name));
        var app=builder.Build();app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();app.UseSwagger();app.MapControllers();await app.StartAsync();return app;
    }
    private sealed class Reader:IParkingPlanRecognition
    {
        public Task<ParkingRecognition> ReadAsync(byte[] bytes,string type,int page,CancellationToken ct)
        {
            var shape=new ParkingShape(Guid.NewGuid(),"SPACE","P-001",30,30,70,100,0,[new(0,0),new(1,.1),new(1,1),new(0,.9)]);
            return Task.FromResult(new ParkingRecognition(new(600,400,[shape],RecognitionReview:new(1,1,0,0,[new("source_edges","Cần đối chiếu đường bao.",[shape.Id])])),"Đã dựng bản nháp."));
        }
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Request.Headers.TryGetValue("X-Role",out var role))return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new List<Claim>{new(ClaimTypes.Role,role!),new("sub","083485b5-2dcc-45cb-bae4-92fec9b89625")};
            if(Request.Headers.TryGetValue("X-Permission",out var permission))claims.Add(new("permission",permission!));
            return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }
}
