namespace PropFlow.Modules.Administration.Contracts;

public sealed record AccountAccess(string Role, string[] Permissions);
public interface IAccountAccess
{
    Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct);
    Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}
