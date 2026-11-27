using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proctor;

// Every number the report shows is computed here, once, into stats.json. The renderers read it.
// Methods: project/learnings/prior-art/stats.md. Every task is scored as its mean over its analysed
// attempts, so n is the task count and a rate never overstates independence between attempts.

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

record Exclusion(string Attempt, string Status, string Reason);

record VariantStats(
    int Planned, int Started, int Completed, int Graded, int Analysed, int Decided, List<Exclusion> Excluded, List<Exclusion> Undecided,
    RateJson? Pass, Dictionary<string, RateJson> Checks, List<CheckFailure> Failures, Dictionary<string, int> ExitReasons,
    Summary? DurationMs, Summary? TokensTotal, bool TokensEstimated, Dictionary<string, TaskScore> Tasks, Summary? CostUsd = null, double? CostTotalUsd = null, List<CheckFailure>? Errored = null);

/// <summary>One variant on one task: its score (the mean over its decided attempts, null when none was decided) and the attempts behind it.</summary>
record TaskScore(double? Score, int Passed, int Decided);

/// <summary>A check that could not decide too many of the attempts it applies to, across variants: the check needs rewriting before the report is conclusive.</summary>
record UndecidedCheck(string Check, int Attempts, int Of);

/// <summary>A check that could not grade more than a few of the attempts it applies to, with how many per variant: the check's defect, said once above the numbers.</summary>
record ErroredCheck(string Check, int Attempts, int Of, Dictionary<string, int> Variants);

/// <summary>
/// A check whose verdicts in some attempts are outdated (a script check on agent attempts, changed or added since they were
/// made), with how many per variant: the attempts to make again, said once above the numbers.
/// </summary>
record OutdatedCheck(string Check, int Attempts, int Of, Dictionary<string, int> Variants);

/// <summary>Where a variant fell down: a check that did not pass in some analysed attempts, most often first, with the tasks it happened in.</summary>
record CheckFailure(string Check, int Attempts, int Of, Dictionary<string, int> Tasks);

/// <summary>What the ids mean, in the exam author's words (or derived from a built-in check), so the report can carry them.</summary>
record Descriptions(string? Exam, Dictionary<string, string> Variants, Dictionary<string, string> Tasks, Dictionary<string, string> Checks);

/// <summary>What a task is made of: the fixture it runs against and the checks its attempts carry (the exam's, the fixture's and its own, in that order), each with the level that declared it and what it tests in that task's words, so the report can show a task's criterion on its row.</summary>
record TaskJson(string? Fixture, List<TaskCheck> Checks);

record TaskCheck(string Name, string Level, string Description);

/// <summary>What a rate looks like in stats.json: the rate and interval, with the task and attempt counts behind it.</summary>
record RateJson(double Rate, Interval Ci95, int N, int KAttempts, int NAttempts, int? Errors = null, int? NeedsJudge = null, int? Outdated = null);

/// <summary>One variant against another, paired by task; <c>Tasks</c> is the difference on each shared task, in points.</summary>
/// <summary><c>MdePoints</c> is set only when this pair rests on fewer tasks than the report's widest: the change it alone can confirm.</summary>
record Comparison(string Variant, string Vs, int NPairs, int DiffPoints, int[] Ci95, int Won, int Lost, int Tied, string Verdict, Dictionary<string, int> Tasks, int? MdePoints = null);

record Mde(int NPairs, int Points, int TasksFor10Points, string Sentence);

/// <summary>
/// The live mark as the report read it: the variant it names and since when. <c>NotReference</c> says why that variant is not
/// the reference the others are compared with (its mark is broken, or it has no attempt in this report); null when it is.
/// </summary>
record LiveStats(string Variant, string Since, string? NotReference = null);

record StatsFile(
    string Exam, int NTasks, bool Paired,
    Dictionary<string, VariantStats> Variants, List<Comparison> Comparisons, Mde Mde, string Methods,
    List<string> Tasks, Dictionary<string, Dictionary<string, List<string>>> Matrix, List<string> CorrectnessChecks, List<string> IntegrityChecks,
    Descriptions Descriptions, List<UndecidedCheck> UndecidedChecks, LiveStats? Live = null, Dictionary<string, TaskJson>? TaskDetails = null,
    List<ErroredCheck>? ErroredChecks = null, BuiltFrom? BuiltFrom = null, List<OutdatedCheck>? OutdatedChecks = null,
    int Attempts = 1, List<string>? QualityChecks = null);

