using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Application;

public sealed partial class AuthUseCases
{
    async Task<InternalAccountProfile?> IInternalAccountProfiles.ReadAsync(Guid userId, CancellationToken ct)
    {
        var account = await store.UserAsync(userId, ct);
        return account is null ? null : InternalProfile(account);
    }

    Task<InternalAccountProfileUpdate?> IInternalAccountProfiles.UpdateAsync(
        Guid userId, UpdateProfileRequest request, Guid actorId, CancellationToken ct) =>
        store.SerializedAsync<InternalAccountProfileUpdate?>("user:" + userId, async () =>
        {
            var account = await store.UserForUpdateAsync(userId, ct);
            if (account is null) return null;

            var changedFields = new List<string>(2);
            if (account.DisplayName != request.DisplayName?.Trim()) changedFields.Add(nameof(account.DisplayName));
            if (account.PhoneNumber != Phone(request.PhoneNumber)) changedFields.Add(nameof(account.PhoneNumber));
            if (changedFields.Count > 0) await ApplyProfileAsync(account, request, actorId, ct);
            return new(InternalProfile(account), changedFields);
        }, ct);

    private static InternalAccountProfile InternalProfile(UserAccount account) =>
        new(account.Id, account.Username, account.DisplayName, account.Email, account.PhoneNumber, account.Status.ToString(), account.EmailVerified);
}
