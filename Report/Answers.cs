using System.Globalization;

namespace Proctor;

// What proctor answers when asked (plans/axes-build.md, slice 3c; plans/axes.md, decision 17). Proctor does no decision
// support: an agent makes the recommendation, and these two queries are what it is given. So an answer is only what
// was measured, worked out by the report's own code (Configurations.Compare) and nowhere else, and it carries what a
// reader needs to not say more than that: its scope, what was not run, when the tries were made, what so few tries
// could have shown, what moved together, and an address for every number. JSON on standard output.

/// <summary>One try behind a number: its id, when it was made, what it held, and where it is (the viewer's address, and the bank's directory for whoever has the files).</summary>
record TryAddress(string Id, string Made, int Held, int Of, int? Flagged, string? Invalid, string Address, string Path);

/// <summary>One side of one setting: the configuration, whether it is known to be its values put together, and its counted tries as of the answer.</summary>
record SideAnswer(string Configuration, string? Name, string? Together, string Address, List<TryAddress> Tries);

/// <summary>
/// One setting of the other axes: the value's side and the other's, compared when both have tries. CouldConfirm is
/// whether this many tries a side could confirm any difference at all; SmallestP the smallest p they could give.
/// </summary>
record CellAnswer(Dictionary<string, string> Holding, string State, SideAnswer Value, SideAnswer Against, Measure? Held, Measure? Flagged, bool CouldConfirm, double SmallestP, List<CheckHeld>? Checks = null);

/// <summary>One headline check in one setting: in how many of each side's counted tries it held. Counts only; no test is made of one check.</summary>
record CheckHeld(string Stage, string Check, string? About, int Value, int ValueTries, int Against, int AgainstTries);

/// <summary>When each side's tries were made, and how far apart the two sides are (between the middle try of each).</summary>
record Timing(string? ValueFirst, string? ValueLast, string? AgainstFirst, string? AgainstLast, double? DaysApart, string? Note);

/// <summary>Another axis that only ever moved with the one asked about: every try on the value's side had WithValue on it, every try on the other side WithAgainst.</summary>
record MovedWith(string Axis, string WithValue, string WithAgainst);

/// <summary>What so few tries could have shown, read as one or in the single setting compared.</summary>
record Power(bool CouldConfirm, double SmallestP, string Note);

record ComparisonAnswer(string Schema, string Exam, string AsOf, string Address, string Axis, string Value, string Against, Dictionary<string, string>? Within,
    string Verdict, bool? Confirmed, string Sentence, List<CellAnswer> Cells, Pooled? ReadAsOne, List<string> NotRun, Power? Power, Timing Timing,
    List<MovedWith> MovedWith, List<string> Cautions, List<string> OtherComparisons, int TriesSince, string Methods);

/// <summary>A configuration as coverage sees it: who asked for it, how far it is from what is live, its tries and how old the newest is.</summary>
record CoverageRow(string Configuration, string? Name, Dictionary<string, string> Values, bool Live, List<string> OffLive, int Tries, int Wanted, int Lost, int Stale,
    string? First, string? Last, double? DaysSinceLast, string? Together, string Address);

/// <summary>One value: whether it is live or its axis's reference, and the tries of every configuration holding it.</summary>
record ValueCoverage(string Axis, string Value, string? Note, bool Live, bool Reference, int Configurations, int Tries, string? Last);

/// <summary>
/// What live spans: the live value of each axis that has one, across every value of the axes that have none (the ones
/// that are not ours to choose). Each is a setting some user may be in. Listed is false when there are too many to
/// list, and then only those with tries are given.
/// </summary>
record LiveSpan(List<string> Fixed, List<string> Across, int Configurations, int WithTries, bool Listed, List<CoverageRow> Rows);

record CoverageAnswer(string Schema, string Exam, string AsOf, string Address, List<ValueCoverage> Values, List<CoverageRow> Configurations, LiveSpan? LiveSpan,
    List<Dictionary<string, string>> Excluded, int Stale, string Methods);

static class Answers
{
    public const string Schema = "answer/1";
    const double Alpha = 0.05;
    const int MaxSpan = 200;

    /// <summary>When a try was made, from its id (yyyyMMdd-HHmm-xxxx, UTC): the one time every try carries without opening it.</summary>
    public static DateTime? MadeAt(string? tryId) =>
        tryId is { Length: >= 13 } && DateTime.TryParseExact(tryId[..13], "yyyyMMdd-HHmm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;

    public static string Iso(DateTime t) => t.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture);

