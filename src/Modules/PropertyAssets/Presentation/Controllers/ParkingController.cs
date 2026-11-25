using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.PropertyAssets.Application.Parking;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Domain.Parking;

namespace PropFlow.Modules.PropertyAssets.Presentation.Controllers;

[ApiController,Route("api/v1/parking-areas"),Authorize(Policy=PropertyAssetsAuthorizationPolicies.Manage),Produces("application/json")]
public sealed class ParkingController(ParkingWorkspace workspace):ControllerBase
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue("sub")??User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try{return Ok(await action());}
        catch(ParkingFailure failure){return Failure(failure);}
    }
    private IActionResult Failure(ParkingFailure failure)
    {var problem=new ProblemDetails{Status=failure.Status,Title=failure.Message};problem.Extensions["code"]=failure.Code;problem.Extensions["traceId"]=HttpContext.TraceIdentifier;return StatusCode(failure.Status,problem);}
    [HttpGet,ProducesResponseType(typeof(ParkingAreaSummary[]),200)]
    public Task<IActionResult> List(CancellationToken ct)=>Run(()=>workspace.ListAsync(ct));
    [HttpPost,ProducesResponseType(typeof(ParkingAreaView),200)]
    public Task<IActionResult> Create(CreateParkingArea input,CancellationToken ct)=>Run(()=>workspace.CreateAsync(input,Actor,ct));
    [HttpGet("{id:guid}"),ProducesResponseType(typeof(ParkingAreaView),200)]
    public Task<IActionResult> Get(Guid id,CancellationToken ct)=>Run(()=>workspace.GetAsync(id,ct));
    [HttpPut("{id:guid}/draft"),RequestSizeLimit(1024*1024),ProducesResponseType(typeof(ParkingAreaView),200)]
    public Task<IActionResult> Save(Guid id,SaveParkingDrawing input,CancellationToken ct)=>Run(()=>workspace.SaveAsync(id,input,Actor,ct));
    [HttpPost("{id:guid}/publish"),ProducesResponseType(typeof(ParkingAreaView),200)]
    public Task<IActionResult> Publish(Guid id,PublishParkingDrawing input,CancellationToken ct)=>Run(()=>workspace.PublishAsync(id,input,Actor,ct));
    [HttpPut("{id:guid}/spaces/{space:guid}"),ProducesResponseType(typeof(ParkingAreaView),200)]
    public Task<IActionResult> ChangeSpace(Guid id,Guid space,ChangeParkingSpace input,CancellationToken ct)=>Run(()=>workspace.ChangeSpaceAsync(id,space,input,Actor,ct));
    [HttpPost("{id:guid}/import"),Consumes("multipart/form-data"),RequestSizeLimit(ParkingWorkspace.MaxFileBytes+65536),EnableRateLimiting("parking-recognition")]
    [ProducesResponseType(typeof(ParkingImportResult),200),ProducesResponseType(413),ProducesResponseType(429)]
    // IFormFile must retain the framework's inferred FormFile binding for Swagger.
    public Task<IActionResult> Import(Guid id,IFormFile file,[FromForm]Guid expectedVersion,[FromForm]int page=1,CancellationToken ct=default)=>Run(async()=>
    {
        if(file.Length>ParkingWorkspace.MaxFileBytes)throw new ParkingFailure(413,"parking_file_too_large","Tệp sơ đồ tối đa 8 MB.");
        await using var stream=file.OpenReadStream();
        return await workspace.ImportAsync(id,expectedVersion,stream,file.FileName,file.ContentType,page,Actor,ct);
    });
    [HttpPost("{id:guid}/recognize"),EnableRateLimiting("parking-recognition"),ProducesResponseType(typeof(ParkingImportResult),200)]
    public Task<IActionResult> Recognize(Guid id,RecognizeParkingDrawing input,CancellationToken ct)=>Run(()=>workspace.RecognizeAsync(id,input.ExpectedVersion,Actor,ct));
    [HttpGet("{id:guid}/sources/{source:guid}")]
    public async Task<IActionResult> Source(Guid id,Guid source,CancellationToken ct)
    {
        try
        {
            var file=await workspace.SourceAsync(id,source,ct);
            Response.Headers.CacheControl="no-store";Response.Headers["X-Content-Type-Options"]="nosniff";
            return File(file.Content,file.ContentType);
        }
        catch(ParkingFailure failure){return Failure(failure);}
    }
    [HttpGet("{id:guid}/history"),ProducesResponseType(typeof(ParkingActionView[]),200)]
    public Task<IActionResult> History(Guid id,[FromQuery]int page=1,CancellationToken ct=default)=>Run(()=>workspace.HistoryAsync(id,page,ct));
}
