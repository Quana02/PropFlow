namespace PropFlow.Web.Client.Features.Apartment.Models;

public static class ApartmentResidencyPresentation
{
    public static bool RequiresRefresh(ApartmentListItem item) =>
        item.CurrentResidents is null || item.IsOccupied != (item.CurrentResidents.Count>0);

    public static string EmptyLabel(ApartmentListItem item) => item.IsOccupied
        ? "Đang có cư dân · chưa tải được danh sách"
        : "Căn hộ trống";
}
