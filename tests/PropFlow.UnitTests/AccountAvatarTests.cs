using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Administration.Contracts;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Domain.Users;
using PropFlow.Modules.Authentication.Presentation;

namespace PropFlow.UnitTests;

[Trait("Feature", "FE-01.6")]
public sealed class AccountAvatarTests
{
    private static readonly byte[] Jpeg = [255, 216, 255, 224, 0, 0];

    [Fact]
    public async Task Own_avatar_survives_reload_and_replacing_it_removes_previous_asset()
    {
        var test = new Scenario();
        var first = await test.Upload();
        var previous = test.User.AvatarStorageKey;
        var second = await test.Upload();
        Assert.NotNull(first.AvatarVersion);
        Assert.NotEqual(first.AvatarVersion, second.AvatarVersion);
        Assert.Equal(second.AvatarVersion, (await test.Auth.GetAccountAsync(test.User.Id, default)).AvatarVersion);
        Assert.Equal([previous!], test.Storage.Deleted);
        Assert.Equal(Jpeg, await test.UseCases.ReadAsync(test.User.Id, default));
        Assert.Equal(test.User.AvatarStorageKey, test.Storage.ReadKey);
        Assert.Equal(test.User.Id, test.User.UpdatedBy);
    }

    [Theory]
    [InlineData("image/svg+xml", 6)]
    [InlineData("image/png", 6)]
    [InlineData("image/jpeg", 100)]
    [InlineData("image/jpeg", 5242881)]
    public async Task Invalid_type_signature_length_or_size_never_reaches_cloud(string contentType, long length)
    {
        var test = new Scenario();
        var failure = await Assert.ThrowsAsync<AuthFailure>(() => test.UseCases.UploadAsync(test.User.Id, new MemoryStream(Jpeg), length, contentType, default));
        Assert.Equal(400, failure.Status);
        Assert.Empty(test.Storage.Uploaded);
        Assert.Null(test.User.AvatarStorageKey);
    }

    [Fact]
    public async Task Unknown_account_cannot_read_or_upload_even_with_guessed_id()
    {
        var test = new Scenario();
        var stranger = Guid.NewGuid();
        Assert.Equal(401, (await Assert.ThrowsAsync<AuthFailure>(() => test.UseCases.ReadAsync(stranger, default))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<AuthFailure>(() => test.UseCases.UploadAsync(stranger, new MemoryStream(Jpeg), Jpeg.Length, "image/jpeg", default))).Status);
        Assert.Empty(test.Storage.Uploaded);
    }

    [Fact]
    public async Task Failed_database_commit_compensates_new_cloud_upload()
    {
        var test = new Scenario { FailCommit = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Upload());
        Assert.Equal(test.Storage.Uploaded, test.Storage.Deleted);
    }

    [Fact]
    public async Task Account_without_avatar_returns_not_found_and_upload_has_no_target_user_parameter()
    {
        var test = new Scenario();
        Assert.Equal(404, (await Assert.ThrowsAsync<AuthFailure>(() => test.UseCases.ReadAsync(test.User.Id, default))).Status);
        Assert.Equal("RESIDENT", typeof(AccountAvatarController).GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        Assert.DoesNotContain(typeof(AccountAvatarController).GetMethod("Upload")!.GetParameters(), x => x.ParameterType == typeof(Guid));
    }

    private sealed class Scenario
    {
        public UserAccount User { get; } = new("resident", "hash", "Cư dân", DateTimeOffset.UtcNow, status: AccountStatus.ACTIVE);
        public Storage Storage { get; } = new();
        public bool FailCommit;
        public AuthUseCases Auth { get; }
        public AccountAvatarUseCases UseCases { get; }
        public Scenario()
        {
            var store = Proxy.Make<IAuthStore>((method, args) => method.Name switch
            {
                "UserAsync" => Task.FromResult((Guid)args![0]! == User.Id ? User : null),
                "SerializedAsync" => Serialize(method, args!),
                _ => throw new NotSupportedException(method.Name)
            });
            var access = Proxy.Make<IAccountAccess>((_, _) => Task.FromResult<AccountAccess?>(new("RESIDENT", [])));
            Auth = new(store, null!, null!, null!, null!, access, new AuthPolicy(), TimeProvider.System);
            UseCases = new(store, Storage, Auth, TimeProvider.System, NullLogger<AccountAvatarUseCases>.Instance);
        }
        private object Serialize(MethodInfo method, object?[] args) => typeof(Scenario).GetMethod(nameof(SerializeCore), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(method.GetGenericArguments()).Invoke(this, [args[1]])!;
        private async Task<T> SerializeCore<T>(Func<Task<T>> action)
        {
            var result = await action();
            if (FailCommit) throw new InvalidOperationException("Simulated commit failure");
            return result;
        }
        public Task<PropFlow.Modules.Authentication.Contracts.AccountResponse> Upload() => UseCases.UploadAsync(User.Id, new MemoryStream(Jpeg), Jpeg.Length, "image/jpeg", default);
    }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler = null!;
        protected override object? Invoke(MethodInfo? target, object?[]? args) => Handler(target!, args);
        public static T Make<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            var instance = Create<T, Proxy>(); ((Proxy)(object)instance).Handler = handler; return instance;
        }
    }
    private sealed class Storage : IAccountAvatarStorage
    {
        public List<string> Uploaded = [], Deleted = [];
        public string? ReadKey;
        public Task<string> UploadAsync(Guid owner, Stream stream, string contentType, CancellationToken ct)
        { var key = Guid.NewGuid().ToString(); Uploaded.Add(key); return Task.FromResult(key); }
        public Task DeleteAsync(string key, CancellationToken ct) { Deleted.Add(key); return Task.CompletedTask; }
        public Task<byte[]> ReadAsync(string key, CancellationToken ct) { ReadKey = key; return Task.FromResult(Jpeg); }
    }
}
