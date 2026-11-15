namespace Proctor;

/// <summary>One method per verb. Each loads what it needs, prints, and returns the exit code.</summary>
static class Verbs
{
    /// <summary>Validate and print. A label filter keeps the exams whose labels, or whose tasks' labels, match every filter.</summary>
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
            Console.WriteLine($"{exam.Id}  ({exam.PlannedCells} cells: {exam.Arms.Count} arm{(exam.Arms.Count == 1 ? "" : "s")} x {exam.Tasks.Count} task{(exam.Tasks.Count == 1 ? "" : "s")}{(exam.Judged ? ", judged" : "")})");
            if (labels.Count > 0) Console.WriteLine($"  labels: {Labels.Format(labels)}");
            foreach (var arm in exam.Arms)
                foreach (var c in exam.Tasks)
                    for (var s = 1; s <= arm.SamplesOrOne; s++)
                        Console.WriteLine($"  {arm.Id}/{c.Id}/{s}");
        }
        foreach (var p in problems) Console.Error.WriteLine(p);
        return exit;
    }

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
        var id = Runner.Start(root, exam, config, nbPath, runner, Environment.CommandLine, Console.Out);
        Console.WriteLine($"experiment {id}");
        return 0;
    }

    public static int Resume(string root, string experimentId, string? nbPath, string? runner, string? configFile = null)
    {
        Runner.Resume(root, experimentId, nbPath, runner, Console.Out, configFile);
        return 0;
    }
    public static int Grade(string root, string experimentId, List<string>? judgeRemaps = null, bool rejudge = false, List<string>? armIds = null, List<string>? taskIds = null, string? configFile = null)
    {
        var (experiment, exam) = LoadExperiment(root, experimentId);
        RequireKnown(exam, armIds, taskIds);
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        if (problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
        var judges = exam.Judged || judgeRemaps is { Count: > 0 } ? JudgeClient.From(config, judgeRemaps, rejudge, Console.WriteLine) : null;
        var grades = Proctor.Grade.Experiment(root, experiment, exam, Console.Out, judges, armIds, taskIds);
        var graded = grades.Count(g => g.Checks is not null);
        var invalid = grades.Count(g => g.Invalid is not null);
        var undecided = grades.Count(g => g.Checks is not null && g.Invalid is null && g.Pass is null);
        var untouched = Runner.Cells(exam).Count() - grades.Count;
        Console.WriteLine($"graded {graded} of {grades.Count} cells; {grades.Count(g => g.Pass == true && g.Invalid is null)} pass{(invalid > 0 ? $", {invalid} invalid" : "")}{(undecided > 0 ? $", {undecided} undecided" : "")}{(untouched > 0 ? $"; {untouched} outside --arm/--tasks left as they were" : "")}");
        foreach (var (judge, reasons) in judges?.Errors ?? [])
        {
            var top = reasons.GroupBy(r => r).OrderByDescending(g => g.Count()).First();
            Console.WriteLine($"  {judge}: {reasons.Count} check{(reasons.Count == 1 ? "" : "s")} errored; most often ({top.Count()}): {top.Key}");
        }
        return 0;
    }

    /// <summary>The friction rules over every completed cell that has a trajectory, into friction.json beside it. No model and no key.</summary>
    /// <param name="reader">A chat judge of the machine's config to read each run as well; a hosted model, so it costs what the model costs.</param>
    public static int Friction(string root, string experimentId, List<string>? armIds = null, List<string>? taskIds = null, string? reader = null, bool again = false, string? configFile = null)
    {
        var (experiment, exam) = LoadExperiment(root, experimentId);
        var ran = Proctor.Friction.Cells(experiment).ToList();
        foreach (var a in armIds ?? [])
            if (ran.All(c => c.Arm != a)) throw new ProctorException($"no arm '{a}' in experiment {experiment.Id} ({string.Join(", ", ran.Select(c => c.Arm).Distinct())})");
        foreach (var t in taskIds ?? [])
            if (!experiment.Tasks.Contains(t)) throw new ProctorException($"no task '{t}' in experiment {experiment.Id}");
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
        if (exam.Def.Surface is null) Console.WriteLine($"  note: exam '{exam.Id}' declares no surface; only help, environment and tool-error points can be found");
        var cells = Proctor.Friction.Experiment(root, experiment, exam, Console.Out, armIds, taskIds, judges, reader);
        var read = cells.Where(c => c.Points is not null).ToList();
        var skipped = cells.Where(c => c.Skipped is not null).GroupBy(c => c.Skipped!).Select(g => $"{g.Count()} {g.Key}").ToList();
        Console.WriteLine($"friction: {read.Count} of {cells.Count} cells read, {read.Sum(c => c.Points)} points{(skipped.Count > 0 ? $"; skipped {string.Join(", ", skipped)}" : "")}");
        if (reader is not null)
        {
            var readings = read.Select(c => c.Reading!).ToList();
            var asked = readings.Where(r => r.Asked).ToList();
            Console.WriteLine($"reader {reader}: {asked.Count(r => r.Problem is null)} asked, {readings.Count(r => !r.Asked)} reused, {readings.Count(r => r.Problem is not null)} without an answer; "
                + $"{readings.Sum(r => r.Kept)} of {readings.Sum(r => r.Labelled)} labelled calls kept; {asked.Sum(r => r.InputTokens ?? 0):N0} tokens in, {asked.Sum(r => r.OutputTokens ?? 0):N0} out");
            foreach (var c in read.Where(c => c.Reading!.Dropped.Count > 0))
                foreach (var drop in c.Reading!.Dropped) Console.WriteLine($"  dropped {c.Arm}/{c.Task}/{c.Sample} {drop}");
        }
        return 0;
    }

    static void RequireKnown(Exam exam, List<string>? armIds, List<string>? taskIds)
    {
        foreach (var a in armIds ?? [])
            if (exam.Arms.All(x => x.Id != a)) throw new ProctorException($"no arm '{a}' in exam '{exam.Id}' ({string.Join(", ", exam.Arms.Select(x => x.Id))})");
        foreach (var c in taskIds ?? [])
            if (exam.Tasks.All(x => x.Id != c)) throw new ProctorException($"no task '{c}' in exam '{exam.Id}'");
    }

    static (Experiment, Exam) LoadExperiment(string root, string experimentId)
    {
        var experiment = Runner.LoadExperiment(root, experimentId);
        var problems = new List<Problem>();
        var exam = Exam.Load(root, experiment.Exam, problems)
            ?? throw new ProctorException($"exam '{experiment.Exam}' no longer loads:\n" + string.Join("\n", problems));
        return (experiment, exam);
    }
    /// <summary>The setup wizard at a terminal; with <c>--check</c>, what is set up, as plain lines an agent reads.</summary>
    public static int Setup(string root, bool check, bool live) =>
        check ? SetupCheck.RunAsync(root, live, Console.Out).GetAwaiter().GetResult()
        : live ? throw new ProctorException("--live goes with --check")
        : SetupWizard.RunAsync(root).GetAwaiter().GetResult();

    /// <summary>Pin an arm's analysed cells as the exam's baseline, per task; tasks the arm did not analyse keep their old pin.</summary>
    public static int Baseline(string root, string experimentId, List<string>? armIds, List<string>? taskIds)
    {
        var (experiment, exam) = LoadExperiment(root, experimentId);
        if (armIds is { Count: > 1 }) throw new ProctorException("baseline pins one arm; give --arm once");
        var armId = armIds?.Single();
        var arm = armId is null
            ? exam.Arms.Count == 1 ? exam.Arms[0] : throw new ProctorException($"exam '{exam.Id}' has {exam.Arms.Count} arms; say which with --arm ({string.Join(", ", exam.Arms.Select(a => a.Id))})")
            : exam.Arms.FirstOrDefault(a => a.Id == armId) ?? throw new ProctorException($"no arm '{armId}' in exam '{exam.Id}' ({string.Join(", ", exam.Arms.Select(a => a.Id))})");
        RequireKnown(exam, null, taskIds);

        var rows = Results.Collect(root, experiment, exam);
        var existing = Proctor.Baseline.Load(exam);
        var pins = existing?.Tasks.ToDictionary(k => k.Key, k => k.Value) ?? [];
        var pinned = 0;
        foreach (var c in exam.Tasks.Where(c => taskIds is null || taskIds.Contains(c.Id!)))
        {
            if (Proctor.Baseline.PinOf(experiment, exam, rows, arm.Id!, c) is not { } pin)
            {
                Console.WriteLine($"  {c.Id}  {(pins.ContainsKey(c.Id!) ? $"kept: {pins[c.Id!].Experiment}/{pins[c.Id!].Arm}" : "not pinned")}: arm {arm.Id} analysed no sample of it");
                continue;
            }
            pins[c.Id!] = pin;
            pinned++;
            Console.WriteLine($"  {c.Id}  pinned {experiment.Id}/{arm.Id} samples {string.Join(",", pin.Samples)}  score {pin.Score:0.###}");
        }
        var baseline = new Baseline(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), Environment.CommandLine, experiment.ExamHash, pins);
        baseline.Save(exam);
        Console.WriteLine($"{Path.GetRelativePath(root, Proctor.Baseline.FileOf(exam))}: {pinned} task{(pinned == 1 ? "" : "s")} pinned, {pins.Count} in the baseline");
        return 0;
    }

    public static int Report(string root, string experimentId, int tolerancePoints = 0, string? failOn = null, string? againstId = null)
    {
        var (experiment, exam) = LoadExperiment(root, experimentId);
        var outDir = Layout.ReportData(root, experimentId);
        Directory.CreateDirectory(outDir);

        var rows = Results.Collect(root, experiment, exam);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        GuardInput? guard = null;
        if (againstId is not null) guard = Against(root, exam, againstId, tolerancePoints);
        else if (Proctor.Baseline.Load(exam) is { } baseline)
        {
            var (scores, source, gaps) = baseline.Resolve(root, exam);
            guard = new GuardInput(baseline.Set, source, scores, tolerancePoints, gaps, Pinned: baseline.Tasks.Values.Select(p => p.Arm).Distinct().ToList());
        }
        var stats = Stats.Compute(experiment, exam, rows, guard);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        File.Copy(Path.Combine(Layout.Experiment(root, experimentId), Layout.ExperimentFile), Path.Combine(outDir, Layout.ExperimentFile), overwrite: true);
        var judges = Judge.Uses(root, experiment, exam);
        File.WriteAllText(Path.Combine(outDir, Layout.ReportHtmlFile), Proctor.Report.Html(stats, rows, experiment, judges));
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Proctor.Report.Markdown(stats, rows, experiment, judges));

        Console.WriteLine($"{Path.GetRelativePath(root, outDir)}/: {Layout.ResultsFile} ({rows.Count} rows), {Layout.StatsFile}, {Layout.ReportHtmlFile}, {Layout.SummaryFile}");
        var ungraded = rows.Where(r => r.Status == CellStatus.Completed && r.Checks is null).ToList();
        if (ungraded.Count > 0)
            Console.WriteLine($"note: {ungraded.Count} completed cell{(ungraded.Count == 1 ? " has" : "s have")} no checks.json ({string.Join(", ", ungraded.GroupBy(r => r.Arm).Select(g => $"arm {g.Key}: {g.Count()}"))}); run `proctor grade {experimentId}` first for them to count");
        if (stats.Baseline is { } bl)
        {
            var vs = bl.Against ?? "baseline";
            foreach (var gap in bl.Gaps ?? [])
                Console.WriteLine($"warning: the {(bl.Against is null ? "baseline's pinned " : "")}cells in {gap.Experiment} were never graded on the pass check '{gap.Check}'; {gap.Tasks.Count} task{(gap.Tasks.Count == 1 ? "" : "s")} use the score as pinned ({string.Join(", ", gap.Tasks)})");
            foreach (var (arm, g) in bl.Arms) Console.WriteLine($"  {arm} vs {vs}: {(g.DiffPoints >= 0 ? "+" : "")}{g.DiffPoints} pts [{g.Ci95[0]}, {g.Ci95[1]}], {g.Verdict}");
            foreach (var arm in bl.Unpaired ?? []) Console.WriteLine($"  {arm} vs {vs}: no arm '{arm}' there");
            if (failOn == "regression" && bl.Arms.Values.Any(g => g.Verdict == "regressed")) return 1;
        }
        else if (failOn is not null) Console.WriteLine($"note: --fail-on {failOn} has nothing to fail on; the exam has no baseline (run `proctor baseline <experiment>`)");
        return 0;
    }

    /// <summary>
    /// report --against: each arm against the arm of the same id in another experiment, of this exam or another.
    /// Its pins are its decided cells as its own exam scored them, then rescored under this exam like a baseline.
    /// </summary>
    static GuardInput Against(string root, Exam exam, string otherId, int tolerancePoints)
    {
        var (other, otherExam) = LoadExperiment(root, otherId);
        var rows = Results.Collect(root, other, otherExam);
        var perArm = new Dictionary<string, Dictionary<string, double>>();
        var sources = new List<string>();
        var gaps = new List<BaselineGap>();
        foreach (var arm in exam.Arms.Where(a => otherExam.Arms.Any(o => o.Id == a.Id)))
        {
            var pins = otherExam.Tasks.Where(c => exam.Tasks.Any(x => x.Id == c.Id))
                .Select(c => (c.Id!, Pin: Proctor.Baseline.PinOf(other, otherExam, rows, arm.Id!, c)))
                .Where(x => x.Pin is not null).ToDictionary(x => x.Item1, x => x.Pin!);
            if (pins.Count == 0) continue;     // its exam may have the arm now, but the experiment decided none of its cells
            var (scores, armSource, armGaps) = new Baseline(other.Created, "", other.ExamHash, pins).Resolve(root, exam);
            perArm[arm.Id!] = scores;
            sources.Add(armSource);
            foreach (var g in armGaps)
                if (gaps.FirstOrDefault(x => x.Check == g.Check) is { } seen) seen.Tasks.AddRange(g.Tasks.Except(seen.Tasks));
                else gaps.Add(g);
        }
        if (perArm.Count == 0)
            throw new ProctorException($"experiment '{otherId}' has none of this exam's arms ({string.Join(", ", exam.Arms.Select(a => a.Id))}); --against pairs arms by id");
        var source = sources.Distinct().Count() == 1 ? sources[0] : "partly recomputed";
        return new GuardInput(other.Created, source, [], tolerancePoints, gaps, otherId, perArm);
    }
}
