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

/// <summary><c>CanConfirm</c> is whether these two counts of tries could confirm anything at 95%: 4 a side can, or 3 against 5.</summary>
record AgainstLive(string Vs, int Tries, int LiveTries, bool CanConfirm, Measure Held, Measure? Flagged, List<string> Moved, string Sentence);

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

record TryVariant(string Id, bool Live, int Tries, int Lost, int Invalid, int Passed, Spread? Held, Spread? Flagged,
    List<StageTally> Stages, List<TryRow> Rows, AgainstLive? AgainstLive, Spread? ToolCalls = null, Spread? Tokens = null, Spread? Duration = null, List<CheckRate>? Checks = null);

/// <summary>A try in which a stage did not run: left out of every number.</summary>
record LostTry(string Variant, int N, string At, string Why);

/// <summary>A stage as the report names it. Checks is every check it carries with its role (correctness, integrity, quality, observation).</summary>
record StageInfo(string Id, int Place, string About, List<string> CorrectnessChecks, Dictionary<string, string>? Checks = null);

/// <summary>stats.json of a staged exam: each variant's tries, and each variant but live against live.</summary>
record TriesFile(string Schema, string Exam, bool Chained, List<StageInfo> Stages, int CorrectnessChecks, LiveStats? Live,
    List<TryVariant> Variants, List<LostTry> Lost, string Methods, BuiltFrom? BuiltFrom);

/// <summary>
/// One kind of friction in a variant's tries: a label and what the rule fired on (a reader's points have no detail and
/// group by label). In how many tries, how many flagged steps when it happens, and its flagged steps over all the tries
/// divided by their number, which is what the kinds sort by. <c>Example</c> is a stage where it happened, as try/stage.
/// </summary>
record FrictionKind(string Label, string? Detail, List<string> Stages, int In, int Of, Spread When, double PerTry, string Example);

record FrictionVariant(string Id, int Tries, double PerTry, List<FrictionKind> Kinds);

/// <summary>reports/&lt;exam&gt;/explore/friction.json: what recurs across a variant's tries (check-evidence.md, C5.4).</summary>
record FrictionRollup(string Schema, string Exam, List<FrictionVariant> Variants, BuiltFrom? BuiltFrom);

/// <summary>
/// The report of a staged exam. A try is the unit: every stage once, in one workspace. Stages share that workspace and
/// so share causes, which makes a stage never a unit of comparison (plans/bank.md). A variant is compared with live try
/// against try, on correctness checks held and on flagged steps, by Mann–Whitney; stages and kinds of friction explain a
/// verdict ("moved most") and never make it.
/// </summary>
static class Tries
{
    public const string Schema = "tries/1", FrictionSchema = "friction-rollup/1";
    const double Alpha = 0.05;

