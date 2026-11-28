using System.Globalization;
using System.Text;

namespace Auspex;

// The report of an exam (plans/axes-build.md, slice 3; the model is plans/axes.md). Three things a person
// needs from it: which configurations have evidence and which do not, how each value on an axis with a live value
// compares with the live one, and exactly which configurations each comparison covered.

/// <summary>An axis as the report shows it: its values in declared order, the live one named.</summary>
record AxisInfo(string Name, string? About, string? Live, List<ValueInfo> Values);
/// <summary>A value: its sentence, its identity, and whether it is the one its axis is held at while another axis's value is looked at alone.</summary>
record ValueInfo(string Name, string? Note, string Hash, bool Reference = false, string? Why = null);

/// <summary>
/// One configuration's standing: one the exam names or has tries of. State is not_run (no counted try), not_graded (it
/// has tries, and none is graded under the checks as they are now), partial (fewer than the exam wants) or made.
/// NotGraded counts its tries without a grade under the current standard: they are in no number until `grade` reads them. Tries holds its tries; null when none were read.
/// </summary>
record ConfigurationRow(string Key, string Label, Dictionary<string, string> Values, string? Name, bool Live, string State, int Wanted, int Made, int Lost, int Stale,
    List<string> Stages, int CorrectnessChecks, ConfigurationTries? Tries, string? Together = null, List<string>? Differs = null, int NotGraded = 0, RetiredMark? Retired = null);

/// <summary>
/// A value against the live value of its axis with every other axis held: one cell of a comparison. State is compared,
/// or which side has no counted try (not_run, live_not_run, neither_run). The measures are null unless compared. Held
/// makes the verdict; of the rest fewer is better (the user, 2026-11-27), and each is null when a try on either side lacks the number.
/// </summary>
record ComparedCell(Dictionary<string, string> Holding, string Key, string LiveKey, string State, int Tries, int LiveTries, Measure? Held, Measure? Flagged,
    Measure? ToolCalls = null, Measure? Tokens = null, Measure? Duration = null);

/// <summary>
/// The cells of a comparison read as one: stratified by cell, so a try is only ever ranked against tries of the same
/// configuration of the other axes (van Elteren). Across names the axes pooled over, which is an assumption the report
/// states: that the difference is the same kind of thing in each. Covered and Of count the cells compared and the cells there are.
/// </summary>
record Pooled(List<string> Across, int Covered, int Of, bool CanConfirm, Measure Held, Measure? Flagged, Measure? ToolCalls = null, Measure? Tokens = null, Measure? Duration = null);

/// <summary>One value of an axis against that axis's live value: cell by cell, pooled when more than one cell was compared, and in a sentence.</summary>
record AxisComparison(string Axis, string Value, string Live, List<ComparedCell> Cells, Pooled? Pooled, List<string> NotRun, string Sentence, List<string>? Cautions = null, string? Verdict = null);

/// <summary>
/// stats.json. Configurations are the ones the exam names or has tries of; Excluded the values it declares impossible
/// together. Judges are the endpoints whose verdicts the tries hold, each with the checks it graded, when any check asks a model.
/// </summary>
record ConfigurationsFile(string Schema, string Exam, bool Chained, List<AxisInfo> Axes, List<StageInfo> Stages, List<ConfigurationRow> Configurations, List<Dictionary<string, string>> Excluded,
    List<AxisComparison> Comparisons, List<LostTry> Lost, string Methods, BuiltFrom? BuiltFrom, List<StandardMove>? StandardMoved = null, List<PredictionReport>? Predictions = null, List<JudgeUse>? Judges = null);

static class Configurations
{
    public const string Schema = "configurations/1";
    const double Alpha = 0.05;

