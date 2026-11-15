namespace Proctor;

/// <summary>One method per verb. Each loads what it needs, prints, and returns the exit code.</summary>
static class Verbs
{
    /// <summary>
    /// Validate and print, per exam: its tasks, checks and the attempts it wants; each variant's current, completed attempts
    /// against that; the stale attempts; and any interrupted batch, with the command that continues it. A label filter keeps
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
            Console.WriteLine($"  {Count(exam.Tasks.Count, "task")}, {Count(levels.Sum(g => g.Count()), "check")}{byLevel}, {Count(exam.Attempts, "attempt")} wanted of each task{(exam.Judged ? "; judged" : "")}");
            if (labels.Count > 0) Console.WriteLine($"  labels: {Labels.Format(labels)}");
            var pool = Pool.Of(root, exam);
            var live = Proctor.Live.Load(exam);
            var broken = live?.Broken(exam);
            var width = Math.Max("variant".Length, exam.Variants.Select(v => v.Id!.Length).DefaultIfEmpty(0).Max());
            Console.WriteLine($"  {"variant".PadRight(width)}  attempts   wanted");
            foreach (var variant in exam.Variants)
            {
                var made = pool.Count(a => a.Variant == variant.Id && a.Status == AttemptStatus.Completed && Pool.Stale(exam, a) is null);
                var mark = broken is null && live?.Variant.Id == variant.Id ? "live" : "";
                Console.WriteLine($"  {variant.Id!.PadRight(width)}  {made,-10} {exam.Attempts * exam.Tasks.Count,-8} {mark}".TrimEnd());
            }
            if (live is null) Console.WriteLine("  nothing is live");
            else if (broken is not null) Console.WriteLine("  " + live.Warning(exam, broken));
            var stale = pool.Select(a => Pool.Stale(exam, a)).OfType<string>().ToList();
            if (stale.Count > 0)
                Console.WriteLine($"  {Count(stale.Count, "stale attempt")}, not counted: {string.Join("; ", stale.GroupBy(w => w).Select(g => $"{g.Key} ({g.Count()})"))}");
            foreach (var interrupted in Pool.Interrupted(exam, pool))
                Console.WriteLine($"  batch {interrupted.Batch.Id} was interrupted: {Count(interrupted.Missing.Count, "attempt")} missing. `proctor run {exam.Id}` continues it.");
        }
        foreach (var p in problems) Console.Error.WriteLine(p);
        return exit;
    }

    /// <summary>Make what the exam lacks, continuing an interrupted batch first; within --variant and --tasks when given.</summary>
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

    public static int Grade(string root, string examId, List<string>? judgeRemaps = null, bool rejudge = false, List<string>? variantIds = null, List<string>? taskIds = null, string? batchId = null, string? configFile = null)
    {
        var exam = LoadExam(root, examId);
        RequireKnown(exam, variantIds, taskIds);
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        if (problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
        var selection = Pool.Select(root, exam, batchId, variantIds, taskIds);
        var judges = exam.Judged || judgeRemaps is { Count: > 0 } ? JudgeClient.From(config, judgeRemaps, rejudge, Console.WriteLine) : null;
        var grades = Proctor.Grade.Attempts(root, exam, selection.Current, Console.Out, judges);
        var graded = grades.Count(g => g.Checks is not null);
        var invalid = grades.Count(g => g.Invalid is not null);
        var undecided = grades.Count(g => g.Checks is not null && g.Invalid is null && g.Pass is null);
        var untouched = batchId is null && variantIds is null && taskIds is null ? 0 : Pool.Select(root, exam).Current.Count - grades.Count;
        Console.WriteLine($"graded {graded} of {grades.Count} attempts; {grades.Count(g => g.Pass == true && g.Invalid is null)} pass{(invalid > 0 ? $", {invalid} invalid" : "")}{(undecided > 0 ? $", {undecided} undecided" : "")}{(untouched > 0 ? $"; {untouched} outside --batch/--variant/--tasks left as they were" : "")}");
        foreach (var line in Pool.Describe(selection.Stale, "not graded")) Console.WriteLine(line);
        foreach (var (judge, reasons) in judges?.Errors ?? [])
        {
            var top = reasons.GroupBy(r => r).OrderByDescending(g => g.Count()).First();
            Console.WriteLine($"  {judge}: {reasons.Count} check{(reasons.Count == 1 ? "" : "s")} errored; most often ({top.Count()}): {top.Key}");
        }
        return 0;
    }

    /// <summary>The friction rules over every current, completed attempt that has a trajectory, into friction.json beside it. No model and no key.</summary>
    /// <param name="reader">A chat judge of the machine's config to read each attempt as well; a hosted model, so it costs what the model costs.</param>
    public static int Friction(string root, string examId, List<string>? variantIds = null, List<string>? taskIds = null, string? batchId = null, string? reader = null, bool again = false, string? configFile = null)
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
        var selection = Pool.Select(root, exam, batchId, variantIds, taskIds);
        if (exam.Def.Surface is null) Console.WriteLine($"  note: exam '{exam.Id}' declares no surface; only help, environment and tool-error points can be found");
        var attempts = Proctor.Friction.Attempts(root, exam, selection.Current, Console.Out, judges, reader);
        var read = attempts.Where(c => c.Points is not null).ToList();
        var skipped = attempts.Where(c => c.Skipped is not null).GroupBy(c => c.Skipped!).Select(g => $"{g.Count()} {g.Key}").ToList();
        Console.WriteLine($"friction: {read.Count} of {attempts.Count} attempts read, {read.Sum(c => c.Points)} points{(skipped.Count > 0 ? $"; skipped {string.Join(", ", skipped)}" : "")}");
        foreach (var line in Pool.Describe(selection.Stale, "not read")) Console.WriteLine(line);
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
        foreach (var a in variantIds ?? [])
            if (exam.Variants.All(x => x.Id != a)) throw new ProctorException($"no variant '{a}' in exam '{exam.Id}' ({string.Join(", ", exam.Variants.Select(x => x.Id))})");
        foreach (var c in taskIds ?? [])
            if (exam.Tasks.All(x => x.Id != c)) throw new ProctorException($"no task '{c}' in exam '{exam.Id}'");
    }

    static Exam LoadExam(string root, string examId)
    {
        var problems = new List<Problem>();
        return Exam.Load(root, examId, problems)
            ?? throw new ProctorException($"exam '{examId}' does not load:\n" + string.Join("\n", problems));
    }

    static (Batch, Exam) LoadBatch(string root, string batchId)
    {
        var batch = Runner.LoadBatch(root, batchId);
        return (batch, LoadExam(root, batch.Exam));
    }

    static string Count(int n, string one) => $"{n} {one}{(n == 1 ? "" : "s")}";

    /// <summary>The setup wizard at a terminal; with <c>--check</c>, what is set up, as plain lines an agent reads.</summary>
    public static int Setup(string root, bool check, bool probe) =>
        check ? SetupCheck.RunAsync(root, probe, Console.Out).GetAwaiter().GetResult()
        : probe ? throw new ProctorException("--probe goes with --check")
        : SetupWizard.RunAsync(root).GetAwaiter().GetResult();

    /// <summary>Pin a variant's analysed attempts as the exam's baseline, per task; tasks the variant did not analyse keep their old pin.</summary>
    public static int Baseline(string root, string batchId, List<string>? variantIds, List<string>? taskIds)
    {
        var (batch, exam) = LoadBatch(root, batchId);
        if (variantIds is { Count: > 1 }) throw new ProctorException("baseline pins one variant; give --variant once");
        var variantId = variantIds?.Single();
        var variant = variantId is null
            ? exam.Variants.Count == 1 ? exam.Variants[0] : throw new ProctorException($"exam '{exam.Id}' has {exam.Variants.Count} variants; say which with --variant ({string.Join(", ", exam.Variants.Select(a => a.Id))})")
            : exam.Variants.FirstOrDefault(a => a.Id == variantId) ?? throw new ProctorException($"no variant '{variantId}' in exam '{exam.Id}' ({string.Join(", ", exam.Variants.Select(a => a.Id))})");
        RequireKnown(exam, null, taskIds);

        var rows = Results.Collect(root, exam, Pool.Select(root, exam, batch.Id).Current);
        var existing = Proctor.Baseline.Load(exam);
        var pins = existing?.Tasks.ToDictionary(k => k.Key, k => k.Value) ?? [];
        var pinned = 0;
        foreach (var c in exam.Tasks.Where(c => taskIds is null || taskIds.Contains(c.Id!)))
        {
            if (Proctor.Baseline.PinOf(batch, exam, rows, variant.Id!, c) is not { } pin)
            {
                Console.WriteLine($"  {c.Id}  {(pins.ContainsKey(c.Id!) ? $"kept: {pins[c.Id!].Experiment}/{pins[c.Id!].Arm}" : "not pinned")}: variant {variant.Id} analysed no attempt of it");
                continue;
            }
            pins[c.Id!] = pin;
            pinned++;
            Console.WriteLine($"  {c.Id}  pinned {batch.Id}/{variant.Id} attempts {string.Join(",", pin.Samples)}  score {pin.Score:0.###}");
        }
        var baseline = new Baseline(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), Environment.CommandLine, batch.ExamHash, pins);
        baseline.Save(exam);
        Console.WriteLine($"{Path.GetRelativePath(root, Proctor.Baseline.FileOf(exam))}: {pinned} task{(pinned == 1 ? "" : "s")} pinned, {pins.Count} in the baseline");
        return 0;
    }

    /// <summary>
    /// The exploration report over the exam's current attempts, within --batch, --variant and --tasks: results.jsonl, stats.json
    /// (stamped with what it was built from), report.html and summary.md under reports/&lt;exam&gt;/explore/. An earlier report
    /// there is moved aside, not overwritten.
    /// </summary>
    public static int Report(string root, string examId, int tolerancePoints = 0, string? failOn = null, string? againstId = null, string? batchId = null, List<string>? variantIds = null, List<string>? taskIds = null)
    {
        var exam = LoadExam(root, examId);
        RequireKnown(exam, variantIds, taskIds);
        var selection = Pool.Select(root, exam, batchId, variantIds, taskIds);
        if (selection.Current.Count == 0)
            throw new ProctorException($"exam '{exam.Id}' has no current attempts{(batchId is null && variantIds is null && taskIds is null ? "" : " within --batch, --variant and --tasks")} to report; `proctor run {exam.Id}` makes them");
        foreach (var variant in exam.Variants.Where(v => (variantIds is null || variantIds.Contains(v.Id!)) && selection.Current.All(a => a.Variant != v.Id)))
            Console.WriteLine($"note: variant {variant.Id} has no current attempts{(batchId is null ? "" : $" in batch {batchId}")}; it is not in the report");

        var rows = Results.Collect(root, exam, selection.Current);
        GuardInput? guard = null;
        if (againstId is not null) guard = Against(root, exam, againstId, tolerancePoints);
        else if (Proctor.Baseline.Load(exam) is { } baseline)
        {
            var (scores, source, gaps) = baseline.Resolve(root, exam);
            guard = new GuardInput(baseline.Set, source, scores, tolerancePoints, gaps, Pinned: baseline.Tasks.Values.Select(p => p.Arm).Distinct().ToList());
        }
        var narrowed = string.Join(" ", new[] { batchId is null ? null : $"--batch {batchId}" }.Concat((variantIds ?? []).Select(v => $"--variant {v}"))
            .Append(taskIds is null ? null : $"--tasks {string.Join(",", taskIds)}").OfType<string>());
        var built = Stats.Stamp(exam, selection, narrowed.Length > 0 ? narrowed : null, DateTime.UtcNow);
        var stats = Stats.Compute(exam, rows, guard, built);
        var view = View(root, exam, selection.Current, stats.Variants.Keys);
        var judges = Judge.Uses(root, selection.Current);

        var outDir = Layout.ReportExplore(root, exam.Id);
        KeepEarlier(outDir);
        Directory.CreateDirectory(outDir);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        File.WriteAllText(Path.Combine(outDir, Layout.ReportHtmlFile), Proctor.Report.Html(stats, rows, view, judges));
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Proctor.Report.Markdown(stats, rows, view, judges));

        Console.WriteLine($"{Path.GetRelativePath(root, Path.Combine(outDir, Layout.ReportHtmlFile))}");
        Console.WriteLine($"  from {Count(built.Current, "current attempt")} in {built.Batches.Count} batch{(built.Batches.Count == 1 ? "" : "es")}: {string.Join(", ", built.Batches)}; also {Layout.ResultsFile} ({rows.Count} rows), {Layout.StatsFile}, {Layout.SummaryFile}");
        foreach (var line in Pool.Describe(selection.Stale, "not counted")) Console.WriteLine(line);
        foreach (var (variant, mixed) in built.Mixed ?? []) Console.WriteLine($"warning: variant {variant}'s attempts were made under mixed conditions: {string.Join("; ", mixed)}");
        var ungraded = rows.Where(r => r.Status == AttemptStatus.Completed && r.Checks is null).ToList();
        if (ungraded.Count > 0)
            Console.WriteLine($"note: {ungraded.Count} completed attempt{(ungraded.Count == 1 ? " has" : "s have")} no checks.json ({string.Join(", ", ungraded.GroupBy(r => r.Variant).Select(g => $"variant {g.Key}: {g.Count()}"))}); run `proctor grade {exam.Id}` first for them to count");
        if (stats.Baseline is { } bl)
        {
            var vs = bl.Against ?? "baseline";
            foreach (var gap in bl.Gaps ?? [])
                Console.WriteLine($"warning: the {(bl.Against is null ? "baseline's pinned " : "")}attempts in {gap.Experiment} were never graded on the pass check '{gap.Check}'; {gap.Tasks.Count} task{(gap.Tasks.Count == 1 ? "" : "s")} use the score as pinned ({string.Join(", ", gap.Tasks)})");
            foreach (var (variant, g) in bl.Variants) Console.WriteLine($"  {variant} vs {vs}: {(g.DiffPoints >= 0 ? "+" : "")}{g.DiffPoints} pts [{g.Ci95[0]}, {g.Ci95[1]}], {g.Verdict}");
            foreach (var variant in bl.Unpaired ?? []) Console.WriteLine($"  {variant} vs {vs}: no variant '{variant}' there");
            if (failOn == "regression" && bl.Variants.Values.Any(g => g.Verdict == "regressed")) return 1;
        }
        else if (failOn is not null) Console.WriteLine($"note: --fail-on {failOn} has nothing to fail on; the exam has no baseline (run `proctor baseline <batch>`)");
        return 0;
    }

    /// <summary>
    /// The batch the page stands for: the latest one the report's attempts came from, with the exam's definition, variants
    /// and tasks as they are now, and each variant's bundle as the latest batch that made its attempts resolved it.
    /// </summary>
    static Batch View(string root, Exam exam, List<PoolAttempt> attempts, IEnumerable<string> variants)
    {
        var latest = attempts.Select(a => a.Batch).MaxBy(b => b.Id, StringComparer.Ordinal)!;
        var bundles = new Dictionary<string, ResolvedBundle>();
        foreach (var variant in variants)
            if (attempts.Where(a => a.Variant == variant).Select(a => a.Batch).MaxBy(b => b.Id, StringComparer.Ordinal)!.BundleOf(variant) is { } bundle) bundles[variant] = bundle;
        return latest with
        {
            ExamHash = exam.Hash,
            ExamDef = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Layout.ExamFileIn(exam.Dir)))!.AsObject(),
            Variants = exam.Variants.Where(v => variants.Contains(v.Id!)).Select(v => new BatchVariant(v.Id!, v.Hash, System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Exam.VariantFile(exam.Dir, v.Id!)))!.AsObject())).ToList(),
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

    /// <summary>
    /// report --against: each variant against the variant of the same id in another batch, of this exam or another.
    /// Its pins are its decided attempts as its own exam scored them, then rescored under this exam like a baseline.
    /// </summary>
    static GuardInput Against(string root, Exam exam, string otherId, int tolerancePoints)
    {
        var (other, otherExam) = LoadBatch(root, otherId);
        var rows = Results.Collect(root, otherExam, Pool.Select(root, otherExam, other.Id).Current);
        var perVariant = new Dictionary<string, Dictionary<string, double>>();
        var sources = new List<string>();
        var gaps = new List<BaselineGap>();
        foreach (var variant in exam.Variants.Where(a => otherExam.Variants.Any(o => o.Id == a.Id)))
        {
            var pins = otherExam.Tasks.Where(c => exam.Tasks.Any(x => x.Id == c.Id))
                .Select(c => (c.Id!, Pin: Proctor.Baseline.PinOf(other, otherExam, rows, variant.Id!, c)))
                .Where(x => x.Pin is not null).ToDictionary(x => x.Item1, x => x.Pin!);
            if (pins.Count == 0) continue;     // its exam may have the variant now, but the batch decided none of its attempts
            var (scores, variantSource, variantGaps) = new Baseline(other.Created, "", other.ExamHash, pins).Resolve(root, exam);
            perVariant[variant.Id!] = scores;
            sources.Add(variantSource);
            foreach (var g in variantGaps)
                if (gaps.FirstOrDefault(x => x.Check == g.Check) is { } seen) seen.Tasks.AddRange(g.Tasks.Except(seen.Tasks));
                else gaps.Add(g);
        }
        if (perVariant.Count == 0)
            throw new ProctorException($"batch '{otherId}' has none of this exam's variants ({string.Join(", ", exam.Variants.Select(a => a.Id))}); --against pairs variants by id");
        var source = sources.Distinct().Count() == 1 ? sources[0] : "partly recomputed";
        return new GuardInput(other.Created, source, [], tolerancePoints, gaps, otherId, perVariant);
    }
}
