namespace PropFlow.IntegrationTests;

public sealed class SwaggerDocumentTests(PropFlowApiFactory factory) : IClassFixture<PropFlowApiFactory>
{
    [Fact]
    public async Task Swagger_document_is_generated_successfully()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        var document = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/apartments", document);
        Assert.Contains("defaultUsableAreaM2", document);
        Assert.Contains("defaultBedroomCount", document);
        Assert.Contains("defaultBathroomCount", document);
    }
}
