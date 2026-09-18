namespace FetchCli;

/// <summary>Parsed command line: the URL to fetch and how long to wait for it.</summary>
public sealed record Options(string Url, int TimeoutSeconds = 30)
{
    public static Options Parse(string[] args)
    {
        string? url = null;
        var timeout = 30;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--timeout" when i + 1 < args.Length:
                    timeout = int.Parse(args[++i]);
                    break;
                case var flag when flag.StartsWith("--"):
                    throw new ArgumentException($"unknown flag {flag}");
                default:
                    url = args[i];
                    break;
            }
        }
        if (url is null) throw new ArgumentException("a url is required");
        return new Options(url, timeout);
    }
}