    public static (TriesFile Stats, FrictionRollup Friction) Compute(string root, Exam exam, List<BankAttempt> current, List<BankAttempt> bank, List<ResultRow> rows, Live? live, BuiltFrom? built)
    {
        var surface = new Surface(exam.Def.Surface);
        var kinds = new Dictionary<string, List<(int Try, string Stage, string Label, string? Detail, int Steps)>>();
        var variants = new List<TryVariant>();
        var ids = exam.Variants.Select(v => v.Id!).Where(v => current.Any(a => a.Variant == v)).ToList();

        foreach (var variant in ids)
        {
            // On an exam with axes a try is the stages that apply in its configuration, and holds of those stages' correctness checks.
            var stages = exam.StagesOf(variant);
            // Of the correctness checks that apply in this configuration: one that does not apply is no part of what a try can hold.
            var headline = stages.Sum(s => Standard.PassIn(exam, s, variant).Count);
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
                tryRows.Add(new TryRow(n, manifests.Select(m => m.Started).Where(s => s is not null).Min(), held, headline, held == headline, flagged,
                    perStage.Any(p => p.Row?.Status == AttemptStatus.Skipped) ? perStage.Last(p => p.Row?.Status == AttemptStatus.Completed).Stage.Id : null,
                    invalid, undecided, results, ran.Sum(r => r.ToolCalls ?? 0), ran.Sum(r => r.Usage?.Input ?? 0), manifests.Sum(m => m.DurationMs ?? 0)));
            }
            var counted = tryRows.Where(t => t.Invalid is null).ToList();
            var lost = bank.Where(a => a.Lost && a.Variant == variant && Bank.Stale(exam, a) is null).Select(a => a.Attempt).Distinct().Count();
            variants.Add(new TryVariant(variant, false, counted.Count, lost, tryRows.Count - counted.Count, counted.Count(t => t.Passed),
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
                }).ToList(), tryRows, null,
                Whole(counted, t => t.ToolCalls), Whole(counted, t => t.TokensIn), Whole(counted, t => t.DurationMs),
                // An integrity check's rate is over every graded try, since it says how many were counted; any other check's is over the counted ones.
                exam.CheckNames.Select(c => (Check: c, Verdicts: rows.Where(r => r.Variant == variant && (exam.RoleOf(c) == Roles.Integrity ? tryRows : counted).Any(t => t.N == r.Attempt)).Select(r => r.Checks?.GetValueOrDefault(c)).Where(v => v is Verdict.Pass or Verdict.Fail).ToList()))
                    .Where(x => x.Verdicts.Count > 0).Select(x => new CheckRate(x.Check, exam.RoleOf(x.Check), x.Verdicts.Count(v => v == Verdict.Pass), x.Verdicts.Count)).ToList()));
        }

        // A variant never in `current` can still have lost every try it made: it is not a column, but its lost tries are said.
        var lostTries = bank.Where(a => a.Lost && Bank.Stale(exam, a) is null).GroupBy(a => (a.Variant, a.Attempt)).OrderBy(g => exam.Variants.FindIndex(v => v.Id == g.Key.Variant)).ThenBy(g => g.Key.Attempt)
            .Select(g =>
            {
                var at = g.FirstOrDefault(a => a.Status == AttemptStatus.Failed) ?? g.First(a => a.Unfinished);
                var manifest = Path.Combine(at.Dir(root), Layout.ManifestFile);
                return new LostTry(g.Key.Variant, g.Key.Attempt, at.Task, (File.Exists(manifest) ? Runner.ReadManifest(manifest).StatusReason : null) ?? at.Status);
            }).ToList();

        var rollup = variants.Select(v => Rollup(v, kinds[v.Id])).ToList();
        var broken = live?.Broken(exam);
        var reference = live is not null && broken is null ? variants.FirstOrDefault(v => v.Id == live.Variant.Id && v.Tries > 0) : null;
        var liveStats = live is null ? null : new LiveStats(live.Variant.Id, live.Since, broken ?? (reference is null ? "it has no counted try in this report" : null));
        variants = variants.Select(v => v == reference ? v with { Live = true }
            : reference is null || v.Tries == 0 ? v
            : v with { AgainstLive = Against(v, reference, rollup.First(r => r.Id == v.Id), rollup.First(r => r.Id == reference.Id)) }).ToList();

        var methods = "A try is every stage of the exam once, in one workspace. Each try gives two numbers: the correctness checks it held, of all the exam has across its stages "
            + "(a stage a chained try did not reach holds nothing), and its flagged steps, the steps inside any friction point. "
            + "A variant is compared with the live one try against try, by the Mann–Whitney test with an exact two-sided p; a difference is confirmed at p < 0.05, "
            + "which needs at least 4 tries a side, or 3 against 5. A stage is never compared on its own: stages share a workspace, so they share causes. "
            + "A try in which a stage did not run is lost and is in no number; a try with a stage that failed a validity check is listed and not counted.";
        return (new TriesFile(Schema, exam.Id, exam.Chained,
                exam.Tasks.Select((s, i) => new StageInfo(s.Id!, i + 1, Exam.Describe(s), exam.CorrectnessFor(s), exam.ChecksFor(s).Keys.ToDictionary(k => k, exam.RoleOf))).ToList(), exam.Tasks.Sum(s => exam.CorrectnessFor(s).Count), liveStats, variants, lostTries, methods, built),
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
        return new FrictionVariant(v.Id, n, flagged.Count == 0 ? 0 : Math.Round(flagged.Average(), 2), kinds);
    }

    static AgainstLive Against(TryVariant v, TryVariant live, FrictionVariant vFriction, FrictionVariant liveFriction)
    {
        var (mine, theirs) = (v.Rows.Where(t => t.Invalid is null).ToList(), live.Rows.Where(t => t.Invalid is null).ToList());
        var can = MinP(mine.Count, theirs.Count) < Alpha;
        var held = Compare(mine.Select(t => (double)t.Held).ToList(), theirs.Select(t => (double)t.Held).ToList(), higherIsBetter: true);
        var flagged = v.Flagged is not null && live.Flagged is not null
            ? Compare(mine.Select(t => (double)t.Flagged!.Value).ToList(), theirs.Select(t => (double)t.Flagged!.Value).ToList(), higherIsBetter: false) : null;

        // What moved: the stages whose share of tries right changed most, then the kinds of friction whose steps a try changed most.
        var moved = v.Stages.Select(s => (s, Live: live.Stages.First(l => l.Id == s.Id)))
            .Select(x => (Text: $"{x.s.Id} (right in {x.s.Right} of {x.s.Of}, live {x.Live.Right} of {x.Live.Of})", Size: Math.Abs((double)x.s.Right / x.s.Of - (double)x.Live.Right / x.Live.Of)))
            .Where(x => x.Size > 0).OrderByDescending(x => x.Size).Take(2).Select(x => x.Text).ToList();
        if (flagged is not null)
            moved.AddRange(vFriction.Kinds.Select(k => (k.Label, k.Detail)).Union(liveFriction.Kinds.Select(k => (k.Label, k.Detail)))
                .Select(k => (k, Diff: (vFriction.Kinds.FirstOrDefault(x => (x.Label, x.Detail) == k)?.PerTry ?? 0) - (liveFriction.Kinds.FirstOrDefault(x => (x.Label, x.Detail) == k)?.PerTry ?? 0)))
                .Where(x => Math.Abs(x.Diff) >= 0.5).OrderByDescending(x => Math.Abs(x.Diff)).Take(2)
                .Select(x => $"{x.k.Label}{(x.k.Detail is null ? "" : $" ({x.k.Detail})")}, {x.Diff.ToString("+0.#;-0.#", CultureInfo.InvariantCulture)} flagged steps a try"));

        var sentence = new StringBuilder();
        if (flagged is null) sentence.Append($"Correctness checks held: {held.Direction}. Flagged steps not read; `proctor friction` reads them.");
        else if (held.Direction == flagged.Direction) sentence.Append(held.Direction == "same" ? "No difference on either measure." : $"{char.ToUpperInvariant(held.Direction[0])}{held.Direction[1..]} on both.");
        else sentence.Append($"Correctness checks held: {held.Direction}. Flagged steps: {flagged.Direction}.");
        if (!can) sentence.Append($" {Count(mine.Count)} against live's {theirs.Count}, too few to confirm.");
        else
        {
            if (held.Direction != "same") sentence.Append(Confirmation(held, "correctness checks held", held.Direction == "better" ? "held more" : "held fewer", held.Direction == "better" ? held.Higher : held.Lower));
            if (flagged is not null && flagged.Direction != "same") sentence.Append(Confirmation(flagged, "flagged steps", flagged.Direction == "better" ? "had fewer flagged steps" : "had more flagged steps", flagged.Direction == "better" ? flagged.Lower : flagged.Higher));
        }
        if (moved.Count > 0) sentence.Append($" Moved most: {string.Join("; ", moved)}.");
        return new AgainstLive(live.Id, mine.Count, theirs.Count, can, held, flagged, moved, sentence.ToString());

        static string Confirmation(Measure m, string name, string verb, int k) =>
            $" {(m.Confirmed ? "Confirmed" : "Not confirmed")} on {name}: a try {verb} than a try of live in {k} of {m.Pairings} pairings.";
    }

    static string Count(int n) => $"{n} {(n == 1 ? "try" : "tries")}";

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

    /// <summary>summary.md: the comparison, each variant's stages by try, what recurs, the tries left out, and how it was counted.</summary>
    public static string Markdown(TriesFile stats, FrictionRollup friction)
    {
        var md = new StringBuilder($"# {stats.Exam}\n\nOne job of {stats.Stages.Count} stage{(stats.Stages.Count == 1 ? "" : "s")}{(stats.Chained ? ", chained" : "")}; {stats.CorrectnessChecks} correctness check{(stats.CorrectnessChecks == 1 ? "" : "s")} a try. Built {stats.BuiltFrom?.At}.\n\n");
        if (stats.Live is { NotReference: { } why }) md.Append($"> {stats.Live.Variant} is marked live, but nothing is compared with it: {why}.\n\n");
        else if (stats.Live is null && stats.Variants.Count > 1) md.Append("> Nothing is live, so no variant is compared with another; `proctor live` marks one.\n\n");

        md.Append("## Did the change help?\n\n| | " + string.Join(" | ", stats.Variants.Select(v => $"{v.Id}{(v.Live ? " (live)" : "")}")) + " |\n|---|" + string.Concat(stats.Variants.Select(_ => "---|")) + "\n");
        md.Append("| Tries | " + string.Join(" | ", stats.Variants.Select(v => $"{v.Tries}{(v.Lost > 0 ? $", {v.Lost} lost" : "")}{(v.Invalid > 0 ? $", {v.Invalid} invalid" : "")}")) + " |\n");
        md.Append("| Correctness checks held per try | " + string.Join(" | ", stats.Variants.Select(v => v.Held is null ? "—" : $"{Range(v.Held)} of {stats.CorrectnessChecks}; passed in {v.Passed} of {v.Tries}")) + " |\n");
        md.Append("| Flagged steps per try | " + string.Join(" | ", stats.Variants.Select(v => v.Flagged is null ? "not read" : $"{Range(v.Flagged)}, median {Num(v.Flagged.Median)}")) + " |\n");
        md.Append("| Against live | " + string.Join(" | ", stats.Variants.Select(v => v.Live ? "this is live" : v.AgainstLive?.Sentence ?? "—")) + " |\n\n");

        foreach (var v in stats.Variants)
        {
            md.Append($"## {v.Id}{(v.Live ? " (live)" : "")}\n\n| Stage | " + string.Join(" | ", v.Rows.Select(t => $"try {t.N}")) + " | |\n|---|" + string.Concat(v.Rows.Select(_ => "---|")) + "---|\n");
            foreach (var s in stats.Stages)
            {
                var tally = v.Stages.First(x => x.Id == s.Id);
                md.Append($"| {s.Place}. {s.About} `{s.Id}` | " + string.Join(" | ", v.Rows.Select(t => t.Stages.First(x => x.Id == s.Id).Result.Replace('_', ' ')))
                    + $" | {(tally.Of == 0 ? "" : tally.Right == tally.Of ? "right every time" : v.Rows.All(t => t.Stages.First(x => x.Id == s.Id).Result == "not_reached") ? "never reached" : tally.Right == 0 ? "never right" : $"right in {tally.Right} of {tally.Of}")} |\n");
            }
            md.Append("| Correctness checks held | " + string.Join(" | ", v.Rows.Select(t => $"{t.Held} of {t.Of}{(t.Invalid is not null ? " (invalid)" : "")}")) + " | |\n\n");
            foreach (var t in v.Rows.Where(t => t.Invalid is not null)) md.Append($"- Try {t.N} is not counted: {t.Invalid}.\n");
            foreach (var t in v.Rows.Where(t => t.BrokeAt is not null)) md.Append($"- Try {t.N} broke at `{t.BrokeAt}`; the stages after it were not reached and hold nothing.\n");
            var kinds = friction.Variants.First(f => f.Id == v.Id);
            if (v.Flagged is null) md.Append("\nFriction not read for every stage; `proctor friction` reads it.\n\n");
            else if (kinds.Kinds.Count == 0) md.Append("\nNo friction found.\n\n");
            else
            {
                md.Append("\n| Kind of friction | In | min | median | max | Per try overall |\n|---|---|---|---|---|---|\n");
                foreach (var k in kinds.Kinds)
                    md.Append($"| {(k.Detail is null ? "" : k.Detail.Replace("|", "\\|") + " ")}`{k.Label}` · {string.Join(", ", k.Stages)} | {k.In} of {k.Of} | {Num(k.When.Min)} | {Num(k.When.Median)} | {Num(k.When.Max)} | {Num(k.PerTry)} |\n");
                md.Append($"| All friction | | | | | {Num(kinds.PerTry)} |\n\n");
            }
        }

        if (stats.Lost.Count > 0)
        {
            md.Append("## Lost tries\n\nA stage did not run, so the try is in no number above.\n\n");
            foreach (var l in stats.Lost) md.Append($"- {l.Variant} try {l.N}, at `{l.At}`: {l.Why}\n");
            md.Append('\n');
        }
        if (stats.BuiltFrom is { Stale: > 0 } b) md.Append($"{b.Stale} stale stage attempt{(b.Stale == 1 ? "" : "s")} left out: made before the exam or a variant changed.\n\n");
        return md.Append($"## How this was counted\n\n{stats.Methods}\n").ToString();

        static string Range(Spread s) => s.Min == s.Max ? Num(s.Min) : $"{Num(s.Min)} to {Num(s.Max)}";
        static string Num(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
