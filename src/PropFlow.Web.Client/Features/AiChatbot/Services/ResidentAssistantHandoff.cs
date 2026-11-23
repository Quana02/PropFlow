namespace PropFlow.Web.Client.Features.AiChatbot.Services;

// One-use handoff within the current WASM session; never put conversation contents in URLs or storage.
public sealed class ResidentAssistantHandoff
{
    private string? question;
    public void Set(string value) => question = value.Trim()[..Math.Min(value.Trim().Length, 1000)];
    public string? Take() { var value = question; question = null; return value; }
}
