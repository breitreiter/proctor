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
          baseline <experiment> pin an arm's analysed cells as the eval's baseline, per case

        --root <dir>          the repository root holding evals/ (default: current directory)
        --label <k[=glob]>    list: only evals carrying the label, or the label with a matching value (repeatable)
        --nb <path>           the nb binary (default: evals/proctor.json nb.path, else PATH)
        --runner <script>     the script that runs nb per cell (default: the eval's nb.runner); none runs nb bare
        --arm <id>            baseline: which arm to pin (required with several arms)
        --cases <a,b>         baseline: only these cases; the rest keep their pins
        --tolerance <points>  report: how far below the baseline still counts as held (default 0)
        --fail-on regression  report: exit 1 when any arm regressed against the baseline
        """;

    static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        string? nbPath = null, runner = null, arm = null, failOn = null;
        List<string>? cases = null, labels = null;
        var tolerance = 0;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = Path.GetFullPath(args[++i]); break;
                case "--nb" when i + 1 < args.Length: nbPath = args[++i]; break;
                case "--runner" when i + 1 < args.Length: runner = args[++i]; break;
                case "--arm" when i + 1 < args.Length: arm = args[++i]; break;
                case "--label" when i + 1 < args.Length: (labels ??= []).Add(args[++i]); break;
                case "--cases" when i + 1 < args.Length: cases = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
                case "--tolerance" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t) && t >= 0: tolerance = t; i++; break;
                case "--fail-on" when i + 1 < args.Length && args[i + 1] == "regression": failOn = args[++i]; break;
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
                ("list", var rest) => Verbs.List(root, rest.FirstOrDefault(), labels),
                ("run", [var eval]) => Verbs.Run(root, eval, nbPath, runner),
                ("resume", [var id]) => Verbs.Resume(root, id, nbPath, runner),
                ("grade", [var id]) => Verbs.Grade(root, id),
                ("report", [var id]) => Verbs.Report(root, id, tolerance, failOn),
                ("baseline", [var id]) => Verbs.Baseline(root, id, arm, cases),
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
