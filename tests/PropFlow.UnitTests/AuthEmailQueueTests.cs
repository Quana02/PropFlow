using Microsoft.Extensions.Logging.Abstractions;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Infrastructure;

namespace PropFlow.UnitTests;

public sealed class AuthEmailQueueTests
{
    [Fact]
    public async Task Full_registration_queue_reports_delivery_failure_instead_of_success()
    {
        using var queue = await FullQueueAsync();
        var failure = await Assert.ThrowsAsync<AuthFailure>(() => queue.SendCodeAsync(
            "queue-test@example.invalid", "000000", false, 10, CancellationToken.None));
        Assert.Equal(503, failure.Status);
        Assert.Equal("email_delivery_unavailable", failure.Code);
    }

    [Fact]
    public async Task Full_recovery_queue_keeps_neutral_response()
    {
        using var queue = await FullQueueAsync();
        await queue.SendCodeAsync("queue-test@example.invalid", "000000", true, 10, CancellationToken.None);
    }

    private static async Task<AuthEmailQueue> FullQueueAsync()
    {
        var queue = new AuthEmailQueue(new AuthMailOptions(), NullLogger<AuthEmailQueue>.Instance);
        for (var i = 0; i < 200; i++)
            await queue.SendCodeAsync("queue-test@example.invalid", "000000", false, 10, CancellationToken.None);
        return queue;
    }
}
