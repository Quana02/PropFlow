using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Infrastructure;
using PropFlow.Modules.Authentication.Presentation;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Infrastructure;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Infrastructure;
using PropFlow.Modules.AiClassification.Infrastructure.Persistence;
using PropFlow.Modules.AiRecommendation.Infrastructure.Persistence;
using PropFlow.Modules.Administration.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Billing.Infrastructure.Persistence;
using PropFlow.Modules.Complaints.Infrastructure.Persistence;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
using PropFlow.Modules.Maintenance.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<DatabaseConnectionOptions>()
    .BindConfiguration("ConnectionStrings")
    .Validate(options => !string.IsNullOrWhiteSpace(options.PropFlowDatabase),
        "Connection string 'PropFlowDatabase' must be configured through the environment or secret provider.")
    .ValidateOnStart();

// Register Batch 1 DbContexts with schema-specific migration history tables
builder.Services.AddDbContext<PropertyAssetsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "property_assets")));

builder.Services.AddDbContext<ApartmentsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "apartments")));

builder.Services.AddDbContext<AuthenticationDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "auth")));

builder.Services.AddDbContext<ResidentsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "residents")));

builder.Services.AddDbContext<AdministrationDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "administration")));

builder.Services.AddDbContext<ServiceRequestsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "service_requests")));

builder.Services.AddDbContext<ComplaintsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "complaints")));

builder.Services.AddDbContext<MaintenanceDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "maintenance")));

builder.Services.AddDbContext<BillingDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "billing")));

builder.Services.AddDbContext<PaymentsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "payments")));

builder.Services.AddDbContext<AiClassificationDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "ai_classification")));

builder.Services.AddDbContext<AiRecommendationDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "ai_recommendation")));

builder.Services.AddDbContext<CommunicationDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "communication")));

// Register Module Services
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.Buildings.Services.IBuildingService, PropFlow.Modules.PropertyAssets.Application.Buildings.Services.BuildingService>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.IPropertyAssetsStore, PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.Facilities.Services.IFacilityService, PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.Equipment.Services.IEquipmentService, PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddPropFlowAuthentication(builder.Configuration);
builder.Services.AddScoped<IResidentOnboarding, ResidentOnboarding>();
builder.Services.AddScoped<IAccountAccess, AccountAccessService>();
builder.Services.AddExceptionHandler<AuthExceptionHandler>();
// AuthExceptionHandler logs safe metadata; avoid framework logging raw exception details twice.
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.OnRejected = async (context, ct) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Results.Problem(statusCode: 429, title: "Bạn gửi yêu cầu quá nhanh. Vui lòng thử lại sau.", extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" }).ExecuteAsync(context.HttpContext);
    };
    foreach (var (name, count, minutes) in new[] { ("auth-login", 10, 1), ("auth-entry", 5, 15), ("auth-session", 60, 1) })
    {
        var limit = builder.Configuration.GetValue($"Authentication:RateLimits:{name}:PermitLimit", count);
        var window = builder.Configuration.GetValue($"Authentication:RateLimits:{name}:WindowMinutes", minutes);
        options.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(window), QueueLimit = 0 }));
    }
});
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS for PropFlow Web host origins
builder.Services.AddCors(options =>
{
    options.AddPolicy("PropFlowWebCors", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseExceptionHandler();

app.UseCors("PropFlowWebCors");

app.UseMiddleware<AuthSecurityEvents>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program { }

public sealed class DatabaseConnectionOptions
{
    public string PropFlowDatabase { get; set; } = "";
}
