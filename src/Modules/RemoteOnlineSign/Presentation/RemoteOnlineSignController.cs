using System.Security.Claims; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Http; using Microsoft.AspNetCore.Mvc; using PropFlow.Modules.RemoteOnlineSign.Application;
namespace PropFlow.Modules.RemoteOnlineSign.Presentation;
[ApiController, Route("api/v1/remote-online-sign"), Authorize(Roles="ADMIN,RESIDENT"), Produces("application/json")]
public sealed class RemoteOnlineSignController(RemoteContractService service):ControllerBase
{
 private Guid Actor=>Guid.Parse(User.FindFirstValue("sub")!); private bool IsAdmin=>User.IsInRole("ADMIN");
 [HttpGet] public async Task<IActionResult> List(CancellationToken ct)=>Ok(await service.ListAsync(Actor,IsAdmin,ct));
 [HttpGet("resident-candidates"),Authorize(Roles="ADMIN")] public Task<IActionResult> ResidentCandidates([FromQuery] string? search,CancellationToken ct)=>Run(async()=>Ok(await service.SearchEligibleResidentsAsync(search,ct)));
 [HttpGet("{id:guid}")] public Task<IActionResult> Get(Guid id,CancellationToken ct)=>Run(async()=>Ok(await service.GetAsync(Actor,IsAdmin,id,ct)));
 [HttpPost,Authorize(Roles="ADMIN")] public Task<IActionResult> Create(CreateRemoteContract request,CancellationToken ct)=>Run(async()=>Created("",await service.CreateAsync(Actor,request,ct)));
 [HttpPut("{id:guid}/signing-mode"),Authorize(Roles="ADMIN")] public Task<IActionResult> SigningMode(Guid id,SelectRemoteSigningMode request,CancellationToken ct)=>Run(async()=>Ok(await service.SelectSigningModeAsync(Actor,id,request,ct)));
 [HttpPut("{id:guid}/metadata"),Authorize(Roles="ADMIN")] public Task<IActionResult> UpdateMetadata(Guid id,UpdateRemoteContractMetadata request,CancellationToken ct)=>Run(async()=>Ok(await service.UpdateMetadataAsync(Actor,id,request,ct)));
 [HttpPut("{id:guid}/deadline"),Authorize(Roles="ADMIN")] public Task<IActionResult> RenewDeadline(Guid id,RenewRemoteDeadline request,CancellationToken ct)=>Run(async()=>Ok(await service.RenewDeadlineAsync(Actor,id,request,ct)));
 [HttpPut("{id:guid}/resident"),Authorize(Roles="ADMIN")] public Task<IActionResult> Assign(Guid id,AssignResident request,CancellationToken ct)=>Run(async()=>Ok(await service.AssignAsync(Actor,id,request,ct)));
 [HttpPut("{id:guid}/signature-fields"),Authorize(Roles="ADMIN")] public Task<IActionResult> Fields(Guid id,IReadOnlyList<SignatureFieldInput> request,CancellationToken ct)=>Run(async()=>{await service.SetFieldsAsync(Actor,id,request,ct);return NoContent();});
 [HttpPost("{id:guid}/send"),Authorize(Roles="ADMIN")] public Task<IActionResult> Send(Guid id,SendRemoteContract request,CancellationToken ct)=>Run(async()=>{await service.SendAsync(Actor,id,request,ct);return NoContent();});
 [HttpPost("{id:guid}/signatures/request"),Authorize(Roles="ADMIN,RESIDENT")] public Task<IActionResult> RequestSignature(Guid id,BeginSignature request,CancellationToken ct)=>Run(async()=>Ok(await service.BeginSigningAsync(Actor,IsAdmin,id,request,ct)));
 [HttpPost("{id:guid}/signatures/verify"),Authorize(Roles="ADMIN,RESIDENT")] public Task<IActionResult> VerifySignature(Guid id,VerifySignature request,CancellationToken ct)=>Run(async()=>Ok(await service.VerifyAndFinalizeAsync(Actor,IsAdmin,id,request,ct)));
 [HttpGet("{id:guid}/history")] public Task<IActionResult> History(Guid id,CancellationToken ct)=>Run(async()=>Ok(await service.HistoryAsync(Actor,IsAdmin,id,ct)));
 [HttpGet("{id:guid}/signature-fields")] public Task<IActionResult> Fields(Guid id,CancellationToken ct)=>Run(async()=>Ok(await service.GetFieldsAsync(Actor,IsAdmin,id,ct)));

 [HttpPost("{id:guid}/reject"),Authorize(Roles="RESIDENT")] public Task<IActionResult> Reject(Guid id,RejectRemoteContract request,CancellationToken ct)=>Run(async()=>{await service.RejectAsync(Actor,id,request.Reason,ct);return NoContent();});
 [HttpPost("{id:guid}/reassign-resident"),Authorize(Roles="ADMIN")] public Task<IActionResult> ReassignResident(Guid id,ReassignRemoteResident request,CancellationToken ct)=>Run(async()=>Ok(await service.ReassignResidentAsync(Actor,id,request,ct)));
 [HttpPost("{id:guid}/document"),Consumes("multipart/form-data"),Authorize(Roles="ADMIN")] public Task<IActionResult> Upload(Guid id,IFormFile file,CancellationToken ct)=>Run(async()=>{await using var s=file.OpenReadStream();return Ok(await service.UploadOriginalAsync(Actor,id,s,file.Length,ct));});
 [HttpGet("{id:guid}/document")] public Task<IActionResult> Download(Guid id,CancellationToken ct)=>Run(async()=>{var result=await service.DownloadOriginalAsync(Actor,IsAdmin,id,ct);return File(result.Content,"application/pdf",enableRangeProcessing:true);});
 [HttpGet("{id:guid}/document/metadata")] public Task<IActionResult> DocumentMetadata(Guid id,CancellationToken ct)=>Run(async()=>Ok(await service.GetDocumentMetadataAsync(Actor,IsAdmin,id,ct)));
 private async Task<IActionResult> Run(Func<Task<IActionResult>> action){try{return await action();}catch(RemoteContractException e){var p=new ProblemDetails{Status=e.Status,Title=e.Message};p.Extensions["code"]=e.Code;p.Extensions["traceId"]=HttpContext.TraceIdentifier;return StatusCode(e.Status,p);}}
}
