namespace PropFlow.Modules.Authentication.Contracts;

public interface IAccountSessionInvalidation
{
    void Invalidate(Guid userId, Guid sessionId);
    Task RecheckAsync(Guid userId, CancellationToken ct);
}