    /// <summary>--as-of as given: a date or a date and time, read as UTC. A date alone is the end of that day.</summary>
    public static DateTime ParseAsOf(string text)
    {
        var styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, styles, out var day)) return day.AddDays(1).AddMinutes(-1);
        if (DateTime.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss'Z'"], CultureInfo.InvariantCulture, styles, out var time)) return time;
        throw new ProctorException($"--as-of {text}: expects a date (2026-11-24) or a time in UTC (2026-11-24T21:05Z), as an answer's as_of gives it");
    }

    static string Encoded(string s) => Uri.EscapeDataString(s);
    static string ConfigurationAddress(Exam exam, string key) => $"/{exam.Id}/configuration/{Encoded(key)}";
    static string TryPath(Exam exam, string id) => Path.Combine(Layout.BankDir, exam.Id, id);

    /// <summary>The current tries as of a time, each stage with the id of its try.</summary>
    static List<(BankAttempt Attempt, string Id, DateTime Made)> Timed(List<BankAttempt> current) =>
        current.Select(a => (a, Id: a.Entry.IdOf(a.Variant, a.Attempt))).Where(x => MadeAt(x.Id) is not null).Select(x => (x.a, x.Id!, MadeAt(x.Id)!.Value)).ToList();

    /// <summary>
    /// One value of an axis against another, every other axis held, as of a time (now when null). Against defaults to
    /// the axis's live value, then its reference. Refused while a stage in it is not graded, since an ungraded try would
    /// read as one that held nothing.
    /// </summary>
    public static ComparisonAnswer Compare(string root, Exam exam, string axis, string value, string? against, IReadOnlyDictionary<string, string>? within, DateTime? asOf)
    {
        var axes = exam.Axes!;
        against ??= axes.Def[axis].Live ?? axes.ReferenceOf(axis)
            ?? throw new ProctorException($"axis '{axis}' has no live value and no reference to compare {axis}={value} with: say --against <value> ({string.Join(", ", axes.Def[axis].Values!.Keys.Where(v => v != value))})");
        if (against == value) throw new ProctorException($"{axis}={value} against itself: say --against <value> ({string.Join(", ", axes.Def[axis].Values!.Keys.Where(v => v != value))})");
        if (within?.ContainsKey(axis) == true) throw new ProctorException($"--within {axis}=…: '{axis}' is the axis compared along; --within holds the other axes");
        var now = DateTime.UtcNow;
        var at = asOf ?? now;

        var all = Timed(Bank.Select(root, exam).Current);
        var then = all.Where(x => x.Made <= at).ToList();
        var current = then.Select(x => x.Attempt).ToList();
        var rows = Results.Collect(root, exam, current);
        var (tries, _) = Tries.Compute(root, exam, current, Bank.Of(root, exam), rows, null, null);
        var byKey = tries.Variants.ToDictionary(v => v.Id);
        var standing = Combinations.Load(exam).Stand(exam);
        var comparison = Configurations.Compare(exam, byKey, standing, axis, value, against, within);

        var keys = comparison.Cells.SelectMany(c => new[] { c.Key, c.LiveKey }).ToHashSet();
        var ungraded = rows.Count(r => keys.Contains(r.Variant) && r.Status == AttemptStatus.Completed && r.Checks is null);
        if (ungraded > 0) throw new ProctorException($"{ungraded} stage{(ungraded == 1 ? "" : "s")} of the tries in this comparison {(ungraded == 1 ? "is" : "are")} not graded, and an ungraded try would read as one that held nothing: run `proctor grade {exam.Id}` first");

        var ids = then.GroupBy(x => (x.Attempt.Variant, x.Attempt.Attempt)).ToDictionary(g => g.Key, g => g.First().Id);
        SideAnswer Side(string key)
        {
            var c = axes.Of(key)!;
            var made = byKey.GetValueOrDefault(key)?.Rows.Where(r => ids.ContainsKey((key, r.N))).Select(r => (Row: r, Id: ids[(key, r.N)])).ToList() ?? [];
            return new SideAnswer(key, c.Name, standing.TryGetValue(key, out var together) ? together.State : null, ConfigurationAddress(exam, key),
                made.Select(m => new TryAddress(m.Id, Iso(MadeAt(m.Id)!.Value), m.Row.Held, m.Row.Of, m.Row.Flagged, m.Row.Invalid, $"/{exam.Id}/try/{m.Id}", TryPath(exam, m.Id))).ToList());
        }
        // Which check: in how many of each side's counted tries each headline check held. Counts, so a reader can see where a difference sits.
        List<CheckHeld>? ChecksOf(ComparedCell c)
        {
            if (c.State != "compared") return null;
            List<int> Counted(string key) => byKey[key].Rows.Where(r => r.Invalid is null).Select(r => r.N).ToList();
            int Held(string key, List<int> ns, string stage, string check) => rows.Count(r => r.Variant == key && ns.Contains(r.Attempt) && r.Task == stage && r.Checks?.GetValueOrDefault(check) == Verdict.Pass);
            var (mineNs, theirNs) = (Counted(c.Key), Counted(c.LiveKey));
            return exam.StagesOf(c.Key).SelectMany(s => exam.PassFor(s).Select(check =>
                new CheckHeld(s.Id!, check, exam.ChecksFor(s).GetValueOrDefault(check) is { } declared ? Checks.Describe(declared.Spec) : null, Held(c.Key, mineNs, s.Id!, check), mineNs.Count, Held(c.LiveKey, theirNs, s.Id!, check), theirNs.Count))).ToList();
        }
        var cells = comparison.Cells.Select(c => new CellAnswer(c.Holding, c.State, Side(c.Key), Side(c.LiveKey), c.Held, c.Flagged,
            Tries.MinP(c.Tries, c.LiveTries) < Alpha, Tries.MinP(c.Tries, c.LiveTries), ChecksOf(c))).ToList();

        // What these counts of tries could have shown at best. A difference not confirmed is not a difference shown to be absent.
        var compared = cells.Where(c => c.State == "compared").ToList();
        Power? power = null;
        if (compared.Count > 0)
        {
            var smallest = comparison.Pooled is not null ? Configurations.MinP(compared.Select(c => (c.Value.Tries.Count(t => t.Invalid is null), c.Against.Tries.Count(t => t.Invalid is null))).ToList()) : compared.Min(c => c.SmallestP);
            power = new Power(smallest < Alpha, smallest, smallest < Alpha
                ? $"{(comparison.Pooled is not null ? "Read as one, these" : "These")} counts of tries could confirm a difference only if nearly every try on one side beat nearly every try on the other (the smallest p they can give is {P(smallest)}). A smaller difference would not be confirmed, and is not shown to be absent."
                : $"These counts of tries cannot confirm any difference, however large: the smallest p they can give is {P(smallest)}. \"The same\" or \"not confirmed\" here says nothing about whether there is a difference.");
        }
        var verdict = comparison.Verdict ?? "nothing_to_compare";
        var decided = comparison.Pooled?.Held ?? (compared.Count == 1 ? compared[0].Held : null);

        // When: a claim's two sides are often made at different times, and a hosted model or the machine may have changed between.
        var (mine, theirs) = (compared.SelectMany(c => c.Value.Tries).Select(t => MadeAt(t.Id)!.Value).Order().ToList(), compared.SelectMany(c => c.Against.Tries).Select(t => MadeAt(t.Id)!.Value).Order().ToList());
        double? apart = mine.Count > 0 && theirs.Count > 0 ? Math.Round(Math.Abs((mine[mine.Count / 2] - theirs[theirs.Count / 2]).TotalDays), 1) : null;
        var timing = new Timing(mine.Count > 0 ? Iso(mine[0]) : null, mine.Count > 0 ? Iso(mine[^1]) : null, theirs.Count > 0 ? Iso(theirs[0]) : null, theirs.Count > 0 ? Iso(theirs[^1]) : null, apart,
            apart >= 1 ? $"The two sides were made about {apart.Value.ToString("0.#", CultureInfo.InvariantCulture)} days apart. A hosted model, the agent or the machine may have changed between them, and that would read as a difference between the values." : null);

        // Which other axis only ever moved with this one, among the tries there are: then nothing here separates the two.
        var withTries = axes.Configurations.Where(c => (within is null || c.Has(within)) && byKey.GetValueOrDefault(c.Key)?.Tries > 0).ToList();
        var (a, b) = (withTries.Where(c => c[axis] == value).ToList(), withTries.Where(c => c[axis] == against).ToList());
        var moved = a.Count == 0 || b.Count == 0 ? [] : axes.Def.Keys.Where(x => x != axis)
            .Where(x => a.Select(c => c[x]).Distinct().Count() == 1 && b.Select(c => c[x]).Distinct().Count() == 1 && a[0][x] != b[0][x])
            .Select(x => new MovedWith(x, a[0][x]!, b[0][x]!)).ToList();
        var cautions = new List<string>(comparison.Cautions ?? []);
        foreach (var m in moved)
            cautions.Add($"Cannot be told apart from {m.Axis}: every try with {axis}={value} had {m.Axis}={m.WithValue}, and every try with {axis}={against} had {m.Axis}={m.WithAgainst}. Telling them apart takes a configuration that changes one and not the other.");
        if (timing.Note is not null) cautions.Add(timing.Note);
        if (power is { CouldConfirm: false }) cautions.Add(power.Note);

        // How many comparisons the exam's report holds beside this one: asked for one at a time, some will look confirmed by chance.
        var others = axes.Def.Where(d => d.Value.Live is not null).SelectMany(d => d.Value.Values!.Keys.Where(v => v != d.Value.Live).Select(v => (Axis: d.Key, Value: v, Against: d.Value.Live!)))
            .Where(o => o != (axis, value, against)).Where(o => Configurations.Compare(exam, byKey, standing, o.Axis, o.Value, o.Against).Cells.Any(c => c.State == "compared"))
            .Select(o => $"{o.Axis}={o.Value} against {o.Axis}={o.Against}").ToList();
        if (others.Count > 0) cautions.Add($"The exam's report holds {others.Count} other comparison{(others.Count == 1 ? "" : "s")} against live with tries on both sides ({string.Join("; ", others)}). At p < 0.05 about one in twenty comparisons of things that do not differ is confirmed by chance.");

        var since = asOf is null ? 0 : all.Where(x => x.Made > at && keys.Contains(x.Attempt.Variant)).Select(x => x.Id).Distinct().Count();
        var address = $"/{exam.Id}/compare?along={Encoded(axis)}&value={Encoded(value)}&against={Encoded(against)}"
            + (within is { Count: > 0 } ? "&within=" + Encoded(string.Join(",", within.Select(w => $"{w.Key}={w.Value}"))) : "") + $"&as_of={Encoded(Iso(at))}";
        return new ComparisonAnswer(Schema, exam.Id, Iso(at), address, axis, value, against, within is { Count: > 0 } ? new Dictionary<string, string>(within) : null,
            verdict, decided?.Confirmed, comparison.Sentence, cells, comparison.Pooled, comparison.NotRun, power, timing, moved, cautions, others, since,
            "Every number here is proctor's, worked out as its report works it out. A try is one configuration run once through the exam's stages; held is the headline checks it held, of how many. "
            + "The value is compared with the other in each setting of the other axes where the exam names or has tries of the value, try against try (Mann-Whitney, exact two-sided p), "
            + "and the settings compared are read as one when they do not point different ways (van Elteren). confirmed means p < 0.05; at the top it is of the one reading there is, "
            + "and absent when there is none (nothing compared, or better here and worse there: then each cell has its own). "
            + "flagged is a try's steps inside any friction point, when friction was run. checks in a cell counts, per headline check, the tries of each side that held it; it tests nothing. "
            + "together on a configuration more than one value off the reference says whether `proctor build` found it to be its values put together (sum), more than that (more), not to be told (unclear), or never checked (not_checked). "
            + "A setting not run is not run, never no difference. tries_since counts tries made after as_of in these configurations: the numbers are as they stood at as_of. "
            + "An address opens the same thing in the viewer; path is its directory under the repository root. Nothing here recommends anything.");
    }

    /// <summary>
    /// Where the exam has evidence and where it has none, as of a time: each value, each configuration the exam names or
    /// has tries of, and what live spans across the axes with no live value. It counts tries; it reads no grade.
    /// </summary>
    public static CoverageAnswer Coverage(string root, Exam exam, DateTime? asOf)
    {
        var axes = exam.Axes!;
        var at = asOf ?? DateTime.UtcNow;
        var bank = Bank.Of(root, exam);
        var counted = bank.Where(a => a.Counts && Bank.Stale(exam, a) is null).Select(a => (a, Id: a.Entry.IdOf(a.Variant, a.Attempt))).Where(x => MadeAt(x.Id) <= at)
            .GroupBy(x => x.a.Variant).ToDictionary(g => g.Key, g => g.Select(x => x.Id!).Distinct().Order(StringComparer.Ordinal).ToList());
        var standing = Combinations.Load(exam).Stand(exam);
        var liveAxes = axes.Def.Where(d => d.Value.Live is not null).Select(d => d.Key).ToList();

        CoverageRow Row(Configuration c)
        {
            var ids = counted.GetValueOrDefault(c.Key) ?? [];
            var times = ids.Select(id => MadeAt(id)!.Value).ToList();
            return new CoverageRow(c.Key, c.Name, c.Values.ToDictionary(v => v.Axis, v => v.Value), liveAxes.Count > 0 && liveAxes.All(x => c[x] == axes.Def[x].Live),
                liveAxes.Where(x => c[x] != axes.Def[x].Live).Select(x => $"{x}={c[x]}").ToList(), ids.Count, exam.Attempts,
                bank.Where(a => a.Lost && a.Variant == c.Key && Bank.Stale(exam, a) is null).Select(a => a.Attempt).Distinct().Count(),
                bank.Where(a => a.Variant == c.Key && Bank.Stale(exam, a) is not null).Select(a => a.Attempt).Distinct().Count(),
                times.Count > 0 ? Iso(times.Min()) : null, times.Count > 0 ? Iso(times.Max()) : null, times.Count > 0 ? Math.Round((at - times.Max()).TotalDays, 1) : null,
                standing.TryGetValue(c.Key, out var together) ? together.State : null, ConfigurationAddress(exam, c.Key));
        }
        var rows = axes.Configurations.Select(Row).ToList();

        var values = axes.Def.SelectMany(d => d.Value.Values!.Select(v =>
        {
            var holding = rows.Where(r => r.Values[d.Key] == v.Key && r.Tries > 0).ToList();
            return new ValueCoverage(d.Key, v.Key, v.Value!.Note, d.Value.Live == v.Key, axes.ReferenceOf(d.Key) == v.Key, holding.Count, holding.Sum(r => r.Tries), holding.Select(r => r.Last).Max(StringComparer.Ordinal));
        })).ToList();

        // What live spans. With no live value anywhere there is nothing in use to span; with one on every axis it is one configuration.
        LiveSpan? span = null;
        if (liveAxes.Count > 0)
        {
            var across = axes.Def.Keys.Where(x => !liveAxes.Contains(x)).ToList();
            var size = across.Aggregate(1L, (n, x) => Math.Min(long.MaxValue / 1000, n * axes.Def[x].Values!.Count));
            IEnumerable<Dictionary<string, string>> settings = [liveAxes.ToDictionary(x => x, x => axes.Def[x].Live!)];
            var listed = size <= MaxSpan;
            if (listed)
                foreach (var x in across) settings = settings.SelectMany(s => axes.Def[x].Values!.Keys.Select(v => new Dictionary<string, string>(s) { [x] = v })).ToList();
            var spanned = listed ? settings.Select(s => axes.Make(s)!).Where(c => !c.Excluded && exam.StagesOf(c.Key).Count > 0).Select(Row).ToList()
                : rows.Where(r => r.Live && r.Tries > 0).ToList();
            span = new LiveSpan(liveAxes.Select(x => $"{x}={axes.Def[x].Live}").ToList(), across, (int)Math.Min(int.MaxValue, size), spanned.Count(r => r.Tries > 0), listed, spanned);
        }
        return new CoverageAnswer(Schema, exam.Id, Iso(at), $"/{exam.Id}/coverage?as_of={Encoded(Iso(at))}", values, rows, span,
            axes.Excludes.Select(e => new Dictionary<string, string>(e)).ToList(), bank.Where(a => Bank.Stale(exam, a) is not null).Select(a => (a.Variant, a.Attempt)).Distinct().Count(),
            "Every number here is proctor's. tries counts whole, current tries as of as_of; wanted is how many the exam asks for of each configuration it makes; a stale try was made before a value or a stage changed and counts nowhere. "
            + "configurations are the ones the exam names or has tries of: any other was never asked for, and nobody spans the rest. "
            + "live on a configuration means it has the live value on every axis that has one; an axis with no live value (one that is not ours to choose) has a reference instead, which is only the value held while others are looked at. "
            + "live_span is every configuration with the live value on each axis that has one, across every value of the axes that have none; a row there with no tries is a setting in use that nothing here has tried. "
            + "It is listed in full unless it is over 200 configurations (listed false), and then only those with tries are given. "
            + "together on a configuration says whether `proctor build` found it to be its values put together (sum), more (more), not to be told (unclear) or never checked (not_checked); absent when it is at most one value off the reference. "
            + "A value nobody declared cannot show here at all. days_since_last is the age of the newest try at as_of: an old try was made on the agent and model as they were then. Nothing here recommends anything.");
    }

    static string P(double p) => p < 0.001 ? "<0.001" : p.ToString("0.###", CultureInfo.InvariantCulture);
}
