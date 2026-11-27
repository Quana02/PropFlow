using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PropFlow.Modules.Communication.Infrastructure;
using PropFlow.Modules.Communication.Infrastructure.Persistence;

namespace PropFlow.BillingCycleTests;

public sealed partial class BillingCyclePostgresTests
{
    [Fact]
    public async Task Finance_delivery_config_is_independent_from_authentication_smtp()
    {
        await using var factory=new Factory(connection,user,apartment);
        await using var configured=factory.WithWebHostBuilder(b=>b.ConfigureAppConfiguration((_,c)=>
            c.AddInMemoryCollection(new Dictionary<string,string?> { ["FinanceEmail:Enabled"]="false" })));
        Assert.False(configured.Services.GetRequiredService<IOptions<FinanceMailOptions>>().Value.Enabled);
        Assert.Equal("invalid.test",configured.Services.GetRequiredService<IOptions<PropFlow.Modules.Authentication.Infrastructure.AuthMailOptions>>().Value.Host);
    }

    [Fact]
    public async Task Disabled_finance_delivery_preserves_due_queue_and_does_not_connect_to_smtp()
    {
        await VerifyNoFinanceDelivery("real@resident.local", enabled: false);
    }

    [Theory]
    [InlineData("demo@example.com")]
    [InlineData("demo@EXAMPLE.NET")]
    [InlineData("demo@tenant.example.org")]
    [InlineData("demo@resident.example")]
    [InlineData("demo@resident.invalid")]
    public async Task Reserved_demo_recipient_is_deferred_without_smtp_or_fake_delivery(string address)
    {
        await VerifyNoFinanceDelivery(address, enabled: true);
    }

    private async Task VerifyNoFinanceDelivery(string address, bool enabled)
    {
        var id=Guid.NewGuid();
        var due=DateTimeOffset.UtcNow.AddMinutes(-1);
        await using(var db=Communication())
        {
            db.FinanceEmails.Add(new() { Id=id, EventId=Guid.NewGuid(), UserId=user,
                Address=address, Subject="Synthetic fixture invoice", Html="<p>Fixture</p>", NextAttemptAt=due });
            await db.SaveChangesAsync();
        }
        var listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);
        listener.Start();
        try
        {
            var services=new ServiceCollection();services.AddScoped(_=>Communication());
            services.AddSingleton<PropFlow.Modules.Communication.Application.IFinanceEmailRecipientDirectory>(new QueuedRecipientDirectory(address));
            await using var provider=services.BuildServiceProvider();
            using var worker=new FinanceEmailWorker(provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new FinanceMailOptions { Enabled=enabled, Host="127.0.0.1",
                    Port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port, From="fixture@propflow.local", EnableSsl=false, PollSeconds=1 }),
                NullLogger<FinanceEmailWorker>.Instance);
            await worker.StartAsync(default);
            try
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(6));
                while(!timeout.IsCancellationRequested)
                {
                    Assert.False(listener.Pending(),"Finance worker must not contact SMTP for paused delivery or reserved demo addresses.");
                    await using var check=Communication();
                    var mail=await check.FinanceEmails.AsNoTracking().SingleAsync(x=>x.Id==id);
                    if(enabled && mail.NextAttemptAt>DateTimeOffset.UtcNow.AddHours(20))break;
                    await Task.Delay(100);
                }
            }
            finally { await worker.StopAsync(default); }
            await using var verify=Communication();var saved=await verify.FinanceEmails.SingleAsync(x=>x.Id==id);
            Assert.Null(saved.SentAt);Assert.Equal(0,saved.Attempts);
            if(enabled)Assert.True(saved.NextAttemptAt>DateTimeOffset.UtcNow.AddHours(20));
            else Assert.Equal(due.ToUnixTimeMilliseconds(),saved.NextAttemptAt.ToUnixTimeMilliseconds());
            Assert.False(listener.Pending());
        }
        finally { listener.Stop(); }
    }
}