    public static (ConfigurationsFile Stats, FrictionRollup Friction) Compute(string root, Exam exam, List<BankAttempt> current, List<BankAttempt> bank, List<ResultRow> rows, BuiltFrom? built, List<Prediction>? predictions = null)
    {
        var axes = exam.Axes;
        var judges = Judge.Uses(root, current);
        // Comparisons use the standard as it is now, both sides alike: a try graded only under earlier checks is not yet graded, and in no number.
        var ungraded = NotGraded(root, exam, current);
        current = current.Where(a => !ungraded.Contains((a.Configuration, a.Attempt))).ToList();
        rows = rows.Where(r => !ungraded.Contains((r.Configuration, r.Attempt))).ToList();
        var moves = Standard.Moves(root, exam, current);
        var (tries, friction) = Tries.Compute(root, exam, current, bank, rows, built);
        var byKey = tries.Configurations.ToDictionary(v => v.Id);
        bool Live(Configuration c) => axes.Def.Any(a => a.Value.Live is not null) && axes.Def.All(a => a.Value.Live is null || a.Value.Live == c[a.Key]);
        var standing = Combinations.Load(exam).Stand(exam);
        var retired = Retired.Load(exam);

        var configurations = axes.Configurations.Select(c =>
        {
            var t = byKey.GetValueOrDefault(c.Key);
            var made = t?.Tries ?? 0;
            var stages = exam.StagesOf(c.Key);
            return new ConfigurationRow(c.Key, c.Label, c.Values.ToDictionary(v => v.Axis, v => v.Value), c.Name, Live(c),
                made == 0 ? (ungraded.Any(u => u.Configuration == c.Key) ? "not_graded" : "not_run") : made < exam.Attempts ? "partial" : "made", exam.Attempts, made,
                bank.Where(a => a.Lost && a.Configuration == c.Key && Bank.Stale(exam, a) is null).Select(a => a.Attempt).Distinct().Count(),
                bank.Where(a => a.Configuration == c.Key && Bank.Stale(exam, a) is not null).Select(a => a.Attempt).Distinct().Count(),
                stages.Select(s => s.Id!).ToList(), stages.Sum(s => Standard.PassIn(exam, s, c.Key).Count), t,
                standing.TryGetValue(c.Key, out var together) ? together.State : null, together.Paths is { Count: > 0 } ? together.Paths : null,
                ungraded.Count(u => u.Configuration == c.Key), c.Name is null ? null : retired.GetValueOrDefault(c.Name));
        }).ToList();

        var comparisons = new List<AxisComparison>();
        foreach (var (axis, def) in axes.Def.Where(a => a.Value.Live is not null))
            foreach (var value in def.Values!.Keys.Where(v => v != def.Live))
                comparisons.Add(Compare(exam, byKey, standing, axis, value, def.Live!, moves: moves));

        var methods = "A configuration is one value on every axis. A try is every stage that applies in its configuration, once, in one workspace, and gives two numbers: "
            + "the correctness checks it held, and its flagged steps (the steps inside any friction point). "
            + "A value is compared with the live value of its axis with every other axis held, try against try, by the Mann–Whitney test with an exact two-sided p. "
            + "Where more than one such cell was compared they are also read as one, by the van Elteren test: a try is ranked only against tries of the same cell, "
            + "so the cells may differ from each other without that counting as a difference between the values. Reading them as one assumes the difference is the same kind of thing in each cell; the cells are always shown. "
            + "Where the cells point different ways the comparison is better here, worse there, and they are not read as one. "
            + "A difference is confirmed at p < 0.05. A configuration with no counted try is not run, never no change. "
            + "A comparison looks only where the exam names or has tries of the value; in the other settings of the other axes nobody asked for it. "
            + "A try in which a stage did not run is lost and is in no number; a try with a stage that failed a validity check is listed and not counted.";
        return (new ConfigurationsFile(Schema, exam.Id, exam.Chained,
                axes.Def.Select(a => new AxisInfo(a.Key, a.Value.About, a.Value.Live, a.Value.Values!.Select(v => new ValueInfo(v.Key, v.Value!.Note, axes.ValueHashes[(a.Key, v.Key)], axes.ReferenceOf(a.Key) == v.Key, v.Value.Why)).ToList())).ToList(),
                tries.Stages, configurations, axes.Excludes.Select(e => new Dictionary<string, string>(e)).ToList(), comparisons, tries.Lost, methods, built, moves.Count > 0 ? moves : null,
                predictions is { Count: > 0 } ? PredictionOutcomes.Compute(exam, predictions, byKey, rows, current) : null, judges.Count > 0 ? judges : null), friction);
    }

