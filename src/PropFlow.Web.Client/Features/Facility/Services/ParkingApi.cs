using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Web.Client.Services.Api;

namespace PropFlow.Web.Client.Features.Facility.Services;

public sealed class ParkingApi(AuthenticatedApiClient api)
{
    private const string Root="api/v1/parking-areas";
    public Task<ApiResult<ParkingAreaSummary[]>> ListAsync(CancellationToken ct)=>api.SendAsync<ParkingAreaSummary[]>(HttpMethod.Get,Root,ct:ct);
    public Task<ApiResult<ParkingAreaView>> GetAsync(Guid id,CancellationToken ct)=>api.SendAsync<ParkingAreaView>(HttpMethod.Get,$"{Root}/{id}",ct:ct);
    public Task<ApiResult<ParkingAreaView>> CreateAsync(string name,CancellationToken ct)=>api.SendAsync<ParkingAreaView>(HttpMethod.Post,Root,new CreateParkingArea(name),ct:ct);
    public Task<ApiResult<ParkingAreaView>> SaveAsync(Guid id,SaveParkingDrawing input,CancellationToken ct)=>api.SendAsync<ParkingAreaView>(HttpMethod.Put,$"{Root}/{id}/draft",input,ct:ct);
    public Task<ApiResult<ParkingAreaView>> PublishAsync(Guid id,Guid version,CancellationToken ct)=>api.SendAsync<ParkingAreaView>(HttpMethod.Post,$"{Root}/{id}/publish",new PublishParkingDrawing(version),ct:ct);
    public Task<ApiResult<ParkingAreaView>> RemovePublishedDrawingAsync(Guid id, Guid version, CancellationToken ct) =>
        api.SendAsync<ParkingAreaView>(HttpMethod.Delete, $"{Root}/{id}/published?expectedVersion={version:D}", ct: ct);
    public Task<ApiResult<ParkingAreaView>> StateAsync(Guid id,Guid space,ChangeParkingSpace input,CancellationToken ct)=>api.SendAsync<ParkingAreaView>(HttpMethod.Put,$"{Root}/{id}/spaces/{space}",input,ct:ct);
    public Task<ApiResult<ParkingAreaView>> UpdateListingAsync(Guid id, Guid space, UpdateParkingSpaceListing input, CancellationToken ct) =>
        api.SendAsync<ParkingAreaView>(HttpMethod.Put, $"{Root}/{id}/spaces/{space}/listing", input, ct: ct);
    public Task<ApiResult<PublishedParkingAreaSummary[]>> ListPublishedAsync(CancellationToken ct) =>
        api.SendAsync<PublishedParkingAreaSummary[]>(HttpMethod.Get, "api/v1/resident/parking-areas", ct: ct);
    public Task<ApiResult<PublishedParkingArea>> GetPublishedAsync(Guid id, CancellationToken ct) =>
        api.SendAsync<PublishedParkingArea>(HttpMethod.Get, $"api/v1/resident/parking-areas/{id}", ct: ct);
    public Task<ApiResult<ParkingImportResult>> ImportAsync(Guid id,MultipartFormDataContent content,CancellationToken ct)=>api.SendMultipartAsync<ParkingImportResult>($"{Root}/{id}/import",content,ct);
    public Task<ApiResult<ParkingImportResult>> RecognizeAsync(Guid id,Guid version,CancellationToken ct)=>api.SendAsync<ParkingImportResult>(HttpMethod.Post,$"{Root}/{id}/recognize",new RecognizeParkingDrawing(version),ct:ct);
    public Task<ApiResult<ApiFileContent>> SourceAsync(Guid id,Guid source,CancellationToken ct)=>api.GetFileAsync($"{Root}/{id}/sources/{source}",ct);
    public Task<ApiResult<ParkingActionView[]>> HistoryAsync(Guid id,int page,CancellationToken ct)=>api.SendAsync<ParkingActionView[]>(HttpMethod.Get,$"{Root}/{id}/history?page={page}",ct:ct);
}
