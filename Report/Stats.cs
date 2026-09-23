using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proctor;

// Every number the report shows is computed here, once, into stats.json. The renderers read it.
// Methods: project/learnings/prior-art/stats.md. Every task is scored as its mean over its analysed
// samples, so n is the task count and a rate never overstates independence between samples.

[JsonConverter(typeof(IntervalConverter))]
record Interval(double Lo, double Hi);

/// <summary>stats.json writes an interval as [lo, hi].</summary>
sealed class IntervalConverter : JsonConverter<Interval>
{
    public override Interval Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var pair = JsonSerializer.Deserialize<double[]>(ref reader, options)!;
        return new Interval(pair[0], pair[1]);
    }

    public override void Write(Utf8JsonWriter writer, Interval value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.Lo);
        writer.WriteNumberValue(value.Hi);
        writer.WriteEndArray();
    }
}

record Summary(int N, double Median, double P90, double Mean, double Min, double Max);

record Exclusion(string Cell, string Status, string Reason);

record ArmStats(
    int Planned, int Attempted, int Completed, int Graded, int Analysed, int Decided, List<Exclusion> Excluded, List<Exclusion> Undecided,
    RateJson? Pass, Dictionary<string, RateJson> Checks, List<CheckFailure> Failures, Dictionary<string, int> ExitReasons,
    Summary? DurationMs, Summary? TokensTotal, bool TokensEstimated);

/// <summary>A check that could not decide too many of the runs it applies to, across arms: the check needs rewriting before the experiment is conclusive.</summary>
record UndecidedCheck(string Check, int Runs, int Of);

/// <summary>Where an arm fell down: a check that did not pass in some analysed cells, most often first, with the tasks it happened in.</summary>
record CheckFailure(string Check, int Cells, int Of, Dictionary<string, int> Tasks);

/// <summary>What the ids mean, in the suite author's words (or derived from a built-in check), so the report can carry them.</summary>
record Descriptions(string? Suite, Dictionary<string, string> Arms, Dictionary<string, string> Tasks, Dictionary<string, string> Checks);

/// <summary>What a task is made of: the fixture it runs against and the checks its runs carry (the suite's, the fixture's and its own, in that order), so the report can show checks as the task's.</summary>
record TaskJson(string? Fixture, List<string> Checks);

/// <summary>What a rate looks like in stats.json: the rate and interval, with the task and cell counts behind it.</summary>
record RateJson(double Rate, Interval Ci95, int N, int KCells, int NCells, int? Errors = null, int? NeedsJudge = null);

record Comparison(string Arm, string Vs, int NPairs, int DiffPoints, int[] Ci95, int Won, int Lost, int Tied, string Verdict);

record Mde(int NPairs, int Points, int TasksFor10Points, string Sentence);

/// <summary>The guard comparison: every arm against the suite's pinned baseline, with a tolerance on the point estimate.</summary>
record BaselineStats(string Set, string Scores, int TolerancePoints, Dictionary<string, BaselineArm> Arms);

record BaselineArm(int NPairs, int DiffPoints, int[] Ci95, int Won, int Lost, int Tied, string Verdict, Dictionary<string, BaselineTask> Tasks);

record BaselineTask(double Baseline, double Arm);

record StatsFile(
    string Experiment, string Suite, int NTasks, bool Paired,
    Dictionary<string, ArmStats> Arms, List<Comparison> Comparisons, Mde Mde, string Methods,
    List<string> Tasks, Dictionary<string, Dictionary<string, List<string>>> Matrix, List<string> PassChecks, List<string> ValidityChecks,
    Descriptions Descriptions, List<UndecidedCheck> UndecidedChecks, BaselineStats? Baseline = null, Dictionary<string, TaskJson>? TaskDetails = null);

/// <summary>What the report knows about the baseline: the resolved per-task scores, where they came from, and the tolerance asked for.</summary>
record GuardInput(string Set, string Source, Dictionary<string, double> Scores, int TolerancePoints);

static class Stats
{
    const double Z = 1.959964;           // 97.5th percentile of the standard normal
    const double ZPower = 2.801582;      // z_0.975 + z_0.80, for MDE at 80% power
    const double AssumedSdOfPairedDifference = 0.5;

