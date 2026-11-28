namespace Auspex;

/// <summary>One method per verb. Each loads what it needs, prints, and returns the exit code.</summary>
static class Verbs
{
    /// <summary>
    /// Validate and print, per exam: its stages, checks and the tries it wants; its axes, and each configuration it names
    /// or has tries of with its current tries against that; the stale ones. A label filter keeps the exams whose labels,
    /// or whose fixtures', match every filter.
    /// </summary>
    public static int List(string root, string? examId, List<string>? labelFilters = null, string? configFile = null)
    {
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        var examIds = examId is not null ? [examId] : Directory.Exists(Layout.Exams(root))
            ? Directory.GetDirectories(Layout.Exams(root)).Where(d => File.Exists(Layout.ExamFileIn(d))).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (examIds.Length == 0) throw new AuspexException($"no exams under {Layout.Exams(root)}" + (Directory.Exists(Path.Combine(root, "suites")) ? "; this root has suites/, the layout of proctor 0.3, which docs/porting-from-suites.md maps to exams of stages and axes" : ""));

        var exit = 0;
        foreach (var id in examIds)
        {
            var exam = Exam.Load(root, id, problems, config.Judges ?? []);
            if (exam is null) { exit = 1; continue; }
            var labels = exam.AllLabels();
            if (labelFilters?.All(f => Labels.Matches(labels, f)) == false) continue;
            Console.WriteLine(exam.Def.Description is { } about ? $"{exam.Id}: {about}" : exam.Id);
            var levels = exam.Stages.SelectMany(c => exam.ChecksFor(c).Select(k => (k.Key, k.Value.Level))).Distinct().GroupBy(k => k.Level).ToList();
            var fixtures = exam.Stages.Select(c => c.Fixture).OfType<string>().Distinct().Count();
            var byLevel = levels.Count < 2 ? "" : " (" + string.Join(", ", levels.Select(g => $"{g.Count()} of {g.Key switch { "exam" => "the exam's", "fixture" => fixtures == 1 ? "the fixture's" : "the fixtures'", _ => "the stages' own" }}")) + ")";
            var size = exam.Axes.Conditions.Count > 0 ? Count(exam.Stages.Count, "stage") : $"one job of {Count(exam.Stages.Count, "stage")}";
            Console.WriteLine($"  {size}, {Count(levels.Sum(g => g.Count()), "check")}{byLevel}, {TryCount(exam.Tries)} wanted{(exam.Judged ? "; judged" : "")}");
            Console.WriteLine($"  stages: {string.Join(", ", exam.Stages.Select(c => c.Id))}");
            // A stage's id is its label on screen; these will not read as one (plans/bank.md, B9). Said, never refused.
            if (exam.UnreadableStageIds() is { Count: > 0 } unreadable) Console.WriteLine($"  stage ids that will not read as a label (over 20 characters, or a number): {string.Join(", ", unreadable)}");
            if (labels.Count > 0) Console.WriteLine($"  labels: {Labels.Format(labels)}");
            // A check with no reason, neither its author's nor a guardrail's default (check-evidence.md, C1). Listed, not refused, until the exams have theirs.
            var unexplained = exam.Stages.SelectMany(c => exam.ChecksFor(c).Where(k => Checks.WhyOf(k.Value.Spec) is null).Select(k => k.Key)).Distinct().ToList();
            if (unexplained.Count > 0) Console.WriteLine($"  {Count(unexplained.Count, "check")} with no why: {string.Join(", ", unexplained)}");
            ListAxes(root, exam);
            if (Bank.Earlier(root, exam.Id) is > 0 and var earlier)
                Console.WriteLine($"  {Count(earlier, "attempt")} made by an earlier proctor {(earlier == 1 ? "is" : "are")} still in {Layout.BankDir}/{exam.Id}/, and read by nothing: its files name what was under test another way");
        }
        foreach (var p in problems) Console.Error.WriteLine(p);
        return exit;
    }

    /// <summary>Make what the exam lacks: the configurations it names, or with --configuration or --value the one asked for.</summary>
    public static int Run(string root, string examId, string? nbPath, string? runner, string? configFile = null)
    {
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        var exam = Exam.Load(root, examId, problems, config.Judges ?? []);
        if (exam is null || problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        }
        (exam, var keys) = ForRun(root, exam, config.Judges ?? [], ValueFilters, ConfigurationName);
        // A prediction whose configuration is about to be run is pinned first: the standard it was made under is on record before the tries.
        foreach (var prediction in PredictionsOf(root, exam).Where(c => keys.Contains(c.Configuration.Key)))
            if (Predictions.Stamp(exam, prediction)) Console.WriteLine($"prediction {prediction.Name}: its standard pinned now, before its tries are made");
        Runner.Run(root, exam, config, nbPath, runner, Environment.CommandLine, Console.Out, keys);
        return 0;
    }

