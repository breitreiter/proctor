using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>
/// One stage of one try, in a word: right, wrong, undecided, invalid, or not_reached (a chained try broke before it).
/// Held and Of are the correctness checks it held, of those that apply to it; null when the stage has no grade.
/// </summary>
record TryStage(string Id, string Result, int? Held = null, int? Of = null);

/// <summary>
/// One try, as two numbers: the correctness checks it held, of every correctness check the job has (a stage not reached holds
/// nothing), and its flagged steps, null until `proctor friction` has read every stage it ran. <c>Invalid</c> is the
/// first validity check a stage of it failed: the exam's author said such a try says nothing, so it is listed and not counted.
/// </summary>
record TryRow(int N, string? Made, int Held, int Of, bool Passed, int? Flagged, string? BrokeAt, string? Invalid, int Undecided,
    List<TryStage> Stages, int? ToolCalls, long? TokensIn, long? DurationMs);

record Spread(double Min, double Median, double Max);

/// <summary>
/// One measure of a variant's tries against live's, try against try (Mann–Whitney): in how many of the pairings the
/// variant's try was higher, lower or tied, the exact two-sided p, and the direction in the report's words: better, worse or same.
/// Median and OtherMedian are each side's middle try, and Change the first as a fraction above or below the second (0.8 is 80% more;
/// null when the other's median is zero). They say how far, never whether: the direction and the p are the pairings'. Null on a measure pooled over several settings.
/// </summary>
record Measure(string Direction, int Higher, int Lower, int Tied, int Pairings, double P, bool Confirmed, double? Median = null, double? OtherMedian = null, double? Change = null);

/// <summary>A correctness check that did not hold in a stage: in how many counted tries, and the reason the first of them gave.</summary>
record NotHeld(string Check, int Tries, string? Reason);

/// <summary>
/// One stage across a configuration's counted tries (the user, 2026-11-27: outcomes and plain statistics, nothing per
/// try). Right of Of tries. Checks is its correctness checks; Share is, per try, the fraction of them that held (so
/// stages with different numbers of checks read alike), as lowest, median and highest, and Average its mean. Graded is
/// the tries the share is over: a try whose stage has no grade is in none of it. DidNotHold names what failed.
/// </summary>
record StageTally(string Id, int Right, int Of, int Checks = 0, int Graded = 0, Spread? Share = null, double? Average = null, List<NotHeld>? DidNotHold = null);

/// <summary>One check across a configuration's counted tries: how often it held, of the times it was asked and answered pass or fail, whatever the stage.</summary>
record CheckRate(string Check, string Role, int Held, int Asked);

/// <summary>One configuration's tries, by its key: how many counted, were lost or were invalid, and the two numbers of each as spreads.</summary>
record TryVariant(string Id, int Tries, int Lost, int Invalid, int Passed, Spread? Held, Spread? Flagged,
    List<StageTally> Stages, List<TryRow> Rows, Spread? ToolCalls = null, Spread? Tokens = null, Spread? Duration = null, List<CheckRate>? Checks = null);

/// <summary>A try in which a stage did not run: left out of every number.</summary>
record LostTry(string Variant, int N, string At, string Why);

/// <summary>A stage as the report names it. Checks is every check it carries with its role (correctness, integrity, quality, observation).</summary>
record StageInfo(string Id, int Place, string About, List<string> CorrectnessChecks, Dictionary<string, string>? Checks = null);

/// <summary>What <see cref="Tries.Compute"/> reads out of the bank: the stages, each configuration's tries, and the tries that were lost.</summary>
record TriesRead(List<StageInfo> Stages, List<TryVariant> Variants, List<LostTry> Lost);

/// <summary>
/// One kind of friction in a variant's tries: a label and what the rule fired on (a reader's points have no detail and
/// group by label). In how many tries, how many flagged steps when it happens, and its flagged steps over all the tries
/// divided by their number, which is what the kinds sort by. <c>Example</c> is a stage where it happened, as try/stage.
/// </summary>
record FrictionKind(string Label, string? Detail, List<string> Stages, int In, int Of, Spread When, double PerTry, string Example);

