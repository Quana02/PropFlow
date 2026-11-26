namespace PropFlow.Modules.Residents.Presentation;

internal static class ResidentDirectorySelection
{
    // Additive contract: legacy scalar + repeated plural values form one OR set.
    public static T[] Merge<T>(T? legacy, T[]? values) where T : struct =>
        (values ?? []).Concat(legacy.HasValue ? [legacy.Value] : []).Distinct().ToArray();
    public static bool InvalidEnum<T>(T[] values) where T : struct, Enum =>
        values.Length > 100 || values.Any(value => !Enum.IsDefined(value));
}
