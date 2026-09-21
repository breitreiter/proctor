namespace FetchCli;

/// <summary>Fetches a URL through an injected getter so tests need no network.</summary>
public sealed class Fetcher(Func<string, TimeSpan, Task<string>> get)
{
    public static Fetcher Http() => new(async (url, timeout) =>
    {
        using var client = new HttpClient { Timeout = timeout };
        return await client.GetStringAsync(url);
    });

    public Task<string> RunAsync(Options options) =>
        get(options.Url, TimeSpan.FromSeconds(options.TimeoutSeconds));
}
