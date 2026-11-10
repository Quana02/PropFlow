using PropFlow.Modules.ServiceRequests.Contracts;

namespace PropFlow.Web.Client.Features.ServiceRequests.Models;

public static class ServiceRequestAreaText
{
    public static string Label(string? code) => code switch
    {
        "MASTER_BATHROOM" => "Phòng tắm chính",
        "LIVING_ROOM" => "Phòng sinh hoạt chung",
        "MASTER_BEDROOM" => "Phòng ngủ chính",
        "SECONDARY_BEDROOM" or "BEDROOM" => "Phòng ngủ",
        "KITCHEN" => "Bếp và lô gia",
        "OTHER_PRIVATE" => "Khu vực khác trong căn hộ",
        "POOL" => "Hồ bơi",
        "GYM" => "Phòng tập",
        "KIDS_ZONE" => "Khu vui chơi trẻ em",
        "ELEVATOR" => "Thang máy",
        "LOBBY" or "HALL" => "Sảnh",
        "PARKING" => "Khu để xe",
        "BBQ" => "Khu nướng BBQ",
        "SPORT" => "Sân thể thao",
        "ROOM" => "Phòng",
        ServiceRequestAreaCodes.CommonArea => "Khu vực chung",
        _ => "Chưa xác định khu vực"
    };

    public static string TimeSlot(string? code) => code switch
    {
        "MORNING" => "Buổi sáng",
        "AFTERNOON" => "Buổi chiều",
        "EVENING" => "Buổi tối",
        "ANYTIME" => "Bất kỳ lúc nào",
        _ => "Chưa chọn khung giờ"
    };
}
