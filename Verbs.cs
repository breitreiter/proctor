namespace Proctor;

/// <summary>One method per verb. Each loads what it needs, prints, and returns the exit code.</summary>
static class Verbs
{
    /// <summary>Validate and print. A label filter keeps the suites whose labels, or whose tasks' labels, match every filter.</summary>
    public static int List(string root, string? suiteId, List<string>? labelFilters = null)
    {
        var problems = new List<Problem>();
        var config = Suite.LoadConfig(root, problems);
        var suiteIds = suiteId is not null ? [suiteId] : Directory.Exists(Layout.Suites(root))
            ? Directory.GetDirectories(Layout.Suites(root)).Where(d => File.Exists(Layout.SuiteFileIn(d))).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (suiteIds.Length == 0) throw new ProctorException($"no suites under {Layout.Suites(root)}");

        var exit = 0;
        foreach (var id in suiteIds)
        {
            var suite = Suite.Load(root, id, problems, config.Judges ?? []);
            if (suite is null) { exit = 1; continue; }
            var labels = suite.AllLabels();
            if (labelFilters?.All(f => Labels.Matches(labels, f)) == false) continue;
            Console.WriteLine($"{suite.Id}  ({suite.PlannedCells} cells: {suite.Arms.Count} arm{(suite.Arms.Count == 1 ? "" : "s")} x {suite.Tasks.Count} task{(suite.Tasks.Count == 1 ? "" : "s")}{(suite.Judged ? ", judged" : "")})");
            if (labels.Count > 0) Console.WriteLine($"  labels: {Labels.Format(labels)}");
            foreach (var arm in suite.Arms)
                foreach (var c in suite.Tasks)
                    for (var s = 1; s <= arm.SamplesOrOne; s++)
                        Console.WriteLine($"  {arm.Id}/{c.Id}/{s}");
        }
        foreach (var p in problems) Console.Error.WriteLine(p);
        return exit;
    }

    public static int Run(string root, string suiteId, string? nbPath, string? runner)
    {
        var problems = new List<Problem>();
        var config = Suite.LoadConfig(root, problems);
        var suite = Suite.Load(root, suiteId, problems, config.Judges ?? []);
        if (suite is null || problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        }
        var id = Runner.Start(root, suite, config, nbPath, runner, Environment.CommandLine, Console.Out);
        Console.WriteLine($"experiment {id}");
        return 0;
    }

