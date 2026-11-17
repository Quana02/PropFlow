using System.Security.Cryptography;
using System.Text.Json;

namespace PropFlow.Modules.Authentication.Application;

public static class SessionAccess
{
    public static string Fingerprint(string role, IEnumerable<string> permissions)
    {
        var grants = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Prepend(role);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(grants)));
    }
}
