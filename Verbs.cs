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

    public static int Run(string root, string evalId, string? nbPath)
    {
        var problems = new List<Problem>();
        var config = Eval.LoadConfig(root, problems);
        var eval = Eval.Load(root, evalId, problems);
        if (eval is null || problems.Count > 0)
        {
            foreach (var p in problems) Console.Error.WriteLine(p);
            return 1;
        }
        var id = Runner.Start(root, eval, config, nbPath, Environment.CommandLine, Console.Out);
        Console.WriteLine($"experiment {id}");
        return 0;
    }

    public static int Resume(string root, string experimentId, string? nbPath)
    {
        Runner.Resume(root, experimentId, nbPath, Console.Out);
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
    public static int Report(string root, string experimentId)
    {
        var (experiment, eval) = LoadExperiment(root, experimentId);
        var outDir = Layout.ReportData(root, experimentId);
        Directory.CreateDirectory(outDir);

        var rows = Results.Collect(root, experiment, eval);
        Results.Write(Path.Combine(outDir, Layout.ResultsFile), rows);
        var stats = Stats.Compute(experiment, eval, rows);
        Runner.WriteJson(Path.Combine(outDir, Layout.StatsFile), stats);
        File.Copy(Path.Combine(Layout.Experiment(root, experimentId), Layout.ExperimentFile), Path.Combine(outDir, Layout.ExperimentFile), overwrite: true);
        File.WriteAllText(Path.Combine(outDir, Layout.ReportHtmlFile), Proctor.Report.Html(stats, rows, experiment));
        File.WriteAllText(Path.Combine(outDir, Layout.SummaryFile), Proctor.Report.Markdown(stats, rows, experiment));

        Console.WriteLine($"{Path.GetRelativePath(root, outDir)}/: {Layout.ResultsFile} ({rows.Count} rows), {Layout.StatsFile}, {Layout.ReportHtmlFile}, {Layout.SummaryFile}");
        var ungraded = rows.Count(r => r.Status == CellStatus.Completed && r.Checks is null);
        if (ungraded > 0) Console.WriteLine($"note: {ungraded} completed cell{(ungraded == 1 ? " has" : "s have")} no checks.json; run `proctor grade {experimentId}` first for them to count");
        return 0;
    }
}
