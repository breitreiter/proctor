using FetchCli;

// Copied into the test project by checks/tests-pass.sh. Names come from the task text.
public class RetryAcceptanceTests
{
    [Fact]
    public void Retry_DefaultsToZero() => Assert.Equal(0, Options.Parse(["http://x/"]).Retry);

    [Fact]
    public void Retry_IsParsed() => Assert.Equal(3, Options.Parse(["--retry", "3", "http://x/"]).Retry);

    [Fact]
    public void Retry_Negative_Throws() =>
        Assert.Throws<ArgumentException>(() => Options.Parse(["--retry", "-1", "http://x/"]));

    [Fact]
    public async Task Run_RetriesUntilSuccess()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => ++calls < 3 ? throw new HttpRequestException("boom") : Task.FromResult("ok"));
        Assert.Equal("ok", await fetcher.RunAsync(new Options("http://x/") { Retry = 2 }));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Run_GivesUpAfterRetries()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => { calls++; throw new HttpRequestException("boom"); });
        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.RunAsync(new Options("http://x/") { Retry = 1 }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Run_WithoutRetry_TriesOnce()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => { calls++; throw new HttpRequestException("boom"); });
        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.RunAsync(new Options("http://x/")));
        Assert.Equal(1, calls);
    }
}
