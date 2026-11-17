namespace PropFlow.Modules.Administration.Contracts;

public sealed record AccountAccess(string Role, string[] Permissions);
public interface IAccountAccess
{
    Task<AccountAccess?> GetAsync(Guid userId, CancellationToken ct);
    async Task<IReadOnlyDictionary<Guid, AccountAccess>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, AccountAccess>();
        foreach (var userId in userIds)
            if (await GetAsync(userId, ct) is { } value)
                result[userId] = value;
        return result;
    }
    Task<Guid[]> GetResidentUserIdsAsync(Guid[] userIds, CancellationToken ct);
    Task GrantResidentAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}
