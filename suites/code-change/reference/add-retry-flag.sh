#!/usr/bin/env bash
# Reference solution for add-retry-flag, applied to the checkout in $1.
set -euo pipefail
cd "$1"
python3 - <<'PY'
import re
p='src/FetchCli/Options.cs'; s=open(p).read()
s=s.replace('public sealed record Options(string Url, int TimeoutSeconds = 30)\n{','public sealed record Options(string Url, int TimeoutSeconds = 30)\n{\n    /// <summary>How many more times to try after a failed fetch.</summary>\n    public int Retry { get; init; }\n')
s=s.replace('        var timeout = 30;\n','        var timeout = 30;\n        var retry = 0;\n')
s=s.replace('''                task var flag when flag.StartsWith("--"):''','''                task "--retry" when i + 1 < args.Length:
                    retry = int.Parse(args[++i]);
                    if (retry < 0) throw new ArgumentException("--retry must not be negative");
                    break;
                task var flag when flag.StartsWith("--"):''')
s=s.replace('return new Options(url, timeout);','return new Options(url, timeout) { Retry = retry };')
open(p,'w').write(s)
p='src/FetchCli/Fetcher.cs'; s=open(p).read()
s=s.replace('''    public Task<string> RunAsync(Options options) =>
        get(options.Url, TimeSpan.FromSeconds(options.TimeoutSeconds));''','''    public async Task<string> RunAsync(Options options)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await get(options.Url, TimeSpan.FromSeconds(options.TimeoutSeconds)); }
            catch (Exception) when (attempt < options.Retry) { }
        }
    }''')
open(p,'w').write(s)
p='src/FetchCli/Program.cs'; s=open(p).read()
s=s.replace('usage: fetchcli <url> [--timeout <seconds>]','usage: fetchcli <url> [--timeout <seconds>] [--retry <n>]')
open(p,'w').write(s)
p='README.md'; s=open(p).read()
s=s.replace('[--timeout <seconds>]`','[--timeout <seconds>] [--retry <n>]`')
open(p,'w').write(s)
PY
cat > tests/FetchCli.Tests/RetryTests.cs <<'CS'
using FetchCli;

public class RetryTests
{
    [Fact]
    public void Retry_IsParsed() => Assert.Equal(2, Options.Parse(["--retry", "2", "http://x/"]).Retry);

    [Fact]
    public async Task Run_Retries()
    {
        var calls = 0;
        var f = new Fetcher((_, _) => ++calls < 2 ? throw new HttpRequestException() : Task.FromResult("ok"));
        Assert.Equal("ok", await f.RunAsync(new Options("http://x/") { Retry = 1 }));
    }
}
CS
