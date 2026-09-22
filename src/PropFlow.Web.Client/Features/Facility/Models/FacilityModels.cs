namespace PropFlow.Web.Client.Features.Facility.Models;

public class FacilityModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? FacilityType { get; set; }
    public string? LocationDescription { get; set; }
    public string? Description { get; set; }
    public MasterDataStatus Status { get; set; }
    public Guid? CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class FacilityDetailModel : FacilityModel
{
    public int TotalEquipment { get; set; }
    public int ActiveEquipment { get; set; }
}

public class CreateFacilityModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? FacilityType { get; set; }
    public string? LocationDescription { get; set; }
    public string? Description { get; set; }
}

public class UpdateFacilityModel
{
    public string Name { get; set; } = string.Empty;
    public string? FacilityType { get; set; }
    public string? LocationDescription { get; set; }
    public string? Description { get; set; }
}

public class FacilityFilterModel
{
    public string? SearchKeyword { get; set; }
    public MasterDataStatus? Status { get; set; }
    public string? FacilityType { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
public class SetFacilityStatusModel
{
    public MasterDataStatus Status { get; set; }
}
