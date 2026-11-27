namespace PropFlow.Modules.PropertyAssets.Contracts;

public sealed record ParkingToolDefinition(string Kind, string Label, string Group, bool Square = false, bool Outline = false);

public static class ParkingToolCatalog
{
    public static IReadOnlyList<ParkingToolDefinition> Additional { get; } = Array.AsReadOnly<ParkingToolDefinition>([
        new("CROSSWALK", "Vạch qua đường", "Giao thông"),
        new("SIDEWALK", "Lối đi bộ / vỉa hè", "Giao thông", Outline: true),
        new("TRAFFIC_ARROW", "Mũi tên lưu thông", "Giao thông"),
        new("DASHED_LINE", "Vạch đứt nét", "Giao thông"),
        new("TRAFFIC_SIGN", "Biển báo", "Giao thông", Square: true),
        new("ROUNDABOUT", "Vòng xuyến", "Giao thông", Square: true),
        new("NO_PARKING", "Vùng cấm đỗ", "Dải phân cách và vật cản", Outline: true),
        new("WHEEL_STOP", "Gờ chặn bánh", "Dải phân cách và vật cản"),
        new("PILLAR", "Cột kết cấu", "Dải phân cách và vật cản"),
        new("GREEN_ISLAND", "Đảo / dải cây xanh", "Dải phân cách và vật cản", Outline: true),
        new("TREE", "Cây bóng mát", "Dải phân cách và vật cản", Square: true),
        new("BARRIER", "Thanh chắn barie", "Hạ tầng"),
        new("GUARD_BOOTH", "Chốt bảo vệ", "Hạ tầng"),
        new("RAMP", "Dốc lên / xuống", "Hạ tầng", Outline: true),
        new("STAIRS", "Thang bộ", "Hạ tầng"),
        new("ELEVATOR", "Thang máy", "Hạ tầng"),
        new("LIGHT", "Đèn chiếu sáng", "Hạ tầng", Square: true)
    ]);

    public static bool IsSquare(string kind) => kind is "SQUARE" or "WALL_SQUARE" or "WALL_CIRCLE" || Additional.Any(tool => tool.Square && tool.Kind == kind);

    public static bool SupportsOutline(string kind) => kind is "SPACE" or "RECT" or "WALL" or "LANE" or "LABEL" || Additional.Any(tool => tool.Outline && tool.Kind == kind);
}
