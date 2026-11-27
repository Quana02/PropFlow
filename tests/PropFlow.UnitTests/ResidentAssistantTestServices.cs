using Microsoft.Extensions.DependencyInjection;
using PropFlow.Web.Client.Features.AiChatbot.Services;
using PropFlow.Web.Client.Features.Resident.Portal.Services;

namespace PropFlow.UnitTests;

internal static class ResidentAssistantTestServices
{
    public static IServiceCollection AddResidentAssistant(this IServiceCollection services) => services
        .AddSingleton<ResidentAssistantHandoff>().AddSingleton<ResidentChatApiClient>()
        .AddSingleton<IResidentServiceRequestApiClient, ResidentServiceRequestApiClient>();
}
