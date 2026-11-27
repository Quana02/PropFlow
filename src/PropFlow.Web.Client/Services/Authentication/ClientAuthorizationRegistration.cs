using Microsoft.AspNetCore.Components.Authorization;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Web.Client.Features.Facility.Services;

namespace PropFlow.Web.Client.Services.Authentication;

public static class ClientAuthorizationRegistration
{
    public static IServiceCollection AddClientAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationCore(options => options.AddPolicy(AssetPermissions.ViewPolicy,
            policy => policy.RequireAssertion(context => AssetPermissions.CanView(context.User))));
        services.AddCascadingAuthenticationState();
        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy(PropFlow.Modules.PropertyAssets.Contracts.PropertyAssetsAuthorizationPolicies.Manage,
                policy => policy.RequireAssertion(context => AssetPermissions.CanManage(context.User)));
            options.AddPolicy(PropFlow.Modules.Administration.Contracts.AdministrationAuthorizationPolicies.ManageSystemConfiguration,
                policy => policy.RequireRole("ADMIN").RequireClaim("permission", "MANAGE_SYSTEM_CONFIGURATION"));
            options.AddPolicy("billing.read", policy => policy.RequireAssertion(context =>
                (context.User.IsInRole("MANAGER") && context.User.HasClaim("permission", "MANAGE_OPERATIONS")) ||
                (context.User.IsInRole("ACCOUNTANT") && context.User.HasClaim("permission", "MANAGE_FINANCE"))));
            options.AddPolicy("finance.manage",
                policy => policy.RequireRole("ACCOUNTANT").RequireClaim("permission", "MANAGE_FINANCE"));
            options.AddPolicy(ResidentAccountPolicies.Read,
                policy => policy.RequireRole("MANAGER").RequireClaim("permission", ResidentAccountPolicies.Read));
            options.AddPolicy(ResidentAccountPolicies.RequestEmailChange,
                policy => policy.RequireRole("MANAGER").RequireClaim("permission", ResidentAccountPolicies.RequestEmailChange));
            options.AddPolicy(PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.Read,
                policy => policy.RequireAssertion(context => PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.CanRead(context.User)));
            options.AddPolicy(PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.Manage,
                policy => policy.RequireAssertion(context => PropFlow.Modules.AiChatbot.Contracts.KnowledgePolicies.CanManage(context.User)));
            options.AddPolicy(PropFlow.Modules.Communication.Contracts.SupportChatPolicies.Manager,
                policy => policy.RequireAssertion(context => PropFlow.Modules.Communication.Contracts.SupportChatPolicies.IsManager(context.User)));
            options.AddPolicy(PropFlow.Modules.Communication.Contracts.SupportChatPolicies.Resident,
                policy => policy.RequireAssertion(context => PropFlow.Modules.Communication.Contracts.SupportChatPolicies.IsResident(context.User)));
        });
        return services;
    }
}
