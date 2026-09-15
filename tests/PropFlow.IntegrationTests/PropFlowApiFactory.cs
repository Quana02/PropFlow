using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PropFlow.IntegrationTests;

public class PropFlowApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Ensures appsettings.Testing.json is loaded and takes precedence
            config.AddJsonFile("appsettings.Testing.json", optional: false, reloadOnChange: false);
        });
    }

    public string GetConnectionString()
    {
        var config = Services.GetRequiredService<IConfiguration>();
        var connStr = config.GetConnectionString("PropFlowDatabase");

        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new InvalidOperationException("Connection string 'PropFlowDatabase' is not configured in Testing environment.");
        }

        return connStr;
    }
}

