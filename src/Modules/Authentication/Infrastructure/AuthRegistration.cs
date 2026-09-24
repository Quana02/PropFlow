using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;
using PropFlow.Modules.Authentication.Presentation;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.Modules.Authentication.Infrastructure;

public static class AuthRegistration
{
    public static IServiceCollection AddPropFlowAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthPolicy>().BindConfiguration("Authentication")
            .Validate(policy => !string.IsNullOrWhiteSpace(policy.Issuer) && !string.IsNullOrWhiteSpace(policy.Audience)
                && !string.IsNullOrWhiteSpace(policy.PrivateKeyPem) && !string.IsNullOrWhiteSpace(policy.OtpHashKey),
                "Configure Authentication issuer, audience, RSA private key and OTP hash key through the environment or secret provider.")
            .Validate(policy => new[] { policy.AccessMinutes, policy.RefreshDays, policy.OtpMinutes, policy.OtpAttempts, policy.ResendSeconds, policy.SendsPerHour, policy.LockoutFailures, policy.LockoutMinutes }.All(x => x > 0),
                "Authentication policy values must be positive.").ValidateOnStart();
        services.AddOptions<AuthMailOptions>().BindConfiguration("Authentication:Mail")
            .Validate(mail => !string.IsNullOrWhiteSpace(mail.Host) && !string.IsNullOrWhiteSpace(mail.From) && mail.Port is >= 1 and <= 65535,
                "Configure Authentication:Mail host, sender and TLS SMTP port.").ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthPolicy>>().Value);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AuthMailOptions>>().Value);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuthSecrets>();
        services.AddSingleton<IAuthSecrets>(sp => sp.GetRequiredService<AuthSecrets>());
        services.AddSingleton<AuthEmailQueue>();
        services.AddSingleton<IAuthEmail>(sp => sp.GetRequiredService<AuthEmailQueue>());
        services.AddHostedService(sp => sp.GetRequiredService<AuthEmailQueue>());
        services.AddScoped<IAuthStore, AuthStore>();
        services.AddScoped<IInternalAccountDirectory, InternalAccountDirectory>();
        services.AddScoped<AuthUseCases>();
        services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(x => x.Key, _ => new[] { "Thông tin không hợp lệ. Vui lòng kiểm tra định dạng và độ dài." });
            var problem = new ValidationProblemDetails(errors) { Status = 400, Title = "Vui lòng kiểm tra thông tin đã nhập." };
            problem.Extensions["code"] = "validation_failed";
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            return new BadRequestObjectResult(problem);
        });
        services.AddAntiforgery();
        services.AddOptions<AntiforgeryOptions>().Configure<AuthPolicy>((options, policy) =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "__Secure-PropFlow.Csrf";
            options.Cookie.Path = "/api/v1/auth";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = policy.CrossSiteCookie ? SameSiteMode.None : SameSiteMode.Strict;
        });
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddScoped<AccessTokenStateValidator>();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<AuthSecrets, AuthPolicy>((options, crypto, policy) =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true, ValidIssuer = policy.Issuer, ValidateAudience = true, ValidAudience = policy.Audience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = crypto.SigningKey, ValidateLifetime = true,
                RequireSignedTokens = true, ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.Zero,
                NameClaimType = "name", RoleClaimType = "role"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (context.Principal is null ||
                        !await context.HttpContext.RequestServices.GetRequiredService<AccessTokenStateValidator>()
                            .IsCurrentAsync(context.Principal, context.HttpContext.RequestAborted))
                    {
                        context.Fail("Account access has changed or is no longer active.");
                    }
                },
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await Results.Problem(statusCode: 401, title: "Vui lòng đăng nhập để tiếp tục.", extensions: new Dictionary<string, object?> { ["code"] = "unauthorized" }).ExecuteAsync(context.HttpContext);
                },
                OnForbidden = context => Results.Problem(statusCode: 403, title: "Bạn không có quyền thực hiện thao tác này.", extensions: new Dictionary<string, object?> { ["code"] = "forbidden" }).ExecuteAsync(context.HttpContext)
            };
        });
        services.AddAuthorization();
        return services;
    }
}
