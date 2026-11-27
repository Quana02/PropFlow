using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace PropFlow.IntegrationTests;

public sealed partial class ParkingHttpTests
{
    [Fact]
    public async Task Swagger_generation_includes_parking_upload_binary_file_version_and_pdf_page()
    {
        await using var app=await Start();using var client=app.GetTestClient();
        using var response=await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths=json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/parking-areas",out _));
        var upload=paths.GetProperty("/api/v1/parking-areas/{id}/import").GetProperty("post");
        var schema=upload.GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
        var properties=schema.GetProperty("properties");
        var file=properties.EnumerateObject().Single(x=>x.Name.Equals("file",StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Equal("string",file.GetProperty("type").GetString());Assert.Equal("binary",file.GetProperty("format").GetString());
        Assert.Contains(properties.EnumerateObject(),x=>x.Name.Equals("expectedVersion",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(properties.EnumerateObject(),x=>x.Name.Equals("page",StringComparison.OrdinalIgnoreCase));
        Assert.True(paths.GetProperty("/api/v1/parking-areas/{id}/publish").TryGetProperty("post",out _));
        var removal = paths.GetProperty("/api/v1/parking-areas/{id}/published").GetProperty("delete");
        Assert.Contains(removal.GetProperty("parameters").EnumerateArray(), parameter =>
            parameter.GetProperty("name").GetString() == "expectedVersion" && parameter.GetProperty("in").GetString() == "query");
        Assert.True(paths.GetProperty("/api/v1/parking-areas/{id}/spaces/{space}/listing").TryGetProperty("put", out _));
        Assert.True(paths.GetProperty("/api/v1/resident/parking-areas/{id}").TryGetProperty("get", out _));
        await app.StopAsync();
    }

    [Fact]
    public async Task Swagger_for_all_application_controllers_is_generated_without_starting_database_or_workers()
    {
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Testing"});
        builder.WebHost.UseTestServer();builder.Logging.ClearProviders();
        var mvc=builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
        foreach(var assembly in typeof(Program).Assembly.GetReferencedAssemblies()
            .Where(x=>x.Name!.StartsWith("PropFlow.Modules.",StringComparison.Ordinal)&&!x.Name.EndsWith(".Contracts",StringComparison.Ordinal)))
            mvc.AddApplicationPart(Assembly.Load(assembly));
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options=>options.CustomSchemaIds(type=>type.FullName?.Replace('+','.')??type.Name));
        await using var app=builder.Build();app.UseSwagger();app.MapControllers();await app.StartAsync();
        using var client=app.GetTestClient();using var response=await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());var paths=json.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/parking-areas/{id}/import",out _));
        Assert.True(paths.TryGetProperty("/api/v1/apartments",out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/me/avatar",out _));
        await app.StopAsync();
    }
}
