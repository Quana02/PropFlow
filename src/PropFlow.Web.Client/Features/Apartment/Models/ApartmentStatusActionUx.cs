namespace PropFlow.Web.Client.Features.Apartment.Models;

public static class ApartmentStatusActionUx
{
    public const string BlockerTitle = "Không thể ngừng quản lý";

    public const string OwnerBlockerMessage =
        "Căn hộ vẫn còn chủ sở hữu hiện hành. Vui lòng kết thúc toàn bộ quyền sở hữu trước khi ngừng quản lý căn hộ.";

    public const string ResidentBlockerMessage =
        "Căn hộ vẫn còn cư dân đang cư trú. Vui lòng kết thúc các quan hệ cư trú hiện hành trước khi ngừng quản lý căn hộ.";

    public const string CombinedBlockerMessage =
        "Căn hộ vẫn còn chủ sở hữu và cư dân đang hoạt động. Hãy kết thúc các quyền sở hữu và quan hệ cư trú hiện hành trước khi ngừng quản lý.";

    public static string? BlockerMessage(bool hasCurrentOwners, bool hasActiveResidents) =>
        (hasCurrentOwners, hasActiveResidents) switch
        {
            (true, true) => CombinedBlockerMessage,
            (true, false) => OwnerBlockerMessage,
            (false, true) => ResidentBlockerMessage,
            _ => null
        };

    public static bool TryMapConflict(string? code, out string message)
    {
        message = code?.ToUpperInvariant() switch
        {
            "APARTMENT_HAS_CURRENT_OWNERS" => OwnerBlockerMessage,
            "APARTMENT_HAS_ACTIVE_RESIDENTS" => ResidentBlockerMessage,
            "APARTMENT_HAS_CURRENT_OWNERS_AND_ACTIVE_RESIDENTS" or
            "APARTMENT_HAS_OWNERS_AND_RESIDENTS" => CombinedBlockerMessage,
            _ => string.Empty
        };

        return message.Length > 0;
    }
}
