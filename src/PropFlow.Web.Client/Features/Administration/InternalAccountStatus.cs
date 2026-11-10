namespace PropFlow.Web.Client.Features.Administration;

internal static class InternalAccountStatus
{
    public static string Label(string value) => value switch
    {
        "ACTIVE" => "Đang hoạt động",
        "DISABLED" => "Bị vô hiệu hóa",
        "LOCKED" => "Đang bị khóa",
        _ => value
    };

    public static string Tone(string value) => value switch
    {
        "ACTIVE" => "connected",
        "LOCKED" => "locked",
        _ => "disabled"
    };
}
