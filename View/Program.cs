using Auspex.View;

// auspex <proctor root> [--port N]: serve the exams in a proctor root's bank.
var port = 0;
string? root = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) { port = p; i++; }
    else if (!args[i].StartsWith("--", StringComparison.Ordinal) && root is null) root = args[i];
    else return Usage($"unexpected argument: {args[i]}");
}
if (root is null) return Usage("no proctor root given");
root = Path.GetFullPath(root);
if (!Directory.Exists(root)) return Usage($"not a directory: {root}");

var (app, url) = await Server.Start(root, port);
Console.WriteLine(url);
await app.WaitForShutdownAsync();
return 0;

static int Usage(string problem)
{
    Console.Error.WriteLine($"auspex: {problem}");
    Console.Error.WriteLine("usage: auspex <proctor root> [--port N]");
    Console.Error.WriteLine("  <proctor root> is the directory holding proctor's bank/ and exams/.");
    return 2;
}
