namespace Proctor;

/// <summary>One method per verb. Each loads what it needs, prints, and returns the exit code.</summary>
static class Verbs
{
    /// <summary>
    /// Validate and print, per exam: its tasks, checks and the attempts it wants; each variant's current, completed attempts
    /// against that; the stale attempts; and any interrupted run, with the command that continues it. A label filter keeps
    /// the exams whose labels, or whose tasks' labels, match every filter.
    /// </summary>
    public static int List(string root, string? examId, List<string>? labelFilters = null, string? configFile = null)
    {
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        var examIds = examId is not null ? [examId] : Directory.Exists(Layout.Exams(root))
            ? Directory.GetDirectories(Layout.Exams(root)).Where(d => File.Exists(Layout.ExamFileIn(d))).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (examIds.Length == 0) throw new ProctorException($"no exams under {Layout.Exams(root)}");

        var exit = 0;
        foreach (var id in examIds)
        {
            var exam = Exam.Load(root, id, problems, config.Judges ?? []);
            if (exam is null) { exit = 1; continue; }
            var labels = exam.AllLabels();
            if (labelFilters?.All(f => Labels.Matches(labels, f)) == false) continue;
            Console.WriteLine(exam.Def.Description is { } about ? $"{exam.Id}: {about}" : exam.Id);
            var levels = exam.Tasks.SelectMany(c => exam.ChecksFor(c).Select(k => (k.Key, k.Value.Level))).Distinct().GroupBy(k => k.Level).ToList();
            var fixtures = exam.Tasks.Select(c => c.Fixture).OfType<string>().Distinct().Count();
            var byLevel = levels.Count < 2 ? "" : " (" + string.Join(", ", levels.Select(g => $"{g.Count()} of {g.Key switch { "exam" => "the exam's", "fixture" => fixtures == 1 ? "the fixture's" : "the fixtures'", _ => "the tasks' own" }}")) + ")";
            var size = exam.Axes?.Conditions.Count > 0 ? Count(exam.Tasks.Count, "stage") : exam.Staged ? $"one job of {Count(exam.Tasks.Count, "stage")}" : Count(exam.Tasks.Count, "task");
            Console.WriteLine($"  {size}, {Count(levels.Sum(g => g.Count()), "check")}{byLevel}, {Count(exam.Attempts, "attempt")} wanted{(exam.Staged ? "" : " of each task")}{(exam.Judged ? "; judged" : "")}");
            if (exam.Staged) Console.WriteLine($"  stages: {string.Join(", ", exam.Tasks.Select(c => c.Id))}");
            // A stage's id is its label on screen; these will not read as one (plans/bank.md, B9). Said, never refused.
            if (exam.UnreadableStageIds() is { Count: > 0 } unreadable) Console.WriteLine($"  stage ids that will not read as a label (over 20 characters, or a number): {string.Join(", ", unreadable)}");
            if (labels.Count > 0) Console.WriteLine($"  labels: {Labels.Format(labels)}");
            // A check with no reason, neither its author's nor a guardrail's default (check-evidence.md, C1). Listed, not refused, until the exams have theirs.
            var unexplained = exam.Tasks.SelectMany(c => exam.ChecksFor(c).Where(k => Checks.WhyOf(k.Value.Spec) is null).Select(k => k.Key)).Distinct().ToList();
            if (unexplained.Count > 0) Console.WriteLine($"  {Count(unexplained.Count, "check")} with no why: {string.Join(", ", unexplained)}");
            if (exam.Axes is not null) { ListAxes(root, exam); continue; }
            var bank = Bank.Of(root, exam);
            var live = Proctor.Live.Load(exam);
            var broken = live?.Broken(exam);
            var width = Math.Max("variant".Length, exam.Variants.Select(v => v.Id!.Length).DefaultIfEmpty(0).Max());
            Console.WriteLine($"  {"variant".PadRight(width)}  attempts   wanted");
            foreach (var variant in exam.Variants)
            {
                var made = bank.Count(a => a.Variant == variant.Id && a.Counts && Bank.Stale(exam, a) is null) / (exam.Staged ? exam.Tasks.Count : 1);
                var mark = broken is null && live?.Variant.Id == variant.Id ? "live" : "";
                Console.WriteLine($"  {variant.Id!.PadRight(width)}  {made,-10} {exam.Attempts * (exam.Staged ? 1 : exam.Tasks.Count),-8} {mark}".TrimEnd());
            }
            if (live is null) Console.WriteLine("  nothing is live");
            else if (broken is not null) Console.WriteLine("  " + live.Warning(exam, broken));
            var stale = bank.Select(a => Bank.Stale(exam, a)).OfType<string>().ToList();
            if (stale.Count > 0)
                Console.WriteLine($"  {Count(stale.Count, "stale attempt")}, not counted: {string.Join("; ", stale.GroupBy(w => w).Select(g => $"{g.Key} ({g.Count()})"))}");
            foreach (var interrupted in Bank.Interrupted(exam, bank))
                Console.WriteLine($"  run {interrupted.Entry.Id} was interrupted: {Count(interrupted.Missing.Count, "attempt")} missing. `proctor run {exam.Id}` continues it.");
        }
        foreach (var p in problems) Console.Error.WriteLine(p);
        return exit;
    }

