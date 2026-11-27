using System.Globalization;
using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Web.Client.Features.Facility.Models;

internal static class ParkingListingPresentation
{
    private static readonly NumberFormatInfo VndFormat = new() { NumberGroupSeparator = "." };

    public static string Label(string status) => status switch
    {
        ParkingListingStatus.Available => "Trống",
        ParkingListingStatus.Processing => "Đang xử lý",
        ParkingListingStatus.Sold => "Đã mua",
        _ => "Chưa xác định"
    };

    public static string Price(decimal? priceVnd) =>
        priceVnd is { } price ? $"{price.ToString("N0", VndFormat)} ₫" : "Chưa niêm yết";
}
