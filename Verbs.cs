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
            var size = exam.Staged ? $"one job of {Count(exam.Tasks.Count, "stage")}" : Count(exam.Tasks.Count, "task");
            Console.WriteLine($"  {size}, {Count(levels.Sum(g => g.Count()), "check")}{byLevel}, {Count(exam.Attempts, "attempt")} wanted{(exam.Staged ? "" : " of each task")}{(exam.Judged ? "; judged" : "")}");
            if (exam.Staged) Console.WriteLine($"  stages: {string.Join(", ", exam.Tasks.Select(c => c.Id))}");
            // A stage's id is its label on screen; these will not read as one (plans/bank.md, B9). Said, never refused.
            if (exam.UnreadableStageIds() is { Count: > 0 } unreadable) Console.WriteLine($"  stage ids that will not read as a label (over 20 characters, or a number): {string.Join(", ", unreadable)}");
            if (labels.Count > 0) Console.WriteLine($"  labels: {Labels.Format(labels)}");
            // A check with no reason, neither its author's nor a guardrail's default (check-evidence.md, C1). Listed, not refused, until the exams have theirs.
            var unexplained = exam.Tasks.SelectMany(c => exam.ChecksFor(c).Where(k => Checks.WhyOf(k.Value.Spec) is null).Select(k => k.Key)).Distinct().ToList();
            if (unexplained.Count > 0) Console.WriteLine($"  {Count(unexplained.Count, "check")} with no why: {string.Join(", ", unexplained)}");
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
        RequireKnown(exam, variantIds, taskIds);
        if (exam.Staged)
        {
            if (taskIds is not null) throw new ProctorException($"exam '{exam.Id}' is one job of stages: a stage is never run alone, so --tasks does not apply");
            if (exam.Variants.Where(v => variantIds is null || variantIds.Contains(v.Id!)).FirstOrDefault(v => v.IsAgent) is { } agent)
                throw new ProctorException($"exam '{exam.Id}' is one job of stages, which only a bare nb variant can run yet; variant '{agent.Id}' is an agent variant (project/plans/bank.md, step 4)");
        }
        if (Proctor.Live.Load(exam) is { } live && live.Broken(exam) is { } broken) Console.WriteLine(live.Warning(exam, broken));
        Runner.Run(root, exam, config, nbPath, runner, Environment.CommandLine, Console.Out, variantIds, taskIds);
        return 0;
    }

    /// <summary>Mark a variant live in exams/&lt;exam&gt;/live.json, by its id and today's hash; with no variant, say which is live.</summary>
    public static int Live(string root, string examId, string? variantId)
    {
        var exam = LoadExam(root, examId);
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
        RequireKnown(exam, variantIds, taskIds);
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
        RequireKnown(exam, variantIds, taskIds);
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

    /// <summary>Grading, friction and the report still read one attempt per task; a try of stages is theirs in a later slice.</summary>
    static void NotYetStaged(Exam exam, string verb)
    {
        if (exam.Staged) throw new ProctorException($"exam '{exam.Id}' is one job of stages, which `proctor {verb}` cannot read yet (project/plans/bank.md, step 4)");
    }

    static Exam LoadExam(string root, string examId)
    {
        var problems = new List<Problem>();
        return Exam.Load(root, examId, problems)
            ?? throw new ProctorException($"exam '{examId}' does not load:\n" + string.Join("\n", problems));
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
        NotYetStaged(exam, "report");
        RequireKnown(exam, variantIds, taskIds);
        var selection = Bank.Select(root, exam, variantIds, taskIds);
        if (selection.Current.Count == 0)
            throw new ProctorException($"exam '{exam.Id}' has no current attempts{(variantIds is null && taskIds is null ? "" : " within --variant and --tasks")} to report; `proctor run {exam.Id}` makes them");
        foreach (var variant in exam.Variants.Where(v => (variantIds is null || variantIds.Contains(v.Id!)) && selection.Current.All(a => a.Variant != v.Id)))
            Console.WriteLine($"note: variant {variant.Id} has no current attempts; it is not in the report");

        var rows = Results.Collect(root, exam, selection.Current);
        var live = Proctor.Live.Load(exam);
        var narrowed = string.Join(" ", (variantIds ?? []).Select(v => $"--variant {v}")
            .Append(taskIds is null ? null : $"--tasks {string.Join(",", taskIds)}").OfType<string>());
        var built = Stats.Stamp(exam, selection, narrowed.Length > 0 ? narrowed : null, DateTime.UtcNow);
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