/// <summary>
/// The report's stamp: when it was built, the log entries of the runs that made its attempts, each variant's and task's hash as the exam has them now, how
/// many current attempts it read and how many stale ones it left out (each with why), the narrowing asked for, and, per
/// variant, the conditions its attempts were made under when they were not all the same.
/// </summary>
record BuiltFrom(string At, List<string> Log, Dictionary<string, string> Variants, Dictionary<string, string> Tasks, int Current, int Stale,
    List<StaleAttempt>? StaleAttempts = null, string? Narrowed = null, Dictionary<string, List<string>>? Mixed = null);

static class Stats
{
    const double Z = 1.959964;           // 97.5th percentile of the standard normal
    const double ZPower = 2.801582;      // z_0.975 + z_0.80, for MDE at 80% power
    const double AssumedSdOfPairedDifference = 0.5;

    /// <summary>
    /// The statistics of the rows given: the exam's variants and tasks that have an attempt among them, in the exam's order.
    /// With a live mark that holds and a live variant among them, every other variant is compared with it; otherwise none is.
    /// </summary>
    public static StatsFile Compute(Exam exam, List<ResultRow> rows, Live? live = null, BuiltFrom? built = null)
    {
        var variants = exam.Variants.Select(v => v.Id!).Where(v => rows.Any(r => r.Variant == v)).ToList();
        var tasks = exam.Tasks.Select(t => t.Id!).Where(t => rows.Any(r => r.Task == t)).ToList();
        var checks = exam.CheckNames;
        var byVariant = variants.ToDictionary(v => v, v => rows.Where(r => r.Variant == v).ToList());

        var validity = exam.Grading.Integrity ?? [];
        var variantStats = variants.ToDictionary(v => v, v => VariantStats(byVariant[v], tasks, checks, validity));

        var scores = variants.ToDictionary(v => v, v => TaskScores(byVariant[v].Where(r => r.Decided).ToList(), tasks, r => r.Pass == true));
        var broken = live?.Broken(exam);
        var reference = live is not null && broken is null && variants.Contains(live.Variant.Id) ? live.Variant.Id : null;
        var liveStats = live is null ? null : new LiveStats(live.Variant.Id, live.Since,
            broken ?? (reference is null ? "it has no current attempt in this report" : null));
        var comparisons = new List<Comparison>();
        foreach (var other in variants.Where(a => reference is not null && a != reference))
        {
            var c = Compare(other, reference!, scores[other], scores[reference!], tasks);
            if (c is not null) comparisons.Add(c);
        }

        // The report's reach is its widest pairing; a pair on fewer tasks carries its own, smaller reach. With no pairing, the most tasks a variant decided.
        var nPairs = comparisons.Count > 0 ? comparisons.Max(c => c.NPairs) : variants.Select(a => scores[a].Count).DefaultIfEmpty(0).Max();
        var mde = Mde(nPairs);
        comparisons = comparisons.Select(c => c with { MdePoints = c.NPairs < nPairs ? Mde(c.NPairs).Points : null }).ToList();

        var matrix = variants.ToDictionary(a => a, a => tasks.ToDictionary(c => c,
            c => byVariant[a].Where(r => r.Task == c).OrderBy(r => r.Attempt).Select(r => r.Invalid is not null ? "invalid" : r.Outdated is not null ? "outdated" : r.Undecided is not null ? "undecided" : r.Pass switch { true => "pass", false => "fail", null => r.Status }).ToList()));

        // A check undecided in more than a few of the attempts it applies to is a defect in the check; the report says so above the numbers.
        var undecidedChecks = checks.Where(name => !validity.Contains(name)).Select(name =>
        {
            var counted = rows.Where(r => r.Analysed && r.Checks!.ContainsKey(name)).ToList();
            return new UndecidedCheck(name, counted.Count(r => r.Checks![name] == Verdict.NeedsJudge), counted.Count);
        }).Where(u => u.Attempts > 5 || (u.Of > 0 && u.Attempts > 0.05 * u.Of)).ToList();

        // Likewise a check that could not grade them: one warning with the count per variant, not a line under every variant's failures.
        var erroredChecks = checks.Where(name => !validity.Contains(name)).Select(name =>
        {
            var counted = rows.Where(r => r.Analysed && r.Checks!.ContainsKey(name)).ToList();
            var errors = counted.Where(r => r.Checks![name] == Verdict.Error).ToList();
            return new ErroredCheck(name, errors.Count, counted.Count, variants.Where(a => errors.Any(r => r.Variant == a)).ToDictionary(a => a, a => errors.Count(r => r.Variant == a)));
        }).Where(u => u.Attempts > 1 && (u.Attempts > 5 || u.Attempts > 0.05 * u.Of)).ToList();   // one error is a blip, listed under its variant

        // Outdated verdicts are no defect of the check or the variant: the attempts are older than the check. Every one is said, over the graded attempts that carry it.
        var outdatedChecks = checks.Select(name =>
        {
            var carrying = rows.Where(r => r.Checks?.ContainsKey(name) == true).ToList();
            var outdated = carrying.Where(r => r.Checks![name] == Verdict.Outdated).ToList();
            return new OutdatedCheck(name, outdated.Count, carrying.Count, variants.Where(a => outdated.Any(r => r.Variant == a)).ToDictionary(a => a, a => outdated.Count(r => r.Variant == a)));
        }).Where(o => o.Attempts > 0).ToList();

        var methods = "Each task is scored as its mean over its counted, decided attempts, so n in every interval is the number of tasks. "
            + "Per-variant rates use the Wilson 95% interval. "
            + (reference is null
                ? "No variant is live, so none is compared with another. "
                : $"Differences from the live variant, {reference}, use Newcombe's paired method (Wilson square-and-add, with phi from the per-task scores). ")
            + $"The detectable difference assumes 80% power and a per-task paired-difference sd of {AssumedSdOfPairedDifference}. "
            + "Durations are the attempt's wall time including hooks; tokens are the attempt's own count."
            + (reference is null ? "" : $" No multiplicity adjustment; {comparisons.Count} comparison{(comparisons.Count == 1 ? "" : "s")} shown.");

        var descriptions = new Descriptions(
            exam.Def.Description,
            exam.Variants.Where(a => a.Note is not null && variants.Contains(a.Id!)).ToDictionary(a => a.Id!, a => a.Note!),
            exam.Tasks.Where(c => tasks.Contains(c.Id!)).ToDictionary(c => c.Id!, Exam.Describe),
            exam.CheckDescriptions());

        var taskDetails = exam.Tasks.Where(c => tasks.Contains(c.Id!)).ToDictionary(c => c.Id!, c => new TaskJson(c.Fixture, exam.ChecksFor(c).Select(k => new TaskCheck(k.Key, k.Value.Level, Checks.Describe(k.Value.Spec))).ToList()));

        return new StatsFile(exam.Id, tasks.Count, Paired: true, variantStats, comparisons, mde, methods, tasks, matrix, exam.Grading.Correctness!, validity, descriptions, undecidedChecks, liveStats, taskDetails,
            erroredChecks.Count > 0 ? erroredChecks : null, built, outdatedChecks.Count > 0 ? outdatedChecks : null, exam.Attempts, exam.Grading.Quality);
    }