    public static StatsFile Compute(Experiment experiment, Suite suite, List<ResultRow> rows, GuardInput? guard = null)
    {
        var arms = suite.Arms.Select(a => a.Id!).ToList();
        var tasks = suite.Tasks.Select(c => c.Id!).ToList();
        var checks = suite.CheckNames;
        var byArm = arms.ToDictionary(a => a, a => rows.Where(r => r.Arm == a).ToList());

        var validity = suite.Grading.Validity ?? [];
        var armStats = arms.ToDictionary(a => a, a => ArmStats(byArm[a], tasks, checks, validity));

        var scores = arms.ToDictionary(a => a, a => TaskScores(byArm[a].Where(r => r.Decided).ToList(), tasks, r => r.Pass == true));
        var comparisons = new List<Comparison>();
        foreach (var other in arms.Skip(1))
        {
            var c = Compare(other, arms[0], scores[other], scores[arms[0]], tasks);
            if (c is not null) comparisons.Add(c);
        }

        BaselineStats? baseline = null;
        if (guard is not null)
        {
            var against = new Dictionary<string, BaselineArm>();
            foreach (var arm in arms)
            {
                var c = Compare(arm, "baseline", scores[arm], guard.Scores, tasks);
                if (c is null) continue;
                var verdict = c.DiffPoints < -guard.TolerancePoints ? "regressed" : c.DiffPoints > guard.TolerancePoints ? "improved" : "held";
                var perTask = tasks.Where(x => scores[arm].ContainsKey(x) && guard.Scores.ContainsKey(x))
                    .ToDictionary(x => x, x => new BaselineTask(Round3(guard.Scores[x]), Round3(scores[arm][x])));
                against[arm] = new BaselineArm(c.NPairs, c.DiffPoints, c.Ci95, c.Won, c.Lost, c.Tied, verdict, perTask);
            }
            baseline = new BaselineStats(guard.Set, guard.Source, guard.TolerancePoints, against);
        }

        var nPairs = comparisons.Count > 0 ? comparisons.Min(c => c.NPairs) : tasks.Count(c => byArm[arms[0]].Any(r => r.Task == c && r.Decided));
        var mde = Mde(nPairs);

        var matrix = arms.ToDictionary(a => a, a => tasks.ToDictionary(c => c,
            c => byArm[a].Where(r => r.Task == c).OrderBy(r => r.Sample).Select(r => r.Invalid is not null ? "invalid" : r.Undecided is not null ? "undecided" : r.Pass switch { true => "pass", false => "fail", null => r.Status }).ToList()));

        // A check undecided in more than a few of the runs it applies to is a defect in the check; the report says so above the numbers.
        var undecidedChecks = checks.Where(name => !validity.Contains(name)).Select(name =>
        {
            var counted = rows.Where(r => r.Analysed && r.Checks!.ContainsKey(name)).ToList();
            return new UndecidedCheck(name, counted.Count(r => r.Checks![name] == Verdict.NeedsJudge), counted.Count);
        }).Where(u => u.Runs > 5 || (u.Of > 0 && u.Runs > 0.05 * u.Of)).ToList();

        var methods = "Each task is scored as its mean over its counted, decided runs, so n in every interval is the number of tasks. "
            + "Per-arm rates use the Wilson 95% interval. Differences between arms, and against the baseline, use Newcombe's paired method (Wilson square-and-add, with phi from the per-task scores). "
            + $"The detectable difference assumes 80% power and a per-task paired-difference sd of {AssumedSdOfPairedDifference}. "
            + "Durations are the run's wall time including hooks; tokens are what nb reported. "
            + $"No multiplicity adjustment; {comparisons.Count} comparison{(comparisons.Count == 1 ? "" : "s")} shown."
            + (baseline is null ? "" : $" The baseline verdict is the point estimate against a tolerance of {baseline.TolerancePoints} points; the interval is shown so a small n cannot hide.");

        var descriptions = new Descriptions(
            suite.Def.Description,
            suite.Arms.Where(a => a.Description is not null).ToDictionary(a => a.Id!, a => a.Description!),
            suite.Tasks.ToDictionary(c => c.Id!, Suite.Describe),
            suite.CheckDescriptions());

        var taskDetails = suite.Tasks.ToDictionary(c => c.Id!, c => new TaskJson(c.Fixture, suite.ChecksFor(c).Keys.ToList()));

        return new StatsFile(experiment.Id, suite.Id, tasks.Count, Paired: true, armStats, comparisons, mde, methods, tasks, matrix, suite.Grading.Pass!, validity, descriptions, undecidedChecks, baseline, taskDetails);
    }

