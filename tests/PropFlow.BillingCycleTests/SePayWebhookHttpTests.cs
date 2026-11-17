using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PropFlow.Modules.Payments.Application.PaymentSettings;
using PropFlow.Modules.Payments.Domain.PaymentDestinations;
using PropFlow.Modules.Payments.Domain.PaymentInstructions;
using PropFlow.Modules.Payments.Domain.Payments;
using PropFlow.Modules.Payments.Infrastructure.Persistence;
using PropFlow.Modules.Payments.Infrastructure.ResidentPayments;

namespace PropFlow.BillingCycleTests;

public sealed partial class BillingCyclePostgresTests
{
    [Theory]
    [InlineData("API_KEY")]
    [InlineData("HMAC")]
    public async Task SePay_http_requires_valid_provider_credentials_and_settles_concurrent_delivery_once(string mode)
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await using var root = new Factory(connection, user, apartment);
        await using var factory = root.WithWebHostBuilder(host => host.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:SePay:Enabled"] = "true",
                ["Payments:SePay:WebhookPublicBaseUrl"] = "https://sepay-fixture.example.invalid",
                ["Payments:SePay:AuthenticationMode"] = mode,
                ["Payments:SePay:WebhookApiKey"] = secret,
                ["Payments:SePay:WebhookSecret"] = secret
            })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new("https://localhost") });
        Guid invoiceId;
        PaymentInstruction instruction;
        const string accountNumber = "123456789";
        await using (var billing = Billing())
        await using (var payments = Payments())
        await using (var communication = Communication())
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var cycle = Cycle(billing, payments, communication);
            await cycle.SaveRateAsync(new(10000, Month), actor, default);
            await cycle.RunAsync(default);
            invoiceId = await billing.Invoices.Select(invoice => invoice.Id).SingleAsync();
            var protector = scope.ServiceProvider.GetRequiredService<IPaymentAccountProtector>();
            var now = DateTimeOffset.UtcNow;
            var destination = new PaymentDestination(1, "970436", "VCB", "Vietcombank",
                protector.Protect(accountNumber), "6789", "TEST BANK OWNER", actor, now, Guid.NewGuid());
            instruction = new PaymentInstruction(invoiceId, destination.Id, now);
            payments.AddRange(destination, instruction);
            await payments.SaveChangesAsync();
        }

        async Task<(int Inbox, int Payments, int History, int Operations, int Notices, int Emails, decimal Balance)> Snapshot()
        {
            await using var billing = Billing();
            await using var payments = Payments();
            await using var communication = Communication();
            return (await payments.BankTransfers.CountAsync(), await payments.Payments.CountAsync(),
                await payments.PaymentStatusHistory.CountAsync(), await payments.PaymentOperations.CountAsync(),
                await communication.Notifications.CountAsync(), await communication.FinanceEmails.CountAsync(),
                (await Cycle(billing, payments, communication).InvoicesAsync(user, default)).Single().Outstanding);
        }

        async Task<(HttpStatusCode Status, string? Code)> Send(string body, string? credential, long timestampOffset = 0)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/webhooks/sepay");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (credential is not null && mode == "API_KEY")
                request.Headers.TryAddWithoutValidation("Authorization", "Apikey " + credential);
            if (credential is not null && mode == "HMAC")
            {
                var stamp = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + timestampOffset).ToString(CultureInfo.InvariantCulture);
                var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(credential), Encoding.UTF8.GetBytes(stamp + "." + body));
                request.Headers.Add("X-SePay-Timestamp", stamp);
                request.Headers.Add("X-SePay-Signature", "sha256=" + Convert.ToHexString(signature));
            }
            using var response = await client.SendAsync(request);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(response.StatusCode == HttpStatusCode.OK, json.RootElement.GetProperty("success").GetBoolean());
            return (response.StatusCode, json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null);
        }

        static string Body(SePayTransfer transfer) => JsonSerializer.Serialize(transfer);
        var transfer = new SePayTransfer(810000000001, "VCB", accountNumber, "in", 700000, instruction.TransferCode,
            null, "FIXTURE-REF", DateTimeOffset.UtcNow.AddDays(-5).ToString("O"));
        var body = Body(transfer);
        var before = await Snapshot();
        Assert.Equal(700000m, before.Balance);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(body, null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(body, "fixture-invalid")).Status);
        if (mode == "HMAC") Assert.Equal(HttpStatusCode.Unauthorized, (await Send(body, secret, -301)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send("{", secret)).Status);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_bank_transfer"),
            await Send(Body(transfer with { TransferAmount = 5.5m }), secret));
        Assert.Equal(before, await Snapshot());

        var unlinked = transfer with { Id = transfer.Id + 1, Code = "PF0000000000000000000000" };
        var otherDestination = transfer with { Id = transfer.Id + 2, AccountNumber = "987654321" };
        Assert.Equal(HttpStatusCode.OK, (await Send(Body(unlinked), secret)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Send(Body(otherDestination), secret)).Status);
        await Process(unlinked.Id);
        await Process(otherDestination.Id);
        var afterRejected = await Snapshot();
        Assert.Equal((before.Inbox + 2, before.Payments, before.History, before.Operations, before.Notices, before.Emails, before.Balance), afterRejected);

        var deliveries = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Send(body, secret)));
        Assert.All(deliveries, result => Assert.Equal(HttpStatusCode.OK, result.Status));
        Assert.Equal((HttpStatusCode.Conflict, "sepay_transaction_conflict"),
            await Send(Body(transfer with { TransferAmount = 1m }), secret));
        Assert.Equal(afterRejected with { Inbox = afterRejected.Inbox + 1 }, await Snapshot());

        await using (var payments = Payments())
        {
            await payments.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION payments.reject_fixture_payment() RETURNS trigger LANGUAGE plpgsql AS
                $$ BEGIN RAISE EXCEPTION 'Synthetic payment storage failure'; END; $$;
                CREATE TRIGGER reject_fixture_payment BEFORE INSERT ON payments.payments
                FOR EACH ROW EXECUTE FUNCTION payments.reject_fixture_payment();
                """);
            try
            {
                await Assert.ThrowsAsync<DbUpdateException>(() => Process(transfer.Id));
                var deferred = await payments.BankTransfers.AsNoTracking().SingleAsync(row => row.TransactionId == transfer.Id);
                Assert.Equal("RETRY", deferred.Status);
                Assert.Equal("PROCESSING_ERROR", deferred.Outcome);
                Assert.Equal(1, deferred.Attempts);
                Assert.Equal(afterRejected with { Inbox = afterRejected.Inbox + 1 }, await Snapshot());
            }
            finally
            {
                await payments.Database.ExecuteSqlRawAsync("""
                    DROP TRIGGER reject_fixture_payment ON payments.payments;
                    DROP FUNCTION payments.reject_fixture_payment();
                    """);
            }
            await payments.BankTransfers.Where(row => row.TransactionId == transfer.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }
        await Task.WhenAll(Process(transfer.Id), Process(transfer.Id));
        var confirmed = await Snapshot();
        Assert.Equal(before.Payments + 1, confirmed.Payments);
        Assert.Equal(before.Operations + 1, confirmed.Operations);
        Assert.Equal(0m, confirmed.Balance);
        using (var readScope = factory.Services.CreateScope())
        {
            var payments = readScope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var rows = await payments.BankTransfers.AsNoTracking().ToDictionaryAsync(row => row.TransactionId);
            Assert.Equal("UNMATCHED_CODE", rows[unlinked.Id].Outcome);
            Assert.Equal("ACCOUNT_MISMATCH", rows[otherDestination.Id].Outcome);
            Assert.Equal("CONFIRMED", rows[transfer.Id].Outcome);
            Assert.Equal(invoiceId, rows[transfer.Id].InvoiceId);
            Assert.Equal(700000m, rows[transfer.Id].AllocatedAmount);
            Assert.Equal(PaymentStatus.CONFIRMED, (await payments.Payments.SingleAsync()).Status);
        }
        Assert.Equal(before.Notices + 1, confirmed.Notices);
        Assert.Equal(before.Emails + 1, confirmed.Emails);
        Assert.Equal(HttpStatusCode.OK, (await Send(body, secret)).Status);
        await Process(transfer.Id);
        Assert.Equal(confirmed, await Snapshot());

        async Task Process(long id)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var inboxId = await payments.BankTransfers.Where(row => row.TransactionId == id).Select(row => row.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<SePayInbox>().ProcessAsync(inboxId, default);
        }
    }
}
