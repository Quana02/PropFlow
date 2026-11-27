namespace PropFlow.Web.Client.Features.RemoteOnlineSign.Models;

public sealed record RemoteSignaturePayload(string Method, byte[] Image);