    /// <summary>
    /// The report's stamp, from the current attempts it read and the stale ones it left out. A variant's conditions are its
    /// runs' versions, runner and engine, as their log entries recorded them; it is mixed when its attempts came from runs that differ in one.
    /// </summary>
    public static BuiltFrom Stamp(Exam exam, BankSelection selection, string? narrowed, DateTime at)
    {
        var current = selection.Current;
        var mixed = new Dictionary<string, List<string>>();
        foreach (var variant in exam.Variants.Where(v => current.Any(a => a.Variant == v.Id)))
        {
            var conditions = current.Where(a => a.Variant == variant.Id).Select(a => a.Entry).DistinctBy(b => b.Id).Select(b => Conditions(b, variant.IsAgent)).ToList();
            var differ = conditions.SelectMany(c => c.Keys).Distinct()
                .Select(k => (k, values: conditions.Select(c => c.GetValueOrDefault(k, "none")).Distinct().Order(StringComparer.Ordinal).ToList()))
                .Where(x => x.values.Count > 1).Select(x => $"{x.k} {string.Join(", ", x.values)}").ToList();
            if (differ.Count > 0) mixed[variant.Id!] = differ;
        }
        return new BuiltFrom(at.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            current.Select(a => a.Entry.Id).Distinct().Order(StringComparer.Ordinal).ToList(),
            exam.Variants.Where(v => current.Any(a => a.Variant == v.Id)).ToDictionary(v => v.Id!, v => v.Hash),
            exam.Tasks.Where(t => current.Any(a => a.Task == t.Id)).ToDictionary(t => t.Id!, t => exam.TaskHashes[t.Id!]),
            current.Count, selection.Stale.Count, selection.Stale.Count > 0 ? selection.Stale : null, narrowed, mixed.Count > 0 ? mixed : null);

        // Harbor's version and engine are an agent variant's conditions; a bare variant's run may or may not have had agents beside it.
        static Dictionary<string, string> Conditions(LogEntry b, bool agent)
        {
            var c = new Dictionary<string, string>(b.Versions) { ["runner"] = b.Nb.Runner is { } r ? $"{r.Script} {r.Hash[..Math.Min(r.Hash.Length, 15)]}" : "none" };
            if (agent && b.Harbor is { } h) c["engine"] = h.Engine;
            else c.Remove("harbor");
            return c;
        }
    }

