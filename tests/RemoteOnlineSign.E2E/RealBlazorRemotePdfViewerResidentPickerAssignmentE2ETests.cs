using System.Text.Json;
using Microsoft.Playwright;

namespace PropFlow.RemoteOnlineSign.E2E;

public sealed partial class RealBlazorRemotePdfViewerE2ETests
{
    [Fact]
    public async Task Resident_picker_is_hidden_for_assigned_resident()
    {
        var residentUser=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000051"); var page=await context!.NewPageAsync(); var diagnostics=new List<string>(); var network=new List<string>(); var candidateRequests=0; var assignmentRequests=0;
        page.PageError += (_, error)=>diagnostics.Add("pageerror: "+error); page.Console += (_, message)=>{if(message.Type=="error")diagnostics.Add($"console: {message.Text}; location={message.Location}");}; page.Response += (_,response)=>{if(response.Status>=400||new Uri(response.Url).AbsolutePath.StartsWith("/api/",StringComparison.Ordinal))network.Add($"response {response.Status} {response.Request.Method} {response.Request.ResourceType} {response.Url}");}; page.RequestFailed += (_,request)=>network.Add($"failed {request.Method} {request.ResourceType} {request.Url} {request.Failure}");
        await page.AddInitScriptAsync("window.__remotePdfTelemetry={stage:'bootstrap',events:[]};"); await InstallApiBoundaryAsync(page);
        await page.RouteAsync("**/api/v1/auth/refresh", route=>route.FulfillAsync(JsonResponse(new{accessToken="a.eyJzaWQiOiIwMDAwMDAwMC0wMDAwLTAwMDAtMDAwMC0wMDAwMDAwMDAwMDAifQ.c",expiresAt=DateTimeOffset.UtcNow.AddMinutes(20),user=new{id=residentUser,username="e2e-resident",displayName="E2E Resident",email="resident@example.test",phoneNumber=(string?)null,status="ACTIVE",emailVerified=true,role="RESIDENT",permissions=Array.Empty<string>(),avatarVersion=(Guid?)null}})));
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}", route=>route.FulfillAsync(JsonResponse(new{id=contractId,creatorId=adminId,residentId=residentUser,residentName="E2E Resident",residentCode="RS-RES",status="PENDING_RESIDENT_SIGNATURE",signingMode="PRE_SIGNED_INVESTOR",deadline=(DateTimeOffset?)null,revision=1})));
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/api/v1/remote-online-sign/resident-candidates(?:\?.*)?$"), route=>{candidateRequests++;return route.FulfillAsync(JsonResponse(Array.Empty<object>()));});
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}/resident", route=>{assignmentRequests++;return route.FulfillAsync(new(){Status=403,ContentType="application/problem+json",Body="{\"title\":\"forbidden\"}"});});
        await page.GotoAsync($"https://127.0.0.1:{port}/remote-contracts/{contractId}",new(){WaitUntil=WaitUntilState.DOMContentLoaded}); await page.Locator(".remote-pdf-stage canvas").WaitForAsync(new(){Timeout=15_000});
        Assert.Contains("E2E Resident",await page.Locator("body").InnerTextAsync()); Assert.Equal(0,await page.Locator("input[placeholder='Tên, mã cư dân hoặc email']").CountAsync()); Assert.Equal(0,await page.GetByRole(AriaRole.Button,new(){Name="Gán cư dân",Exact=true}).CountAsync()); Assert.Equal(0,candidateRequests); Assert.Equal(0,assignmentRequests); Assert.True(diagnostics.Count==0,"Actor=RESIDENT; user="+residentUser+"; state=PENDING_RESIDENT_SIGNATURE; network="+string.Join(" | ",network)+"; diagnostics="+string.Join(" | ",diagnostics));
    }
    [Fact]
    public async Task Resident_picker_handles_eligibility_change_and_recovers_assignment()
    {
        var setup = await NewPickerPageAsync(); var page = setup.Page; var diagnostics = setup.Diagnostics;
        var aUser=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000031"); var bUser=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000032"); var aResident=Guid.Parse("bbbbbbbb-0000-0000-0000-000000000031"); var bResident=Guid.Parse("bbbbbbbb-0000-0000-0000-000000000032"); var assigned=false; var puts=new List<Guid>(); var expectedErrors=0; var expected400Responses=new List<string>();
        page.Response += (_, response)=>{var uri=new Uri(response.Url);if(response.Status==400&&response.Request.Method=="PUT"&&uri.AbsolutePath=="/api/v1/remote-online-sign/"+contractId+"/resident")expected400Responses.Add(response.Url);};
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/api/v1/remote-online-sign/resident-candidates(?:\?.*)?$"), route => { var term=System.Web.HttpUtility.ParseQueryString(new Uri(route.Request.Url).Query)["search"]; return route.FulfillAsync(JsonResponse(term=="b" ? new[]{new{userId=bUser,residentId=bResident,fullName="Resident B",residentCode="RS-B",apartments=Array.Empty<string>()}} : new[]{new{userId=aUser,residentId=aResident,fullName="Resident A",residentCode="RS-A",apartments=Array.Empty<string>()}})); });
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}/resident", async route => { using var body=JsonDocument.Parse(route.Request.PostData??"{}"); var id=body.RootElement.GetProperty("residentUserId").GetGuid(); puts.Add(id); if(id==aUser){expectedErrors++;await route.FulfillAsync(new(){Status=400,ContentType="application/problem+json",Body="{\"title\":\"Cư dân không còn đủ điều kiện ký\",\"code\":\"resident_not_eligible\"}"});return;} assigned=true;await route.FulfillAsync(JsonResponse(new{id=contractId,creatorId=adminId,residentId=bUser,residentName="Resident B",residentCode="RS-B",status="DRAFT",signingMode="INVESTOR_AND_RESIDENT",deadline=(DateTimeOffset?)null,revision=1})); });
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}", route=>route.FulfillAsync(JsonResponse(new{id=contractId,creatorId=adminId,residentId=assigned?bUser:(Guid?)null,residentName=assigned?"Resident B":null,residentCode=assigned?"RS-B":null,status="DRAFT",signingMode="INVESTOR_AND_RESIDENT",deadline=(DateTimeOffset?)null,revision=1})));
        var input=page.Locator("input[placeholder='Tên, mã cư dân hoặc email']"); await input.FillAsync("a"); await page.GetByText("Resident A",new(){Exact=false}).ClickAsync(); await page.GetByRole(AriaRole.Button,new(){Name="Gán cư dân",Exact=true}).ClickAsync(); await page.GetByText("Cư dân không còn đủ điều kiện ký",new(){Exact=false}).WaitForAsync(); Assert.False(assigned); Assert.Equal(new[]{aUser},puts);
        await input.FillAsync("b"); await page.GetByText("Resident B",new(){Exact=false}).ClickAsync(); await page.GetByRole(AriaRole.Button,new(){Name="Gán cư dân",Exact=true}).ClickAsync(); await page.GetByText("Đã gán cư dân.",new(){Exact=true}).WaitForAsync(); await page.ReloadAsync(); await page.GetByText("RS-B",new(){Exact=false}).WaitForAsync(); Assert.True(assigned); Assert.Equal(new[]{aUser,bUser},puts); Assert.Equal(1,expectedErrors); Assert.Single(expected400Responses); var unexpected=diagnostics.Where(x=>!x.Contains("Failed to load resource",StringComparison.OrdinalIgnoreCase)).ToArray(); Assert.True(unexpected.Length==0,string.Join("\n",unexpected));
    }

    [Fact]
    public async Task Resident_picker_is_hidden_for_non_editable_admin_contract()
    {
        var setup=await NewPickerPageAsync(); var page=setup.Page; var diagnostics=setup.Diagnostics; var candidateRequests=0;
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/api/v1/remote-online-sign/resident-candidates(?:\?.*)?$"), route=>{candidateRequests++;return route.FulfillAsync(JsonResponse(Array.Empty<object>()));});
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}", route=>route.FulfillAsync(JsonResponse(new{id=contractId,creatorId=adminId,residentId=Guid.Parse("aaaaaaaa-0000-0000-0000-000000000041"),residentName="Read Only",residentCode="RS-RO",status="PENDING_INVESTOR_SIGNATURE",signingMode="INVESTOR_AND_RESIDENT",deadline=(DateTimeOffset?)null,revision=1})));
        await page.ReloadAsync(); await page.GetByText("RS-RO",new(){Exact=false}).WaitForAsync(); Assert.Equal(0,await page.Locator("input[placeholder='Tên, mã cư dân hoặc email']").CountAsync()); Assert.Equal(0,await page.GetByRole(AriaRole.Button,new(){Name="Gán cư dân",Exact=true}).CountAsync()); Assert.Equal(0,candidateRequests); Assert.True(diagnostics.Count==0,string.Join("\n",diagnostics));
    }
    [Fact]
    public async Task Resident_picker_prevents_duplicate_assignment_while_request_is_pending()
    {
        var setup = await NewPickerPageAsync(); var page = setup.Page; var diagnostics = setup.Diagnostics;
        var userId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000021"); var residentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000021");
        var putStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var putCount = 0; Guid? payloadUser = null; var assigned = false;
        await page.RouteAsync(new System.Text.RegularExpressions.Regex(@"^https://[^/]+/api/v1/remote-online-sign/resident-candidates(?:\?.*)?$"), route => route.FulfillAsync(JsonResponse(new[] { new { userId, residentId, fullName = "Safe Resident", residentCode = "RS-SAFE", apartments = Array.Empty<string>() } })));
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}/resident", async route => { putCount++; using var json = JsonDocument.Parse(route.Request.PostData ?? "{}"); payloadUser = json.RootElement.GetProperty("residentUserId").GetGuid(); putStarted.TrySetResult(); await release.Task; assigned = true; await route.FulfillAsync(JsonResponse(new { id = contractId, creatorId = adminId, residentId = userId, residentName = "Safe Resident", residentCode = "RS-SAFE", status = "DRAFT", signingMode = "INVESTOR_AND_RESIDENT", deadline = (DateTimeOffset?)null, revision = 1 })); });
        await page.RouteAsync($"**/api/v1/remote-online-sign/{contractId}", route => route.FulfillAsync(JsonResponse(new { id = contractId, creatorId = adminId, residentId = assigned ? userId : (Guid?)null, residentName = assigned ? "Safe Resident" : null, residentCode = assigned ? "RS-SAFE" : null, status = "DRAFT", signingMode = "INVESTOR_AND_RESIDENT", deadline = (DateTimeOffset?)null, revision = 1 })));
        var input = page.Locator("input[placeholder='Tên, mã cư dân hoặc email']"); await input.FillAsync("safe"); await page.GetByText("Safe Resident", new() { Exact = false }).ClickAsync();
        var assign = page.GetByRole(AriaRole.Button, new() { Name = "Gán cư dân", Exact = true }); await assign.ClickAsync(); await putStarted.Task;
        Assert.True(await assign.IsDisabledAsync()); Assert.Equal(1, putCount); Assert.Equal(userId, payloadUser); Assert.NotEqual(residentId, payloadUser);
        release.TrySetResult(); await page.GetByText("Safe Resident", new() { Exact = false }).Last.WaitForAsync(); await page.ReloadAsync(); await page.GetByText("RS-SAFE", new() { Exact = false }).WaitForAsync();
        Assert.Equal(1, putCount); Assert.True(diagnostics.Count == 0, string.Join("\n", diagnostics));
    }
}
