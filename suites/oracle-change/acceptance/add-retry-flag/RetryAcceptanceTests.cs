using FetchCli;

// Copied into the test project by checks/acceptance.sh. Names come from the task text, semantics
// from oracle/add-retry-flag.md: none of them is the conventional guess, so these pass only after asking.
public class RetryAcceptanceTests
{
    [Fact]
    public void Retry_DefaultsToOneAttempt() => Assert.Equal(1, Options.Parse(["http://x/"]).Retry);

    [Fact]
    public void Retry_IsParsed() => Assert.Equal(3, Options.Parse(["--retry", "3", "http://x/"]).Retry);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Retry_ZeroOrNegative_Throws(string n) =>
        Assert.Throws<ArgumentException>(() => Options.Parse(["--retry", n, "http://x/"]));

    [Fact]
    public async Task Run_RetriesUntilSuccess()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => ++calls < 3 ? throw new HttpRequestException("boom") : Task.FromResult("ok"));
        Assert.Equal("ok", await fetcher.RunAsync(new Options("http://x/") { Retry = 3 }));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Run_RetryIsTheTotalNumberOfAttempts()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => { calls++; throw new HttpRequestException("boom"); });
        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.RunAsync(new Options("http://x/") { Retry = 2 }));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Run_ByDefault_TriesOnce()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => { calls++; throw new HttpRequestException("boom"); });
        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.RunAsync(new Options("http://x/")));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Run_GivingUp_WrapsTheLastFailure()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => throw new HttpRequestException($"boom {++calls}"));
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.RunAsync(new Options("http://x/") { Retry = 3 }));
        Assert.Equal("gave up after 3 attempts", e.Message);
        Assert.Equal("boom 3", e.InnerException?.Message);
    }

    [Fact]
    public async Task Run_ATimeout_IsNotRetried()
    {
        var calls = 0;
        var fetcher = new Fetcher((_, _) => { calls++; throw new TaskCanceledException("timed out"); });
        await Assert.ThrowsAsync<TaskCanceledException>(() => fetcher.RunAsync(new Options("http://x/") { Retry = 3 }));
        Assert.Equal(1, calls);
    }
}
