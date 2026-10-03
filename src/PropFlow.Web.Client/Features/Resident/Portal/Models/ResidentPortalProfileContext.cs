using PropFlow.Web.Client.Features.Resident.Shared.Models;

namespace PropFlow.Web.Client.Features.Resident.Portal.Models;

public sealed record ResidentPortalProfileContext(
    ResidentDetailResponse? Profile,
    bool IsLoading,
    string? Error)
{
    public static ResidentPortalProfileContext Loading { get; } = new(null, true, null);

    public ResidencyItem? CurrentResidency => Profile?.Residencies
        .Where(item =>
            string.Equals(item.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
            item.StartDate <= DateOnly.FromDateTime(DateTime.Today) &&
            item.EndDate is null)
        .OrderByDescending(item => item.StartDate)
        .FirstOrDefault();

    public OwnershipItem? CurrentOwnership => Profile?.Ownerships
        .Where(item => item.StartDate <= DateOnly.FromDateTime(DateTime.Today) && item.EndDate is null)
        .OrderByDescending(item => item.StartDate)
        .FirstOrDefault();

    public Guid? CurrentApartmentUnitId => CurrentResidency?.ApartmentUnitId ?? CurrentOwnership?.ApartmentUnitId;

    public string? CurrentUnitNumber => CurrentResidency?.UnitNumber ?? CurrentOwnership?.UnitNumber;

    public int? CurrentFloorNumber => CurrentResidency?.FloorNumber ?? CurrentOwnership?.FloorNumber;

    public string ApartmentLabel => IsLoading
        ? "Đang tải thông tin căn hộ..."
        : Error is not null
            ? "Chưa tải được thông tin căn hộ"
            : CurrentUnitNumber is null
                ? "Chưa liên kết căn hộ"
                : CurrentFloorNumber is null
                    ? $"Căn hộ {CurrentUnitNumber}"
                    : $"Căn hộ {CurrentUnitNumber} • Tầng {CurrentFloorNumber}";

    public string RelationshipLabel => IsLoading
        ? "Đang tải quan hệ"
        : Error is not null
            ? "Chưa tải được quan hệ"
            : CurrentResidency?.HouseholdRole switch
    {
        "HOUSEHOLD_HEAD" => "Chủ hộ",
        "HOUSEHOLD_MEMBER" => "Thành viên hộ",
        _ when CurrentOwnership is not null => "Chủ sở hữu",
        _ => "Chưa có quan hệ căn hộ"
    };
}