    /// <summary>
    /// Build an exam's values without running anything (plans/axes-build.md, 3b): the reference, each value alone against
    /// it, and each carried configuration of several values against its values alone. With --value, that one value, with
    /// its text. What each value changes is for whoever asked to read against its note; that they did is recorded nowhere.
    /// </summary>
    public static int Build(string root, string examId, string? nbPath, string? runner, string? configFile)
    {
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        var exam = Exam.Load(root, examId, problems, config.Judges ?? []);
        if (exam is null || problems.Count > 0) throw new AuspexException(string.Join("\n", problems));
        var axes = exam.Axes;
        if (ConfigurationName is not null) throw new AuspexException("--configuration does not go with build: every configuration the exam carries is checked when no --value is given");
        if (ValueFilters is { Count: > 1 }) throw new AuspexException("build looks at one value at a time: one --value. Several values together are a configuration: name it under \"configurations\" and build with no --value");

        (string Axis, string Value)? only = null;
        if (ValueFilters?[0] is { } named)
        {
            var given = ValuesGiven(exam, [named]).Single();
            if (axes.ReferenceOf(given.Key) == given.Value)
                throw new AuspexException($"{named} is the reference of its axis: the other values are built against it, so there is nothing of its own to look at");
            only = (given.Key, given.Value);
        }

        var result = Auspex.Build.Run(root, exam, config, nbPath, runner, only, Console.Out);
        foreach (var change in result.Values)
        {
            foreach (var line in Auspex.Build.Describe(change, withDiff: only is not null)) Console.WriteLine(line);
            Console.WriteLine();
        }
        var combinations = Combinations.Load(exam);
        var changed = false;
        foreach (var c in result.Combinations)
        {
            Console.WriteLine(c.NotBuilt is not null ? $"{c.Name ?? c.Label}: not checked: {c.NotBuilt}"
                : c.Differs.Count == 0 && c.Unclear.Count > 0 ? $"{c.Name ?? c.Label}: cannot be told from its values alone, since several of them change the same lines. What each value does alone does not say what this does; put what each value writes on lines apart, or look at this configuration itself."
                : c.Differs.Count == 0 ? $"{c.Name ?? c.Label}: its values put together, and nothing more"
                : $"{c.Name ?? c.Label}: MORE THAN ITS VALUES PUT TOGETHER, so some script reads more than one axis. What each value does alone does not say what this does.");
            foreach (var path in c.Differs.Concat(c.Unclear)) Console.WriteLine($"  {path}");
            if (c.NotBuilt is null) changed |= combinations.Add(new CombinationCheck(c.Configuration, c.Hash, result.Base, c.Differs, DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), c.Unclear.Count > 0 ? c.Unclear : null));
        }
        if (changed) combinations.Save(exam);
        if (only is null) Console.WriteLine($"`proctor build {exam.Id} --value <axis=value>` shows one value's change with its text, to read against what the value says");
        return 0;
    }

    /// <summary>
    /// One value of an axis against another, every other axis held, as JSON on standard output (plans/axes-build.md, 3c):
    /// what an agent quotes when it answers for a person. Against defaults to the axis's live value.
    /// </summary>
    public static int Compare(string root, string examId, string? what, string? against, List<string>? within, string? asOf)
    {
        var exam = LoadExam(root, examId);
        var axes = exam.Axes;
        if ((what is null) == (ConfigurationName is null))
            throw new AuspexException($"compare takes one thing to ask about: `proctor compare {exam.Id} <axis>=<value>` for a value, or `proctor compare {exam.Id} --configuration <name>` for a configuration");
        var (holding, at) = (within is null ? null : ValuesGiven(exam, within), asOf is null ? (DateTime?)null : Answers.ParseAsOf(asOf));
        ComparisonAnswer answer;
        if (what is not null)
        {
            var (axis, value) = ValuesGiven(exam, [what]).Single();
            if (against is not null && !axes.Def[axis].Values!.ContainsKey(against))
                throw new AuspexException($"--against {against}: not a value of axis '{axis}' ({string.Join(", ", axes.Def[axis].Values!.Keys)})");
            answer = Answers.Compare(root, exam, axis, value, against, holding, at);
        }
        else
        {
            // A configuration by the name the exam gives it, or spelled out as axis=value,…
            Configuration Named(string flag, string given) => axes.Named.FirstOrDefault(c => c.Name == given) ?? axes.Of(given)
                ?? throw new AuspexException($"{flag} {given}: not a configuration exam '{exam.Id}' names ({string.Join(", ", axes.Named.Select(c => c.Name!).DefaultIfEmpty("none"))}), nor one spelled as axis=value for every axis");
            answer = Answers.Compare(root, exam, Named("--configuration", ConfigurationName!), against is null ? null : Named("--against", against), holding, at);
        }
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(answer, AnswerJson));
        return 0;
    }

    /// <summary>An answer is read as text by an agent: an address keeps its &amp; and a label its ×, where the files' JSON escapes both.</summary>
    static readonly System.Text.Json.JsonSerializerOptions AnswerJson = new(Exam.JsonOptions) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Where an exam has tries and where it has none, as JSON on standard output (plans/axes-build.md, 3c).</summary>
    public static int Coverage(string root, string examId, string? asOf)
    {
        var exam = LoadExam(root, examId);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(Answers.Coverage(root, exam, asOf is null ? null : Answers.ParseAsOf(asOf)), AnswerJson));
        return 0;
    }

    /// <summary>The exam's predictions, or a refusal naming what is wrong with their files.</summary>
    static List<Prediction> PredictionsOf(string root, Exam exam)
    {
        var problems = new List<Problem>();
        var predictions = Predictions.Load(root, exam, problems);
        return problems.Count == 0 ? predictions : throw new AuspexException(string.Join("\n", problems));
    }

    /// <summary>
    /// Pin a prediction (plans/axes-build.md, slice 6): record the standard it was made under, once, in its file. When
    /// it was made is its author's to write, and required. With no name, every prediction not yet pinned. `run` does this for a prediction whose
    /// configuration it is about to make tries of; this is for pinning one ahead of that, or saying how each stands.
    /// </summary>
    public static int Predict(string root, string examId, string? name)
    {
        var exam = LoadExam(root, examId);
        var predictions = PredictionsOf(root, exam);
        if (name is not null && predictions.All(c => c.Name != name))
            throw new AuspexException($"exam '{exam.Id}' has no prediction '{name}' ({string.Join(", ", predictions.Select(c => c.Name).DefaultIfEmpty("none"))}); a prediction is a file, exams/{exam.Id}/{Layout.PredictionsDir}/<name>.json");
        if (predictions.Count == 0) Console.WriteLine($"{exam.Id}: no predictions. One is a file, exams/{exam.Id}/{Layout.PredictionsDir}/<name>.json: {{\"configuration\": \"<a named configuration>\", \"changes\": [{{\"check\": \"<check>\", \"direction\": \"more\"|\"less\"}}]}}");
        var bank = Bank.Select(root, exam).Current;
        foreach (var c in predictions.Where(c => name is null || c.Name == name))
        {
            var written = Predictions.WrittenAt(c.Def.Written!);
            var made = bank.Where(a => a.Configuration == c.Configuration.Key).Select(a => (a.TryNumber, Made: Answers.MadeAt(a.Entry.IdOf(a.Configuration, a.TryNumber)))).Distinct().Count(a => a.Made < written);
            var pinned = Predictions.Stamp(exam, c);
            Console.WriteLine($"{c.Name}: {c.Configuration.Name} against {c.Against.Name ?? c.Against.Label}; written {c.Def.Written}"
                + (pinned ? ", its standard pinned now" : "")
                + (made > 0 ? $". {made} {(made == 1 ? "try" : "tries")} of its configuration had been made before then, so this prediction is post hoc, and the report says so" : ""));
            foreach (var p in c.Def.Changes!) Console.WriteLine($"  predicts {Predictions.Said(p, c)}{(p.Because is null ? "" : $": {p.Because}")}");
        }
        return 0;
    }

    /// <summary>
    /// Retire a named configuration, with a reason, or bring one back; with no configuration, say
    /// which are retired. Nothing is deleted: `run` makes no new tries of a retired configuration and the main lists leave it out.
    /// With `--json` the answer is what was done and what is retired now (`retire/1`), in place of the sentence.
    /// </summary>
    public static int Retire(string root, string examId, string? name, string? reason, bool bringBack, bool json = false)
    {
        var (answer, said) = Retiring(root, examId, name, reason, bringBack);
        if (json) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(answer, AnswerJson));
        else foreach (var line in said) Console.WriteLine(line);
        return 0;
    }

    /// <summary>What `retire` did, and what it says of it to a person.</summary>
    internal static (RetireAnswer Answer, List<string> Said) Retiring(string root, string examId, string? name, string? reason, bool bringBack)
    {
        var exam = LoadExam(root, examId);
        var axes = exam.Axes;
        var file = Path.GetRelativePath(root, Retired.FileOf(exam));
        var marks = Retired.Load(exam);
        bool Named(string n) => axes.Named.Any(c => c.Name == n);
        RetireAnswer Answer(string did, RetiredMark? was = null) => new(RetireAnswer.SchemaName, exam.Id, file.Replace('\\', '/'), did, name, was,
            marks.OrderBy(m => m.Key, StringComparer.Ordinal).ToDictionary(m => m.Key, m => new RetiredNow(m.Value.Reason, m.Value.Since, Named(m.Key))));
        if (name is null)
        {
            if (reason is not null || bringBack) throw new AuspexException("say which configuration: proctor retire <exam> <configuration> --reason \"...\", or --bring-back");
            var lines = marks.Select(m => $"{m.Key}: retired {m.Value.Since}: {m.Value.Reason}{(Named(m.Key) ? "" : "  (exam.json names no such configuration now)")}").ToList();
            if (marks.Count == 0) lines.Add($"{exam.Id}: nothing is retired; `proctor retire {exam.Id} <configuration> --reason \"...\"` retires one");
            return (Answer("listed"), lines);
        }
        if (bringBack)
        {
            if (reason is not null) throw new AuspexException("--bring-back takes no --reason: the reason it was retired for goes with the mark");
            if (!marks.Remove(name, out var was))
                throw new AuspexException($"{name} is not retired ({string.Join(", ", marks.Keys.DefaultIfEmpty("nothing is"))})");
            Retired.Save(exam, marks);
            return (Answer("brought_back", was), [$"{file}: {name} is back (it was retired {was.Since}: {was.Reason}); `proctor run {exam.Id}` makes what it lacks again"]);
        }
        var configuration = axes.Named.FirstOrDefault(c => c.Name == name)
            ?? throw new AuspexException($"exam '{exam.Id}' names no configuration '{name}' ({string.Join(", ", axes.Named.Select(c => c.Name!).DefaultIfEmpty("none"))})");
        if (string.IsNullOrWhiteSpace(reason)) throw new AuspexException($"retiring {name} needs its reason, which is kept with it: --reason \"...\"");
        if (axes.IsLive(configuration)) throw new AuspexException($"{name} is live, and the live configuration cannot be retired: make another value live in exam.json first");
        var before = marks.GetValueOrDefault(name);
        marks[name] = new RetiredMark(reason.Trim(), before?.Since ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        Retired.Save(exam, marks);
        return before is null
            ? (Answer("retired"), [$"{file}: {name} is retired. Its tries, predictions and comparisons stay; `run` makes no new tries of it. `proctor retire {exam.Id} {name} --bring-back` undoes this"])
            : (Answer("reason_replaced", before), [$"{file}: {name} was already retired, since {before.Since}; its reason is replaced"]);
    }

    public static int Grade(string root, string examId, List<string>? judgeRemaps = null, bool rejudge = false, string? configFile = null)
    {
        var exam = LoadExam(root, examId);
        var keys = Narrowed(exam);
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        if (problems.Count > 0) throw new AuspexException(string.Join("\n", problems));
        var selection = Bank.Select(root, exam, keys);
        var judges = exam.Judged || judgeRemaps is { Count: > 0 } ? JudgeClient.From(config, judgeRemaps, rejudge, Console.WriteLine) : null;
        var before = Auspex.Grade.Before(root, selection.Current);
        // What the judges will be asked, said before any is: a regrade's cost is the verdicts not already on file.
        if (judges is { Comparing: false } && Auspex.Grade.Cost(root, exam, selection.Current, judges) is { Count: > 0 } cost)
        {
            Console.WriteLine("judged checks, before any is asked:");
            foreach (var line in cost) Console.WriteLine(line);
        }
        var grades = Auspex.Grade.Attempts(root, exam, selection.Current, Console.Out, judges);
        var graded = grades.Count(g => g.Checks is not null);
        var invalid = grades.Count(g => g.Invalid is not null);
        var undecided = grades.Where(g => g.Checks is not null && g.Invalid is null && g.Pass is null).ToList();
        var outdated = undecided.Count(g => Auspex.Grade.Outdated(g.Checks!, exam.CorrectnessFor(exam.StageOf(g.Stage))) is not null);
        var untouched = keys is null ? 0 : Bank.Select(root, exam).Current.Count - grades.Count;
        Console.WriteLine($"graded {graded} of {grades.Count} stages{(grades.Count(g => g.Status == AttemptStatus.Skipped) is > 0 and var skipped ? $" ({skipped} not reached)" : "")}; {grades.Count(g => g.Pass == true && g.Invalid is null)} pass{(invalid > 0 ? $", {invalid} invalid" : "")}{(undecided.Count > outdated ? $", {undecided.Count - outdated} undecided" : "")}{(outdated > 0 ? $", {outdated} outdated" : "")}{(untouched > 0 ? $"; {untouched} outside --value/--configuration left as they were" : "")}");
        foreach (var line in Auspex.Grade.ByCheck(grades, before)) Console.WriteLine(line);
        if (Auspex.Grade.OutdatedSummary(exam.Id, grades) is { } summary) Console.WriteLine(summary);
        foreach (var move in Standard.Moves(root, exam, selection.Current)) Console.WriteLine($"  the standard moved: {move.Sentence}");
        if (untouched == 0 && Configurations.NotGraded(root, exam, Bank.Select(root, exam).Current) is { Count: > 0 } left)
            Console.WriteLine($"  {left.Count} {(left.Count == 1 ? "try" : "tries")} not yet graded under the checks as they are now");
        else if (untouched > 0 && Configurations.NotGraded(root, exam, Bank.Select(root, exam).Current) is { Count: > 0 } rest)
            Console.WriteLine($"  {rest.Count} {(rest.Count == 1 ? "try" : "tries")} elsewhere not yet graded under the checks as they are now: in no number until graded");
        foreach (var line in Bank.Describe(selection.Stale, "not graded")) Console.WriteLine(line);
        foreach (var (judge, reasons) in judges?.Errors ?? [])
        {
            var top = reasons.GroupBy(r => r).OrderByDescending(g => g.Count()).First();
            Console.WriteLine($"  {judge}: {reasons.Count} check{(reasons.Count == 1 ? "" : "s")} errored; most often ({top.Count()}): {top.Key}");
        }
        return 0;
    }

    /// <summary>The friction rules over every current, completed stage that has a trajectory, into friction.json beside it. No model and no key.</summary>
    /// <param name="reader">A chat judge of the machine's config to read each stage as well; a hosted model, so it costs what the model costs.</param>
    public static int Friction(string root, string examId, string? reader = null, bool again = false, string? configFile = null)
    {
        var exam = LoadExam(root, examId);
        var keys = Narrowed(exam);
        JudgeClient? judges = null;
        if (reader is not null)
        {
            var problems = new List<Problem>();
            var config = Exam.LoadConfig(root, problems, configFile);
            if (problems.Count > 0) throw new AuspexException(string.Join("\n", problems));
            if (config.Judges?.GetValueOrDefault(reader) is not { } def) throw new AuspexException($"--reader {reader}: no judge '{reader}' in {config.Source} ({string.Join(", ", (config.Judges ?? []).Keys)})");
            if (def.Kind != JudgeDef.Chat) throw new AuspexException($"--reader {reader}: '{reader}' is {def.Kind}; a reader is a chat judge");
            judges = new JudgeClient { Defs = config.Judges, Rejudge = again, Note = Console.WriteLine };
        }
        else if (again) throw new AuspexException("--rejudge goes with --reader");
        var selection = Bank.Select(root, exam, keys);
        if (exam.Def.Surface is null) Console.WriteLine($"  note: exam '{exam.Id}' declares no surface; only help, environment and tool-error points can be found");
        var attempts = Auspex.Friction.Attempts(root, exam, selection.Current, Console.Out, judges, reader);
        var read = attempts.Where(c => c.Points is not null).ToList();
        var skipped = attempts.Where(c => c.Skipped is not null).GroupBy(c => c.Skipped!).Select(g => $"{g.Count()} {g.Key}").ToList();
        Console.WriteLine($"friction: {read.Count} of {attempts.Count} stages read, {read.Sum(c => c.Points)} points{(skipped.Count > 0 ? $"; skipped {string.Join(", ", skipped)}" : "")}");
        foreach (var line in Bank.Describe(selection.Stale, "not read")) Console.WriteLine(line);
        if (reader is not null)
        {
            var readings = read.Select(c => c.Reading!).ToList();
            var asked = readings.Where(r => r.Asked).ToList();
            Console.WriteLine($"reader {reader}: {asked.Count(r => r.Problem is null)} asked, {readings.Count(r => !r.Asked)} reused, {readings.Count(r => r.Problem is not null)} without an answer; "
                + $"{readings.Sum(r => r.Kept)} of {readings.Sum(r => r.Labelled)} labelled calls kept; {asked.Sum(r => r.InputTokens ?? 0):N0} tokens in, {asked.Sum(r => r.OutputTokens ?? 0):N0} out");
            foreach (var c in read.Where(c => c.Reading!.Dropped.Count > 0))
                foreach (var drop in c.Reading!.Dropped) Console.WriteLine($"  dropped {c.Configuration}/{c.TryNumber}/{c.Stage} {drop}");
        }
        return 0;
    }

    static Exam LoadExam(string root, string examId)
    {
        var problems = new List<Problem>();
        var exam = Exam.Load(root, examId, problems)
            ?? throw new AuspexException($"exam '{examId}' does not load:\n" + string.Join("\n", problems));
        return exam;
    }

    /// <summary>--value axis=value (repeatable) and --configuration name, set by the command line: which configurations a verb reads.</summary>
    public static List<string>? ValueFilters;
    public static string? ConfigurationName;

    /// <summary>
    /// The configurations `run` makes, by key (plans/axes-build.md, 3a): the ones the exam names;
    /// with --configuration, the one of that name; with --value, those values with every other axis at its reference,
    /// which is a value alone. Never the cross product.
    /// </summary>
    internal static (Exam Exam, List<string> Keys) ForRun(string root, Exam exam, Dictionary<string, JudgeDef>? judges, List<string>? values, string? name)
    {
        var asked = Asked(exam, values, name);
        // A retired configuration gets no new tries: left out of a plain run, and refused when asked for by name or by its values.
        var retired = Retired.Load(exam);
        string? RetiredName(string key) => exam.Axes!.Of(key)?.Name is { } n && retired.ContainsKey(n) ? n : null;
        if (values is not null || name is not null)
        {
            if (RetiredName(asked[0]) is { } n)
                throw new AuspexException($"{n} is retired ({retired[n].Since}: {retired[n].Reason}), so no new tries are made of it; `proctor retire {exam.Id} {n} --bring-back` first");
        }
        else
        {
            asked = asked.Where(k => RetiredName(k) is null).ToList();
            if (asked.Count == 0) throw new AuspexException($"every configuration exam '{exam.Id}' names is retired, so there is nothing to run; `proctor retire {exam.Id}` lists them");
        }
        // One asked for by its values may be one the exam does not carry yet: load again, carrying it, so it is validated as the others were.
        if (exam.Axes!.Configurations.All(c => c.Key != asked[0]))
        {
            var problems = new List<Problem>();
            exam = Exam.Load(root, exam.Id, problems, judges, asked) ?? throw new AuspexException(string.Join("\n", problems));
            if (exam.Axes!.Configurations.All(c => c.Key != asked[0]))
                throw new AuspexException($"{exam.Axes.Of(asked[0])!.Label}: no stage applies in it, so there is nothing to run");
        }
        return (exam, asked);
    }

    static List<string> Asked(Exam exam, List<string>? values, string? name)
    {
        var axes = exam.Axes!;
        if (values is not null && name is not null)
            throw new AuspexException("--value and --configuration: one or the other. --configuration is one the exam names; --value is those values with every other axis at its reference");
        if (name is not null) return [NamedConfiguration(exam, name).Key];
        if (values is not null)
        {
            var alone = axes.Alone(ValuesGiven(exam, values), out var problem) ?? throw new AuspexException(problem!);
            if (alone.Excluded) throw new AuspexException($"{alone.Label} is excluded by exclude in exam.json: it cannot be made");
            return [alone.Key];
        }
        var named = axes.Named.Select(c => c.Key).ToList();
        if (named.Count == 0)
            throw new AuspexException($"exam '{exam.Id}' names no configuration, and `run` makes only what is named: name one under \"configurations\" in exam.json (one value per axis), "
                + "or say --value axis=value for that value with every other axis at its reference");
        return named;
    }

    /// <summary>--value axis=value, each checked against the exam's axes.</summary>
    static Dictionary<string, string> ValuesGiven(Exam exam, List<string>? values)
    {
        var axes = exam.Axes!;
        var given = new Dictionary<string, string>();
        foreach (var filter in values ?? [])
        {
            var (axis, value) = filter.Split('=', 2) is [var a, var v] ? (a, v) : throw new AuspexException($"--value {filter}: expects axis=value");
            if (!axes.Def.TryGetValue(axis, out var def)) throw new AuspexException($"--value {filter}: '{axis}' is not an axis of exam '{exam.Id}' ({string.Join(", ", axes.Def.Keys)})");
            if (!def.Values!.ContainsKey(value)) throw new AuspexException($"--value {filter}: '{value}' is not a value of axis '{axis}' ({string.Join(", ", def.Values.Keys)})");
            given[axis] = value;
        }
        return given;
    }

    static Configuration NamedConfiguration(Exam exam, string name) => exam.Axes!.Named.FirstOrDefault(c => c.Name == name)
        ?? throw new AuspexException($"--configuration {name}: exam '{exam.Id}' names no such configuration ({string.Join(", ", exam.Axes.Named.Select(c => c.Name!).DefaultIfEmpty("none"))})");

    /// <summary>
    /// What --value and --configuration narrow a verb that reads the bank to, as the keys of the configurations the
    /// exam carries, the named and the made. Null when nothing narrows.
    /// </summary>
    static List<string>? Narrowed(Exam exam)
    {
        if (ValueFilters is null && ConfigurationName is null) return null;
        var wanted = ValuesGiven(exam, ValueFilters);
        var named = ConfigurationName is null ? null : NamedConfiguration(exam, ConfigurationName);
        var keys = exam.Axes.Configurations.Where(c => c.Has(wanted) && (named is null || c.Key == named.Key)).Select(c => c.Key).ToList();
        if (keys.Count == 0) throw new AuspexException("no configuration the exam names or has tries of holds those values");
        return keys;
    }

    /// <summary>
    /// An exam's axes and the configurations it carries: each axis with its values, the live one and the reference
    /// marked, then a row per configuration the exam names or has tries of. Never the cross product (plans/axes-build.md, 3a).
    /// </summary>
    static void ListAxes(string root, Exam exam)
    {
        var axes = exam.Axes!;
        var bank = Bank.Of(root, exam);
        // Tries made: a configuration's current stages that count, over the stages a try of it has.
        int Made(Configuration c) => bank.Count(a => a.Configuration == c.Key && a.Counts && Bank.Stale(exam, a) is null) / Math.Max(1, exam.StagesOf(c.Key).Count);
        var name = axes.Def.Keys.Max(k => k.Length);
        var values = axes.Def.ToDictionary(a => a.Key, a => string.Join(", ", a.Value.Values!.Keys.Select(v =>
            v == a.Value.Live ? v + " (live)" : v == a.Value.Reference ? v + " (reference)" : v)));
        var wide = values.Values.Max(v => v.Length);
        Console.WriteLine("  axes:");
        foreach (var (axis, def) in axes.Def)
            Console.WriteLine($"    {axis.PadRight(name)}  {values[axis].PadRight(wide)}  {def.About}".TrimEnd());
        var unheld = axes.Def.Keys.Where(a => axes.ReferenceOf(a) is null).ToList();
        var named = axes.Named.Count();
        var others = axes.Configurations.Count - named;
        Console.WriteLine($"  {Count(named, "configuration")} named{(others > 0 ? $", {others} more with tries" : "")}; {TryCount(exam.Tries)} wanted of each"
            + (unheld.Count > 0 ? $"; no live value or reference on {string.Join(", ", unheld)}" : ""));
        bool Live(Configuration c) => axes.IsLive(c);
        var retired = Retired.Load(exam);
        if (axes.Configurations.Count == 0)
            Console.WriteLine($"  `proctor run {exam.Id}` makes what is named: name one under \"configurations\" in exam.json, or say --value axis=value for that value with the rest at their reference");
        else
        {
            var width = Math.Max("configuration".Length, axes.Configurations.Max(c => c.Label.Length));
            Console.WriteLine($"  {"configuration".PadRight(width)}  stages  attempts  wanted");
            foreach (var c in axes.Configurations)
            {
                var notes = string.Join(", ", new[] { c.Name ?? "not named", Live(c) ? "live" : null, c.Name is not null && retired.ContainsKey(c.Name) ? "retired" : null }.OfType<string>());
                Console.WriteLine($"  {c.Label.PadRight(width)}  {exam.Stages.Count(s => axes.Applies(s, c)),-6}  {Made(c),-8}  {exam.Tries,-6}  {notes}".TrimEnd());
            }
        }
        foreach (var excluded in axes.Excludes)
            Console.WriteLine($"  excluded: {string.Join(" × ", excluded.Select(v => $"{v.Key}={v.Value}"))}");
        var predictionProblems = new List<Problem>();
        foreach (var c in Predictions.Load(root, exam, predictionProblems))
            Console.WriteLine($"  prediction {c.Name}: {c.Configuration.Name} against {c.Against.Name ?? c.Against.Label}, {Count(c.Def.Changes!.Count, "change")} expected, written {c.Def.Written}");
        foreach (var problem in predictionProblems) Console.WriteLine($"  {problem}");
        // Where what each value does alone does not say what a configuration does (plans/axes-build.md, 3b): measured by `build`.
        var standing = Combinations.Load(exam).Stand(exam);
        foreach (var c in axes.Configurations.Where(c => standing.GetValueOrDefault(c.Key).State == Combinations.More))
            Console.WriteLine($"  more than its values put together: {c.Name ?? c.Label} ({Count(standing[c.Key].Paths.Count, "path")}); some script reads more than one axis");
        foreach (var c in axes.Configurations.Where(c => standing.GetValueOrDefault(c.Key).State == Combinations.Unclear))
            Console.WriteLine($"  cannot be told from its values alone: {c.Name ?? c.Label}; several of them change the same lines");
        var unchecked_ = axes.Configurations.Where(c => standing.GetValueOrDefault(c.Key).State == Combinations.NotChecked).Select(c => c.Name ?? c.Label).ToList();
        // Where an axis has no reference nothing can be built alone, which the line above already says; listing every configuration adds nothing.
        if (unchecked_.Count > 0 && unheld.Count == 0) Console.WriteLine($"  not checked against its values alone: {string.Join("; ", unchecked_)}  (`proctor build {exam.Id}`)");
        var stale = bank.Select(a => Bank.Stale(exam, a)).OfType<string>().ToList();
        if (stale.Count > 0) Console.WriteLine($"  {Count(stale.Count, "stale stage")}, not counted: {string.Join("; ", stale.GroupBy(w => w).Select(g => $"{g.Key} ({g.Count()})"))}");
    }

    static string Count(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";
    static string TryCount(int n) => $"{n} {(n == 1 ? "try" : "tries")}";

    /// <summary>The setup wizard at a terminal; with <c>--check</c>, what is set up, as plain lines an agent reads.</summary>
    public static int Setup(string root, bool check, bool probe) =>
        check ? SetupCheck.RunAsync(root, probe, Console.Out).GetAwaiter().GetResult()
        : probe ? throw new AuspexException("--probe goes with --check")
        : SetupWizard.RunAsync(root).GetAwaiter().GetResult();

    /// <summary>
    /// The exploration report over the exam's current tries, within --value and --configuration: stats.json (the
    /// configurations and each value against its axis's live one, configurations/1, stamped with what it was built from),
    /// results.jsonl a row per stage, friction.json what recurs across a configuration's tries, and summary.md, under
    /// reports/&lt;exam&gt;/explore/. An earlier report there is moved aside, not overwritten.
    /// </summary>
    public static int Report(string root, string examId)
    {
        var exam = LoadExam(root, examId);
        var keys = Narrowed(exam);
        var selection = Bank.Select(root, exam, keys);
        if (selection.Current.Count == 0)
            throw new AuspexException($"exam '{exam.Id}' has no current tries{(keys is null ? "" : " within --value and --configuration")} to report; `proctor run {exam.Id}` makes them");
        var rows = Results.Collect(root, exam, selection.Current);
        var narrowed = string.Join(" ", (ValueFilters ?? []).Select(v => $"--value {v}").Append(ConfigurationName is null ? null : $"--configuration {ConfigurationName}").OfType<string>());
        var built = Stamp.Of(exam, selection, narrowed.Length > 0 ? narrowed : null, DateTime.UtcNow);

        var (stats, friction) = Configurations.Compute(root, exam, selection.Current, Bank.Of(root, exam), rows, built, PredictionsOf(root, exam));
        var outDir = Layout.ReportExplore(root, exam.Id);
        KeepEarlier(outDir);
        Directory.CreateDirectory(outDir);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        Runner.WriteJson(Path.Combine(outDir, Layout.FrictionFile), friction);
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Configurations.Markdown(stats, friction));
        Console.WriteLine($"{Path.GetRelativePath(root, Path.Combine(outDir, Layout.SummaryFile))}");
        var notRun = stats.Configurations.Where(c => c.State == "not_run").ToList();
        var notGraded = stats.Configurations.Sum(c => c.NotGraded);
        Console.WriteLine($"  {stats.Configurations.Count(c => c.State is "made" or "partial")} of {stats.Configurations.Count} configurations have graded tries{(notRun.Count > 0 ? $"; not run: {string.Join(", ", notRun.Select(c => c.Label))}" : "")}"
            + (notGraded > 0 ? $"; {notGraded} {(notGraded == 1 ? "try is" : "tries are")} not yet graded under the checks as they are now and in no number: `proctor grade {exam.Id}`" : ""));
        foreach (var c in stats.Comparisons) Console.WriteLine($"  {c.Sentence}");
        foreach (var c in stats.Predictions ?? [])
        {
            Console.WriteLine($"  prediction {c.Name}:");
            foreach (var p in c.Changes) Console.WriteLine($"    {p.Sentence}");
            foreach (var label in c.Labels) Console.WriteLine($"    {label}");
        }
        foreach (var (key, mixed) in built.Mixed ?? []) Console.WriteLine($"warning: the tries of {key} were made under mixed conditions: {string.Join("; ", mixed)}");
        foreach (var line in Bank.Describe(selection.Stale, "left out")) Console.WriteLine(line);
        return 0;
    }

    /// <summary>An earlier report in the directory is moved into one of its own beside the new one, named by when it was built, so a report is never overwritten.</summary>
    static void KeepEarlier(string outDir)
    {
        var stats = Path.Combine(outDir, Layout.StatsFile);
        if (!File.Exists(stats)) return;
        var at = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(stats))?["built_from"]?["at"]?.GetValue<string>() is { } stamp && DateTime.TryParse(stamp, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var when)
            ? when : File.GetLastWriteTimeUtc(stats);
        var earlier = Path.Combine(outDir, at.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(earlier);
        foreach (var file in Directory.GetFiles(outDir)) File.Move(file, Path.Combine(earlier, Path.GetFileName(file)), overwrite: true);
    }
}
