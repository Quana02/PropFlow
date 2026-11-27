using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropFlow.Modules.Authentication.Application;
using PropFlow.Modules.Authentication.Contracts;

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
    private enum MailPurpose { Registration, Recovery, PrivilegedAction, RemoteOnlineSign, EmailChange }
    private sealed record Mail(string Address, string Code, MailPurpose Purpose, int Minutes, DateTimeOffset CreatedAt, string? ContractName = null);
    private readonly Channel<Mail> queue = Channel.CreateBounded<Mail>(new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.Wait });
    public Task SendCodeAsync(string email, string code, bool recovery, int expiryMinutes, CancellationToken ct)
    {
        if (!queue.Writer.TryWrite(new(email, code, recovery ? MailPurpose.Recovery : MailPurpose.Registration, expiryMinutes, DateTimeOffset.UtcNow)))
        {
            logger.LogWarning("Authentication email queue is full; a new code may be requested later.");
            if (!recovery)
                throw new AuthFailure(503, "email_delivery_unavailable", "Chưa thể gửi mã xác minh. Vui lòng thử lại sau.");
        }
        return Task.CompletedTask;
    }
    public Task SendPrivilegedActionCodeAsync(string email, string code, int expiryMinutes, CancellationToken ct) =>
        SendPrivilegedActionCodeAsync(email, code, expiryMinutes, string.Empty, null, ct);
    public Task SendPrivilegedActionCodeAsync(string email, string code, int expiryMinutes, string purpose, PrivilegedActionEmailContext? context, CancellationToken ct)
    {
        var remoteSigning = string.Equals(purpose, "REMOTE_ONLINE_SIGN", StringComparison.Ordinal);
        var mailPurpose = remoteSigning ? MailPurpose.RemoteOnlineSign : MailPurpose.PrivilegedAction;
        var contractName = remoteSigning ? context?.ContractName : null;
        if (!queue.Writer.TryWrite(new(email, code, mailPurpose, expiryMinutes, DateTimeOffset.UtcNow, contractName)))
            throw new AuthFailure(503, "email_delivery_unavailable", "Chưa thể gửi mã xác minh. Vui lòng thử lại sau.");
        return Task.CompletedTask;
    }
    public Task SendEmailChangeCodeAsync(string email, string code, int expiryMinutes, CancellationToken ct)
    {
        if (!queue.Writer.TryWrite(new(email, code, MailPurpose.EmailChange, expiryMinutes, DateTimeOffset.UtcNow)))
            throw new AuthFailure(503, "email_delivery_unavailable", "Chưa thể gửi mã xác minh. Vui lòng thử lại sau.");
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
                    var remoteSigning = item.Purpose == MailPurpose.RemoteOnlineSign;
                    using var mail = new MailMessage(options.From, item.Address)
                    {
                        SubjectEncoding = Encoding.UTF8,
                        BodyEncoding = Encoding.UTF8,
                        Subject = item.Purpose switch
                        {
                            MailPurpose.Recovery => "PropFlow — Khôi phục mật khẩu",
                            MailPurpose.RemoteOnlineSign => "PropFlow — Xác nhận ký hợp đồng điện tử",
                            MailPurpose.PrivilegedAction => "PropFlow — Xác nhận thay đổi cấu hình thanh toán",
                            MailPurpose.EmailChange => "PropFlow — Xác minh email mới của tài khoản",
                            _ => "PropFlow — Xác minh tài khoản cư dân"
                        },
                        Body = remoteSigning
                            ? $"Hệ thống PropFlow gửi bạn mã xác minh OTP ký hợp đồng {item.ContractName ?? ""} có hiệu lực {item.Minutes} phút kể từ khi yêu cầu.\n\nMã xác minh OTP của bạn là:\n\n{item.Code}\n\nKhông chia sẻ mã này.\n\nNếu bạn không yêu cầu, hãy kiểm tra lại tài khoản để hủy xác nhận ký hợp đồng.\n\nTrân trọng,\nHệ thống PropFlow."
                            : $"Mã xác minh của bạn: {item.Code}\nMã có hiệu lực {item.Minutes} phút kể từ khi yêu cầu. Không chia sẻ mã này. Nếu bạn không yêu cầu, hãy bỏ qua email này."
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
