namespace PropFlow.Modules.PropertyAssets.Infrastructure.Parking;

public sealed class ParkingVisionCooldown(TimeProvider clock)
{
    private readonly object sync = new();
    private readonly Dictionary<string, DateTimeOffset> pausedUntil = new(StringComparer.Ordinal);

    internal TimeSpan Remaining(string model)
    {
        lock (sync)
        {
            if (!pausedUntil.TryGetValue(model, out var deadline))
                return TimeSpan.Zero;

            var remaining = deadline - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
                return remaining;

            pausedUntil.Remove(model);
            return TimeSpan.Zero;
        }
    }

    internal TimeSpan Pause(string model, TimeSpan duration)
    {
        lock (sync)
        {
            var now = clock.GetUtcNow();
            var current = pausedUntil.GetValueOrDefault(model) - now;
            if (current >= duration)
                return current;

            pausedUntil[model] = now + duration;
            return duration;
        }
    }
}