    static VariantStats VariantStats(List<ResultRow> rows, List<string> tasks, List<string> checks, List<string> validity)
    {
        var analysed = rows.Where(r => r.Analysed).ToList();
        var excluded = rows.Where(r => r.Status != AttemptStatus.Pending && !r.Analysed)
            .Select(r => r.Invalid is not null
                ? new Exclusion($"{r.Variant}/{r.Task}/{r.Attempt}", "invalid", r.Invalid)
                : new Exclusion($"{r.Variant}/{r.Task}/{r.Attempt}", r.Status, r.StatusReason ?? "")).ToList();

        var decided = analysed.Where(r => r.Decided).ToList();
        var undecided = analysed.Where(r => r.Undecided is not null).Select(r => new Exclusion($"{r.Variant}/{r.Task}/{r.Attempt}", r.Outdated is not null ? "outdated" : "undecided", r.Undecided!)).ToList();
        var pass = TaskRate(decided, tasks, r => r.Pass == true);
        var checkRates = checks.ToDictionary(name => name, name =>
        {
            // A validity check's rate is over every graded attempt: it says how many counted. Other checks are over the attempts that count.
            // An attempt the check could not decide, or could not grade at all (an error), is out of its denominator on both sides, and counted beside the rate:
            // either is the check's weakness, not the variant's. So is an outdated verdict, which no current check gave.
            var pool = validity.Contains(name) ? rows.Where(r => r.Checks is not null) : analysed;
            var graded = pool.Where(r => r.Checks!.ContainsKey(name)).ToList();
            var decidedByCheck = graded.Where(r => r.Checks![name] is not (Verdict.NeedsJudge or Verdict.Error or Verdict.Outdated)).ToList();
            var rate = TaskRate(decidedByCheck, tasks, r => r.Checks![name] == Verdict.Pass);
            return rate is null ? null : rate with
            {
                Errors = graded.Count(r => r.Checks![name] == Verdict.Error),
                NeedsJudge = graded.Count(r => r.Checks![name] == Verdict.NeedsJudge),
                Outdated = graded.Count(r => r.Checks![name] == Verdict.Outdated) is > 0 and var o ? o : null,
            };
        }).Where(k => k.Value is not null).ToDictionary(k => k.Key, k => k.Value!);

        // Failures: over the counted attempts, every non-validity check that failed, most often first, out of the attempts it decided. A validity fail is an exclusion;
        // an undecided check is listed apart, and so is one that errored, out of the attempts that carry it.
        List<CheckFailure> Tally(string verdict, Func<string, bool> of) => checks.Where(name => !validity.Contains(name))
            .Select(name => (name, attempts: analysed.Where(r => r.Checks!.TryGetValue(name, out var v) && v == verdict).ToList()))
            .Where(x => x.attempts.Count > 0)
            .OrderByDescending(x => x.attempts.Count).ThenBy(x => checks.IndexOf(x.name))
            .Select(x => new CheckFailure(x.name, x.attempts.Count, analysed.Count(r => r.Checks!.TryGetValue(x.name, out var v) && of(v)),
                tasks.Where(c => x.attempts.Any(r => r.Task == c)).ToDictionary(c => c, c => x.attempts.Count(r => r.Task == c))))
            .ToList();
        var failures = Tally(Verdict.Fail, v => v is not (Verdict.NeedsJudge or Verdict.Error or Verdict.Outdated));
        var errored = Tally(Verdict.Error, _ => true);

        var completed = rows.Where(r => r.Status == AttemptStatus.Completed).ToList();
        var exitReasons = completed.Where(r => r.ExitReason is not null).GroupBy(r => r.ExitReason!)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());

