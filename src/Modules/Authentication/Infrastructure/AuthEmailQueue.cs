using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Authentication.Application;

namespace PropFlow.Modules.Authentication.Infrastructure;

public sealed class AuthMailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string From { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

// A bounded memory queue decouples recovery response timing from account existence/SMTP latency.
// Pending messages are not durable across a process restart; users can request another code.
public sealed class AuthEmailQueue(AuthMailOptions options, ILogger<AuthEmailQueue> logger) : BackgroundService, IAuthEmail
{
    private sealed record Mail(string Address, string Code, bool Recovery, int Minutes, DateTimeOffset CreatedAt);
    private readonly Channel<Mail> queue = Channel.CreateBounded<Mail>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.Wait });
    public Task SendCodeAsync(string email, string code, bool recovery, int expiryMinutes, CancellationToken ct)
    {
        if (!queue.Writer.TryWrite(new(email, code, recovery, expiryMinutes, DateTimeOffset.UtcNow)))
            logger.LogWarning("Authentication email queue is full; a new code may be requested later.");
        return Task.CompletedTask;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            for (var attempt = 0; attempt < 3 && item.CreatedAt.AddMinutes(item.Minutes) > DateTimeOffset.UtcNow; attempt++)
            {
                try
                {
                    using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = true,
                        Credentials = new NetworkCredential(options.Username, options.Password), Timeout = 15000 };
                    using var mail = new MailMessage(options.From, item.Address)
                    {
                        Subject = item.Recovery ? "PropFlow — Khôi phục mật khẩu" : "PropFlow — Xác minh tài khoản cư dân",
                        Body = $"Mã xác minh của bạn: {item.Code}\nMã có hiệu lực {item.Minutes} phút kể từ khi yêu cầu. Không chia sẻ mã này. Nếu bạn không yêu cầu, hãy bỏ qua email này."
                    };
                    await client.SendMailAsync(mail, stoppingToken);
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
                {
                    // Never log the exception text, recipient, OTP or message body.
                    logger.LogWarning("Authentication email delivery failed (attempt {Attempt}).", attempt + 1);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
        }
    }
}