/// <summary>One label of friction in a variant's tries: its flagged steps a try, over all the counted tries. A step may carry several labels, so the labels add up to more than the flagged steps.</summary>
record FrictionLabel(string Label, double PerTry);

record FrictionVariant(string Id, int Tries, double PerTry, List<FrictionKind> Kinds, List<FrictionLabel>? Labels = null);

/// <summary>reports/&lt;exam&gt;/explore/friction.json: what recurs across a variant's tries (check-evidence.md, C5.4).</summary>
record FrictionRollup(string Schema, string Exam, List<FrictionVariant> Variants, BuiltFrom? BuiltFrom);

/// <summary>
/// The tries of an exam, read out of its bank. A try is the unit: every stage that applies in its configuration once,
/// in one workspace. Stages share that workspace and so share causes, which makes a stage never a unit of comparison
/// (plans/bank.md). Two sets of tries are compared try against try, on correctness checks held and on flagged steps, by
/// Mann–Whitney (<see cref="Compare"/>); which sets, and what is held still between them, is the report's
/// (<see cref="Configurations"/>).
/// </summary>
static class Tries
{
    public const string FrictionSchema = "friction-rollup/1";
    const double Alpha = 0.05;

    public static (TriesRead Stats, FrictionRollup Friction) Compute(string root, Exam exam, List<BankAttempt> current, List<BankAttempt> bank, List<ResultRow> rows, BuiltFrom? built)
    {
        var surface = new Surface(exam.Def.Surface);
        var kinds = new Dictionary<string, List<(int Try, string Stage, string Label, string? Detail, int Steps)>>();
        var variants = new List<TryVariant>();
        var ids = exam.Variants.Select(v => v.Id!).Where(v => current.Any(a => a.Variant == v)).ToList();

        foreach (var variant in ids)
        {
            // A try is the stages that apply in its configuration, and holds of those stages' correctness checks.
            var stages = exam.StagesOf(variant);
            // Of the correctness checks that apply in this configuration: one that does not apply is no part of what a try can hold.
            var of = stages.Sum(s => Standard.PassIn(exam, s, variant).Count);
            var found = kinds[variant] = [];
            var tryRows = new List<TryRow>();
            foreach (var group in current.Where(a => a.Variant == variant).GroupBy(a => a.Attempt).OrderBy(g => g.Key))
            {
                var n = group.Key;
                var perStage = stages.Select(s => (Stage: s, Attempt: group.FirstOrDefault(a => a.Task == s.Id), Row: rows.FirstOrDefault(r => r.Variant == variant && r.Attempt == n && r.Task == s.Id))).ToList();
                int held = 0, undecided = 0;
                int? flagged = 0;
                string? invalid = null;
                var results = new List<TryStage>();
                foreach (var (stage, attempt, row) in perStage)
                {
                    if (row?.Checks is null) { results.Add(new TryStage(stage.Id!, row?.Status == AttemptStatus.Skipped ? "not_reached" : "undecided")); if (row?.Status == AttemptStatus.Completed) flagged = null; continue; }
                    var pass = Standard.PassIn(exam, stage, variant);
                    held += pass.Count(c => row.Checks.GetValueOrDefault(c) == Verdict.Pass);
                    undecided += pass.Count(c => row.Checks.GetValueOrDefault(c) is Verdict.NeedsJudge or Verdict.Outdated);
                    invalid ??= row.Invalid is null ? null : $"{stage.Id}: {row.Invalid}";
                    results.Add(new TryStage(stage.Id!, row.Invalid is not null ? "invalid" : row.Pass switch { true => "right", false => "wrong", null => "undecided" },
                        pass.Count(c => row.Checks.GetValueOrDefault(c) == Verdict.Pass), pass.Count));
                    var points = ReadFriction(root, attempt!, surface);
                    if (points is null) { flagged = null; continue; }
                    if (flagged is not null) flagged += points.Value.Flagged;
                    found.AddRange(points.Value.Kinds.Select(k => (n, stage.Id!, k.Label, k.Detail, k.Steps)));
                }
                var manifests = group.Select(a => Path.Combine(a.Dir(root), Layout.ManifestFile)).Where(File.Exists).Select(Runner.ReadManifest).ToList();
                var ran = perStage.Where(p => p.Row is { Status: AttemptStatus.Completed }).Select(p => p.Row!).ToList();
                tryRows.Add(new TryRow(n, manifests.Select(m => m.Started).Where(s => s is not null).Min(), held, of, held == of, flagged,
                    perStage.Any(p => p.Row?.Status == AttemptStatus.Skipped) ? perStage.Last(p => p.Row?.Status == AttemptStatus.Completed).Stage.Id : null,
                    invalid, undecided, results, ran.Sum(r => r.ToolCalls ?? 0), ran.Sum(r => r.Usage?.Input ?? 0), manifests.Sum(m => m.DurationMs ?? 0)));
            }
            var counted = tryRows.Where(t => t.Invalid is null).ToList();
            var lost = bank.Where(a => a.Lost && a.Variant == variant && Bank.Stale(exam, a) is null).Select(a => a.Attempt).Distinct().Count();
            variants.Add(new TryVariant(variant, counted.Count, lost, tryRows.Count - counted.Count, counted.Count(t => t.Passed),
                SpreadOf(counted.Select(t => (double)t.Held)), counted.Count > 0 && counted.All(t => t.Flagged is not null) ? SpreadOf(counted.Select(t => (double)t.Flagged!.Value)) : null,
                stages.Select(s =>
                {
                    var graded = counted.Select(t => t.Stages.First(x => x.Id == s.Id)).Where(x => x.Of is > 0).ToList();
                    var shares = graded.Select(x => (double)x.Held!.Value / x.Of!.Value).ToList();
                    var mine = rows.Where(r => r.Variant == variant && r.Task == s.Id && r.Checks is not null && counted.Any(t => t.N == r.Attempt)).ToList();
                    var notHeld = Standard.PassIn(exam, s, variant).Select(c => (Check: c, Failed: mine.Where(r => r.Checks!.GetValueOrDefault(c) == Verdict.Fail).OrderBy(r => r.Attempt).ToList()))
                        .Where(x => x.Failed.Count > 0).Select(x => new NotHeld(x.Check, x.Failed.Count, x.Failed[0].Reasons?.GetValueOrDefault(x.Check))).ToList();
                    return new StageTally(s.Id!, counted.Count(t => t.Stages.First(x => x.Id == s.Id).Result == "right"), counted.Count, Standard.PassIn(exam, s, variant).Count, graded.Count,
                        SpreadOf(shares), shares.Count > 0 ? Math.Round(shares.Average(), 4) : null, notHeld.Count > 0 ? notHeld : null);
                }).ToList(), tryRows,
                Whole(counted, t => t.ToolCalls), Whole(counted, t => t.TokensIn), Whole(counted, t => t.DurationMs),
                // An integrity check's rate is over every graded try, since it says how many were counted; any other check's is over the counted ones.
                exam.CheckNames.Select(c => (Check: c, Verdicts: rows.Where(r => r.Variant == variant && (exam.RoleOf(c) == Roles.Integrity ? tryRows : counted).Any(t => t.N == r.Attempt)).Select(r => r.Checks?.GetValueOrDefault(c)).Where(v => v is Verdict.Pass or Verdict.Fail).ToList()))
                    .Where(x => x.Verdicts.Count > 0).Select(x => new CheckRate(x.Check, exam.RoleOf(x.Check), x.Verdicts.Count(v => v == Verdict.Pass), x.Verdicts.Count)).ToList()));
        }

        // A configuration never in `current` can still have lost every try it made: it has no numbers, but its lost tries are said.
        var lostTries = bank.Where(a => a.Lost && Bank.Stale(exam, a) is null).GroupBy(a => (a.Variant, a.Attempt)).OrderBy(g => exam.Variants.FindIndex(v => v.Id == g.Key.Variant)).ThenBy(g => g.Key.Attempt)
            .Select(g =>
            {
                var at = g.FirstOrDefault(a => a.Status == AttemptStatus.Failed) ?? g.First(a => a.Unfinished);
                var manifest = Path.Combine(at.Dir(root), Layout.ManifestFile);
                return new LostTry(g.Key.Variant, g.Key.Attempt, at.Task, (File.Exists(manifest) ? Runner.ReadManifest(manifest).StatusReason : null) ?? at.Status);
            }).ToList();

        var rollup = variants.Select(v => Rollup(v, kinds[v.Id])).ToList();
        return (new TriesRead(
                exam.Tasks.Select((s, i) => new StageInfo(s.Id!, i + 1, Exam.Describe(s), exam.CorrectnessFor(s), exam.ChecksFor(s).Keys.ToDictionary(k => k, exam.RoleOf))).ToList(), variants, lostTries),
            new FrictionRollup(FrictionSchema, exam.Id, rollup, built));
    }

