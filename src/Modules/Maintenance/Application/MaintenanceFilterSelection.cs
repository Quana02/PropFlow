using System.ComponentModel.DataAnnotations;

namespace PropFlow.Modules.Maintenance.Application;
internal static class MaintenanceFilterSelection
{
    public static T[] Merge<T>(T? scalar,T[]? values) where T:struct =>(values??[]).Concat(scalar.HasValue?[scalar.Value]:[]).Distinct().ToArray();
    public static IEnumerable<ValidationResult> Validate<T>(T? scalar,T[]? values,DateTimeOffset? from,DateTimeOffset? to,params Guid[]?[] ids) where T:struct,Enum
    {
        if((scalar.HasValue&&!Enum.IsDefined(scalar.Value)) || (values?.Any(x=>!Enum.IsDefined(x))??false))yield return new("Trạng thái lọc không hợp lệ.");
        if(values?.Length>100 || ids.Any(x=>x?.Length>100 || (x?.Contains(Guid.Empty)??false)))yield return new("Mỗi bộ lọc nhận tối đa 100 mã hợp lệ, không rỗng.");
        if(from>to)yield return new("Từ ngày không được sau đến ngày.");
    }
    public static IEnumerable<ValidationResult> ValidatePriorities(string[]? priorities)
    {
        if(priorities?.Length>100 || (priorities?.Any(x=>x is not ("HIGH" or "MEDIUM" or "LOW"))??false))yield return new("Mức ưu tiên lọc không hợp lệ.");
    }
}
