namespace PropFlow.Web.Client.Features.Administration;

public static class AdminDashboardTabs
{
    public const string Overview = "overview";
    public const string Accounts = "accounts";
    public const string Activity = "activity";

    public static string Normalize(string? requested) => requested is Accounts or Activity or Overview ? requested : Overview;
}
