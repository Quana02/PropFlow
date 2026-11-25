namespace PropFlow.Web.Client.Features.Maintenance.Models;
public sealed record MaintenanceDirectorySelection
{
    public IReadOnlyList<string> Statuses {get;init;}=[];
    public IReadOnlyList<string> Facilities {get;init;}=[];
    public IReadOnlyList<string> Equipment {get;init;}=[];
    public IReadOnlyList<string> Staff {get;init;}=[];
    public IReadOnlyList<string> Priorities {get;init;}=[];
    public bool? IsEmergency {get;init;}
    public int ActiveCount=>new[]{Statuses.Count>0,Facilities.Count>0,Equipment.Count>0,Staff.Count>0,Priorities.Count>0,IsEmergency.HasValue}.Count(x=>x);
    public IEnumerable<string> Parameters(string staffKey="assignedStaffUserIds") =>
        Values("statuses",Statuses).Concat(Values("facilityIds",Facilities)).Concat(Values("equipmentIds",Equipment)).Concat(Values(staffKey,Staff)).Concat(Values("priorities",Priorities)).Concat(IsEmergency.HasValue?["isEmergency="+IsEmergency.Value.ToString().ToLowerInvariant()]:[]);
    public string Query=>string.Join('&',Parameters());
    private static IEnumerable<string> Values(string key,IReadOnlyList<string> values)=>values.Distinct().Select(x=>key+"="+Uri.EscapeDataString(x));
}
