using System.Collections.Concurrent;
using PropFlow.Modules.Authentication.Contracts;

namespace PropFlow.UnitTests;

// Contract-level fake: it records the exact user/purpose/binding Remote supplies.
// It intentionally contains no production OTP persistence or mail delivery.
public sealed class RemoteOnlineSignOtpSecurityTests
{
    [Fact] public async Task Valid_code_is_accepted_once() { var otp = new StatefulOtp(); var c = await otp.RequestAsync(Guid.NewGuid(), "REMOTE_ONLINE_SIGN", "binding-a", default); await otp.VerifyAsync(otp.User(c.ChallengeId), c.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "123456", default); await Assert.ThrowsAsync<InvalidOperationException>(() => otp.VerifyAsync(otp.User(c.ChallengeId), c.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "123456", default)); }
    [Fact] public async Task Wrong_code_and_attempt_limit_are_rejected() { var otp = new StatefulOtp(maxAttempts: 2); var c = await otp.RequestAsync(Guid.NewGuid(), "REMOTE_ONLINE_SIGN", "binding-a", default); await Assert.ThrowsAsync<InvalidOperationException>(() => otp.VerifyAsync(otp.User(c.ChallengeId), c.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "bad", default)); await Assert.ThrowsAsync<InvalidOperationException>(() => otp.VerifyAsync(otp.User(c.ChallengeId), c.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "bad", default)); await Assert.ThrowsAsync<InvalidOperationException>(() => otp.VerifyAsync(otp.User(c.ChallengeId), c.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "123456", default)); }
    [Fact] public async Task Expired_and_resended_challenges_are_rejected() { var otp = new StatefulOtp(); var user = Guid.NewGuid(); var old = await otp.RequestAsync(user, "REMOTE_ONLINE_SIGN", "binding-a", default); otp.Expire(old.ChallengeId); await Assert.ThrowsAsync<InvalidOperationException>(() => otp.VerifyAsync(user, old.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "123456", default)); var first = await otp.RequestAsync(user, "REMOTE_ONLINE_SIGN", "binding-b", default); var replacement = await otp.RequestAsync(user, "REMOTE_ONLINE_SIGN", "binding-b", default); await Assert.ThrowsAsync<InvalidOperationException>(() => otp.VerifyAsync(user, first.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-b", "123456", default)); await otp.VerifyAsync(user, replacement.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-b", "123456", default); }
    [Theory]
    [InlineData("wrong-user", "REMOTE_ONLINE_SIGN", "binding-a")]
    [InlineData("same-user", "OTHER_PURPOSE", "binding-a")]
    [InlineData("same-user", "REMOTE_ONLINE_SIGN", "binding-other-contract")]
    public async Task User_purpose_or_binding_mismatch_is_rejected(string userMode, string purpose, string binding) { var otp = new StatefulOtp(); var user = Guid.NewGuid(); var c = await otp.RequestAsync(user, "REMOTE_ONLINE_SIGN", "binding-a", default); var actual = userMode == "wrong-user" ? Guid.NewGuid() : user; await Assert.ThrowsAsync<InvalidOperationException>(() => otp.VerifyAsync(actual, c.ChallengeId, purpose, binding, "123456", default)); }
    [Fact] public async Task Concurrent_verification_consumes_only_once() { var otp = new StatefulOtp(); var c = await otp.RequestAsync(Guid.NewGuid(), "REMOTE_ONLINE_SIGN", "binding-a", default); var user = otp.User(c.ChallengeId); var results = await Task.WhenAll(Capture(() => otp.VerifyAsync(user, c.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "123456", default)), Capture(() => otp.VerifyAsync(user, c.ChallengeId, "REMOTE_ONLINE_SIGN", "binding-a", "123456", default))); Assert.Single(results, x => x is null); Assert.Single(results, x => x is not null); }
    private static async Task<Exception?> Capture(Func<Task> action) { try { await action(); return null; } catch (Exception e) { return e; } }

    private sealed class StatefulOtp(int maxAttempts = 3) : IPrivilegedActionOtp
    {
        private sealed record Challenge(Guid User, string Purpose, string Binding, DateTimeOffset Expires, int Attempts, bool Used, bool Cancelled);
        private readonly ConcurrentDictionary<Guid, Challenge> items = new();
        public Task<PrivilegedActionChallengeResult> RequestAsync(Guid userId, string purpose, string payloadHash, CancellationToken ct) { foreach (var item in items.Where(x => x.Value.User == userId && x.Value.Purpose == purpose && !x.Value.Used)) items[item.Key] = item.Value with { Cancelled = true }; var id = Guid.NewGuid(); var expires = DateTimeOffset.UtcNow.AddMinutes(5); items[id] = new(userId, purpose, payloadHash, expires, 0, false, false); return Task.FromResult(new PrivilegedActionChallengeResult(id, expires, DateTimeOffset.UtcNow, "***")); }
        public Task VerifyAsync(Guid userId, Guid challengeId, string purpose, string payloadHash, string code, CancellationToken ct) { while (true) { if (!items.TryGetValue(challengeId, out var old) || old.User != userId || old.Purpose != purpose || old.Binding != payloadHash || old.Cancelled || old.Used || old.Expires <= DateTimeOffset.UtcNow) throw new InvalidOperationException("OTP rejected"); var next = code == "123456" ? old with { Used = true } : old with { Attempts = old.Attempts + 1, Cancelled = old.Attempts + 1 >= maxAttempts }; if (items.TryUpdate(challengeId, next, old)) { if (code != "123456") throw new InvalidOperationException("OTP rejected"); return Task.CompletedTask; } } }
        public Guid User(Guid id) => items[id].User; public void Expire(Guid id) => items[id] = items[id] with { Expires = DateTimeOffset.UtcNow.AddSeconds(-1) };
    }
}
