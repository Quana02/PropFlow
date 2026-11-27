using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.IntegrationTests;

public sealed partial class ParkingHttpTests
{
    [Fact]
    public async Task Resident_reads_published_listings_while_manager_sets_price_and_status_without_exposing_drafts()
    {
        await using var app = await Start();
        using var manager = app.GetTestClient();
        using var resident = app.GetTestClient();
        using var anonymous = app.GetTestClient();
        manager.DefaultRequestHeaders.Add("X-Role", "MANAGER");
        manager.DefaultRequestHeaders.Add("X-Permission", "MANAGE_OPERATIONS");
        resident.DefaultRequestHeaders.Add("X-Role", "RESIDENT");
        const string root = "/api/v1/parking-areas";
        const string publicRoot = "/api/v1/resident/parking-areas";

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(publicRoot)).StatusCode);
        Assert.Empty((await resident.GetFromJsonAsync<PublishedParkingAreaSummary[]>(publicRoot))!);
        using var created = await manager.PostAsJsonAsync(root, new CreateParkingArea("Bãi B1"));
        var area = (await created.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        var managerUrl = $"{root}/{area.Id}";
        var residentUrl = $"{publicRoot}/{area.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync(residentUrl)).StatusCode);

        var first = new ParkingShape(Guid.NewGuid(), "SPACE", "P-01", 20, 50, 80, 120);
        var second = first with { Id = Guid.NewGuid(), Label = "P-02", X = 120 };
        var sold = first with { Id = Guid.NewGuid(), Label = "P-03", X = 220 };
        var drawing = ParkingDrawing.Empty with { Shapes = [first, second, sold] };
        using var saved = await manager.PutAsJsonAsync($"{managerUrl}/draft", new SaveParkingDrawing(area.Version, area.Name, drawing));
        saved.EnsureSuccessStatusCode();
        area = (await saved.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        using var published = await manager.PostAsJsonAsync($"{managerUrl}/publish", new PublishParkingDrawing(area.Version));
        published.EnsureSuccessStatusCode();
        area = (await published.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        Assert.All(area.Spaces, space => Assert.Equal(ParkingListingStatus.Available, space.ListingStatus));
        var staleVersion = area.Version;

        string ListingUrl(Guid id) => $"{managerUrl}/spaces/{id}/listing";
        var firstListing = new UpdateParkingSpaceListing(area.Version, 12_500_000m, ParkingListingStatus.Available);
        Assert.Equal(HttpStatusCode.Forbidden, (await resident.PutAsJsonAsync(ListingUrl(first.Id), firstListing)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(ListingUrl(first.Id), firstListing with { PriceVnd = 12.5m })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.PutAsJsonAsync(ListingUrl(first.Id), firstListing with { ListingStatus = "MAINTENANCE" })).StatusCode);
        using var firstUpdate = await manager.PutAsJsonAsync(ListingUrl(first.Id), firstListing);
        firstUpdate.EnsureSuccessStatusCode();
        area = (await firstUpdate.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        using var secondUpdate = await manager.PutAsJsonAsync(ListingUrl(second.Id),
            new UpdateParkingSpaceListing(area.Version, 15_000_000m, ParkingListingStatus.Processing));
        secondUpdate.EnsureSuccessStatusCode();
        area = (await secondUpdate.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        using var soldUpdate = await manager.PutAsJsonAsync(ListingUrl(sold.Id),
            new UpdateParkingSpaceListing(area.Version, 17_000_000m, ParkingListingStatus.Sold));
        soldUpdate.EnsureSuccessStatusCode();
        area = (await soldUpdate.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        Assert.Equal(17_000_000m, area.Spaces.Single(space => space.Id == sold.Id).PriceVnd);
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PutAsJsonAsync(ListingUrl(first.Id), firstListing with { ExpectedVersion = staleVersion })).StatusCode);

        using var unsyncedDraft = await manager.PutAsJsonAsync($"{managerUrl}/draft",
            new SaveParkingDrawing(area.Version, area.Name, drawing with { Shapes = [first with { Label = "NHÁP" }] }));
        unsyncedDraft.EnsureSuccessStatusCode();
        area = (await unsyncedDraft.Content.ReadFromJsonAsync<ParkingAreaView>())!;
        Assert.Equal(area.Id, Assert.Single((await resident.GetFromJsonAsync<PublishedParkingAreaSummary[]>(publicRoot))!).Id);
        using var response = await resident.GetAsync(residentUrl);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        foreach (var field in new[] { "draft", "draftSource", "publishedSource", "version" })
            Assert.False(json.RootElement.TryGetProperty(field, out _));
        var visible = JsonSerializer.Deserialize<PublishedParkingArea>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Collection(visible.Spaces,
            item => Assert.Equal(new PublishedParkingSpace(first.Id, 1, 12_500_000m, ParkingListingStatus.Available), item),
            item => Assert.Equal(new PublishedParkingSpace(second.Id, 2, 15_000_000m, ParkingListingStatus.Processing), item),
            item => Assert.Equal(new PublishedParkingSpace(sold.Id, 3, null, ParkingListingStatus.Sold), item));
        Assert.Equal(new[] { "P-01", "P-02", "P-03" }, visible.Drawing.Shapes.Select(shape => shape.Label));
        Assert.Equal(HttpStatusCode.Forbidden, (await resident.GetAsync(managerUrl)).StatusCode);

        using var removed = await manager.DeleteAsync($"{managerUrl}/published?expectedVersion={area.Version:D}");
        removed.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await resident.GetAsync(residentUrl)).StatusCode);
        Assert.Empty((await resident.GetFromJsonAsync<PublishedParkingAreaSummary[]>(publicRoot))!);
        await app.StopAsync();
    }
}
