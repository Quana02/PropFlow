using System.ComponentModel.DataAnnotations;

namespace PropFlow.Modules.PropertyAssets.Application;

internal static class AssetFilterSelection
{
    public static T[] Merge<T>(T? scalar, T[]? values) where T : struct =>
        (values ?? []).Concat(scalar.HasValue ? [scalar.Value] : []).Distinct().ToArray();

    public static IEnumerable<ValidationResult> Validate<T>(T? scalar, T[]? values, params Guid[]?[] ids) where T : struct, Enum
    {
        if ((scalar.HasValue && !Enum.IsDefined(scalar.Value)) || (values?.Any(x => !Enum.IsDefined(x)) ?? false))
            yield return new ValidationResult("Trạng thái lọc không hợp lệ.", ["Statuses"]);
        if (Merge(scalar,values).Length > 100 || ids.Any(x => x?.Length > 100))
            yield return new ValidationResult("Mỗi trường được chọn tối đa 100 giá trị.");
        if (ids.Any(x => x?.Contains(Guid.Empty) ?? false))
            yield return new ValidationResult("Mã lựa chọn không được rỗng.");
    }
}
