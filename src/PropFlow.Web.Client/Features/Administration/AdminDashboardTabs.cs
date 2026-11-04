namespace PropFlow.Web.Client.Features.Administration;

public static class AdminDashboardTabs
{
    public const string Overview = "overview";
    public const string Accounts = "accounts";
    public const string Activity = "activity";
    public const string Payment = "payment";

    public static string Normalize(string? requested) => requested is Accounts or Activity or Overview or Payment ? requested : Overview;
}