    public static int Resume(string root, string experimentId, string? nbPath, string? runner)
    {
        Runner.Resume(root, experimentId, nbPath, runner, Console.Out);
        return 0;
    }
    public static int Grade(string root, string experimentId, List<string>? judgeRemaps = null, bool rejudge = false)
    {
        var (experiment, suite) = LoadExperiment(root, experimentId);
        var problems = new List<Problem>();
        var config = Suite.LoadConfig(root, problems);
        if (problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
        var judges = suite.Judged || judgeRemaps is { Count: > 0 } ? JudgeClient.From(config, judgeRemaps, rejudge) : null;
        var grades = Proctor.Grade.Experiment(root, experiment, suite, Console.Out, judges);
        var graded = grades.Count(g => g.Checks is not null);
        var invalid = grades.Count(g => g.Invalid is not null);
        var undecided = grades.Count(g => g.Checks is not null && g.Invalid is null && g.Pass is null);
        Console.WriteLine($"graded {graded} of {grades.Count} cells; {grades.Count(g => g.Pass == true && g.Invalid is null)} pass{(invalid > 0 ? $", {invalid} invalid" : "")}{(undecided > 0 ? $", {undecided} undecided" : "")}");
        return 0;
    }

    static (Experiment, Suite) LoadExperiment(string root, string experimentId)
    {
        var experiment = Runner.LoadExperiment(root, experimentId);
        var problems = new List<Problem>();
        var suite = Suite.Load(root, experiment.Suite, problems)
            ?? throw new ProctorException($"suite '{experiment.Suite}' no longer loads:\n" + string.Join("\n", problems));
        return (experiment, suite);
    }
    /// <summary>Pin an arm's analysed cells as the suite's baseline, per task; tasks the arm did not analyse keep their old pin.</summary>
    public static int Baseline(string root, string experimentId, string? armId, List<string>? taskIds)
    {
        var (experiment, suite) = LoadExperiment(root, experimentId);
        var arm = armId is null
            ? suite.Arms.Count == 1 ? suite.Arms[0] : throw new ProctorException($"suite '{suite.Id}' has {suite.Arms.Count} arms; say which with --arm ({string.Join(", ", suite.Arms.Select(a => a.Id))})")
            : suite.Arms.FirstOrDefault(a => a.Id == armId) ?? throw new ProctorException($"no arm '{armId}' in suite '{suite.Id}' ({string.Join(", ", suite.Arms.Select(a => a.Id))})");
        foreach (var c in taskIds ?? [])
            if (suite.Tasks.All(x => x.Id != c)) throw new ProctorException($"no task '{c}' in suite '{suite.Id}'");

        var rows = Results.Collect(root, experiment, suite).Where(r => r.Arm == arm.Id && r.Decided).ToList();
        var existing = Proctor.Baseline.Load(suite);
        var pins = existing?.Tasks.ToDictionary(k => k.Key, k => k.Value) ?? [];
        var pinned = 0;
        foreach (var c in suite.Tasks.Where(c => taskIds is null || taskIds.Contains(c.Id!)))
        {
            var analysed = rows.Where(r => r.Task == c.Id).OrderBy(r => r.Sample).ToList();
            if (analysed.Count == 0)
            {
                Console.WriteLine($"  {c.Id}  {(pins.ContainsKey(c.Id!) ? $"kept: {pins[c.Id!].Experiment}/{pins[c.Id!].Arm}" : "not pinned")}: arm {arm.Id} analysed no sample of it");
                continue;
            }
            pins[c.Id!] = new BaselinePin(experiment.Id, arm.Id!, analysed.Select(r => r.Sample).ToList(),
                experiment.BundleOf(arm.Id!)?.Hash, suite.FixtureOf(c)?.Hash, Math.Round(analysed.Average(r => r.Pass == true ? 1.0 : 0.0), 3));
            pinned++;
            Console.WriteLine($"  {c.Id}  pinned {experiment.Id}/{arm.Id} samples {string.Join(",", pins[c.Id!].Samples)}  score {pins[c.Id!].Score:0.###}");
        }
        var baseline = new Baseline(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), Environment.CommandLine, experiment.SuiteHash, pins);
        baseline.Save(suite);
        Console.WriteLine($"{Path.GetRelativePath(root, Proctor.Baseline.FileOf(suite))}: {pinned} task{(pinned == 1 ? "" : "s")} pinned, {pins.Count} in the baseline");
        return 0;
    }

    public static int Report(string root, string experimentId, int tolerancePoints = 0, string? failOn = null)
    {
        var (experiment, suite) = LoadExperiment(root, experimentId);
        var outDir = Layout.ReportData(root, experimentId);
        Directory.CreateDirectory(outDir);

        var rows = Results.Collect(root, experiment, suite);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        GuardInput? guard = null;
        if (Proctor.Baseline.Load(suite) is { } baseline)
        {
            var (scores, source) = baseline.Resolve(root, suite);
            guard = new GuardInput(baseline.Set, source, scores, tolerancePoints);
        }
        var stats = Stats.Compute(experiment, suite, rows, guard);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        File.Copy(Path.Combine(Layout.Experiment(root, experimentId), Layout.ExperimentFile), Path.Combine(outDir, Layout.ExperimentFile), overwrite: true);
        var judges = Judge.Uses(root, experiment, suite);
        File.WriteAllText(Path.Combine(outDir, Layout.ReportHtmlFile), Proctor.Report.Html(stats, rows, experiment, judges));
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Proctor.Report.Markdown(stats, rows, experiment, judges));

        Console.WriteLine($"{Path.GetRelativePath(root, outDir)}/: {Layout.ResultsFile} ({rows.Count} rows), {Layout.StatsFile}, {Layout.ReportHtmlFile}, {Layout.SummaryFile}");
        var ungraded = rows.Count(r => r.Status == CellStatus.Completed && r.Checks is null);
        if (ungraded > 0) Console.WriteLine($"note: {ungraded} completed cell{(ungraded == 1 ? " has" : "s have")} no checks.json; run `proctor grade {experimentId}` first for them to count");
        if (stats.Baseline is { } bl)
        {
            foreach (var (arm, g) in bl.Arms) Console.WriteLine($"  {arm} vs baseline: {(g.DiffPoints >= 0 ? "+" : "")}{g.DiffPoints} pts [{g.Ci95[0]}, {g.Ci95[1]}], {g.Verdict}");
            if (failOn == "regression" && bl.Arms.Values.Any(g => g.Verdict == "regressed")) return 1;
        }
        else if (failOn is not null) Console.WriteLine($"note: --fail-on {failOn} has nothing to fail on; the suite has no baseline (run `proctor baseline <experiment>`)");
        return 0;
    }
}
