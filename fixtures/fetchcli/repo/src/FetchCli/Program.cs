using FetchCli;

try
{
    var options = Options.Parse(args);
    Console.Write(await Fetcher.Http().RunAsync(options));
    return 0;
}
catch (ArgumentException e)
{
    Console.Error.WriteLine($"fetchcli: {e.Message}");
    Console.Error.WriteLine("usage: fetchcli <url> [--timeout <seconds>]");
    return 2;
}
catch (HttpRequestException e)
{
    Console.Error.WriteLine($"fetchcli: {e.Message}");
    return 1;
}
