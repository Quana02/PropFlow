using Microsoft.Extensions.Logging;
using PropFlow.Modules.Authentication.Contracts;
using PropFlow.Modules.Authentication.Domain.Users;

namespace PropFlow.Modules.Authentication.Application;

public interface IAccountAvatarStorage
{
    Task<string> UploadAsync(Guid owner, Stream content, string contentType, CancellationToken ct);
    Task<byte[]> ReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
}

// FE-01.6: avatar belongs to the authenticated account, never to a client-supplied user ID.
public sealed class AccountAvatarUseCases(IAuthStore store, IAccountAvatarStorage storage,
    AuthUseCases auth, TimeProvider clock, ILogger<AccountAvatarUseCases> logger)
{
    public const int MaxBytes = 5 * 1024 * 1024;

    private async Task<UserAccount> OwnerAsync(Guid owner, CancellationToken ct)
    {
        var user = await store.UserAsync(owner, ct);
        if (user is null || user.Status != AccountStatus.ACTIVE)
            throw new AuthFailure(401, "invalid_credentials", "Phiên đăng nhập không hợp lệ.");
        return user;
    }

    public async Task<AccountResponse> UploadAsync(Guid owner, Stream input, long length, string contentType, CancellationToken ct)
    {
        await OwnerAsync(owner, ct);
        if (length is <= 0 or > MaxBytes || contentType is not ("image/png" or "image/jpeg"))
            throw InvalidImage();
        using var image = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (image.Length + count > MaxBytes) throw InvalidImage();
            await image.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        var bytes = image.ToArray();
        if (image.Length != length || !ValidSignature(bytes, contentType)) throw InvalidImage();
        image.Position = 0;
        var key = await storage.UploadAsync(owner, image, contentType, ct);
        string? previous = null;
        AccountResponse result;
        try
        {
            result = await store.SerializedAsync("user:" + owner, async () =>
            {
                var user = await OwnerAsync(owner, ct);
                previous = user.AvatarStorageKey;
                user.ChangeAvatar(key, Guid.NewGuid(), clock.GetUtcNow());
                return await auth.GetAccountAsync(owner, ct);
            }, ct);
        }
        catch
        {
            await CleanupAsync(key);
            throw;
        }
        if (previous is not null && previous != key) await CleanupAsync(previous);
        return result;
    }

    public async Task<byte[]> ReadAsync(Guid owner, CancellationToken ct)
    {
        var user = await OwnerAsync(owner, ct);
        if (user.AvatarStorageKey is not { } key)
            throw new AuthFailure(404, "avatar_not_found", "Bạn chưa có ảnh đại diện.");
        return await storage.ReadAsync(key, ct);
    }

    private async Task CleanupAsync(string key)
    {
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await storage.DeleteAsync(key, timeout.Token); }
        catch (Exception) { logger.LogWarning("Account avatar cleanup failed; an unreferenced asset needs cleanup."); }
    }
    private static AuthFailure InvalidImage() => new(400, "invalid_avatar", "Chọn ảnh PNG hoặc JPG hợp lệ, tối đa 5 MB.");
    private static bool ValidSignature(byte[] bytes, string type) => type == "image/png"
        ? bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
        : bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
}
