using Microsoft.EntityFrameworkCore;
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

// Database Connection String
var connectionString =
    builder.Configuration.GetConnectionString("PropFlowDatabase");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'PropFlowDatabase' is not configured.");
}

// Register Batch 1 DbContexts with schema-specific migration history tables
builder.Services.AddDbContext<PropertyAssetsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "property_assets")));

builder.Services.AddDbContext<ApartmentsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "apartments")));

builder.Services.AddDbContext<AuthenticationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "auth")));

builder.Services.AddDbContext<ResidentsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "residents")));

builder.Services.AddDbContext<AdministrationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "administration")));

builder.Services.AddDbContext<ServiceRequestsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "service_requests")));

builder.Services.AddDbContext<ComplaintsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "complaints")));

builder.Services.AddDbContext<MaintenanceDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "maintenance")));

builder.Services.AddDbContext<BillingDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "billing")));

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "payments")));

builder.Services.AddDbContext<AiClassificationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "ai_classification")));

builder.Services.AddDbContext<AiRecommendationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "ai_recommendation")));

builder.Services.AddDbContext<CommunicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "communication")));

// Register Module Services
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.Buildings.Services.IBuildingService, PropFlow.Modules.PropertyAssets.Application.Buildings.Services.BuildingService>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS for PropFlow Web host origins
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("PropFlowWebCors", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
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

app.UseCors("PropFlowWebCors");

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
