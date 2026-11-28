using Auspex.View;

// auspex-view <root> [--port N]: serve the exams in the bank of an auspex root.
var port = 0;
string? root = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p)) { port = p; i++; }
    else if (!args[i].StartsWith("--", StringComparison.Ordinal) && root is null) root = args[i];
    else return Usage($"unexpected argument: {args[i]}");
}
if (root is null) return Usage("no auspex root given");
root = Path.GetFullPath(root);
if (!Directory.Exists(root)) return Usage($"not a directory: {root}");

var (app, url) = await Server.Start(root, port);
Console.WriteLine(url);
await app.WaitForShutdownAsync();
return 0;

static int Usage(string problem)
{
    Console.Error.WriteLine($"auspex-view: {problem}");
    Console.Error.WriteLine("usage: auspex-view <auspex root> [--port N]");
    Console.Error.WriteLine("  <auspex root> is the directory holding auspex's bank/ and exams/.");
    return 2;
}
