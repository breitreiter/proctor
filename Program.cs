namespace Proctor;

static class Program
{
    const string Usage = """
        usage: proctor <verb> [args] [--root <dir>]

          list [exam]         validate and print the cells that would run
          run <exam>          run every cell of an exam into a new experiment
          resume <experiment> run the cells of an experiment that have not completed
          grade <experiment>  run the checks over every completed cell; a model check's verdict is kept in the cell
          friction <experiment> write friction.json beside each completed cell's trajectory: where the run paid a cost the task did not ask for; rules only, no model
          report <experiment> write results.jsonl, stats.json, report.html and summary.md
          baseline <experiment> pin a variant's analysed cells as the exam's baseline, per task
          setup                set up providers and keys for this machine, job by job (needs a terminal)

        --root <dir>          the repository root holding exams/ (default: current directory)
        --config <file>       list, run, resume, grade: this machine's config in place of exams/proctor.json, such as one for Modal
        --label <k[=glob]>    list: only exams carrying the label, or the label with a matching value (repeatable)
        --nb <path>           the nb binary (default: exams/proctor.json nb.path, else PATH)
        --runner <script>     the script that runs nb per cell (default: the exam's nb.runner); none runs nb bare
        --variant <id>            grade, friction: only this variant's cells, the others left as they are (repeatable); baseline: which variant to pin (required with several variants)
        --tasks <a,b>         grade, friction: only these tasks' cells; baseline: only these tasks, the rest keep their pins
        --judge <a=b>         grade: compare judge b against judge a on the checks that name a; writes b's verdicts beside, never into checks.json (repeatable)
        --rejudge             grade: call the judges again instead of reusing the cells' verdict files; friction --reader: ask the reader again
        --reader <judge>      friction: a chat judge reads each run too and adds what rules cannot see (repair, lost, reread, dead-end, workaround), each with a checked quote; a model call per run, kept in the cell and reused
        --version             print proctor's version (see CHANGELOG.md for what changed between versions)
        --tolerance <points>  report: how far below the baseline still counts as held (default 0)
        --fail-on regression  report: exit 1 when any variant regressed against the baseline
        --against <experiment> report: compare each variant with the variant of the same id in that experiment, in place of the baseline
        --check               setup: print what is set up and what is missing, plain lines for an agent; exit 1 on a problem
        --live                setup --check: repeat each provider's key check (lists models; no tokens)
        """;

    static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        string? nbPath = null, runner = null, failOn = null, against = null, configFile = null, reader = null;
        List<string>? tasks = null, labels = null, judges = null, variants = null;
        var tolerance = 0;
        var rejudge = false;
        var check = false;
        var live = false;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = Path.GetFullPath(args[++i]); break;
                case "--nb" when i + 1 < args.Length: nbPath = args[++i]; break;
                case "--config" when i + 1 < args.Length: configFile = args[++i]; break;
                case "--runner" when i + 1 < args.Length: runner = args[++i]; break;
                case "--variant" when i + 1 < args.Length: (variants ??= []).Add(args[++i]); break;
                case "--label" when i + 1 < args.Length: (labels ??= []).Add(args[++i]); break;
                case "--judge" when i + 1 < args.Length: (judges ??= []).Add(args[++i]); break;
                case "--rejudge": rejudge = true; break;
                case "--reader" when i + 1 < args.Length: reader = args[++i]; break;
                case "--check": check = true; break;
                case "--live": live = true; break;
                case "--tasks" when i + 1 < args.Length: tasks = args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); break;
                case "--tolerance" when i + 1 < args.Length && int.TryParse(args[i + 1], out var t) && t >= 0: tolerance = t; i++; break;
                case "--against" when i + 1 < args.Length: against = args[++i]; break;
                case "--fail-on" when i + 1 < args.Length && args[i + 1] == "regression": failOn = args[++i]; break;
                case "-h" or "--help": Console.WriteLine(Usage); return 0;
                case "--version": Console.WriteLine($"proctor {Runner.ProctorVersion}"); return 0;
                case var flag when flag.StartsWith("--"): Console.Error.WriteLine($"unknown flag {flag}"); Console.Error.WriteLine(Usage); return 1;
                default: positional.Add(args[i]); break;
            }
        }
        if (positional.Count == 0) { Console.Error.WriteLine(Usage); return 1; }

        try
        {
            EnvFile.Load(EnvFile.DefaultPath());
            return (positional[0], positional.Skip(1).ToList()) switch
            {
                ("list", var rest) => Verbs.List(root, rest.FirstOrDefault(), labels, configFile),
                ("run", [var exam]) => Verbs.Run(root, exam, nbPath, runner, configFile),
                ("resume", [var id]) => Verbs.Resume(root, id, nbPath, runner, configFile),
                ("grade", [var id]) => Verbs.Grade(root, id, judges, rejudge, variants, tasks, configFile),
                ("friction", [var id]) => Verbs.Friction(root, id, variants, tasks, reader, rejudge, configFile),
                ("report", [var id]) => Verbs.Report(root, id, tolerance, failOn, against),
                ("baseline", [var id]) => Verbs.Baseline(root, id, variants, tasks),
                ("setup", []) => Verbs.Setup(root, check, live),
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
