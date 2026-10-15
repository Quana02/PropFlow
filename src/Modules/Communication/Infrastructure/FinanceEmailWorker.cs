using System.Net;
using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Communication.Infrastructure.Persistence;
namespace PropFlow.Modules.Communication.Infrastructure;
public sealed class FinanceMailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string From { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool EnableSsl { get; set; } = true;
    public int PollSeconds { get; set; } = 30;
}
public sealed class FinanceEmailWorker(IServiceScopeFactory scopes, IOptions<FinanceMailOptions> config,
    ILogger<FinanceEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(config.Value.PollSeconds, 1, 600)));
        try { while (await timer.WaitForNextTickAsync(ct)) {
            var options = config.Value;
            if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From)) continue;
            try {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<CommunicationDbContext>();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var mails = await db.FinanceEmails.FromSqlRaw("SELECT * FROM communication.finance_emails WHERE sent_at IS NULL AND next_attempt_at <= now() ORDER BY next_attempt_at LIMIT 10 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
                foreach (var item in mails) {
                    // Demo accounts use reserved domains and must never send to external mailboxes.
                    if (item.Address.EndsWith(".example", StringComparison.OrdinalIgnoreCase) || item.Address.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)) {
                        item.NextAttemptAt = DateTimeOffset.UtcNow.AddDays(1); continue;
                    }
                    try {
                        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = options.EnableSsl,
                            Credentials = new NetworkCredential(options.Username, options.Password), Timeout = 15000 };
                        using var mail = new MailMessage(options.From, item.Address) { Subject = item.Subject, Body = item.Html, IsBodyHtml = true };
                        mail.Headers.Add("X-PropFlow-Event", item.EventId.ToString());
                        // The complete receipt is rendered in the email body for direct viewing.
                        // Do not attach a duplicate HTML file that mail clients display as source.
                        await client.SendMailAsync(mail, ct); item.SentAt = DateTimeOffset.UtcNow;
                    } catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException) {
                        item.Attempts++; item.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(60, 2 * item.Attempts));
                        logger.LogWarning("Finance email delivery deferred; attempt {Attempt}.", item.Attempts);
                    }
                }
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
              catch (Exception ex) { logger.LogWarning("Finance email dispatch failed ({Type}).", ex.GetType().Name); }
        } } catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
