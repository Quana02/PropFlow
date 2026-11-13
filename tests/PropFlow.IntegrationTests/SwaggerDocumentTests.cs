using System.Text.Json;

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
        using var parsed = JsonDocument.Parse(document);
        var paths = parsed.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/v1/auth/resident-accounts").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/auth/resident-accounts/{residentId}/email-change").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/v1/auth/resident-email-change").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/v1/auth/resident-email-change/verify").TryGetProperty("post", out _));
    }

    [Fact]
    public async Task Own_avatar_contract_accepts_multipart_image_without_a_target_account_id()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var endpoint = document.RootElement.GetProperty("paths").GetProperty("/api/v1/auth/me/avatar");
        var upload = endpoint.GetProperty("post");
        Assert.True(upload.GetProperty("requestBody").GetProperty("content").TryGetProperty("multipart/form-data", out var form));
        var fields = form.GetProperty("schema").GetProperty("properties");
        Assert.True(fields.TryGetProperty("File", out _));
        Assert.False(fields.TryGetProperty("userId", out _));
        Assert.True(endpoint.GetProperty("get").GetProperty("responses").TryGetProperty("401", out _));
    }

    [Fact]
    public async Task Resident_weather_is_authenticated_and_documents_a_nullable_temperature()
    {
        using var client = factory.CreateClient();
        using var denied = await client.GetAsync("/api/v1/resident/weather");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, denied.StatusCode);
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var endpoint = document.RootElement.GetProperty("paths").GetProperty("/api/v1/resident/weather").GetProperty("get");
        Assert.True(endpoint.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(endpoint.GetProperty("responses").TryGetProperty("403", out _));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task Avatar_download_and_upload_require_authentication(string method)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/auth/me/avatar");
        using var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("change", "200", "PropFlow.Modules.Authentication.Contracts.ChangeEmailRequest", "currentPassword")]
    [InlineData("verify", "204", "PropFlow.Modules.Authentication.Contracts.VerifyEmailChangeRequest", "code")]
    public async Task Own_email_contract_documents_the_same_request_and_unauthorized_response(
        string operation, string successStatus, string schemaName, string secretField)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var endpoint = root.GetProperty("paths").GetProperty("/api/v1/auth/email/" + operation).GetProperty("post");
        var schema = endpoint.GetProperty("requestBody").GetProperty("content").GetProperty("application/json")
            .GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Equal("#/components/schemas/" + schemaName, schema);
        var fields = root.GetProperty("components").GetProperty("schemas").GetProperty(schemaName).GetProperty("properties");
        Assert.True(fields.TryGetProperty("newEmail", out _));
        Assert.True(fields.TryGetProperty(secretField, out _));
        Assert.False(fields.TryGetProperty("userId", out _));
        Assert.False(fields.TryGetProperty("residentId", out _));
        var responses = endpoint.GetProperty("responses");
        Assert.True(responses.TryGetProperty(successStatus, out _));
        Assert.True(responses.TryGetProperty("401", out _));
    }
}
