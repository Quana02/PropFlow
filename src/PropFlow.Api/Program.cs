using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PropFlow.Modules.Authentication.Infrastructure;
using PropFlow.Modules.Authentication.Presentation;
using PropFlow.Modules.Residents.Contracts;
using PropFlow.Modules.Residents.Infrastructure;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Administration.Domain.Permissions;
using PropFlow.Modules.Administration.Domain.Roles;
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
using PropFlow.Modules.Maintenance.Application;
using PropFlow.Modules.Maintenance.Contracts;
using PropFlow.Modules.Maintenance.Presentation;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Contracts;
using PropFlow.Modules.Payments.Application.Finance;
using PropFlow.Modules.Payments.Infrastructure.Finance;
using PropFlow.Modules.Billing.Contracts;
using PropFlow.Modules.Billing.Application.Finance;
using PropFlow.Modules.Billing.Infrastructure.Finance;
using PropFlow.Modules.PropertyAssets.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Infrastructure.Persistence;
using PropFlow.Modules.Residents.Presentation;
using PropFlow.Modules.ServiceRequests.Infrastructure.Persistence;
using PropFlow.Modules.Apartments.Contracts;
using PropFlow.Modules.Apartments.Infrastructure;
using PropFlow.Modules.PropertyAssets.Contracts;
using PropFlow.Modules.PropertyAssets.Infrastructure;
using PropFlow.Modules.ServiceRequests.Contracts;
using PropFlow.Modules.ServiceRequests.Application.SubmitServiceRequest;
using PropFlow.Modules.ServiceRequests.Application.ListResidentServiceRequests;
using PropFlow.Modules.ServiceRequests.Application.GetResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Application.RateResidentServiceRequest;
using PropFlow.Modules.ServiceRequests.Infrastructure;
using PropFlow.Modules.ServiceRequests.Infrastructure.SubmitServiceRequest;
using PropFlow.Modules.ServiceRequests.Presentation;
using PropFlow.Api.Serialization;
using PropFlow.Modules.Reporting.Application.AdministrationOverview;
using Npgsql;
using PropFlow.Api.Composition;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
    options.JsonSerializerOptions.Converters.Insert(0, new FlexibleEnumJsonConverterFactory()));

builder.Services.AddOptions<DatabaseConnectionOptions>()
    .BindConfiguration("ConnectionStrings")
    .Validate(options => !string.IsNullOrWhiteSpace(options.PropFlowDatabase),
        "Connection string 'PropFlowDatabase' must be configured through the environment or secret provider.")
    .ValidateOnStart();

// Authentication and Administration share one scoped connection so their
// account-provisioning workflow can commit atomically across module DbContexts.
builder.Services.AddScoped(services => new NpgsqlConnection(
    services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase")));

// Register Batch 1 DbContexts with schema-specific migration history tables
builder.Services.AddDbContext<PropertyAssetsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<IConfiguration>().GetConnectionString("PropFlowDatabase"),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "property_assets")));

builder.Services.AddDbContext<ApartmentsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<NpgsqlConnection>(),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "apartments")));

builder.Services.AddDbContext<AuthenticationDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<NpgsqlConnection>(),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "auth")));

builder.Services.AddDbContext<ResidentsDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<NpgsqlConnection>(),
        npgsql => npgsql.MigrationsHistoryTable(
            "__EFMigrationsHistory",
            "residents")));

