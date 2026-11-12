using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PropFlow.Modules.Communication.Application.SupportChat;
using PropFlow.Modules.Communication.Contracts;

namespace PropFlow.Modules.Communication.Infrastructure;

public sealed class EmergencyRtcOptions
{
    public string[] StunUrls { get; set; } = ["stun:stun.l.google.com:19302"];
    public string[] TurnUrls { get; set; } = [];
    public string TurnSharedSecret { get; set; } = "";
    public int TurnCredentialLifetimeSeconds { get; set; } = 600;
    public bool Valid => StunUrls.Length <= 8 && TurnUrls.Length <= 8 &&
        StunUrls.All(x => ValidUrl(x, "stun:", "stuns:")) && TurnUrls.All(x => ValidUrl(x, "turn:", "turns:")) &&
        (TurnUrls.Length == 0 || !string.IsNullOrWhiteSpace(TurnSharedSecret)) && TurnCredentialLifetimeSeconds is >= 300 and <= 3600;
    private static bool ValidUrl(string value, string first, string second) => value.Length is > 5 and <= 512 &&
        !value.Any(char.IsWhiteSpace) && !value.Contains('@') && (value.StartsWith(first, StringComparison.OrdinalIgnoreCase) || value.StartsWith(second, StringComparison.OrdinalIgnoreCase));
}
public sealed class EmergencyRtcConfigurationProvider(IOptions<EmergencyRtcOptions> options, ISupportChatActors actors, TimeProvider clock) : PropFlow.Modules.Communication.Application.EmergencyCalls.IEmergencyRtcConfiguration
{
    public async Task<EmergencyRtcConfiguration> GetAsync(Guid user, CancellationToken ct)
    {
        if ((await actors.FindAsync(user, ct))?.Role is not ("RESIDENT" or "MANAGER"))
            throw new SupportChatFailure(403, "emergency_forbidden", "Tài khoản chưa đủ điều kiện gọi hỗ trợ.");
        var value = options.Value;
        var servers = new List<EmergencyIceServer>();
        if (value.StunUrls.Length > 0) servers.Add(new(value.StunUrls));
        if (value.TurnUrls.Length > 0)
        {
            var username = $"{clock.GetUtcNow().AddSeconds(value.TurnCredentialLifetimeSeconds).ToUnixTimeSeconds()}:{user:N}";
            var credential = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(value.TurnSharedSecret), Encoding.UTF8.GetBytes(username)));
            servers.Add(new(value.TurnUrls, username, credential));
        }
        return new(servers, value.TurnUrls.Length > 0);
    }
}
