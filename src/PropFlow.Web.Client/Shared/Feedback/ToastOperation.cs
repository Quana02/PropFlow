namespace PropFlow.Web.Client.Shared.Feedback;

// Invalidates presentation callbacks only; it never cancels HTTP requests or mutations.
public sealed class ToastLifetime : IDisposable
{
    private long generation;
    private bool disposed;
    internal long Generation => generation;
    internal bool IsCurrent(long captured) => !disposed && captured == generation;
    public void Invalidate() => generation++;
    public void Dispose() => disposed = true;
}

public sealed class ToastOperation
{
    private readonly ToastService service;
    private readonly long navigationGeneration;
    private readonly (ToastLifetime Lifetime, long Generation)[] lifetimes;

    internal ToastOperation(ToastService service, long navigationGeneration, ToastLifetime[] lifetimes)
    {
        this.service = service;
        this.navigationGeneration = navigationGeneration;
        this.lifetimes = lifetimes.Select(lifetime => (lifetime, lifetime.Generation)).ToArray();
    }

    public bool IsCurrent => service.IsCurrent(navigationGeneration) &&
        lifetimes.All(stamp => stamp.Lifetime.IsCurrent(stamp.Generation));

    public void Publish(Action feedback)
    {
        if (IsCurrent) feedback();
    }

    public bool Apply(Func<bool> feedback) => IsCurrent && feedback();
}