        return new VariantStats(
            Planned: rows.Count,
            Started: rows.Count(r => r.Status != AttemptStatus.Pending),
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
            TokensEstimated: completed.Any(r => r.Usage?.Estimated == true),
            CostUsd: Summarise(completed.Where(r => r.Usage?.CostUsd is not null).Select(r => r.Usage!.CostUsd!.Value), digits: 4),
            CostTotalUsd: completed.Any(r => r.Usage?.CostUsd is not null) ? Math.Round(completed.Sum(r => r.Usage?.CostUsd ?? 0), 4) : null,
            Errored: errored.Count > 0 ? errored : null,
            Tasks: tasks.ToDictionary(c => c, c =>
            {
                var runs = decided.Where(r => r.Task == c).ToList();
                return new TaskScore(runs.Count == 0 ? null : Round3(runs.Average(r => r.Pass == true ? 1.0 : 0.0)), runs.Count(r => r.Pass == true), runs.Count);
            }));
    }

    /// <summary>Per-task mean scores over the analysed attempts, for the tasks that have any.</summary>
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
    /// Newcombe 1998 method 10 for paired proportions: the Wilson limits of each variant combined
    /// square-and-add, with phi estimated from the 2x2 task table with Newcombe's continuity
    /// correction (so phi is 0 when a margin is empty and below 1 when no task is discordant).
    /// With one attempt per task the table is the literal count of tasks both passed, only the
    /// first passed, only the second passed, neither; with several attempts it is the expected
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

    static Comparison? Compare(string variant, string vs, Dictionary<string, double> a, Dictionary<string, double> b, List<string> tasks)
    {
        var shared = tasks.Where(c => a.ContainsKey(c) && b.ContainsKey(c)).ToList();
        if (shared.Count == 0) return null;
        var sa = shared.Select(c => a[c]).ToArray();
        var sb = shared.Select(c => b[c]).ToArray();
        var (diff, ci) = NewcombePaired(sa, sb);
        var lo = Points(ci.Lo);
        var hi = Points(ci.Hi);
        return new Comparison(variant, vs, shared.Count, Points(diff), [lo, hi],
            Won: shared.Count(c => a[c] > b[c]), Lost: shared.Count(c => a[c] < b[c]), Tied: shared.Count(c => a[c] == b[c]),
            Verdict: lo > 0 ? "better" : hi < 0 ? "worse" : "no-detectable-difference",
            Tasks: shared.ToDictionary(c => c, c => Points(a[c] - b[c])));
    }

    /// <summary>The minimum detectable effect at 80% power for a paired design with the assumed sd, and the n a 10-point effect needs.</summary>
    public static Mde Mde(int nPairs)
    {
        var points = nPairs == 0 ? 100 : Math.Min(100, (int)Math.Round(100 * ZPower * AssumedSdOfPairedDifference / Math.Sqrt(nPairs)));
        var tasksFor10 = (int)Math.Round(Sq(ZPower * AssumedSdOfPairedDifference / 0.10));
        var rounded = tasksFor10 >= 100 ? (int)Math.Round(tasksFor10 / 10.0) * 10 : tasksFor10;
        var sentence = nPairs == 0
            ? "No paired tasks were analysed, so no difference can be detected."
            : $"With {nPairs} paired task{(nPairs == 1 ? "" : "s")} this report can reliably detect a difference of about {points} points. To detect 10 points you need about {rounded} tasks.";
        return new Mde(nPairs, points, rounded, sentence);
    }

    public static Summary? Summarise(IEnumerable<double> values, int digits = 0)
    {
        var v = values.Order().ToArray();
        if (v.Length == 0) return null;
        return new Summary(v.Length, Median(v), Percentile(v, 0.9), Math.Round(v.Average(), digits), v[0], v[^1]);
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
