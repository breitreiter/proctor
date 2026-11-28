namespace Auspex;

static class Program
{
    const string Usage = """
        usage: proctor <verb> [args] [--root <dir>]

          list [exam]            validate; the exam's axes, and per configuration it names or has tries of, its current
                                 tries against those wanted
          run <exam>             make the tries the exam lacks: of the configurations it names, or of the one asked for
                                 with --configuration or --value. A try that was lost or never finished is made again, whole
          build <exam>           nothing run and no model: build the starting workspace of each value alone and say what it
                                 changes from the reference; check each configuration of several values against its values
                                 alone; --value for one value with its text
          grade <exam>           run the checks over every stage of every current try; a model check's verdict is kept beside it
          friction <exam>        write friction.json beside each stage's trajectory: where it paid a cost the stage did not ask for; rules only, no model
          report <exam>          write reports/<exam>/explore/: results.jsonl, stats.json, friction.json and summary.md, stamped with what they were built from
          compare <exam> <axis=value>  as JSON: that value against its axis's live value (or --against <value>), every
                                 other axis held; what was found, in which settings, what was not run, when the tries
                                 were made and what so few could have shown.
                                 With --configuration <name> in place of the value: everything that configuration
                                 changes, together, against what is live (or --against <name>)
          coverage <exam>        as JSON: the tries of each value and configuration, and of every configuration live spans
          predict <exam> [name]  pin a prediction (exams/<exam>/predictions/<name>.json), recording the checks it was made
                                 under (when it was made is its own "written", required); run does this for a prediction
                                 whose configuration it is about to make tries of. The report says how each change it expects came out
          retire <exam> [configuration]  retire a named configuration with --reason "..." (kept in
                                 exams/<exam>/retired.json; commit it), or bring one back with --bring-back; with no
                                 configuration, list the retired. Nothing is deleted: `run` makes no new tries of it
          setup                  set up providers and keys for this machine, job by job (needs a terminal)

        A try is current while its configuration's values and the exam's stages are as they were when it was made; one
        made before either changed is stale, listed and never counted. What is live is declared on an axis in exam.json
        ("live": "<value>").

        --root <dir>          the repository root holding exams/ (default: current directory)
        --config <file>       list, run, grade: this machine's config in place of exams/proctor.json
        --label <k[=glob]>    list: only exams carrying the label, or the label with a matching value (repeatable)
        --nb <path>           the nb binary (default: exams/proctor.json nb.path, else PATH)
        --runner <script>     the script that runs nb (default: the exam's nb.runner); none runs nb bare
        --value <axis=value>  run: make this value with every other axis at its reference (its live value, or the
                              one it names). build: this value only. grade, friction, report: only the configurations
                              holding it (repeatable)
        --configuration <name>  only the configuration the exam gives this name; without either, run makes the
                              configurations the exam names
        --against <value>     compare: the value to compare with, in place of the axis's live value; with
                              --configuration, the configuration to compare with, by name
        --within <axis=value> compare: only the settings of the other axes that hold this value (repeatable)
        --as-of <time>        compare, coverage: the answer as it stood then (a date, or a UTC time as an answer gives it)
        --judge <a=b>         grade: compare judge b against judge a on the checks that name a; writes b's verdicts beside, never into checks.json (repeatable)
        --rejudge             grade: call the judges again instead of reusing the verdict files; friction --reader: ask the reader again
        --reader <judge>      friction: a chat judge reads each stage too and adds what rules cannot see (repair, lost, reread, dead-end, workaround), each with a checked quote; a model call per stage, kept beside it and reused
        --reason <text>       retire: why, kept with the mark
        --bring-back          retire: undo it
        --json                retire: answer as JSON (retire/1): what was done and what is retired now
        --version             print proctor's version (see CHANGELOG.md for what changed between versions)
        --check               setup: print what is set up and what is missing, plain lines for an agent; exit 1 on a problem
        --probe               setup --check: repeat each provider's key check (lists models; no tokens)
        """;

    /// <summary>Flags and a verb of the earlier shape, each refused with what stands in its place.</summary>
    const string NoArms = "--arm went with arms: what is under test is an axis's values now. Narrow with --configuration <name> or --value <axis=value>",
        NoResume = "there is no resume: `proctor run <exam>` makes the tries each configuration lacks, and a try a run lost is made again whole",
        NoTasks = "--tasks went with tasks: an exam is one job of stages, and a stage is never run alone",
        NoLive = "`baseline` and --live went with arms: what is live is declared on an axis in exam.json (\"live\": \"<value>\"), and `proctor list <exam>` says which configuration that makes live";

    static int Main(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        string? nbPath = null, runner = null, configFile = null, reader = null, against = null, asOf = null, reason = null;
        var bringBack = false;
        var json = false;
        List<string>? within = null;
        List<string>? labels = null, judges = null;
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
                case "--arm": return Fail(NoArms);
                case "--live": return Fail(NoLive);
                case "--tasks": return Fail(NoTasks);
                case "--value" when i + 1 < args.Length: (Verbs.ValueFilters ??= []).Add(args[++i]); break;
                case "--configuration" when i + 1 < args.Length: Verbs.ConfigurationName = args[++i]; break;
                case "--against" when i + 1 < args.Length: against = args[++i]; break;
                case "--within" when i + 1 < args.Length: (within ??= []).Add(args[++i]); break;
                case "--as-of" when i + 1 < args.Length: asOf = args[++i]; break;
                case "--label" when i + 1 < args.Length: (labels ??= []).Add(args[++i]); break;
                case "--judge" when i + 1 < args.Length: (judges ??= []).Add(args[++i]); break;
                case "--reason" when i + 1 < args.Length: reason = args[++i]; break;
                case "--bring-back": bringBack = true; break;
                case "--json": json = true; break;
                case "--rejudge": rejudge = true; break;
                case "--reader" when i + 1 < args.Length: reader = args[++i]; break;
                case "--check": check = true; break;
                case "--probe": probe = true; break;
                case "-h" or "--help": Console.WriteLine(Usage); return 0;
                case "--version": Console.WriteLine($"proctor {Runner.AuspexVersion}"); return 0;
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
                ("build", [var exam]) => Verbs.Build(root, exam, nbPath, runner, configFile),
                ("compare", [var exam, var what]) => Verbs.Compare(root, exam, what, against, within, asOf),
                ("compare", [var exam]) => Verbs.Compare(root, exam, null, against, within, asOf),
                ("coverage", [var exam]) => Verbs.Coverage(root, exam, asOf),
                ("predict", [var exam]) => Verbs.Predict(root, exam, null),
                ("predict", [var exam, var prediction]) => Verbs.Predict(root, exam, prediction),
                ("grade", [var exam]) => Verbs.Grade(root, exam, judges, rejudge, configFile),
                ("friction", [var exam]) => Verbs.Friction(root, exam, reader, rejudge, configFile),
                ("report", [var exam]) => Verbs.Report(root, exam),
                ("baseline", _) => Fail(NoLive),
                ("resume", _) => Fail(NoResume),
                ("retire", [var exam]) => Verbs.Retire(root, exam, null, reason, bringBack, json),
                ("retire", [var exam, var configuration]) => Verbs.Retire(root, exam, configuration, reason, bringBack, json),
                ("setup", []) => Verbs.Setup(root, check, probe),
                _ => Fail(Usage),
            };
        }
        catch (AuspexException e)
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
sealed class AuspexException(string message) : Exception(message);
