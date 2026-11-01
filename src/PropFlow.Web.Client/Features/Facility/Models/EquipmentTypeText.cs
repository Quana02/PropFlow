namespace PropFlow.Web.Client.Features.Facility.Models;

public static class EquipmentTypeText
{
    public static string Display(string? name) => name?.Trim().ToUpperInvariant() switch
    {
        null or "" => "Chưa phân loại",
        "ACCESS_CONTROL" => "Kiểm soát ra vào",
        "AIR_CONDITIONER" => "Máy điều hòa",
        "CAMERA" => "Camera giám sát",
        "ELECTRICAL_PANEL" => "Tủ điện",
        "ELEVATOR" => "Thang máy",
        "FIRE_ALARM" => "Hệ thống báo cháy",
        "FIRE_PUMP" => "Máy bơm chữa cháy",
        "FITNESS" => "Thiết bị thể dục",
        "GENERATOR" => "Máy phát điện",
        "LIGHTING" => "Hệ thống chiếu sáng",
        "POOL_PUMP" => "Máy bơm hồ bơi",
        "PUMP" => "Máy bơm nước",
        _ => name.Trim()
    };
}
