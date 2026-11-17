using PropFlow.Modules.PropertyAssets.Contracts;

namespace PropFlow.Modules.PropertyAssets.Domain.Facilities;

public sealed class FacilityGuide
{
    private FacilityGuide() { }
    public FacilityGuide(Guid facilityId) { FacilityId = facilityId; }
    public Guid FacilityId { get; private set; }
    public bool Published { get; private set; }
    public string Description { get; private set; } = "";
    public string Audience { get; private set; } = "GENERAL";
    public int? MinimumAge { get; private set; }
    public int? MaximumAge { get; private set; }
    public string ChildPolicy { get; private set; } = "GUARDIAN_REQUIRED";
    public string SupervisionNote { get; private set; } = "";
    public string Rules { get; private set; } = "";
    public string HoursJson { get; private set; } = "[]";
    public DateOnly[] ClosedDates { get; private set; } = [];
    public Guid? ResponsibleStaffId { get; private set; }
    public string ContactDetails { get; private set; } = "";
    public Guid Token { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public void Change(SaveFacilityGuide input, Guid actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Length > 2000 || (input.Rules?.Length ?? 0) > 4000 ||
            (input.SupervisionNote?.Length ?? 0) > 1000 || (input.ContactDetails?.Length ?? 0) > 300 ||
            input.Audience is not ("GENERAL" or "CHILDREN" or "FAMILY" or "ADULTS") ||
            input.ChildPolicy is not ("GUARDIAN_REQUIRED" or "SUPERVISED_CONTACT_REQUIRED" or "NOT_FOR_CHILDREN") ||
            input.MinimumAge is < 0 or > 120 || input.MaximumAge is < 0 or > 120 || input.MinimumAge > input.MaximumAge ||
            input.Hours is null || input.Hours.Length > 7 || input.ClosedDates is null || input.ClosedDates.Length > 120)
            throw Invalid("Kiểm tra mô tả, đối tượng, độ tuổi và thông tin liên hệ của tiện ích.");
        if (input.Hours.Any(x => x is null) || input.Hours.Select(x => x.DayOfWeek).Distinct().Count() != input.Hours.Length ||
            input.Hours.Any(x => x.DayOfWeek is < 0 or > 6 || x.OpensAt >= x.ClosesAt || x.OpensAt.Ticks % TimeSpan.TicksPerMinute != 0 || x.ClosesAt.Ticks % TimeSpan.TicksPerMinute != 0))
            throw Invalid("Mỗi thứ có một khoảng mở cửa trong cùng ngày, giờ đóng phải sau giờ mở.");
        if (input.Published && (input.Hours.Length == 0 || input.ResponsibleStaffId is null || string.IsNullOrWhiteSpace(input.ContactDetails)))
            throw Invalid("Cần lịch hoạt động, người phụ trách và đầu mối liên hệ trước khi xuất bản.");
        if (input.ChildPolicy == "SUPERVISED_CONTACT_REQUIRED" && (string.IsNullOrWhiteSpace(input.SupervisionNote) || input.ResponsibleStaffId is null || string.IsNullOrWhiteSpace(input.ContactDetails)))
            throw Invalid("Hoạt động có giám sát phải có mô tả chương trình, người phụ trách và cách liên hệ xác nhận.");
        if (input.Audience == "ADULTS" && input.ChildPolicy != "NOT_FOR_CHILDREN")
            throw Invalid("Tiện ích dành cho người lớn cần đặt điều kiện không dành cho trẻ em.");
        Published = input.Published; Description = input.Description.Trim().Normalize(); Audience = input.Audience;
        MinimumAge = input.MinimumAge; MaximumAge = input.MaximumAge; ChildPolicy = input.ChildPolicy;
        SupervisionNote = (input.SupervisionNote ?? "").Trim().Normalize(); Rules = (input.Rules ?? "").Trim().Normalize();
        HoursJson = System.Text.Json.JsonSerializer.Serialize(input.Hours.OrderBy(x => x.DayOfWeek));
        ClosedDates = input.ClosedDates.Distinct().Order().ToArray(); ResponsibleStaffId = input.ResponsibleStaffId;
        ContactDetails = (input.ContactDetails ?? "").Trim().Normalize(); Token = Guid.NewGuid(); UpdatedBy = actor; UpdatedAt = now;
    }
    private static FacilityGuideFailure Invalid(string message) => new(400, "facility_guide_invalid", message);
}