    static ArmStats ArmStats(List<ResultRow> rows, List<string> tasks, List<string> checks, List<string> validity)
    {
        var analysed = rows.Where(r => r.Analysed).ToList();
        var excluded = rows.Where(r => r.Status != CellStatus.Pending && !r.Analysed)
            .Select(r => r.Invalid is not null
                ? new Exclusion($"{r.Arm}/{r.Task}/{r.Sample}", "invalid", r.Invalid)
                : new Exclusion($"{r.Arm}/{r.Task}/{r.Sample}", r.Status, r.StatusReason ?? "")).ToList();

        var decided = analysed.Where(r => r.Decided).ToList();
        var undecided = analysed.Where(r => r.Undecided is not null).Select(r => new Exclusion($"{r.Arm}/{r.Task}/{r.Sample}", "undecided", r.Undecided!)).ToList();
        var pass = TaskRate(decided, tasks, r => r.Pass == true);
        var checkRates = checks.ToDictionary(name => name, name =>
        {
            // A validity check's rate is over every graded run: it says how many counted. Other checks are over the runs that count.
            // A run the check could not decide is out of its denominator on both sides, and counted beside the rate.
            var pool = validity.Contains(name) ? rows.Where(r => r.Checks is not null) : analysed;
            var graded = pool.Where(r => r.Checks!.ContainsKey(name)).ToList();
            var decidedByCheck = graded.Where(r => r.Checks![name] != Verdict.NeedsJudge).ToList();
            var rate = TaskRate(decidedByCheck, tasks, r => r.Checks![name] == Verdict.Pass);
            return rate is null ? null : rate with
            {
                Errors = graded.Count(r => r.Checks![name] == Verdict.Error),
                NeedsJudge = graded.Count(r => r.Checks![name] == Verdict.NeedsJudge),
            };
        }).Where(k => k.Value is not null).ToDictionary(k => k.Key, k => k.Value!);

        // Failures: over the counted runs, every non-validity check that failed or errored, most often first. A validity fail is an exclusion; an undecided check is listed apart.
        var failures = checks.Where(name => !validity.Contains(name))
            .Select(name => (name, cells: analysed.Where(r => r.Checks!.TryGetValue(name, out var v) && v is Verdict.Fail or Verdict.Error).ToList()))
            .Where(x => x.cells.Count > 0)
            .OrderByDescending(x => x.cells.Count).ThenBy(x => checks.IndexOf(x.name))
            .Select(x => new CheckFailure(x.name, x.cells.Count, analysed.Count(r => r.Checks!.TryGetValue(x.name, out var v) && v != Verdict.NeedsJudge),
                tasks.Where(c => x.cells.Any(r => r.Task == c)).ToDictionary(c => c, c => x.cells.Count(r => r.Task == c))))
            .ToList();

        var completed = rows.Where(r => r.Status == CellStatus.Completed).ToList();
        var exitReasons = completed.Where(r => r.ExitReason is not null).GroupBy(r => r.ExitReason!)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());