    /// <summary>
    /// One value of an axis against another, every other axis held: the one place a comparison is worked out, for the
    /// report (each value against its axis's live one) and for a query (plans/axes-build.md, 3c) alike. Within narrows
    /// it to the settings of the other axes that hold those values. Where the settings compared point different ways it
    /// is "better here, worse there", and they are not read as one: opposite differences would cancel into "the same".
    /// </summary>
    public static AxisComparison Compare(Exam exam, IReadOnlyDictionary<string, ConfigurationTries> byKey, Dictionary<string, (string State, List<string> Paths)> standing,
        string axis, string value, string against, IReadOnlyDictionary<string, string>? within = null, IReadOnlyList<StandardMove>? moves = null) =>
        Compare(exam, byKey, standing, new Dictionary<string, string> { [axis] = value }, new Dictionary<string, string> { [axis] = against }, within, moves);

    /// <summary>The tries, by configuration and number, with a completed stage that holds no grade under the standard as it is now.</summary>
    public static HashSet<(string Configuration, int Attempt)> NotGraded(string root, Exam exam, IEnumerable<BankAttempt> current) =>
        current.Where(a => a.Status == AttemptStatus.Completed && !Standard.Current(exam, exam.StageOf(a.Stage), a.Dir(root))).Select(a => (a.Configuration, a.Attempt)).ToHashSet();

