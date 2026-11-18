using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Transactions;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PropFlow.Modules.Authentication.Infrastructure.Persistence;

namespace PropFlow.IntegrationTests;

public sealed partial class AuthenticationFlowTests
{
    [Fact]
    [Trait("Security", "PF-AUTHZ-04")]
    public async Task Revocation_timing_observes_database_commit_registry_notification_and_both_same_sid_transport_closures()
    {
        var account = await SeedRealtimeAccount("RESIDENT");
        using var browser = Client();
        using var otherBrowser = Client();
        var session = await SignIn(browser, account.Username);
        var otherSession = await SignIn(otherBrowser, account.Username);
        await using var first = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "notifications", session.AccessToken);
        await using var second = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "notifications", session.AccessToken);
        await using var unaffected = AuthenticationTestSessions.ConnectWebSocket(database.Factory, "notifications", otherSession.AccessToken);
        await first.StartAsync(); await second.StartAsync(); await unaffected.StartAsync();
        var clock = new Stopwatch();
        long committed = -1, registry = -1, firstClosed = -1, secondClosed = -1;
        var armed = 0;
        var subscribedTransaction = 0;
        var subscriptions = new ConcurrentBag<IDisposable>();
        using var diagnostics = DiagnosticListener.AllListeners.Subscribe(new TimingObserver<DiagnosticListener>(listener =>
        {
            if (listener.Name != "Microsoft.EntityFrameworkCore") return;
            subscriptions.Add(listener.Subscribe(new TimingObserver<KeyValuePair<string, object?>>(item =>
            {
                if (Volatile.Read(ref armed) == 0 || !item.Key.EndsWith(".CommandExecuted", StringComparison.Ordinal)
                    || item.Value is not CommandEventData { Context: AuthenticationDbContext }
                    || Transaction.Current is not { } transaction
                    || Interlocked.CompareExchange(ref subscribedTransaction, 1, 0) != 0) return;
                transaction.TransactionCompleted += (_, completed) =>
                {
                    if (completed.Transaction?.TransactionInformation.Status == TransactionStatus.Committed)
                        Interlocked.CompareExchange(ref committed, clock.ElapsedTicks, -1);
                };
            })));
        }));
        using var meter = new MeterListener();
        meter.InstrumentPublished = (instrument, observer) =>
        {
            if (instrument.Meter.Name == "PropFlow.Authorization" && instrument.Name == "propflow.signalr.revocations")
                observer.EnableMeasurementEvents(instrument);
        };
        meter.SetMeasurementEventCallback<int>((_, _, _, _) =>
        {
            if (Volatile.Read(ref armed) == 1) Interlocked.CompareExchange(ref registry, clock.ElapsedTicks, -1);
        });
        meter.Start();
        var bothClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Closed += _ =>
        {
            Interlocked.CompareExchange(ref firstClosed, clock.ElapsedTicks, -1);
            if (Volatile.Read(ref secondClosed) >= 0) bothClosed.TrySetResult();
            return Task.CompletedTask;
        };
        second.Closed += _ =>
        {
            Interlocked.CompareExchange(ref secondClosed, clock.ElapsedTicks, -1);
            if (Volatile.Read(ref firstClosed) >= 0) bothClosed.TrySetResult();
            return Task.CompletedTask;
        };
        try
        {
            clock.Start();
            Volatile.Write(ref armed, 1);
            using var response = await Post(browser, "logout", new { }, true);
            Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
            await bothClosed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(committed >= 0, "The ambient PostgreSQL transaction commit was not observed.");
            Assert.True(registry >= committed, "The registry revoked a connection before the database transaction committed.");
            Assert.True(firstClosed >= registry && secondClosed >= registry);
            var lastClosed = Math.Max(firstClosed, secondClosed);
            Assert.InRange(Milliseconds(lastClosed - committed), 0, 2000);
            Assert.Equal(HubConnectionState.Connected, unaffected.State);
            Assert.Equal(System.Net.HttpStatusCode.OK, await BearerStatus(otherSession.AccessToken));
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, await BearerStatus(session.AccessToken));
            output.WriteLine($"PF04 timeline samples=1 connections=2 T0CommitMs={Milliseconds(committed):F2} T1RegistryMs={Milliseconds(registry):F2} T2LastClosedMs={Milliseconds(lastClosed):F2} CommitToLastClosedMs={Milliseconds(lastClosed - committed):F2}");
        }
        finally
        {
            Volatile.Write(ref armed, 0);
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
        static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    }

    private sealed class TimingObserver<T>(Action<T> next) : IObserver<T>
    {
        public void OnNext(T value) => next(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
