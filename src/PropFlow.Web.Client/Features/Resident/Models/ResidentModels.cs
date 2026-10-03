using System.ComponentModel.DataAnnotations;

namespace PropFlow.Web.Client.Features.Resident.Models;

public sealed record PagedResidentsResponse(IReadOnlyList<ResidentListItem> Items, int TotalCount, int PageIndex, int PageSize);
public sealed record ResidentListItem(Guid Id, string ResidentCode, string FullName, string? PhoneNumber, string? Email, string Status, bool HasAccount, IReadOnlyList<ResidentApartmentRelationshipSummary> ApartmentRelationships);
public sealed record ResidentApartmentRelationshipSummary(Guid ApartmentUnitId, string UnitNumber, int FloorNumber, bool IsCurrentOwner, bool IsCurrentResident);
public sealed record ResidentDetailResponse(Guid Id, string ResidentCode, string FullName, DateOnly? DateOfBirth, string? Gender, string? Nationality, string? IdentityType, string? IdentityNumber, DateOnly? IdentityIssuedDate, DateOnly? IdentityExpiryDate, string? PhoneNumber, string? Email, string? Note, string Status, bool HasAccount, IReadOnlyList<OwnershipItem> Ownerships, IReadOnlyList<ResidencyItem> Residencies);
public sealed record OwnershipItem(Guid Id, Guid ApartmentUnitId, string UnitNumber, int FloorNumber, DateOnly StartDate, DateOnly? EndDate);
public sealed record ResidencyItem(Guid Id, Guid ApartmentUnitId, string UnitNumber, int FloorNumber, string HouseholdRole, string ResidencyType, Guid? HouseholdHeadResidencyId, string? HouseholdHeadName, string? HouseholdHeadResidentCode, string? RelationshipToHead, DateOnly StartDate, DateOnly? EndDate, string Status, string? Note);
public sealed record ActiveApartmentOption(Guid Id, string UnitNumber, int FloorNumber);
public sealed record EligibleHouseholdHead(Guid ResidencyId, Guid ResidentId, string FullName, string ResidentCode);
public sealed class CreateResidencyRequest { public Guid ApartmentUnitId { get; set; } public string HouseholdRole { get; set; } = "HOUSEHOLD_HEAD"; public string ResidencyType { get; set; } = "OWNER_OCCUPIED"; public Guid? HouseholdHeadResidencyId { get; set; } public string? RelationshipToHead { get; set; } public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today); public string? Note { get; set; } }
public sealed record MoveResidencyRequest(Guid SourceResidencyId, CreateResidencyRequest Target);
public sealed class CreateResidentRequest : IValidatableObject
{
    [Required(ErrorMessage = "Họ và tên là bắt buộc.")]
    [StringLength(150, ErrorMessage = "Họ và tên không được vượt quá 150 ký tự.")]
    public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Ngày sinh là bắt buộc.")] public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    [Required(ErrorMessage = "Quốc tịch là bắt buộc.")]
    [StringLength(80, ErrorMessage = "Quốc tịch không được vượt quá 80 ký tự.")]
    public string? Nationality { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn loại giấy tờ.")] public string? IdentityType { get; set; }
    [Required(ErrorMessage = "Vui lòng nhập số giấy tờ.")] public string? IdentityNumber { get; set; }
    public DateOnly? IdentityIssuedDate { get; set; }
    public DateOnly? IdentityExpiryDate { get; set; }
    [Required(ErrorMessage = "Số điện thoại là bắt buộc.")] public string? PhoneNumber { get; set; }
    [EmailAddress(ErrorMessage = "Email không đúng định dạng.")] public string? Email { get; set; }
    public string? Note { get; set; }
    public Guid ApartmentUnitId { get; set; }
    [Required(ErrorMessage = "Vui lòng chọn quan hệ với căn hộ.")] public string RelationshipKind { get; set; } = "";
    public CreateResidencyRequest? Residency { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var identity = string.IsNullOrWhiteSpace(IdentityNumber)
            ? null
            : new string(IdentityNumber.Where(character => character is >= '0' and <= '9').ToArray());
        if (!string.IsNullOrWhiteSpace(IdentityNumber) &&
            !System.Text.RegularExpressions.Regex.IsMatch(IdentityNumber.Trim(), "^[0-9\\s./-]+$"))
            yield return new("Số giấy tờ chỉ được gồm chữ số và dấu phân cách.", [nameof(IdentityNumber)]);
        else if (string.Equals(IdentityType, "CCCD", StringComparison.OrdinalIgnoreCase) &&
            (identity is null || !System.Text.RegularExpressions.Regex.IsMatch(identity, "^\\d{12}$")))
            yield return new("Số CCCD phải gồm đúng 12 chữ số.", [nameof(IdentityNumber)]);
        else if (string.Equals(IdentityType, "CMND", StringComparison.OrdinalIgnoreCase) &&
            (identity is null || !System.Text.RegularExpressions.Regex.IsMatch(identity, "^(\\d{9}|\\d{12})$")))
            yield return new("Số CMND phải gồm 9 hoặc 12 chữ số.", [nameof(IdentityNumber)]);
        else if (!string.IsNullOrWhiteSpace(IdentityType) && IdentityType is not ("CCCD" or "CMND"))
            yield return new("Loại giấy tờ chỉ được phép là CCCD hoặc CMND.", [nameof(IdentityType)]);

        var phone = PhoneNumber?.Trim();
        if (!string.IsNullOrWhiteSpace(phone) && !System.Text.RegularExpressions.Regex.IsMatch(phone, "^(?:\\+84|0)\\d{9,10}$"))
            yield return new("Số điện thoại không đúng định dạng.", [nameof(PhoneNumber)]);
        if (DateOfBirth > DateOnly.FromDateTime(DateTime.Today))
            yield return new("Ngày sinh không được ở tương lai.", [nameof(DateOfBirth)]);
        if (IdentityIssuedDate > DateOnly.FromDateTime(DateTime.Today))
            yield return new("Ngày cấp không được ở tương lai.", [nameof(IdentityIssuedDate)]);
        if (IdentityIssuedDate.HasValue && IdentityExpiryDate < IdentityIssuedDate)
            yield return new("Ngày hết hạn phải từ ngày cấp trở đi.", [nameof(IdentityExpiryDate)]);
        if (ApartmentUnitId == Guid.Empty)
            yield return new("Bắt buộc chọn căn hộ.", [nameof(ApartmentUnitId)]);
        if (RelationshipKind is not ("OWNER_ONLY" or "RESIDENT_ONLY" or "OWNER_AND_RESIDENT"))
            yield return new("Vui lòng chọn quan hệ với căn hộ.", [nameof(RelationshipKind)]);
        if ((RelationshipKind is "RESIDENT_ONLY" or "OWNER_AND_RESIDENT") && Residency is null)
            yield return new("Thông tin cư trú là bắt buộc.", [nameof(Residency)]);
    }
}
public sealed class UpdateResidentRequest { public string FullName { get; set; } = ""; public DateOnly? DateOfBirth { get; set; } public string? Gender { get; set; } public string? Nationality { get; set; } public string? IdentityType { get; set; } public string? IdentityNumber { get; set; } public DateOnly? IdentityIssuedDate { get; set; } public DateOnly? IdentityExpiryDate { get; set; } public string? PhoneNumber { get; set; } public string? Email { get; set; } public string? Note { get; set; } }
public sealed class SetResidentStatusRequest { public string Status { get; set; } = "ACTIVE"; }
public sealed class EndResidencyRequest { public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.Today); }
