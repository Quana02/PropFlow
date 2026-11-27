namespace PropFlow.Modules.Apartments.Presentation;

internal static class ApartmentDirectorySelection
{
    public static T[] Merge<T>(T? legacy, T[]? values) where T : struct =>
        (values ?? []).Concat(legacy.HasValue ? [legacy.Value] : []).Distinct().ToArray();
}
