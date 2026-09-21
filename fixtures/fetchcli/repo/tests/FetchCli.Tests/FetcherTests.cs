using FetchCli;

public class FetcherTests
{
    [Fact]
    public async Task Run_ReturnsTheBody()
    {
        var fetcher = new Fetcher((url, _) => Task.FromResult($"body of {url}"));
        Assert.Equal("body of http://x/", await fetcher.RunAsync(new Options("http://x/")));
    }

    [Fact]
    public async Task Run_PassesTheTimeout()
    {
        TimeSpan? seen = null;
        var fetcher = new Fetcher((_, t) => { seen = t; return Task.FromResult(""); });
        await fetcher.RunAsync(new Options("http://x/", TimeoutSeconds: 7));
        Assert.Equal(TimeSpan.FromSeconds(7), seen);
    }
}
