using PropFlow.Modules.Authentication.Domain.PrivilegedActions;

namespace PropFlow.UnitTests;

public sealed class PrivilegedActionChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Challenge_IsSingleUse()
    {
        var challenge = Create();

        challenge.Consume(Now.AddMinutes(1));

        Assert.False(challenge.IsActiveAt(Now.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => challenge.Consume(Now.AddMinutes(2)));
    }

    [Fact]
    public void Challenge_LocksAfterMaximumFailedAttempts()
    {
        var challenge = Create();

        for (var attempt = 0; attempt < 5; attempt++)
            challenge.RecordFailedAttempt(Now.AddSeconds(attempt), maximumAttempts: 5);

        Assert.Equal(5, challenge.AttemptCount);
        Assert.False(challenge.IsActiveAt(Now.AddMinutes(1)));
        Assert.NotNull(challenge.UsedAt);
    }

    [Fact]
    public void Challenge_ExpiresAtConfiguredTime()
    {
        var challenge = Create();

        Assert.True(challenge.IsActiveAt(Now.AddMinutes(4).AddSeconds(59)));
        Assert.False(challenge.IsActiveAt(Now.AddMinutes(5)));
    }

    private static PrivilegedActionChallenge Create() =>
        new(Guid.NewGuid(), " payment_destination_change ", new string('A', 64), "hash", Now.AddMinutes(5), Now);
}