    /// <summary>Make what the exam lacks, continuing an interrupted run first; within --variant and --tasks when given.</summary>
    public static int Run(string root, string examId, string? nbPath, string? runner, string? configFile = null, List<string>? variantIds = null, List<string>? taskIds = null)
    {
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        var exam = Exam.Load(root, examId, problems, config.Judges ?? []);
        if (exam is null || problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        }
        if (exam.Axes is null) variantIds = Narrowed(exam, variantIds, taskIds);
        else
        {
            if (variantIds is not null) throw new ProctorException($"exam '{exam.Id}' has axes, not variants: say --configuration name, or --value axis=value for that value with the rest at their reference");
            (exam, variantIds) = ForRun(root, exam, config.Judges ?? [], ValueFilters, ConfigurationName);
        }
        if (exam.Staged)
        {
            if (taskIds is not null) throw new ProctorException($"exam '{exam.Id}' is one job of stages: a stage is never run alone, so --tasks does not apply");
        }
        if (Proctor.Live.Load(exam) is { } live && live.Broken(exam) is { } broken) Console.WriteLine(live.Warning(exam, broken));
        Runner.Run(root, exam, config, nbPath, runner, Environment.CommandLine, Console.Out, variantIds, taskIds);
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
        if (exam is null || problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
        if (exam.Axes is not { } axes) throw new ProctorException($"exam '{exam.Id}' has variants, not axes: `build` looks at what each value of an axis changes");
        if (ConfigurationName is not null) throw new ProctorException("--configuration does not go with build: every configuration the exam carries is checked when no --value is given");
        if (ValueFilters is { Count: > 1 }) throw new ProctorException("build looks at one value at a time: one --value. Several values together are a configuration: name it under \"configurations\" and build with no --value");

        (string Axis, string Value)? only = null;
        if (ValueFilters?[0] is { } named)
        {
            var given = ValuesGiven(exam, [named]).Single();
            if (axes.ReferenceOf(given.Key) == given.Value)
                throw new ProctorException($"{named} is the reference of its axis: the other values are built against it, so there is nothing of its own to look at");
            only = (given.Key, given.Value);
        }

        var result = Proctor.Build.Run(root, exam, config, nbPath, runner, only, Console.Out);
        foreach (var change in result.Values)
        {
            foreach (var line in Proctor.Build.Describe(change, withDiff: only is not null)) Console.WriteLine(line);
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
    public static int Compare(string root, string examId, string what, string? against, List<string>? within, string? asOf)
    {
        var exam = LoadExam(root, examId);
        if (exam.Axes is null) throw new ProctorException($"exam '{exam.Id}' has variants, not axes: `compare` asks about one value of an axis against another");
        var (axis, value) = ValuesGiven(exam, [what]).Single();
        if (against is not null && !exam.Axes.Def[axis].Values!.ContainsKey(against))
            throw new ProctorException($"--against {against}: not a value of axis '{axis}' ({string.Join(", ", exam.Axes.Def[axis].Values!.Keys)})");
        var answer = Answers.Compare(root, exam, axis, value, against, within is null ? null : ValuesGiven(exam, within), asOf is null ? null : Answers.ParseAsOf(asOf));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(answer, AnswerJson));
        return 0;
    }

    /// <summary>An answer is read as text by an agent: an address keeps its &amp; and a label its ×, where the files' JSON escapes both.</summary>
    static readonly System.Text.Json.JsonSerializerOptions AnswerJson = new(Exam.JsonOptions) { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Where an exam with axes has tries and where it has none, as JSON on standard output (plans/axes-build.md, 3c).</summary>
    public static int Coverage(string root, string examId, string? asOf)
    {
        var exam = LoadExam(root, examId);
        if (exam.Axes is null) throw new ProctorException($"exam '{exam.Id}' has variants, not axes: `coverage` is over an exam's axes; `proctor list {exam.Id}` shows each variant's attempts");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(Answers.Coverage(root, exam, asOf is null ? null : Answers.ParseAsOf(asOf)), AnswerJson));
        return 0;
    }

    /// <summary>Mark a variant live in exams/&lt;exam&gt;/live.json, by its id and today's hash; with no variant, say which is live.</summary>
    public static int Live(string root, string examId, string? variantId)
    {
        var exam = LoadExam(root, examId);
        if (exam.Axes is not null) throw new ProctorException($"exam '{exam.Id}' has axes: a live value is declared on its axis in exam.json (`\"live\": \"<value>\"`), not marked");
        var file = Path.GetRelativePath(root, Proctor.Live.FileOf(exam));
        var live = Proctor.Live.Load(exam);
        if (variantId is null)
        {
            if (live is null) Console.WriteLine($"{exam.Id}: nothing is live; `proctor live {exam.Id} <variant>` marks one");
            else if (live.Broken(exam) is { } broken) Console.WriteLine(live.Warning(exam, broken));
            else Console.WriteLine($"{file}: {live.Variant.Id} is live, since {live.Since}");
            return 0;
        }
        RequireKnown(exam, [variantId], null);
        var variant = exam.Variants.Single(v => v.Id == variantId);
        new Proctor.Live(new Ref(variant.Id!, variant.Hash), DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")).Save(exam);
        var was = live is null ? "" : live.Variant.Id != variant.Id ? $" (was {live.Variant.Id})" : live.Variant.Hash != variant.Hash ? " (marked again: its file had changed)" : " (already; marked again)";
        Console.WriteLine($"{file}: {variant.Id} is live{was}");
        return 0;
    }

    public static int Grade(string root, string examId, List<string>? judgeRemaps = null, bool rejudge = false, List<string>? variantIds = null, List<string>? taskIds = null, string? configFile = null)
    {
        var exam = LoadExam(root, examId);
        variantIds = Narrowed(exam, variantIds, taskIds);
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        if (problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
        var selection = Bank.Select(root, exam, variantIds, taskIds);
        var judges = exam.Judged || judgeRemaps is { Count: > 0 } ? JudgeClient.From(config, judgeRemaps, rejudge, Console.WriteLine) : null;
        var before = selection.Current.ToDictionary(a => a.Label, a => Proctor.Grade.ReadChecks(a.Dir(root)));
        var grades = Proctor.Grade.Attempts(root, exam, selection.Current, Console.Out, judges);
        var graded = grades.Count(g => g.Checks is not null);
        var invalid = grades.Count(g => g.Invalid is not null);
        var undecided = grades.Where(g => g.Checks is not null && g.Invalid is null && g.Pass is null).ToList();
        var outdated = undecided.Count(g => Proctor.Grade.Outdated(g.Checks!, exam.PassFor(exam.TaskOf(g.Task))) is not null);
        var untouched = variantIds is null && taskIds is null ? 0 : Bank.Select(root, exam).Current.Count - grades.Count;
        Console.WriteLine($"graded {graded} of {grades.Count} {(exam.Staged ? "stages" : "attempts")}{(exam.Staged && grades.Count(g => g.Status == AttemptStatus.Skipped) is > 0 and var skipped ? $" ({skipped} not reached)" : "")}; {grades.Count(g => g.Pass == true && g.Invalid is null)} pass{(invalid > 0 ? $", {invalid} invalid" : "")}{(undecided.Count > outdated ? $", {undecided.Count - outdated} undecided" : "")}{(outdated > 0 ? $", {outdated} outdated" : "")}{(untouched > 0 ? $"; {untouched} outside --variant/--tasks left as they were" : "")}");
        foreach (var line in Proctor.Grade.ByCheck(grades, before)) Console.WriteLine(line);
        if (Proctor.Grade.OutdatedSummary(exam.Id, grades) is { } summary) Console.WriteLine(summary);
        foreach (var line in Bank.Describe(selection.Stale, "not graded")) Console.WriteLine(line);
        foreach (var (judge, reasons) in judges?.Errors ?? [])
        {
            var top = reasons.GroupBy(r => r).OrderByDescending(g => g.Count()).First();
            Console.WriteLine($"  {judge}: {reasons.Count} check{(reasons.Count == 1 ? "" : "s")} errored; most often ({top.Count()}): {top.Key}");
        }
        return 0;
    }

    /// <summary>The friction rules over every current, completed attempt that has a trajectory, into friction.json beside it. No model and no key.</summary>
    /// <param name="reader">A chat judge of the machine's config to read each attempt as well; a hosted model, so it costs what the model costs.</param>
    public static int Friction(string root, string examId, List<string>? variantIds = null, List<string>? taskIds = null, string? reader = null, bool again = false, string? configFile = null)
    {
        var exam = LoadExam(root, examId);
        variantIds = Narrowed(exam, variantIds, taskIds);
        JudgeClient? judges = null;
        if (reader is not null)
        {
            var problems = new List<Problem>();
            var config = Exam.LoadConfig(root, problems, configFile);
            if (problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
            if (config.Judges?.GetValueOrDefault(reader) is not { } def) throw new ProctorException($"--reader {reader}: no judge '{reader}' in {config.Source} ({string.Join(", ", (config.Judges ?? []).Keys)})");
            if (def.Kind != JudgeDef.Chat) throw new ProctorException($"--reader {reader}: '{reader}' is {def.Kind}; a reader is a chat judge");
            judges = new JudgeClient { Defs = config.Judges, Rejudge = again, Note = Console.WriteLine };
        }
        else if (again) throw new ProctorException("--rejudge goes with --reader");
        var selection = Bank.Select(root, exam, variantIds, taskIds);
        if (exam.Def.Surface is null) Console.WriteLine($"  note: exam '{exam.Id}' declares no surface; only help, environment and tool-error points can be found");
        var attempts = Proctor.Friction.Attempts(root, exam, selection.Current, Console.Out, judges, reader);
        var read = attempts.Where(c => c.Points is not null).ToList();
        var skipped = attempts.Where(c => c.Skipped is not null).GroupBy(c => c.Skipped!).Select(g => $"{g.Count()} {g.Key}").ToList();
        Console.WriteLine($"friction: {read.Count} of {attempts.Count} {(exam.Staged ? "stages" : "attempts")} read, {read.Sum(c => c.Points)} points{(skipped.Count > 0 ? $"; skipped {string.Join(", ", skipped)}" : "")}");
        foreach (var line in Bank.Describe(selection.Stale, "not read")) Console.WriteLine(line);
        if (reader is not null)
        {
            var readings = read.Select(c => c.Reading!).ToList();
            var asked = readings.Where(r => r.Asked).ToList();
            Console.WriteLine($"reader {reader}: {asked.Count(r => r.Problem is null)} asked, {readings.Count(r => !r.Asked)} reused, {readings.Count(r => r.Problem is not null)} without an answer; "
                + $"{readings.Sum(r => r.Kept)} of {readings.Sum(r => r.Labelled)} labelled calls kept; {asked.Sum(r => r.InputTokens ?? 0):N0} tokens in, {asked.Sum(r => r.OutputTokens ?? 0):N0} out");
            foreach (var c in read.Where(c => c.Reading!.Dropped.Count > 0))
                foreach (var drop in c.Reading!.Dropped) Console.WriteLine($"  dropped {c.Variant}/{c.Task}/{c.Attempt} {drop}");
        }
        return 0;
    }

    static void RequireKnown(Exam exam, List<string>? variantIds, List<string>? taskIds)
    {
        foreach (var variant in variantIds ?? [])
            if (exam.Variants.All(x => x.Id != variant)) throw new ProctorException($"no variant '{variant}' in exam '{exam.Id}' ({string.Join(", ", exam.Variants.Select(x => x.Id))})");
        foreach (var task in taskIds ?? [])
            if (exam.Tasks.All(x => x.Id != task)) throw new ProctorException($"no task '{task}' in exam '{exam.Id}'");
    }

    static Exam LoadExam(string root, string examId)
    {
        var problems = new List<Problem>();
        var exam = Exam.Load(root, examId, problems)
            ?? throw new ProctorException($"exam '{examId}' does not load:\n" + string.Join("\n", problems));
        return exam;
    }

    /// <summary>Axes are declared, validated and listed; nothing else reads a configuration yet (plans/axes-build.md, slice 1).</summary>
    static void NotYetWithAxes(Exam exam, string verb)
    {
        if (exam.Axes is not null) throw new ProctorException($"exam '{exam.Id}' declares axes, and {verb} does not read a configuration yet; `proctor list {exam.Id}` shows its configurations");
    }

    /// <summary>--value axis=value (repeatable) and --configuration name, set by the command line: which configurations of an exam with axes a verb reads.</summary>
    public static List<string>? ValueFilters;
    public static string? ConfigurationName;

    /// <summary>
    /// The configurations `run` makes on an exam with axes, by key (plans/axes-build.md, 3a): the ones the exam names;
    /// with --configuration, the one of that name; with --value, those values with every other axis at its reference,
    /// which is a value alone. Never the cross product.
    /// </summary>
    internal static (Exam Exam, List<string> Keys) ForRun(string root, Exam exam, Dictionary<string, JudgeDef>? judges, List<string>? values, string? name)
    {
        var asked = Asked(exam, values, name);
        // One asked for by its values may be one the exam does not carry yet: load again, carrying it, so it is validated as the others were.
        if (exam.Axes!.Configurations.All(c => c.Key != asked[0]))
        {
            var problems = new List<Problem>();
            exam = Exam.Load(root, exam.Id, problems, judges, asked) ?? throw new ProctorException(string.Join("\n", problems));
            if (exam.Axes!.Configurations.All(c => c.Key != asked[0]))
                throw new ProctorException($"{exam.Axes.Of(asked[0])!.Label}: no stage applies in it, so there is nothing to run");
        }
        foreach (var c in exam.Axes.Configurations.Where(c => asked.Contains(c.Key)))
            if (exam.Axes.NotYet(c) is { } why) throw new ProctorException($"{c.Label} {why}");
        return (exam, asked);
    }

    static List<string> Asked(Exam exam, List<string>? values, string? name)
    {
        var axes = exam.Axes!;
        if (values is not null && name is not null)
            throw new ProctorException("--value and --configuration: one or the other. --configuration is one the exam names; --value is those values with every other axis at its reference");
        if (name is not null) return [NamedConfiguration(exam, name).Key];
        if (values is not null)
        {
            var alone = axes.Alone(ValuesGiven(exam, values), out var problem) ?? throw new ProctorException(problem!);
            if (alone.Excluded) throw new ProctorException($"{alone.Label} is excluded by exclude in exam.json: it cannot be made");
            return [alone.Key];
        }
        var named = axes.Named.Select(c => c.Key).ToList();
        if (named.Count == 0)
            throw new ProctorException($"exam '{exam.Id}' names no configuration, and `run` makes only what is named: name one under \"configurations\" in exam.json (one value per axis), "
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
            var (axis, value) = filter.Split('=', 2) is [var a, var v] ? (a, v) : throw new ProctorException($"--value {filter}: expects axis=value");
            if (!axes.Def.TryGetValue(axis, out var def)) throw new ProctorException($"--value {filter}: '{axis}' is not an axis of exam '{exam.Id}' ({string.Join(", ", axes.Def.Keys)})");
            if (!def.Values!.ContainsKey(value)) throw new ProctorException($"--value {filter}: '{value}' is not a value of axis '{axis}' ({string.Join(", ", def.Values.Keys)})");
            given[axis] = value;
        }
        return given;
    }

    static Configuration NamedConfiguration(Exam exam, string name) => exam.Axes!.Named.FirstOrDefault(c => c.Name == name)
        ?? throw new ProctorException($"--configuration {name}: exam '{exam.Id}' names no such configuration ({string.Join(", ", exam.Axes.Named.Select(c => c.Name!).DefaultIfEmpty("none"))})");

    /// <summary>
    /// What --variant, or on an exam with axes --value and --configuration, narrow a verb that reads the bank to, as
    /// the ids the bank knows: a variant's id, or a configuration's key. On an exam with axes that is among the
    /// configurations it carries, the named and the made. Null when nothing narrows. Each flag is refused where it does not apply.
    /// </summary>
    static List<string>? Narrowed(Exam exam, List<string>? variantIds, List<string>? taskIds)
    {
        if (exam.Axes is not { } axes)
        {
            if (ValueFilters is not null || ConfigurationName is not null) throw new ProctorException($"exam '{exam.Id}' has variants, not axes: --value and --configuration do not apply; use --variant");
            RequireKnown(exam, variantIds, taskIds);
            return variantIds;
        }
        if (variantIds is not null) throw new ProctorException($"exam '{exam.Id}' has axes, not variants: narrow with --value axis=value or --configuration name");
        if (taskIds is not null) throw new ProctorException($"exam '{exam.Id}' is one job of stages: a stage is never run alone, so --tasks does not apply");
        if (ValueFilters is null && ConfigurationName is null) return null;
        var wanted = ValuesGiven(exam, ValueFilters);
        var named = ConfigurationName is null ? null : NamedConfiguration(exam, ConfigurationName);
        var keys = axes.Configurations.Where(c => c.Has(wanted) && (named is null || c.Key == named.Key)).Select(c => c.Key).ToList();
        if (keys.Count == 0) throw new ProctorException("no configuration the exam names or has tries of holds those values");
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
        int Made(Configuration c) => bank.Count(a => a.Variant == c.Key && a.Counts && Bank.Stale(exam, a) is null) / Math.Max(1, exam.StagesOf(c.Key).Count);
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
        Console.WriteLine($"  {Count(named, "configuration")} named{(others > 0 ? $", {others} more with attempts" : "")}; {Count(exam.Attempts, "attempt")} wanted of each"
            + (unheld.Count > 0 ? $"; no live value or reference on {string.Join(", ", unheld)}" : ""));
        bool Live(Configuration c) => axes.Def.Any(a => a.Value.Live is not null) && axes.Def.All(a => a.Value.Live is null || a.Value.Live == c[a.Key]);
        if (axes.Configurations.Count == 0)
            Console.WriteLine($"  `proctor run {exam.Id}` makes what is named: name one under \"configurations\" in exam.json, or say --value axis=value for that value with the rest at their reference");
        else
        {
            var width = Math.Max("configuration".Length, axes.Configurations.Max(c => c.Label.Length));
            Console.WriteLine($"  {"configuration".PadRight(width)}  stages  attempts  wanted");
            foreach (var c in axes.Configurations)
            {
                var notes = string.Join(", ", new[] { c.Name ?? "not named", Live(c) ? "live" : null }.OfType<string>());
                Console.WriteLine($"  {c.Label.PadRight(width)}  {exam.Tasks.Count(s => axes.Applies(s, c)),-6}  {Made(c),-8}  {exam.Attempts,-6}  {notes}".TrimEnd());
            }
        }
        foreach (var excluded in axes.Excludes)
            Console.WriteLine($"  excluded: {string.Join(" × ", excluded.Select(v => $"{v.Key}={v.Value}"))}");
        // Where what each value does alone does not say what a configuration does (plans/axes-build.md, 3b): measured by `build`.
        var standing = Combinations.Load(exam).Stand(exam);
        foreach (var c in axes.Configurations.Where(c => standing.GetValueOrDefault(c.Key).State == Combinations.More))
            Console.WriteLine($"  more than its values put together: {c.Name ?? c.Label} ({Count(standing[c.Key].Paths.Count, "path")}); some script reads more than one axis");
        foreach (var c in axes.Configurations.Where(c => standing.GetValueOrDefault(c.Key).State == Combinations.Unclear))
            Console.WriteLine($"  cannot be told from its values alone: {c.Name ?? c.Label}; several of them change the same lines");
        var unchecked_ = axes.Configurations.Where(c => standing.GetValueOrDefault(c.Key).State == Combinations.NotChecked).Select(c => c.Name ?? c.Label).ToList();
        if (unchecked_.Count > 0) Console.WriteLine($"  not checked against its values alone: {string.Join("; ", unchecked_)}  (`proctor build {exam.Id}`)");
        var stale = bank.Select(a => Bank.Stale(exam, a)).OfType<string>().ToList();
        if (stale.Count > 0) Console.WriteLine($"  {Count(stale.Count, "stale stage")}, not counted: {string.Join("; ", stale.GroupBy(w => w).Select(g => $"{g.Key} ({g.Count()})"))}");
    }

    static string Count(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";

    /// <summary>The setup wizard at a terminal; with <c>--check</c>, what is set up, as plain lines an agent reads.</summary>
    public static int Setup(string root, bool check, bool probe) =>
        check ? SetupCheck.RunAsync(root, probe, Console.Out).GetAwaiter().GetResult()
        : probe ? throw new ProctorException("--probe goes with --check")
        : SetupWizard.RunAsync(root).GetAwaiter().GetResult();

    /// <summary>
    /// The exploration report over the exam's current attempts, within --variant and --tasks: results.jsonl, stats.json
    /// (stamped with what it was built from) and summary.md under reports/&lt;exam&gt;/explore/. An earlier report
    /// there is moved aside, not overwritten. With a variant live, every other variant is compared with it.
    /// </summary>
    public static int Report(string root, string examId, List<string>? variantIds = null, List<string>? taskIds = null)
    {
        var exam = LoadExam(root, examId);
        variantIds = Narrowed(exam, variantIds, taskIds);
        var selection = Bank.Select(root, exam, variantIds, taskIds);
        if (selection.Current.Count == 0)
            throw new ProctorException($"exam '{exam.Id}' has no current attempts{(variantIds is null && taskIds is null ? "" : " within --variant and --tasks")} to report; `proctor run {exam.Id}` makes them");
        // On an exam with axes a configuration without tries is a row of the report itself: not run.
        foreach (var variant in exam.Variants.Where(v => exam.Axes is null && (variantIds is null || variantIds.Contains(v.Id!)) && selection.Current.All(a => a.Variant != v.Id)))
            Console.WriteLine($"note: variant {variant.Id} has no current attempts; it is not in the report");

        var rows = Results.Collect(root, exam, selection.Current);
        var live = Proctor.Live.Load(exam);
        var narrowed = string.Join(" ", (variantIds ?? []).Select(v => $"--variant {v}")
            .Append(taskIds is null ? null : $"--tasks {string.Join(",", taskIds)}").OfType<string>());
        var built = Stats.Stamp(exam, selection, narrowed.Length > 0 ? narrowed : null, DateTime.UtcNow);
        if (exam.Axes is not null) return ReportConfigurations(root, exam, selection, rows, built);
        if (exam.Staged) return ReportTries(root, exam, selection, rows, live, built);
        var stats = Stats.Compute(exam, rows, live, built);
        var view = View(root, exam, selection.Current, stats.Variants.Keys);
        var judges = Judge.Uses(root, selection.Current);

        var outDir = Layout.ReportExplore(root, exam.Id);
        KeepEarlier(outDir);
        Directory.CreateDirectory(outDir);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Proctor.Report.Markdown(stats, rows, view, judges));

        Console.WriteLine($"{Path.GetRelativePath(root, Path.Combine(outDir, Layout.SummaryFile))}");
        Console.WriteLine($"  from {Count(built.Current, "current attempt")}, made by {Count(built.Log.Count, "run")} ({Layout.LogDir}/: {string.Join(", ", built.Log)}); also {Layout.ResultsFile} ({rows.Count} rows) and {Layout.StatsFile}");
        foreach (var line in Bank.Describe(selection.Stale, "not counted")) Console.WriteLine(line);
        foreach (var (variant, mixed) in built.Mixed ?? []) Console.WriteLine($"warning: variant {variant}'s attempts were made under mixed conditions: {string.Join("; ", mixed)}");
        var ungraded = rows.Where(r => r.Status == AttemptStatus.Completed && r.Checks is null).ToList();
        if (ungraded.Count > 0)
            Console.WriteLine($"note: {ungraded.Count} completed attempt{(ungraded.Count == 1 ? " has" : "s have")} no checks.json ({string.Join(", ", ungraded.GroupBy(r => r.Variant).Select(g => $"variant {g.Key}: {g.Count()}"))}); run `proctor grade {exam.Id}` first for them to count");
        if (live?.Broken(exam) is { } broken) Console.WriteLine(live.Warning(exam, broken));
        else if (stats.Live is { NotReference: { } why }) Console.WriteLine($"note: {stats.Live.Variant} is live, but {why}; no variant is compared with it");
        else if (stats.Live is { } mark)
            foreach (var c in stats.Comparisons) Console.WriteLine($"  {c.Variant} against {mark.Variant} (live): {(c.DiffPoints >= 0 ? "+" : "")}{c.DiffPoints} points [{c.Ci95[0]}, {c.Ci95[1]}], {c.Verdict}");
        else Console.WriteLine("  nothing is live, so no variant is compared with another; `proctor live " + exam.Id + " <variant>` marks one");
        return 0;
    }

    /// <summary>
    /// The conditions the page stands for: the log entry of the latest run the report's attempts came from, with the exam's
    /// variants and tasks as they are now, and each variant's bundle as the latest run that made its attempts resolved it.
    /// </summary>
    static LogEntry View(string root, Exam exam, List<BankAttempt> attempts, IEnumerable<string> variants)
    {
        var latest = attempts.Select(a => a.Entry).MaxBy(b => b.Id, StringComparer.Ordinal)!;
        var bundles = new Dictionary<string, ResolvedBundle>();
        foreach (var variant in variants)
            if (attempts.Where(a => a.Variant == variant).Select(a => a.Entry).MaxBy(b => b.Id, StringComparer.Ordinal)!.BundleOf(variant) is { } bundle) bundles[variant] = bundle;
        return latest with
        {
            ExamHash = exam.Hash,
            Variants = exam.Variants.Where(v => variants.Contains(v.Id!)).Select(v => new SeenVariant(v.Id!, v.Hash, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Exam.VariantFile(exam.Dir, v.Id!)))!.AsObject())).ToList(),
            Tasks = exam.Tasks.Select(c => new Ref(c.Id!, exam.TaskHashes[c.Id!])).ToList(),
            Bundles = bundles,
        };
    }

    /// <summary>An earlier report in the directory is moved into one of its own beside the new one, named by when it was built, so a report is never overwritten.</summary>
    /// <summary>
    /// A staged exam's report: results.jsonl a row per stage, stats.json the tries and each variant against live
    /// (tries/1), friction.json what recurs across a variant's tries, and summary.md. An earlier report is moved aside.
    /// </summary>
    /// <summary>An exam with axes: stats.json the configurations and each value against its axis's live one (configurations/1), with friction.json, results.jsonl and summary.md as a staged exam has them.</summary>
    static int ReportConfigurations(string root, Exam exam, BankSelection selection, List<ResultRow> rows, BuiltFrom built)
    {
        var (stats, friction) = Configurations.Compute(root, exam, selection.Current, Bank.Of(root, exam), rows, built);
        var outDir = Layout.ReportExplore(root, exam.Id);
        KeepEarlier(outDir);
        Directory.CreateDirectory(outDir);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        Runner.WriteJson(Path.Combine(outDir, Layout.FrictionFile), friction);
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Configurations.Markdown(stats, friction));
        Console.WriteLine($"{Path.GetRelativePath(root, Path.Combine(outDir, Layout.SummaryFile))}");
        var notRun = stats.Configurations.Where(c => c.State == "not_run").ToList();
        Console.WriteLine($"  {stats.Configurations.Count(c => c.State is "made" or "partial")} of {stats.Configurations.Count(c => c.State != "excluded")} configurations have tries{(notRun.Count > 0 ? $"; not run: {string.Join(", ", notRun.Select(c => c.Label))}" : "")}");
        foreach (var c in stats.Comparisons) Console.WriteLine($"  {c.Sentence}");
        foreach (var line in Bank.Describe(selection.Stale, "left out")) Console.WriteLine(line);
        return 0;
    }

    static int ReportTries(string root, Exam exam, BankSelection selection, List<ResultRow> rows, Live? live, BuiltFrom built)
    {
        var (stats, friction) = Tries.Compute(root, exam, selection.Current, Bank.Of(root, exam), rows, live, built);
        var outDir = Layout.ReportExplore(root, exam.Id);
        KeepEarlier(outDir);
        Directory.CreateDirectory(outDir);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        Runner.WriteJson(Path.Combine(outDir, Layout.FrictionFile), friction);
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Tries.Markdown(stats, friction));
        Console.WriteLine($"{Path.GetRelativePath(root, Path.Combine(outDir, Layout.SummaryFile))}");
        foreach (var v in stats.Variants)
            Console.WriteLine($"  {v.Id}{(v.Live ? " (live)" : "")}: {v.Tries} {(v.Tries == 1 ? "try" : "tries")}{(v.Lost > 0 ? $", {v.Lost} lost" : "")}, passed in {v.Passed}{(v.AgainstLive is { } a ? $". {a.Sentence}" : "")}");
        foreach (var line in Bank.Describe(selection.Stale, "left out")) Console.WriteLine(line);
        return 0;
    }

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
