namespace PropFlow.Web.Client.Shared.Forms;

public static class FilterDateRange
{
    public static DateTimeOffset? Boundary(DateTime? value,TimeZoneInfo zone,bool end=false) => value is null ? null :
        new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value.Value.Date.Add(end?TimeOnly.MaxValue.ToTimeSpan():TimeSpan.Zero),DateTimeKind.Unspecified),zone));
}
