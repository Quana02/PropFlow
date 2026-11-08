using PropFlow.Web.Client.Features.Finance.Services;

public sealed class LatestRequestTests
{
    [Fact]
    public async Task Superseded_response_cannot_replace_the_new_search_even_when_transport_ignores_cancellation()
    {
        using var requests = new LatestRequest();
        var slow = new TaskCompletionSource<string>();
        string? visible = null;
        var first = requests.RunAsync(_ => slow.Task, value => visible = value);
        Assert.True(await requests.RunAsync(_ => Task.FromResult("new"), value => visible = value));
        slow.SetResult("old");
        Assert.False(await first);
        Assert.Equal("new", visible);
    }

    [Fact]
    public async Task Rapid_typing_makes_one_request_and_clearing_search_is_applied()
    {
        using var requests = new LatestRequest();
        var calls = 0;
        string? visible = null;
        Task<bool> Search(string term) => requests.RunAsync(_ => { calls++; return Task.FromResult(term); }, x => visible = x, 40);
        var a = Search("A"); var b = Search("A10"); var clear = Search("");
        await Task.WhenAll(a, b, clear);
        Assert.Equal(1, calls);
        Assert.Equal("", visible);
    }
}
