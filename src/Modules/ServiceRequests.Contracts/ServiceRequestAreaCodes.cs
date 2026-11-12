namespace PropFlow.Modules.ServiceRequests.Contracts;

public static class ServiceRequestAreaCodes
{
    public const string CommonArea = "COMMON_AREA";

    public static bool IsCommonArea(string? code) =>
        code is CommonArea or "POOL" or "GYM" or "KIDS_ZONE" or "ELEVATOR" or "LOBBY" or "PARKING" or "BBQ" or "SPORT";
}