    /// <summary>
    /// The same for a change on several axes at once: these values together against those, every other axis held. It is
    /// how a configuration is compared with another (plans/axes-build.md, 3c): the change is the axes on which they differ.
    /// </summary>
    public static AxisComparison Compare(Exam exam, IReadOnlyDictionary<string, ConfigurationTries> byKey, Dictionary<string, (string State, List<string> Paths)> standing,
        IReadOnlyDictionary<string, string> change, IReadOnlyDictionary<string, string> instead, IReadOnlyDictionary<string, string>? within = null, IReadOnlyList<StandardMove>? moves = null)
    {
        var axes = exam.Axes!;
        // In the order the exam declares its axes, so a change reads the same however it was asked for.
        string Said(IReadOnlyDictionary<string, string> values) => string.Join(" × ", axes.Def.Keys.Where(values.ContainsKey).Select(a => $"{a}={values[a]}"));
        var (axis, value, against) = (string.Join("+", axes.Def.Keys.Where(change.ContainsKey)), Said(change), Said(instead));
        var cells = new List<ComparedCell>();
        // Where to look: every setting of the other axes in which the exam carries this value, named or made. Where nobody asked
        // for the value there is no gap to report, only the cross product, which nobody spans.
        var holdings = axes.Configurations.Where(k => k.Has(change) && (within is null || k.Has(within)))
            .Select(k => k.Values.Where(v => !change.ContainsKey(v.Axis)).ToDictionary(v => v.Axis, v => v.Value)).DistinctBy(Holding).ToList();
        foreach (var holding in holdings)
        {
            Configuration? With(IReadOnlyDictionary<string, string> values) => axes.Make(new Dictionary<string, string>(holding.Concat(values))) is { Excluded: false } made && exam.StagesOf(made.Key).Count > 0 ? made : null;
            if (With(change) is not { } c || With(instead) is not { } other) continue;   // one side cannot be made here: there is nothing to compare
            var (mine, theirs) = (byKey.GetValueOrDefault(c.Key), byKey.GetValueOrDefault(other.Key));
            var (n, m) = (mine?.Tries ?? 0, theirs?.Tries ?? 0);
            var state = n > 0 && m > 0 ? "compared" : n == 0 && m == 0 ? "neither_run" : n == 0 ? "not_run" : "live_not_run";
            cells.Add(new ComparedCell(holding, c.Key, other.Key, state, n, m,
                state == "compared" ? Tries.Compare(Held(mine!), Held(theirs!), higherIsBetter: true) : null,
                state == "compared" && Flagged(mine!) is { } f && Flagged(theirs!) is { } g ? Tries.Compare(f, g, higherIsBetter: false) : null,
                state == "compared" ? Fewer(mine!, theirs!, r => r.ToolCalls) : null, state == "compared" ? Fewer(mine!, theirs!, r => r.TokensIn) : null, state == "compared" ? Fewer(mine!, theirs!, r => r.DurationMs) : null));
        }
        var compared = cells.Where(c => c.State == "compared").ToList();
        var disagree = compared.Any(c => c.Held!.Direction == "better") && compared.Any(c => c.Held!.Direction == "worse");
        Pooled? pooled = null;
        if (compared.Count > 1 && !disagree)
        {
            var strata = compared.Select(c => (Mine: byKey[c.Key], Theirs: byKey[c.LiveKey])).ToList();
            var flagged = strata.All(s => Flagged(s.Mine) is not null && Flagged(s.Theirs) is not null)
                ? Stratified(strata.Select(s => (Flagged(s.Mine)!, Flagged(s.Theirs)!)).ToList(), higherIsBetter: false) : null;
            pooled = new Pooled(axes.Def.Keys.Where(a => !change.ContainsKey(a) && compared.Select(c => c.Holding[a]).Distinct().Count() > 1).ToList(), compared.Count, cells.Count,
                MinP(strata.Select(s => (s.Mine.Tries, s.Theirs.Tries)).ToList()) < Alpha,
                Stratified(strata.Select(s => (Held(s.Mine), Held(s.Theirs))).ToList(), higherIsBetter: true), flagged,
                FewerAcross(r => r.ToolCalls), FewerAcross(r => r.TokensIn), FewerAcross(r => r.DurationMs));
            Measure? FewerAcross(Func<TryRow, double?> of) => strata.All(s => Numbers(s.Mine, of) is not null && Numbers(s.Theirs, of) is not null)
                ? Stratified(strata.Select(s => (Numbers(s.Mine, of)!, Numbers(s.Theirs, of)!)).ToList(), higherIsBetter: false) : null;
        }
        var notRun = cells.Where(c => c.State != "compared").Select(c => Holding(c.Holding) + c.State switch { "not_run" => "", "live_not_run" => $" (no {against} there)", _ => " (neither side)" }).ToList();
        // Where a configuration in the comparison is not simply its values: that travels with the sentence (plans/axes.md, "What follows", 2).
        var inIt = cells.SelectMany(k => new[] { k.Key, k.LiveKey }).Distinct().Select(key => axes.Of(key)!).ToList();
        List<string> In(string state) => inIt.Where(k => standing.GetValueOrDefault(k.Key).State == state).Select(k => k.Name ?? k.Label).ToList();
        var cautions = new List<string>();
        if (In(Combinations.More) is { Count: > 0 } more) cautions.Add($"More than its values put together, since some script reads more than one axis: {string.Join("; ", more)}.");
        if (In(Combinations.Unclear) is { Count: > 0 } unclear) cautions.Add($"Cannot be told from its values alone: {string.Join("; ", unclear)}.");
        if (In(Combinations.NotChecked) is { Count: > 0 } unchecked_) cautions.Add($"Not checked against its values alone: {string.Join("; ", unchecked_)}.");
        // Where the checks changed after these tries were graded: a number here may have moved with nothing re-run.
        foreach (var move in (moves ?? []).Where(m => m.Configurations.Any(k => inIt.Any(c => c.Key == k))))
            cautions.Add("The standard moved: " + move.Sentence);
        var verdict = compared.Count == 0 ? "nothing_to_compare" : disagree ? "better_here_worse_there" : (pooled?.Held ?? compared[0].Held!).Direction;
        var sentence = Sentence($"{value} against {against}", cells, compared, pooled, notRun, disagree);
        // In the record one axis reads as it always did: the axis, its value and the value it is compared with.
        if (change.Count == 1) (value, against) = (change.Values.First(), instead.Values.First());
        return new AxisComparison(axis, value, against, cells, pooled, notRun,
            sentence + string.Concat(cautions.Select(c => " " + c)), cautions.Count > 0 ? cautions : null, verdict);

        // A count of a try's work (tool calls, tokens read, time): fewer is better, and it is compared only when every counted try on both sides has it.
        static List<double>? Numbers(ConfigurationTries v, Func<TryRow, double?> of) => v.Rows.Where(r => r.Invalid is null).ToList() is var counted && counted.All(r => of(r) is not null) ? counted.Select(r => of(r)!.Value).ToList() : null;
        static Measure? Fewer(ConfigurationTries mine, ConfigurationTries theirs, Func<TryRow, double?> of) => Numbers(mine, of) is { } a && Numbers(theirs, of) is { } b ? Tries.Compare(a, b, higherIsBetter: false) : null;
        static List<double> Held(ConfigurationTries v) => v.Rows.Where(r => r.Invalid is null).Select(r => (double)r.Held).ToList();
        static List<double>? Flagged(ConfigurationTries v) => v.Rows.Where(r => r.Invalid is null).ToList() is var counted && counted.All(r => r.Flagged is not null) ? counted.Select(r => (double)r.Flagged!.Value).ToList() : null;
    }