        return new ArmStats(
            Planned: rows.Count,
            Attempted: rows.Count(r => r.Status != CellStatus.Pending),
            Completed: completed.Count,
            Graded: rows.Count(r => r.Checks is not null),
            Analysed: analysed.Count,
            Decided: decided.Count,
            Excluded: excluded,
            Undecided: undecided,
            Pass: pass,
            Checks: checkRates,
            Failures: failures,
            ExitReasons: exitReasons,
            DurationMs: Summarise(completed.Where(r => r.DurationMs is not null).Select(r => (double)r.DurationMs!)),
            TokensTotal: Summarise(completed.Where(r => r.Usage?.Total is not null).Select(r => (double)r.Usage!.Total!)),
            TokensEstimated: completed.Any(r => r.Usage?.Estimated == true));
    }

    /// <summary>Per-task mean scores over the analysed samples, for the tasks that have any.</summary>
    static Dictionary<string, double> TaskScores(List<ResultRow> analysed, List<string> tasks, Func<ResultRow, bool> hit) =>
        tasks.Where(c => analysed.Any(r => r.Task == c))
             .ToDictionary(c => c, c => analysed.Where(r => r.Task == c).Average(r => hit(r) ? 1.0 : 0.0));

    static RateJson? TaskRate(List<ResultRow> analysed, List<string> tasks, Func<ResultRow, bool> hit)
    {
        var scores = TaskScores(analysed, tasks, hit);
        if (scores.Count == 0) return null;
        var p = scores.Values.Average();
        return new RateJson(Round3(p), Wilson(p, scores.Count), scores.Count, analysed.Count(hit), analysed.Count);
    }

    /// <summary>Wilson score interval, 95%, no continuity correction. Non-degenerate at 0/n and n/n.</summary>
    public static Interval Wilson(double p, int n)
    {
        if (n == 0) return new Interval(0, 1);
        var z2 = Z * Z;
        var centre = (p + z2 / (2 * n)) / (1 + z2 / n);
        var half = Z * Math.Sqrt(p * (1 - p) / n + z2 / (4.0 * n * n)) / (1 + z2 / n);
        return new Interval(Round3(Math.Max(0, centre - half)), Round3(Math.Min(1, centre + half)));
    }

    /// <summary>
    /// Newcombe 1998 method 10 for paired proportions: the Wilson limits of each arm combined
    /// square-and-add, with phi estimated from the 2x2 task table with Newcombe's continuity
    /// correction (so phi is 0 when a margin is empty and below 1 when no task is discordant).
    /// With one sample per task the table is the literal count of tasks both passed, only the
    /// first passed, only the second passed, neither; with several samples it is the expected
    /// table from the per-task means, which reduces to the same thing when the scores are 0/1.
    /// </summary>
    public static (double Diff, Interval Ci) NewcombePaired(double[] a, double[] b)
    {
        var n = a.Length;
        var both = a.Zip(b, (x, y) => x * y).Sum();
        var onlyA = a.Zip(b, (x, y) => x * (1 - y)).Sum();
        var onlyB = a.Zip(b, (x, y) => (1 - x) * y).Sum();
        var neither = a.Zip(b, (x, y) => (1 - x) * (1 - y)).Sum();
        var p1 = a.Average();
        var p2 = b.Average();
        var (l1, u1) = Wilson(p1, n);
        var (l2, u2) = Wilson(p2, n);
        var phi = Phi(both, onlyA, onlyB, neither);
        var d = p1 - p2;
        var lower = d - Math.Sqrt(Sq(p1 - l1) + Sq(u2 - p2) - 2 * phi * (p1 - l1) * (u2 - p2));
        var upper = d + Math.Sqrt(Sq(u1 - p1) + Sq(p2 - l2) - 2 * phi * (u1 - p1) * (p2 - l2));
        return (d, new Interval(Math.Max(-1, lower), Math.Min(1, upper)));
    }

    /// <summary>Newcombe's phi-hat: (ad - bc)/sqrt of the margins, less n/2 when positive, 0 when a margin is empty.</summary>
    static double Phi(double a, double b, double c, double d)
    {
        var n = a + b + c + d;
        var margins = (a + b) * (c + d) * (a + c) * (b + d);
        if (margins <= 0) return 0;
        var num = a * d - b * c;
        if (num > 0) num = Math.Max(0, num - n / 2);
        return num / Math.Sqrt(margins);
    }

    static Comparison? Compare(string arm, string vs, Dictionary<string, double> a, Dictionary<string, double> b, List<string> tasks)
    {
        var shared = tasks.Where(c => a.ContainsKey(c) && b.ContainsKey(c)).ToList();
        if (shared.Count == 0) return null;
        var sa = shared.Select(c => a[c]).ToArray();
        var sb = shared.Select(c => b[c]).ToArray();
        var (diff, ci) = NewcombePaired(sa, sb);
        var lo = Points(ci.Lo);
        var hi = Points(ci.Hi);
        return new Comparison(arm, vs, shared.Count, Points(diff), [lo, hi],
            Won: shared.Count(c => a[c] > b[c]), Lost: shared.Count(c => a[c] < b[c]), Tied: shared.Count(c => a[c] == b[c]),
            Verdict: lo > 0 ? "better" : hi < 0 ? "worse" : "no-detectable-difference");
    }

    /// <summary>The minimum detectable effect at 80% power for a paired design with the assumed sd, and the n a 10-point effect needs.</summary>
    public static Mde Mde(int nPairs)
    {
        var points = nPairs == 0 ? 100 : Math.Min(100, (int)Math.Round(100 * ZPower * AssumedSdOfPairedDifference / Math.Sqrt(nPairs)));
        var casesFor10 = (int)Math.Round(Sq(ZPower * AssumedSdOfPairedDifference / 0.10));
        var rounded = casesFor10 >= 100 ? (int)Math.Round(casesFor10 / 10.0) * 10 : casesFor10;
        var sentence = nPairs == 0
            ? "No paired tasks were analysed, so no difference can be detected."
            : $"With {nPairs} paired task{(nPairs == 1 ? "" : "s")} this experiment can reliably detect a difference of about {points} points. To detect 10 points you need about {rounded} tasks.";
        return new Mde(nPairs, points, rounded, sentence);
    }

    public static Summary? Summarise(IEnumerable<double> values)
    {
        var v = values.Order().ToArray();
        if (v.Length == 0) return null;
        return new Summary(v.Length, Median(v), Percentile(v, 0.9), Math.Round(v.Average()), v[0], v[^1]);
    }

    static double Median(double[] sorted) =>
        sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;

    /// <summary>Nearest-rank percentile: the smallest value at or above which the given share of values lies.</summary>
    static double Percentile(double[] sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];

    static int Points(double x) => (int)Math.Round(100 * x, MidpointRounding.AwayFromZero);
    static double Round3(double x) => Math.Round(x, 3);
    static double Sq(double x) => x * x;
}
