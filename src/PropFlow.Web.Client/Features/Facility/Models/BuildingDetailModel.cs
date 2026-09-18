namespace PropFlow.Web.Client.Features.Facility.Models;

public class BuildingDetailModel : BuildingModel
{
    public int FacilityCount { get; set; }
    public int EquipmentCount { get; set; }
    public int ActiveFacilityCount { get; set; }
    public int ActiveEquipmentCount { get; set; }
}