builder.Services.AddDbContext<AdministrationDbContext>((services, options) =>
    options.UseNpgsql(
        services.GetRequiredService<NpgsqlConnection>(),
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

// Read scope uses the existing FE-05/FE-07 assignment data.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAssignedAssetSource, PropFlow.Modules.Maintenance.Infrastructure.MaintenanceAssignedAssetSource>();
builder.Services.AddScoped<IAssignedAssetSource, ServiceRequestAssignedAssetSource>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.IAssetReadAccess, AssignedAssetReadAccess>();

// Register Module Services
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.Buildings.Services.IBuildingService, PropFlow.Modules.PropertyAssets.Application.Buildings.Services.BuildingService>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.IPropertyAssetsStore, PropFlow.Modules.PropertyAssets.Infrastructure.Persistence.EfPropertyAssetsStore>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.Facilities.Services.IFacilityService, PropFlow.Modules.PropertyAssets.Application.Facilities.Services.FacilityService>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Application.Equipment.Services.IEquipmentService, PropFlow.Modules.PropertyAssets.Application.Equipment.Services.EquipmentService>();
builder.Services.AddScoped<PropFlow.Modules.PropertyAssets.Contracts.IMaintenanceAssetSource, PropFlow.Modules.PropertyAssets.Infrastructure.MaintenanceAssetSource>();
builder.Services.AddScoped<IMaintenanceStaffDirectory, MaintenanceStaffDirectory>();
builder.Services.AddScoped<MaintenanceService>();
builder.Services.AddScoped<PropFlow.Modules.Maintenance.Application.IMaintenanceNotifier, PropFlow.Modules.Maintenance.Presentation.Hubs.MaintenanceNotifier>();
builder.Services.AddHostedService<MaintenanceScheduleActivationWorker>();
builder.Services.AddScoped<PropFlow.Modules.Apartments.Application.IApartmentStatisticsReader, PropFlow.Modules.Apartments.Infrastructure.Persistence.EfApartmentStatisticsReader>();

// Add services to the container.
builder.Services.AddSignalR();
builder.Services.AddControllers()
    .AddApplicationPart(typeof(PropFlow.Modules.Administration.Presentation.AdministrationController).Assembly)
    .AddApplicationPart(typeof(PropFlow.Modules.Reporting.Presentation.AdministrationOverviewController).Assembly)
    .AddApplicationPart(typeof(PropFlow.Modules.PropertyAssets.Presentation.Controllers.FacilitiesController).Assembly)
    .AddApplicationPart(typeof(PropFlow.Modules.Apartments.Presentation.ApartmentsController).Assembly)
    .AddApplicationPart(typeof(ResidentsController).Assembly)
    .AddApplicationPart(typeof(ResidentServiceRequestsController).Assembly)
    .AddApplicationPart(typeof(MaintenanceController).Assembly)
    .AddApplicationPart(typeof(PropFlow.Modules.Billing.Presentation.BillingFinanceController).Assembly)
    .AddApplicationPart(typeof(PropFlow.Modules.Payments.Presentation.PaymentsFinanceController).Assembly);
builder.Services.AddScoped<IInvoiceFinancialSource, InvoiceFinancialSource>();
builder.Services.AddScoped<IConfirmedPaymentSource, ConfirmedPaymentSource>();
builder.Services.AddScoped<BillingFinanceQueries>();
builder.Services.AddScoped<IPaymentFinanceStore, PaymentFinanceStore>();
builder.Services.AddScoped<PaymentFinanceUseCases>();
builder.Services.AddPropFlowAuthentication(builder.Configuration);
builder.Services.AddScoped<IResidentOnboarding, ResidentOnboarding>();
builder.Services.AddScoped<IResidentResidenceSource, ResidentResidenceSource>();
builder.Services.AddScoped<IServiceRequestSubmissionStore, ServiceRequestSubmissionStore>();
builder.Services.AddScoped<SubmitServiceRequestHandler>();
builder.Services.AddScoped<IResidentServiceRequestReadStore, ResidentServiceRequestReadStore>();
builder.Services.AddScoped<ListResidentServiceRequestsHandler>();
builder.Services.AddScoped<IResidentServiceRequestDetailStore, ResidentServiceRequestDetailStore>();
builder.Services.AddScoped<GetResidentServiceRequestHandler>();
builder.Services.AddScoped<IResidentServiceRequestFeedbackStore, ResidentServiceRequestFeedbackStore>();
builder.Services.AddScoped<RateResidentServiceRequestHandler>();
builder.Services.AddScoped<IServiceRequestMaintenanceSource, ServiceRequestMaintenanceSource>();
builder.Services.AddScoped<PropFlow.Modules.Residents.Application.ResidentResidencyService>();
builder.Services.AddScoped<PropFlow.Modules.Residents.Application.ResidentOnboardingService>();
builder.Services.AddScoped<PropFlow.Modules.Residents.Application.ResidentCodeGenerator>();
builder.Services.AddScoped<PropFlow.Modules.Residents.Application.ResidentDuplicatePolicy>();
builder.Services.AddScoped<PropFlow.Modules.Residents.Application.ResidentProfileService>();
builder.Services.AddScoped<IAccountAccess, AccountAccessService>();
builder.Services.AddScoped<IAdministrationTransaction, AdministrationTransaction>();
builder.Services.AddScoped<IAtomicTransactionCoordinator, AtomicTransactionCoordinator>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("finance.manage", policy => policy.RequireRole(SystemRoleCodes.Accountant).RequireClaim("permission", SystemPermissionCodes.ManageFinance));
    options.AddPolicy(AdministrationAuthorizationPolicies.ManageInternalAccounts,
        policy => policy.RequireRole(SystemRoleCodes.Admin).RequireClaim("permission", SystemPermissionCodes.ManageInternalAccounts));
    options.AddPolicy(AdministrationAuthorizationPolicies.ViewAdministrationActivity,
        policy => policy.RequireRole(SystemRoleCodes.Admin).RequireClaim("permission", SystemPermissionCodes.ViewAdministrationActivity));
    options.AddPolicy(AdministrationAuthorizationPolicies.ViewSystemOverview,
        policy => policy.RequireRole(SystemRoleCodes.Admin).RequireClaim("permission", SystemPermissionCodes.ViewSystemOverview));
    options.AddPolicy(ServiceRequestsAuthorizationPolicies.Manage,
        policy => policy.RequireRole(SystemRoleCodes.Manager).RequireClaim("permission", SystemPermissionCodes.ManageOperations));
    options.AddPolicy(PropertyAssetsAuthorizationPolicies.View, policy =>
        policy.RequireAssertion(context =>
            (context.User.IsInRole(SystemRoleCodes.Manager) && context.User.HasClaim("permission", SystemPermissionCodes.ManageOperations)) ||
            (context.User.IsInRole(SystemRoleCodes.Staff) && context.User.HasClaim("permission", SystemPermissionCodes.PerformAssignedOperations))));
    options.AddPolicy(PropertyAssetsAuthorizationPolicies.Manage,
        policy => policy.RequireRole(SystemRoleCodes.Manager).RequireClaim("permission", SystemPermissionCodes.ManageOperations));
    options.AddPolicy(MaintenanceAuthorizationPolicies.Manage,
        policy => policy.RequireRole(SystemRoleCodes.Manager).RequireClaim("permission", SystemPermissionCodes.ManageOperations));
    options.AddPolicy(MaintenanceAuthorizationPolicies.View, policy => policy.RequireAssertion(context =>
        (context.User.IsInRole(SystemRoleCodes.Manager) && context.User.HasClaim("permission", SystemPermissionCodes.ManageOperations)) ||
        (context.User.IsInRole(SystemRoleCodes.Staff) && context.User.HasClaim("permission", SystemPermissionCodes.PerformAssignedOperations))));
    options.AddPolicy(MaintenanceAuthorizationPolicies.Work,
        policy => policy.RequireRole(SystemRoleCodes.Staff).RequireClaim("permission", SystemPermissionCodes.PerformAssignedOperations));
    options.AddPolicy(ResidentsAuthorizationPolicies.Manage,
        policy => policy.RequireRole(SystemRoleCodes.Manager).RequireClaim("permission", SystemPermissionCodes.ManageOperations));
    options.AddPolicy(PropFlow.Modules.Apartments.Presentation.ApartmentsAuthorizationPolicies.Manage, policy => policy.RequireRole(SystemRoleCodes.Manager).RequireClaim("permission", SystemPermissionCodes.ManageOperations));
    options.AddPolicy(PropFlow.Modules.Apartments.Presentation.ApartmentsAuthorizationPolicies.Read, policy => policy.RequireAssertion(context =>
        (context.User.IsInRole(SystemRoleCodes.Manager) && context.User.HasClaim("permission", SystemPermissionCodes.ManageOperations)) ||
        (context.User.IsInRole(SystemRoleCodes.Staff) && context.User.HasClaim("permission", SystemPermissionCodes.PerformAssignedOperations)) ||
        (context.User.IsInRole(SystemRoleCodes.Accountant) && context.User.HasClaim("permission", SystemPermissionCodes.ManageFinance))));
    options.AddPolicy(ResidentsAuthorizationPolicies.Read, policy => policy.RequireAssertion(context =>
        (context.User.IsInRole(SystemRoleCodes.Manager) && context.User.HasClaim("permission", SystemPermissionCodes.ManageOperations)) ||
        (context.User.IsInRole(SystemRoleCodes.Staff) && context.User.HasClaim("permission", SystemPermissionCodes.PerformAssignedOperations)) ||
        (context.User.IsInRole(SystemRoleCodes.Accountant) && context.User.HasClaim("permission", SystemPermissionCodes.ManageFinance))));
    options.AddPolicy(ServiceRequestsAuthorizationPolicies.SubmitAsResident,
        policy => policy.RequireRole(SystemRoleCodes.Resident));
});
builder.Services.AddScoped<IApartmentOverviewSource, ApartmentOverviewSource>();
builder.Services.AddScoped<IApartmentResidentRelationshipSource, ApartmentResidentRelationshipSource>();
builder.Services.AddScoped<IApartmentOwnershipCommand, PropFlow.Modules.Apartments.Application.ApartmentOwnershipCommand>();
builder.Services.AddScoped<PropFlow.Modules.Apartments.Application.IApartmentStatusCommand, PropFlow.Modules.Apartments.Application.ApartmentStatusCommand>();
builder.Services.AddScoped<IResidentApartmentReadSource, ResidentApartmentReadSource>();
builder.Services.AddScoped<ICurrentBuildingTimeZone, CurrentBuildingTimeZone>();
builder.Services.AddScoped<IResidentOverviewSource, ResidentOverviewSource>();
builder.Services.AddScoped<IServiceRequestOverviewSource, ServiceRequestOverviewSource>();
builder.Services.AddScoped<AdministrationOverviewQuery>();
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
    var residentSubmitLimit = builder.Configuration.GetValue("ServiceRequests:RateLimits:Submit:PermitLimit", 5);
    var residentSubmitWindow = builder.Configuration.GetValue("ServiceRequests:RateLimits:Submit:WindowMinutes", 1);
    options.AddPolicy("resident-service-request-submit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = residentSubmitLimit,
                Window = TimeSpan.FromMinutes(residentSubmitWindow),
                QueueLimit = 0
            }));
});
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name));

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
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
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
app.MapHub<PropFlow.Modules.Maintenance.Presentation.Hubs.MaintenanceHub>("/hubs/maintenance");

app.Run();

public partial class Program { }

public sealed class DatabaseConnectionOptions
{
    public string PropFlowDatabase { get; set; } = "";
}
