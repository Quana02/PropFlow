using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using PropFlow.Modules.Authentication.Application;

namespace PropFlow.Api.Composition;

public sealed class CloudinaryAccountAvatarStorage(Cloudinary cloudinary, IHttpClientFactory clients) : IAccountAvatarStorage
{
    public async Task<string> UploadAsync(Guid owner, Stream content, string contentType, CancellationToken ct)
    {
        var result = await cloudinary.UploadAsync(new ImageUploadParams
        {
            File = new FileDescription(contentType == "image/png" ? "avatar.png" : "avatar.jpg", content),
            PublicId = $"propflow/account-avatars/{owner:N}/{Guid.NewGuid():N}",
            Type = "authenticated", Overwrite = false, UseFilename = false, UniqueFilename = false,
            Format = "jpg",
            Transformation = new Transformation().Width(512).Height(512).Crop("fill").Flags("strip_profile")
        }, ct);
        if (result.Error is not null || string.IsNullOrWhiteSpace(result.PublicId))
            throw new AuthFailure(503, "avatar_storage_unavailable", "Chưa lưu được ảnh đại diện. Kiểm tra ảnh và thử lại sau.");
        return result.PublicId;
    }

    public async Task<byte[]> ReadAsync(string storageKey, CancellationToken ct)
    {
        var url = cloudinary.Api.UrlImgUp.Secure(true).Signed(true).Type("authenticated").BuildUrl(storageKey + ".jpg");
        using var response = await clients.CreateClient("AccountAvatarCloudinary").GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new AuthFailure(503, "avatar_storage_unavailable", "Chưa tải được ảnh đại diện. Vui lòng thử lại.");
        using var output = new MemoryStream();
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > AccountAvatarUseCases.MaxBytes)
                throw new AuthFailure(503, "invalid_avatar_response", "Không thể tải ảnh đại diện.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return output.ToArray();
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var result = await cloudinary.DestroyAsync(new DeletionParams(storageKey)
        { ResourceType = ResourceType.Image, Type = "authenticated", Invalidate = true }).WaitAsync(ct);
        if (result.Error is not null) throw new InvalidOperationException("Avatar cleanup failed.");
    }
}