    static string Holding(Dictionary<string, string> holding) => holding.Count == 0 ? "the one configuration" : string.Join(" × ", holding.Select(h => $"{h.Key}={h.Value}"));

    /// <summary>The comparison in a sentence that says its scope: what was found, in which cells, and which were not run.</summary>
    static string Sentence(string name, List<ComparedCell> cells, List<ComparedCell> compared, Pooled? pooled, List<string> notRun, bool disagree = false)
    {
        var gaps = notRun.Count == 0 ? "" : $" Not run: {string.Join("; ", notRun)}.";
        if (compared.Count == 0) return $"{name}: nothing to compare yet.{gaps}";
        if (disagree)
            return $"{name}: better here, worse there on correctness checks held. "
                + string.Join(" ", compared.GroupBy(c => c.Held!.Direction).Select(g => $"{char.ToUpperInvariant(g.Key[0])}{g.Key[1..]} in {string.Join("; ", g.Select(c => Holding(c.Holding)))}."))
                + $" They are not read as one, since opposite differences would cancel.{gaps}";
        if (pooled is null)
        {
            var only = compared[0];
            return $"{name}{(only.Holding.Count > 0 ? $", in {Holding(only.Holding)}" : "")}: {Said(only.Held!, "correctness checks held", only.Tries, only.LiveTries, Tries.MinP(only.Tries, only.LiveTries) < Alpha)}.{gaps}";
        }
        var directions = compared.GroupBy(c => c.Held!.Direction).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} in {g.Count()}").ToList();
        return $"{name}, over {compared.Count} of {cells.Count} configurations of the other axes ({string.Join(", ", directions)}): read as one"
            + (pooled.Across.Count > 0 ? $", across {string.Join(" and ", pooled.Across)}" : "")
            + $", {Said(pooled.Held, "correctness checks held", 0, 0, pooled.CanConfirm)}.{gaps}";
    }

    static string Said(Measure m, string name, int n, int k, bool canConfirm) =>
        m.Direction == "same" ? $"the same on {name}"
        : $"{m.Direction} on {name}, " + (m.Confirmed ? $"confirmed (p = {P(m.P)})" : canConfirm ? $"not confirmed (p = {P(m.P)})" : $"which this many tries cannot confirm (p = {P(m.P)})");

    static string P(double p) => p < 0.001 ? "<0.001" : p.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The smallest two-sided p these strata could give: every stratum at its extreme, in one direction.</summary>
    public static double MinP(List<(int N, int M)> strata) => Math.Min(1, 2 * strata.Aggregate(1.0, (p, s) => p * Tries.MinP(s.N, s.M) / 2));

    /// <summary>
    /// The van Elteren test: Mann–Whitney within each stratum, the U statistics added with weight 1/(n+m+1). The p is
    /// exact (every way of dealing each stratum's values to its two sides) while that is at most 400,000 arrangements
    /// and the sums stay countable; past that the normal approximation, with each stratum's own variance.
    /// </summary>
    public static Measure Stratified(List<(List<double> Mine, List<double> Theirs)> strata, bool higherIsBetter)
    {
        int higher = 0, lower = 0, tied = 0;
        double observed = 0, expected = 0, variance = 0, arrangements = 1;
        var exact = new Dictionary<long, double> { [0] = 1 };
        foreach (var (mine, theirs) in strata)
        {
            var (n, m) = (mine.Count, theirs.Count);
            var w = 1.0 / (n + m + 1);
            foreach (var a in mine) foreach (var b in theirs) { if (a > b) higher++; else if (a < b) lower++; else tied++; }
            observed += w * U(mine, theirs);
            expected += w * n * m / 2.0;
            var all = mine.Concat(theirs).ToList();
            var ways = Choose(n + m, n);
            arrangements *= ways;
            if (exact is not null && arrangements <= 400_000)
            {
                // This stratum's U under every deal of its values, each deal equally likely, folded into the sums so far.
                var dist = new Dictionary<long, double>();
                foreach (var pick in Picks(n + m, n))
                {
                    var chosen = pick.Select(i => all[i]).ToList();
                    var rest = Enumerable.Range(0, n + m).Except(pick).Select(i => all[i]).ToList();
                    var key = (long)Math.Round(w * U(chosen, rest) * 1e9);
                    dist[key] = dist.GetValueOrDefault(key) + 1 / ways;
                }
                var next = new Dictionary<long, double>();
                foreach (var (sum, soFar) in exact) foreach (var (u, q) in dist) next[sum + u] = next.GetValueOrDefault(sum + u) + soFar * q;
                exact = next.Count <= 200_000 ? next : null;
            }
            else exact = null;
            // The variance of U with ties: nm/12 times (N+1) less the tie correction.
            var total = n + m;
            var ties = all.GroupBy(x => x).Sum(g => (double)g.Count() * g.Count() * g.Count() - g.Count());
            variance += w * w * (total > 1 ? n * m / 12.0 * (total + 1 - ties / (total * (total - 1.0))) : 0);
        }
        double p;
        var distance = Math.Abs(observed - expected);
        if (exact is not null) p = Math.Min(1, exact.Where(e => Math.Abs(e.Key / 1e9 - expected) >= distance - 1e-7).Sum(e => e.Value));
        else p = variance <= 0 ? 1 : Math.Min(1, 2 * (1 - Tries.Phi(distance / Math.Sqrt(variance))));
        var up = observed > expected + 1e-9;
        var down = observed < expected - 1e-9;
        var direction = !up && !down ? "same" : up == higherIsBetter ? "better" : "worse";
        return new Measure(direction, higher, lower, tied, higher + lower + tied, p, direction != "same" && p < Alpha);
    }

    static double U(List<double> a, List<double> b) => a.Sum(x => b.Sum(y => x > y ? 1.0 : x == y ? 0.5 : 0));

    static double Choose(int n, int k)
    {
        double c = 1;
        for (var i = 1; i <= k; i++) c = c * (n - k + i) / i;
        return c;
    }

    /// <summary>Every way to pick k of n positions.</summary>
    static IEnumerable<List<int>> Picks(int n, int k)
    {
        var pick = Enumerable.Range(0, k).ToList();
        while (true)
        {
            yield return [.. pick];
            var i = k - 1;
            while (i >= 0 && pick[i] == n - k + i) i--;
            if (i < 0) yield break;
            pick[i]++;
            for (var j = i + 1; j < k; j++) pick[j] = pick[j - 1] + 1;
        }
    }

    public static string Markdown(ConfigurationsFile stats, FrictionRollup friction)
    {
        var md = new StringBuilder();
        md.AppendLine($"# {stats.Exam}").AppendLine();
        md.AppendLine("## Axes").AppendLine();
        foreach (var a in stats.Axes)
            md.AppendLine($"- **{a.Name}**{(a.About is null ? "" : $" ({a.About})")}: {string.Join(", ", a.Values.Select(v =>
                v.Name + (v.Name == a.Live ? " (live)" : v.Reference ? " (reference)" : "")))}{(a.Values.All(v => !v.Reference) ? "; no live value and no reference, so no value of this exam can be built alone" : "")}");
        md.AppendLine().AppendLine("## Configurations").AppendLine();
        md.AppendLine("| Configuration | Tries | Passed | Checks held | Flagged steps | |").AppendLine("|---|---|---|---|---|---|");
        foreach (var c in stats.Configurations)
        {
            var notes = string.Join(", ", new[] { c.Name, c.Live ? "live" : null, c.Retired is not null ? "retired" : null, c.Lost > 0 ? $"{c.Lost} lost" : null, c.Stale > 0 ? $"{c.Stale} stale" : null,
                c.NotGraded > 0 ? $"{c.NotGraded} not yet graded" : null,
                c.Together == Combinations.More ? "more than its values put together" : c.Together == Combinations.Unclear ? "cannot be told from its values alone" : c.Together == Combinations.NotChecked ? "not checked against its values alone" : null }.OfType<string>());
            md.AppendLine(c.State switch
            {
                "not_run" => $"| {c.Label} | **not run** (0 of {c.Wanted}) | | | | {notes} |",
                "not_graded" => $"| {c.Label} | **not yet graded** | | | | {notes} |",
                _ => $"| {c.Label} | {c.Made} of {c.Wanted} | {c.Tries!.Passed} | {Range(c.Tries.Held)} of {c.CorrectnessChecks} | {Range(c.Tries.Flagged)} | {notes} |",
            });
        }
        md.AppendLine().AppendLine("These are the configurations the exam names or has tries of. Any other was never asked for, and is in no number here.");
        if (stats.Excluded.Count > 0)
            md.AppendLine().AppendLine($"Declared impossible, and never made: {string.Join("; ", stats.Excluded.Select(Holding))}.");
        if (stats.Configurations.Any(c => c.NotGraded > 0))
            md.AppendLine().AppendLine($"A try is not yet graded when it holds no grade under the checks as they are now. It is in no number here; `proctor grade {stats.Exam}` reads it.");
        if (stats.StandardMoved is { Count: > 0 } moved)
        {
            md.AppendLine().AppendLine("## The standard moved").AppendLine();
            md.AppendLine("The checks changed after some of these tries were graded, and the tries were graded again without being made again. Every number here is under the checks as they are now.").AppendLine();
            foreach (var m in moved) md.AppendLine($"- {m.Sentence}");
        }
        if (stats.Predictions is { Count: > 0 } proposed)
        {
            md.AppendLine().AppendLine("## Predictions").AppendLine();
            md.AppendLine("A prediction is what somebody said a configuration would change, written before its tries were made. Each change it expects is held against the tries, try against try, and a miss is said as plainly as a hit.");
            foreach (var c in proposed)
            {
                md.AppendLine().AppendLine($"**{c.Name}**{(c.Note is null ? "" : $": {c.Note}")}").AppendLine();
                foreach (var p in c.Changes) md.AppendLine($"- {p.Sentence}{(p.Because is null ? "" : $" The reason given: {p.Because}")}");
                foreach (var label in c.Labels) md.AppendLine($"- *{label}*");
            }
        }
        if (stats.Comparisons.Count > 0)
        {
            md.AppendLine().AppendLine("## Against live").AppendLine();
            foreach (var c in stats.Comparisons)
            {
                md.AppendLine($"**{c.Sentence}**").AppendLine();
                if (c.Cells.Count < 2) continue;
                md.AppendLine("| Holding | Tries | Live's tries | Checks held | p | Flagged steps | p |").AppendLine("|---|---|---|---|---|---|---|");
                foreach (var cell in c.Cells)
                    md.AppendLine(cell.State == "compared"
                        ? $"| {Holding(cell.Holding)} | {cell.Tries} | {cell.LiveTries} | {cell.Held!.Direction} | {P(cell.Held.P)} | {cell.Flagged?.Direction ?? "not read"} | {(cell.Flagged is null ? "" : P(cell.Flagged.P))} |"
                        : $"| {Holding(cell.Holding)} | {cell.Tries} | {cell.LiveTries} | **not run** | | | |");
                if (c.Pooled is { } pooled)
                    md.AppendLine($"| read as one ({pooled.Covered} of {pooled.Of}) | | | {pooled.Held.Direction} | {P(pooled.Held.P)} | {pooled.Flagged?.Direction ?? "not read"} | {(pooled.Flagged is null ? "" : P(pooled.Flagged.P))} |");
                md.AppendLine();
            }
        }
        if (stats.Lost.Count > 0)
        {
            md.AppendLine("## Lost tries").AppendLine();
            foreach (var l in stats.Lost) md.AppendLine($"- {l.Configuration}, try {l.N}, at {l.At}: {l.Why}");
            md.AppendLine();
        }
        if (stats.Judges is { Count: > 0 } judges)
        {
            md.AppendLine("## Judges").AppendLine();
            foreach (var j in judges) md.AppendLine($"- {j.Judge}: {j.Kind}{(j.Model is null ? "" : $" {j.Model}")} at `{j.Endpoint}`; graded {string.Join(", ", j.Checks)}");
            md.AppendLine();
        }
        md.AppendLine("## How this was counted").AppendLine().AppendLine(stats.Methods);
        return md.ToString();

        static string Range(Spread? s) => s is null ? "not read" : s.Min == s.Max ? Num(s.Min) : $"{Num(s.Min)} to {Num(s.Max)} (median {Num(s.Median)})";
        static string Num(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
