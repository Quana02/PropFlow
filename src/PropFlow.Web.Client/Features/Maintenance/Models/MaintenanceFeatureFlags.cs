namespace PropFlow.Web.Client.Features.Maintenance.Models;

/// <summary>
/// Controls optional FE-07 UI surfaces without removing their API or persisted data.
/// </summary>
internal static class MaintenanceFeatureFlags
{
    public static bool ServiceRequestLinkingEnabled => false;
}