    /// <summary>A stage's friction.json as kinds: each label and detail with the steps its points cover, and the stage's flagged steps. Null when the stage has not been read.</summary>
    static (int Flagged, List<(string Label, string? Detail, int Steps)> Kinds)? ReadFriction(string root, BankAttempt a, Surface surface)
    {
        var dir = a.Dir(root);
        var file = Path.Combine(dir, Layout.FrictionFile);
        var trajectory = Path.Combine(dir, Layout.TrajectoryFile);
        if (!File.Exists(file) || !File.Exists(trajectory)) return null;
        var friction = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        // A point names its first and last step; how many steps lie between is read off the steps as friction numbered them.
        var order = Friction.StepIds(JsonNode.Parse(File.ReadAllText(trajectory))!.AsObject(), surface);
        var byKind = new Dictionary<(string, string?), HashSet<string>>();
        foreach (var p in (friction["points"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var label = p["label"]!.GetValue<string>();
            var detail = p["detail"] is JsonArray { Count: > 0 } d ? string.Join("; ", d.Select(x => x!.GetValue<string>())) : null;
            int from = order.IndexOf(p["steps"]![0]!.GetValue<string>()), to = order.IndexOf(p["steps"]![1]!.GetValue<string>());
            if (!byKind.TryGetValue((label, detail), out var steps)) byKind[(label, detail)] = steps = [];
            steps.UnionWith(from < 0 || to < from ? [p["steps"]![0]!.GetValue<string>()] : order.Skip(from).Take(to - from + 1));
        }
        var flagged = friction["summary"]?["flagged_steps"]?.GetValue<int>() ?? byKind.Values.SelectMany(s => s).Distinct().Count();
        return (flagged, byKind.Select(k => (k.Key.Item1, k.Key.Item2, k.Value.Count)).ToList());
    }

    static FrictionVariant Rollup(TryVariant v, List<(int Try, string Stage, string Label, string? Detail, int Steps)> found)
    {
        var counted = v.Rows.Where(t => t.Invalid is null).Select(t => t.N).ToHashSet();
        var n = counted.Count;
        var kinds = found.Where(f => counted.Contains(f.Try)).GroupBy(f => (f.Label, f.Detail)).Select(g =>
        {
            var perTry = g.GroupBy(f => f.Try).Select(t => (double)t.Sum(f => f.Steps)).ToList();
            var example = g.OrderBy(f => f.Try).First();
            return new FrictionKind(g.Key.Label, g.Key.Detail, g.Select(f => f.Stage).Distinct().ToList(), perTry.Count, n, SpreadOf(perTry)!, Math.Round(perTry.Sum() / n, 2), $"{example.Try}/{example.Stage}");
        }).OrderByDescending(k => k.PerTry).ThenBy(k => k.Label, StringComparer.Ordinal).ThenBy(k => k.Detail, StringComparer.Ordinal).ToList();
        var flagged = v.Rows.Where(t => t.Invalid is null && t.Flagged is not null).Select(t => (double)t.Flagged!.Value).ToList();
        var labels = n == 0 ? [] : found.Where(f => counted.Contains(f.Try)).GroupBy(f => f.Label).Select(g => new FrictionLabel(g.Key, Math.Round((double)g.Sum(f => f.Steps) / n, 2)))
            .OrderByDescending(l => l.PerTry).ThenBy(l => l.Label, StringComparer.Ordinal).ToList();
        return new FrictionVariant(v.Id, n, flagged.Count == 0 ? 0 : Math.Round(flagged.Average(), 2), kinds, labels);
    }

    public static Measure Compare(List<double> mine, List<double> theirs, bool higherIsBetter)
    {
        int higher = mine.Sum(a => theirs.Count(b => a > b)), lower = mine.Sum(a => theirs.Count(b => a < b));
        var p = MannWhitneyP(mine, theirs);
        var direction = higher == lower ? "same" : (higher > lower) == higherIsBetter ? "better" : "worse";
        var (median, other) = (SpreadOf(mine)?.Median, SpreadOf(theirs)?.Median);
        return new Measure(direction, higher, lower, mine.Count * theirs.Count - higher - lower, mine.Count * theirs.Count, Math.Round(p, 4), direction != "same" && p < Alpha,
            median, other, median is { } m && other is { } o && o != 0 ? Math.Round((m - o) / o, 4) : null);
    }

    /// <summary>The smallest two-sided p two samples of these sizes can reach: every value of one above every value of the other.</summary>
    public static double MinP(int n, int m) => n == 0 || m == 0 ? 1 : Math.Min(1, 2 / Choose(n + m, n));

    static double Choose(int n, int k)
    {
        double c = 1;
        for (var i = 1; i <= k; i++) c = c * (n - k + i) / i;
        return c;
    }

    /// <summary>
    /// Mann–Whitney, two-sided. Exact while the ways of choosing which values are the first sample's can be listed (ties
    /// take the mean of their ranks, and the listing is over those same ranks); past that, the normal approximation
    /// corrected for ties.
    /// </summary>
    public static double MannWhitneyP(List<double> a, List<double> b)
    {
        int n = a.Count, m = b.Count, total = n + m;
        if (n == 0 || m == 0) return 1;
        var all = a.Concat(b).ToList();
        var sorted = all.Order().ToList();
        var ranks = all.Select(x => (sorted.IndexOf(x) + 1 + sorted.LastIndexOf(x) + 1) / 2.0).ToArray();
        double observed = ranks.Take(n).Sum(), mean = n * (total + 1) / 2.0, distance = Math.Abs(observed - mean);
        if (distance < 1e-9) return 1;
        if (Choose(total, n) <= 400_000)
        {
            long atLeast = 0, ways = 0;
            void Pick(int from, int left, double sum)
            {
                if (left == 0) { ways++; if (Math.Abs(sum - mean) >= distance - 1e-9) atLeast++; return; }
                for (var i = from; i <= total - left; i++) Pick(i + 1, left - 1, sum + ranks[i]);
            }
            Pick(0, n, 0);
            return (double)atLeast / ways;
        }
        var ties = sorted.GroupBy(x => x).Sum(g => Math.Pow(g.Count(), 3) - g.Count());
        var variance = n * m / 12.0 * (total + 1 - ties / (total * (total - 1.0)));
        if (variance <= 0) return 1;
        var z = distance / Math.Sqrt(variance);
        return Math.Min(1, 2 * (1 - Phi(z)));
    }

    // The standard normal's distribution function (Abramowitz and Stegun 26.2.17).
    public static double Phi(double z)
    {
        var t = 1 / (1 + 0.2316419 * z);
        var tail = Math.Exp(-z * z / 2) / Math.Sqrt(2 * Math.PI) * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return 1 - tail;
    }

    /// <summary>A count every counted try gives, as a spread; null when a try lacks it, since a median over some of the tries is not the tries'.</summary>
    static Spread? Whole(List<TryRow> counted, Func<TryRow, double?> of) => counted.Count > 0 && counted.All(t => of(t) is not null) ? SpreadOf(counted.Select(t => of(t)!.Value)) : null;

    static Spread? SpreadOf(IEnumerable<double> values)
    {
        var v = values.Order().ToArray();
        return v.Length == 0 ? null : new Spread(v[0], v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2, v[^1]);
    }
}
