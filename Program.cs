namespace Proctor;

static class Program
{
    const string Usage = """
        usage: proctor <verb> [args] [--root <dir>]

          list [eval]         validate and print the cells that would run
          run <eval>          run every cell of an eval into a new experiment
          resume <experiment> run the cells of an experiment that have not completed
          grade <experiment>  run the checks over every completed cell
          report <experiment> write results.jsonl, stats.json, report.html and summary.md

        --root <dir>          the repository root holding evals/ (default: current directory)
        --nb <path>           the nb binary (default: evals/proctor.json nb.path, else PATH)
        """;

    static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        string? nbPath = null;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = Path.GetFullPath(args[++i]); break;
                case "--nb" when i + 1 < args.Length: nbPath = args[++i]; break;
                case "-h" or "--help": Console.WriteLine(Usage); return 0;
                case var flag when flag.StartsWith("--"): Console.Error.WriteLine($"unknown flag {flag}"); Console.Error.WriteLine(Usage); return 1;
                default: positional.Add(args[i]); break;
            }
        }
        if (positional.Count == 0) { Console.Error.WriteLine(Usage); return 1; }

        try
        {
            return (positional[0], positional.Skip(1).ToList()) switch
            {
                ("list", var rest) => Verbs.List(root, rest.FirstOrDefault()),
                ("run", [var eval]) => Verbs.Run(root, eval, nbPath),
                ("resume", [var id]) => Verbs.Resume(root, id, nbPath),
                ("grade", [var id]) => Verbs.Grade(root, id),
                ("report", [var id]) => Verbs.Report(root, id),
                _ => Fail(Usage),
            };
        }
        catch (ProctorException e)
        {
            return Fail(e.Message);
        }
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

/// <summary>A user-facing failure: the message is the whole story, no stack trace.</summary>
sealed class ProctorException(string message) : Exception(message);
