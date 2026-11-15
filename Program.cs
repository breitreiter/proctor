namespace Proctor;

static class Program
{
    const string Usage = """
        usage: proctor <verb> [args] [--root <dir>]

          list [exam]            validate; per variant, its current attempts against those wanted; an interrupted batch
          run <exam>             make the attempts the exam lacks. After a crash this is the command: it continues an
                                 interrupted batch first and says so (there is no resume verb)
          grade <exam>           run the checks over every current, completed attempt; a model check's verdict is kept in the attempt
          friction <exam>        write friction.json beside each current, completed attempt's trajectory: where the attempt paid a cost the task did not ask for; rules only, no model
          report <exam>          write reports/<exam>/explore/: results.jsonl, stats.json, report.html and summary.md, stamped with what they were built from
          live <exam> [variant]  mark a variant live in exams/<exam>/live.json (commit it): the one in use now, which the report
                                 compares the others with; with no variant, say which is live. Delete the file to have none
          baseline <batch>       pin a variant's analysed attempts in that batch as the exam's baseline, per task
          setup                  set up providers and keys for this machine, job by job (needs a terminal)

        An attempt is current while its variant's and its task's hashes are today's; one made before either changed is
        stale, listed and never counted.

        --root <dir>          the repository root holding exams/ (default: current directory)
        --config <file>       list, run, grade: this machine's config in place of exams/proctor.json, such as one for Modal
        --label <k[=glob]>    list: only exams carrying the label, or the label with a matching value (repeatable)
        --nb <path>           the nb binary (default: exams/proctor.json nb.path, else PATH)
        --runner <script>     the script that runs nb per attempt (default: the exam's nb.runner); none runs nb bare
        --batch <id>          grade, friction, report: only that batch's attempts
        --variant <id>        run, grade, friction, report: only this variant's attempts, the others left as they are (repeatable); baseline: which variant to pin (required with several variants)
        --tasks <a,b>         run, grade, friction, report: only these tasks' attempts; baseline: only these tasks, the rest keep their pins
        --judge <a=b>         grade: compare judge b against judge a on the checks that name a; writes b's verdicts beside, never into checks.json (repeatable)
        --rejudge             grade: call the judges again instead of reusing the attempts' verdict files; friction --reader: ask the reader again
        --reader <judge>      friction: a chat judge reads each attempt too and adds what rules cannot see (repair, lost, reread, dead-end, workaround), each with a checked quote; a model call per attempt, kept in the attempt and reused
        --version             print proctor's version (see CHANGELOG.md for what changed between versions)
        --tolerance <points>  report: how far below the baseline still counts as held (default 0)
        --fail-on regression  report: exit 1 when any variant regressed against the baseline
        --against <batch>     report: compare each variant with the variant of the same id in that batch, in place of the baseline
        --check               setup: print what is set up and what is missing, plain lines for an agent; exit 1 on a problem
        --probe               setup --check: repeat each provider's key check (lists models; no tokens)
        """;

    static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        string? nbPath = null, runner = null, failOn = null, against = null, configFile = null, reader = null, batch = null;
        List<string>? tasks = null, labels = null, judges = null, variants = null;
        var tolerance = 0;
        var rejudge = false;
        var check = false;
        var probe = false;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--root" when i + 1 < args.Length: root = Path.GetFullPath(args[++i]); break;
                case "--nb" when i + 1 < args.Length: nbPath = args[++i]; break;
                case "--config" when i + 1 < args.Length: configFile = args[++i]; break;
                case "--runner" when i + 1 < args.Length: runner = args[++i]; break;
                case "--batch" when i + 1 < args.Length: batch = args[++i]; break;
                case "--variant" when i + 1 < args.Length: (variants ??= []).Add(args[++i]); break;
                case "--label" when i + 1 < args.Length: (labels ??= []).Add(args[++i]); break;
                case "--judge" when i + 1 < args.Length: (judges ??= []).Add(args[++i]); break;
                case "--rejudge": rejudge = true; break;
                case "--reader" when i + 1 < args.Length: reader = args[++i]; break;
                case "--check": check = true; break;
                case "--probe": probe = true; break;
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
                ("run", [var exam]) => Verbs.Run(root, exam, nbPath, runner, configFile, variants, tasks),
                ("grade", [var exam]) => Verbs.Grade(root, exam, judges, rejudge, variants, tasks, batch, configFile),
                ("friction", [var exam]) => Verbs.Friction(root, exam, variants, tasks, batch, reader, rejudge, configFile),
                ("report", [var exam]) => Verbs.Report(root, exam, tolerance, failOn, against, batch, variants, tasks),
                ("live", [var exam]) => Verbs.Live(root, exam, null),
                ("live", [var exam, var variant]) => Verbs.Live(root, exam, variant),
                ("baseline", [var id]) => Verbs.Baseline(root, id, variants, tasks),
                ("setup", []) => Verbs.Setup(root, check, probe),
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
