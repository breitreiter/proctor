namespace Proctor;

/// <summary>One method per verb. Each loads what it needs, prints, and returns the exit code.</summary>
static class Verbs
{
    public static int List(string root, string? evalId)
    {
        var problems = new List<Problem>();
        var evalIds = evalId is not null ? [evalId] : Directory.Exists(Layout.Evals(root))
            ? Directory.GetDirectories(Layout.Evals(root)).Select(Path.GetFileName).Where(d => d is not null).Select(d => d!).Order(StringComparer.Ordinal).ToArray()
            : [];
        if (evalIds.Length == 0) throw new ProctorException($"no evals under {Layout.Evals(root)}");

        var exit = 0;
        foreach (var id in evalIds)
        {
            var eval = Eval.Load(root, id, problems);
            if (eval is null) { exit = 1; continue; }
            Console.WriteLine($"{eval.Id}  ({eval.PlannedCells} cells: {eval.Arms.Count} arm{(eval.Arms.Count == 1 ? "" : "s")} x {eval.Cases.Count} case{(eval.Cases.Count == 1 ? "" : "s")})");
            foreach (var arm in eval.Arms)
                foreach (var c in eval.Cases)
                    for (var s = 1; s <= arm.SamplesOrOne; s++)
                        Console.WriteLine($"  {arm.Id}/{c.Id}/{s}");
        }
        foreach (var p in problems) Console.Error.WriteLine(p);
        return exit;
    }

    public static int Run(string root, string evalId, string? nbPath, string? runner)
    {
        var problems = new List<Problem>();
        var config = Eval.LoadConfig(root, problems);
        var eval = Eval.Load(root, evalId, problems);
        if (eval is null || problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        }
        var id = Runner.Start(root, eval, config, nbPath, runner, Environment.CommandLine, Console.Out);
        Console.WriteLine($"experiment {id}");
        return 0;
    }

    public static int Resume(string root, string experimentId, string? nbPath, string? runner)
    {
        Runner.Resume(root, experimentId, nbPath, runner, Console.Out);
        return 0;
    }
    public static int Grade(string root, string experimentId)
    {
        var (experiment, eval) = LoadExperiment(root, experimentId);
        var grades = Proctor.Grade.Experiment(root, experiment, eval, Console.Out);
        var graded = grades.Count(g => g.Checks is not null);
        var invalid = grades.Count(g => g.Invalid is not null);
        Console.WriteLine($"graded {graded} of {grades.Count} cells; {grades.Count(g => g.Pass == true && g.Invalid is null)} pass{(invalid > 0 ? $", {invalid} invalid" : "")}");
        return 0;
    }

    static (Experiment, Eval) LoadExperiment(string root, string experimentId)
    {
        var experiment = Runner.LoadExperiment(root, experimentId);
        var problems = new List<Problem>();
        var eval = Eval.Load(root, experiment.Eval, problems)
            ?? throw new ProctorException($"eval '{experiment.Eval}' no longer loads:\n" + string.Join("\n", problems));
        return (experiment, eval);
    }
    /// <summary>Pin an arm's analysed cells as the eval's baseline, per case; cases the arm did not analyse keep their old pin.</summary>
    public static int Baseline(string root, string experimentId, string? armId, List<string>? caseIds)
    {
        var (experiment, eval) = LoadExperiment(root, experimentId);
        var arm = armId is null
            ? eval.Arms.Count == 1 ? eval.Arms[0] : throw new ProctorException($"eval '{eval.Id}' has {eval.Arms.Count} arms; say which with --arm ({string.Join(", ", eval.Arms.Select(a => a.Id))})")
            : eval.Arms.FirstOrDefault(a => a.Id == armId) ?? throw new ProctorException($"no arm '{armId}' in eval '{eval.Id}' ({string.Join(", ", eval.Arms.Select(a => a.Id))})");
        foreach (var c in caseIds ?? [])
            if (eval.Cases.All(x => x.Id != c)) throw new ProctorException($"no case '{c}' in eval '{eval.Id}'");

        var rows = Results.Collect(root, experiment, eval).Where(r => r.Arm == arm.Id && r.Analysed).ToList();
        var existing = Proctor.Baseline.Load(eval);
        var pins = existing?.Cases.ToDictionary(k => k.Key, k => k.Value) ?? [];
        var pinned = 0;
        foreach (var c in eval.Cases.Where(c => caseIds is null || caseIds.Contains(c.Id!)))
        {
            var analysed = rows.Where(r => r.Case == c.Id).OrderBy(r => r.Sample).ToList();
            if (analysed.Count == 0)
            {
                Console.WriteLine($"  {c.Id}  {(pins.ContainsKey(c.Id!) ? $"kept: {pins[c.Id!].Experiment}/{pins[c.Id!].Arm}" : "not pinned")}: arm {arm.Id} analysed no sample of it");
                continue;
            }
            pins[c.Id!] = new BaselinePin(experiment.Id, arm.Id!, analysed.Select(r => r.Sample).ToList(),
                experiment.BundleOf(arm.Id!)?.Hash, eval.FixtureOf(c)?.Hash, Math.Round(analysed.Average(r => r.Pass == true ? 1.0 : 0.0), 3));
            pinned++;
            Console.WriteLine($"  {c.Id}  pinned {experiment.Id}/{arm.Id} samples {string.Join(",", pins[c.Id!].Samples)}  score {pins[c.Id!].Score:0.###}");
        }
        var baseline = new Baseline(DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"), Environment.CommandLine, experiment.EvalHash, pins);
        baseline.Save(eval);
        Console.WriteLine($"{Path.GetRelativePath(root, Proctor.Baseline.FileOf(eval))}: {pinned} case{(pinned == 1 ? "" : "s")} pinned, {pins.Count} in the baseline");
        return 0;
    }

    public static int Report(string root, string experimentId, int tolerancePoints = 0, string? failOn = null)
    {
        var (experiment, eval) = LoadExperiment(root, experimentId);
        var outDir = Layout.ReportData(root, experimentId);
        Directory.CreateDirectory(outDir);

        var rows = Results.Collect(root, experiment, eval);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        GuardInput? guard = null;
        if (Proctor.Baseline.Load(eval) is { } baseline)
        {
            var (scores, source) = baseline.Resolve(root, eval);
            guard = new GuardInput(baseline.Set, source, scores, tolerancePoints);
        }
        var stats = Stats.Compute(experiment, eval, rows, guard);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        File.Copy(Path.Combine(Layout.Experiment(root, experimentId), Layout.ExperimentFile), Path.Combine(outDir, Layout.ExperimentFile), overwrite: true);
        File.WriteAllText(Path.Combine(outDir, Layout.ReportHtmlFile), Proctor.Report.Html(stats, rows, experiment));
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Proctor.Report.Markdown(stats, rows, experiment));

        Console.WriteLine($"{Path.GetRelativePath(root, outDir)}/: {Layout.ResultsFile} ({rows.Count} rows), {Layout.StatsFile}, {Layout.ReportHtmlFile}, {Layout.SummaryFile}");
        var ungraded = rows.Count(r => r.Status == CellStatus.Completed && r.Checks is null);
        if (ungraded > 0) Console.WriteLine($"note: {ungraded} completed cell{(ungraded == 1 ? " has" : "s have")} no checks.json; run `proctor grade {experimentId}` first for them to count");
        if (stats.Baseline is { } bl)
        {
            foreach (var (arm, g) in bl.Arms) Console.WriteLine($"  {arm} vs baseline: {(g.DiffPoints >= 0 ? "+" : "")}{g.DiffPoints} pts [{g.Ci95[0]}, {g.Ci95[1]}], {g.Verdict}");
            if (failOn == "regression" && bl.Arms.Values.Any(g => g.Verdict == "regressed")) return 1;
        }
        else if (failOn is not null) Console.WriteLine($"note: --fail-on {failOn} has nothing to fail on; the eval has no baseline (run `proctor baseline <experiment>`)");
        return 0;
    }
}
