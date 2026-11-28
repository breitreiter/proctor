using System.Globalization;

namespace Proctor;

// What proctor answers when asked (plans/axes-build.md, slice 3c; plans/axes.md, decision 17). Proctor does no decision
// support: an agent makes the recommendation, and these two queries are what it is given. So an answer is only what
// was measured, worked out by the report's own code (Configurations.Compare) and nowhere else, and it carries what a
// reader needs to not say more than that: its scope, what was not run, when the tries were made, what so few tries
// could have shown, what moved together, and an address for every number. JSON on standard output.

/// <summary>One try behind a number: its id, when it was made, what it held, and where it is (the viewer's address, and the bank's directory for whoever has the files).</summary>
record TryAddress(string Id, string Made, int Held, int Of, int? Flagged, string? Invalid, string Address, string Path, int? ToolCalls = null, long? Tokens = null, long? DurationMs = null);

/// <summary>One side of one setting: the configuration, whether it is known to be its values put together, and its counted tries as of the answer.</summary>
record SideAnswer(string Configuration, string? Name, string? Together, string Address, List<TryAddress> Tries);

/// <summary>
/// One setting of the other axes: the value's side and the other's, compared when both have tries. CouldConfirm is
/// whether this many tries a side could confirm any difference at all; SmallestP the smallest p they could give.
/// </summary>
record CellAnswer(Dictionary<string, string> Holding, string State, SideAnswer Value, SideAnswer Against, Measure? Held, Measure? Flagged, bool CouldConfirm, double SmallestP, List<CheckHeld>? Checks = null,
    Measure? ToolCalls = null, Measure? Tokens = null, Measure? Duration = null,
    List<CheckCompared>? ByCheck = null, List<StageCompared>? ByStage = null, List<LabelCompared>? Friction = null, double? FlaggedPerTry = null, double? OtherFlaggedPerTry = null);

/// <summary>
/// One check in one setting, both sides: how often it held of the times it was asked, and the two compared try against
/// try on the stages of a try in which it held. Higher is the measure's "better" whatever the check's role: an
/// observation has no preferred answer, and a reader of one takes the direction and leaves the word.
/// </summary>
record CheckCompared(string Check, string Role, string? About, int Held, int Asked, int OtherHeld, int OtherAsked, Measure Measure);

/// <summary>One stage in one setting, both sides as the configuration's page has them, compared try against try on the share of its correctness checks a try held.</summary>
record StageCompared(string Stage, StageTally Value, StageTally Against, Measure? Measure);

/// <summary>One label of friction in one setting: flagged steps a try on each side.</summary>
record LabelCompared(string Label, double PerTry, double OtherPerTry);

/// <summary>One correctness check in one setting: in how many of each side's counted tries it held. Counts only; no test is made of one check.</summary>
record CheckHeld(string Stage, string Check, string? About, int Value, int ValueTries, int Against, int AgainstTries);

/// <summary>When each side's tries were made, and how far apart the two sides are (between the middle try of each).</summary>
record Timing(string? ValueFirst, string? ValueLast, string? AgainstFirst, string? AgainstLast, double? DaysApart, string? Note);

/// <summary>Another axis that only ever moved with the one asked about: every try on the value's side had WithValue on it, every try on the other side WithAgainst.</summary>
record MovedWith(string Axis, string WithValue, string WithAgainst);

/// <summary>What so few tries could have shown, read as one or in the single setting compared.</summary>
record Power(bool CouldConfirm, double SmallestP, string Note);

/// <summary>
/// Change is what is compared: one value, or several changed together, by axis. Against is what it is compared with on
/// those axes. Configuration is the configuration asked about, when one was: the change is then the axes on which it
/// differs from the other.
/// </summary>
record ComparisonAnswer(string Schema, string Exam, string AsOf, string Address, string? Configuration, Dictionary<string, string> Change, Dictionary<string, string> Against, Dictionary<string, string>? Within,
    string Verdict, bool? Confirmed, string Sentence, List<CellAnswer> Cells, Pooled? ReadAsOne, List<string> NotRun, Power? Power, Timing Timing,
    List<MovedWith> MovedWith, List<string> Cautions, List<string> OtherComparisons, int TriesSince, string Methods, List<PredictionReport>? Predictions = null);

/// <summary>A configuration as coverage sees it: who asked for it, how far it is from what is live, its tries and how old the newest is; and, when it is retired, why and since when.</summary>
record CoverageRow(string Configuration, string? Name, Dictionary<string, string> Values, bool Live, List<string> OffLive, int Tries, int Wanted, int Lost, int Stale,
    string? First, string? Last, double? DaysSinceLast, string? Together, string Address, int NotGraded = 0, RetiredMark? Retired = null);

/// <summary>One value: whether it is live or its axis's reference, and the tries of every configuration holding it.</summary>
record ValueCoverage(string Axis, string Value, string? Note, bool Live, bool Reference, int Configurations, int Tries, string? Last, string? Why = null);

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
        current.Select(a => (a, Id: a.Entry.IdOf(a.Configuration, a.Attempt))).Where(x => MadeAt(x.Id) is not null).Select(x => (x.a, x.Id!, MadeAt(x.Id)!.Value)).ToList();

    /// <summary>
    /// One value of an axis against another, every other axis held, as of a time (now when null). Against defaults to
    /// the axis's live value, then its reference.
    /// </summary>
    public static ComparisonAnswer Compare(string root, Exam exam, string axis, string value, string? against, IReadOnlyDictionary<string, string>? within, DateTime? asOf)
    {
        var axes = exam.Axes!;
        against ??= axes.Def[axis].Live ?? axes.ReferenceOf(axis)
            ?? throw new ProctorException($"axis '{axis}' has no live value and no reference to compare {axis}={value} with: say --against <value> ({string.Join(", ", axes.Def[axis].Values!.Keys.Where(v => v != value))})");
        if (against == value) throw new ProctorException($"{axis}={value} against itself: say --against <value> ({string.Join(", ", axes.Def[axis].Values!.Keys.Where(v => v != value))})");
        return Compare(root, exam, new Dictionary<string, string> { [axis] = value }, new Dictionary<string, string> { [axis] = against }, within, asOf, null);
    }

    /// <summary>
    /// One configuration against another: what the first changes is the axes on which they differ, taken together.
    /// Every other axis that has a live value is held as the configuration has it, since those are choices of ours and
    /// part of what was asked about; the change is compared across the axes that have none (the ones not ours to
    /// choose), wherever the exam carries it. Against defaults to the configuration with the live value on every axis
    /// that has one and the rest as they are: what is in use now, for the same user.
    /// </summary>
    public static ComparisonAnswer Compare(string root, Exam exam, Configuration configuration, Configuration? against, IReadOnlyDictionary<string, string>? within, DateTime? asOf)
    {
        var axes = exam.Axes!;
        var other = against ?? axes.Make(configuration.Values.ToDictionary(v => v.Axis, v => axes.Def[v.Axis].Live ?? v.Value))!;
        var differs = configuration.Values.Where(v => other[v.Axis] != v.Value).Select(v => v.Axis).ToList();
        if (differs.Count == 0)
            throw new ProctorException(against is not null ? $"{configuration.Name ?? configuration.Label} against itself: there is nothing to compare"
                : $"{configuration.Name ?? configuration.Label} has the live value on every axis that has one, so it is what is in use: say --against <configuration> to compare it with another");
        var held = new Dictionary<string, string>(within ?? new Dictionary<string, string>());
        foreach (var (axis, def) in axes.Def)
            if (def.Live is not null && !differs.Contains(axis)) held.TryAdd(axis, configuration[axis]!);
        return Compare(root, exam, differs.ToDictionary(a => a, a => configuration[a]!), differs.ToDictionary(a => a, a => other[a]!), held, asOf, configuration.Name ?? configuration.Key);
    }

    /// <summary>
    /// These values together against those, every other axis held. Refused while a stage in it is not graded, since an
    /// ungraded try would read as one that held nothing.
    /// </summary>
    internal static ComparisonAnswer Compare(string root, Exam exam, Dictionary<string, string> change, Dictionary<string, string> against, IReadOnlyDictionary<string, string>? within, DateTime? asOf, string? asked)
    {
        var axes = exam.Axes!;
        if (within?.Keys.FirstOrDefault(change.ContainsKey) is { } clash) throw new ProctorException($"--within {clash}=…: '{clash}' is an axis the comparison is along; --within holds the other axes");
        string Said(Dictionary<string, string> values) => string.Join(" × ", axes.Def.Keys.Where(values.ContainsKey).Select(x => $"{x}={values[x]}"));
        var (changeSaid, againstSaid) = (Said(change), Said(against));
        var now = DateTime.UtcNow;
        var at = asOf ?? now;

        var all = Timed(Bank.Select(root, exam).Current);
        var then = all.Where(x => x.Made <= at).ToList();
        var current = then.Select(x => x.Attempt).ToList();
        var rows = Results.Collect(root, exam, current);
        var (tries, rollup) = Tries.Compute(root, exam, current, Bank.Of(root, exam), rows, null);
        var byKey = tries.Configurations.ToDictionary(v => v.Id);
        var standing = Combinations.Load(exam).Stand(exam);
        var moves = Standard.Moves(root, exam, current);
        var comparison = Configurations.Compare(exam, byKey, standing, change, against, within, moves);

        // Both sides under the checks as they are now. A try with no grade, or one graded only under earlier checks, is not yet graded;
        // the rest of the exam may stay that way, and this comparison may not.
        var keys = comparison.Cells.SelectMany(c => new[] { c.Key, c.LiveKey }).ToHashSet();
        var ungraded = current.Count(a => keys.Contains(a.Configuration) && a.Status == AttemptStatus.Completed && !Standard.Current(exam, exam.TaskOf(a.Task), a.Dir(root)));
        if (ungraded > 0) throw new ProctorException($"{ungraded} stage{(ungraded == 1 ? "" : "s")} of the tries in this comparison {(ungraded == 1 ? "is" : "are")} not yet graded under the checks as they are now, and an ungraded try would read as one that held nothing: run `proctor grade {exam.Id}` first");

        var ids = then.GroupBy(x => (x.Attempt.Configuration, x.Attempt.Attempt)).ToDictionary(g => g.Key, g => g.First().Id);
        SideAnswer Side(string key)
        {
            var c = axes.Of(key)!;
            var made = byKey.GetValueOrDefault(key)?.Rows.Where(r => ids.ContainsKey((key, r.N))).Select(r => (Row: r, Id: ids[(key, r.N)])).ToList() ?? [];
            return new SideAnswer(key, c.Name, standing.TryGetValue(key, out var together) ? together.State : null, ConfigurationAddress(exam, key),
                made.Select(m => new TryAddress(m.Id, Iso(MadeAt(m.Id)!.Value), m.Row.Held, m.Row.Of, m.Row.Flagged, m.Row.Invalid, $"/{exam.Id}/try/{m.Id}", TryPath(exam, m.Id), m.Row.ToolCalls, m.Row.TokensIn, m.Row.DurationMs)).ToList());
        }
        // Which check: in how many of each side's counted tries each correctness check held. Counts, so a reader can see where a difference sits.
        List<CheckHeld>? ChecksOf(ComparedCell c)
        {
            if (c.State != "compared") return null;
            List<int> Counted(string key) => byKey[key].Rows.Where(r => r.Invalid is null).Select(r => r.N).ToList();
            int Held(string key, List<int> ns, string stage, string check) => rows.Count(r => r.Configuration == key && ns.Contains(r.Attempt) && r.Task == stage && r.Checks?.GetValueOrDefault(check) == Verdict.Pass);
            var (mineNs, theirNs) = (Counted(c.Key), Counted(c.LiveKey));
            return exam.StagesOf(c.Key).SelectMany(s => Standard.PassIn(exam, s, c.Key).Select(check =>
                new CheckHeld(s.Id!, check, exam.ChecksFor(s).GetValueOrDefault(check) is { } declared ? Checks.Describe(declared.Spec) : null, Held(c.Key, mineNs, s.Id!, check), mineNs.Count, Held(c.LiveKey, theirNs, s.Id!, check), theirNs.Count))).ToList();
        }
        // Check by check and stage by stage, each side as its configuration's page has it, and the two compared as every measure is.
        List<CheckCompared>? ByCheck(ComparedCell c)
        {
            if (c.State != "compared") return null;
            var (mine, theirs) = (byKey[c.Key], byKey[c.LiveKey]);
            List<double> Stages(ConfigurationTries v, string check) => v.Rows.Where(r => r.Invalid is null).Select(r => (double)rows.Count(x => x.Configuration == v.Id && x.Attempt == r.N && x.Checks?.GetValueOrDefault(check) == Verdict.Pass)).ToList();
            return (mine.Checks ?? []).Select(k => (Mine: k, Theirs: theirs.Checks?.FirstOrDefault(o => o.Check == k.Check))).Where(k => k.Theirs is not null).Select(k =>
                new CheckCompared(k.Mine.Check, k.Mine.Role, exam.Tasks.Select(s => exam.ChecksFor(s).GetValueOrDefault(k.Mine.Check)).FirstOrDefault(d => d is not null) is { } declared ? Checks.Describe(declared.Spec) : null,
                    k.Mine.Held, k.Mine.Asked, k.Theirs!.Held, k.Theirs.Asked, Tries.Compare(Stages(mine, k.Mine.Check), Stages(theirs, k.Mine.Check), higherIsBetter: true))).ToList();
        }
        List<StageCompared>? ByStage(ComparedCell c)
        {
            if (c.State != "compared") return null;
            var (mine, theirs) = (byKey[c.Key], byKey[c.LiveKey]);
            List<double> Shares(ConfigurationTries v, string stage) => v.Rows.Where(r => r.Invalid is null).Select(r => r.Stages.FirstOrDefault(s => s.Id == stage)).Where(s => s?.Of is > 0).Select(s => (double)s!.Held!.Value / s.Of!.Value).ToList();
            return mine.Stages.Select(s => (Mine: s, Theirs: theirs.Stages.FirstOrDefault(o => o.Id == s.Id))).Where(s => s.Theirs is not null).Select(s =>
                new StageCompared(s.Mine.Id, s.Mine, s.Theirs!, Shares(mine, s.Mine.Id) is { Count: > 0 } a && Shares(theirs, s.Mine.Id) is { Count: > 0 } b ? Tries.Compare(a, b, higherIsBetter: true) : null)).ToList();
        }
        List<LabelCompared>? ByLabel(ComparedCell c)
        {
            if (c.State != "compared" || byKey[c.Key].Flagged is null || byKey[c.LiveKey].Flagged is null) return null;
            var (mine, theirs) = (rollup.Configurations.FirstOrDefault(v => v.Id == c.Key)?.Labels ?? [], rollup.Configurations.FirstOrDefault(v => v.Id == c.LiveKey)?.Labels ?? []);
            return mine.Select(l => l.Label).Union(theirs.Select(l => l.Label)).Select(l => new LabelCompared(l, mine.FirstOrDefault(x => x.Label == l)?.PerTry ?? 0, theirs.FirstOrDefault(x => x.Label == l)?.PerTry ?? 0))
                .OrderByDescending(l => Math.Max(l.PerTry, l.OtherPerTry)).ToList();
        }
        var cells = comparison.Cells.Select(c => new CellAnswer(c.Holding, c.State, Side(c.Key), Side(c.LiveKey), c.Held, c.Flagged,
            Tries.MinP(c.Tries, c.LiveTries) < Alpha, Tries.MinP(c.Tries, c.LiveTries), ChecksOf(c), c.ToolCalls, c.Tokens, c.Duration,
            ByCheck(c), ByStage(c), ByLabel(c), c.State == "compared" ? rollup.Configurations.FirstOrDefault(v => v.Id == c.Key)?.PerTry : null, c.State == "compared" ? rollup.Configurations.FirstOrDefault(v => v.Id == c.LiveKey)?.PerTry : null)).ToList();

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
        var (a, b) = (withTries.Where(c => c.Has(change)).ToList(), withTries.Where(c => c.Has(against)).ToList());
        var moved = a.Count == 0 || b.Count == 0 ? [] : axes.Def.Keys.Where(x => !change.ContainsKey(x))
            .Where(x => a.Select(c => c[x]).Distinct().Count() == 1 && b.Select(c => c[x]).Distinct().Count() == 1 && a[0][x] != b[0][x])
            .Select(x => new MovedWith(x, a[0][x]!, b[0][x]!)).ToList();
        var cautions = new List<string>(comparison.Cautions ?? []);
        foreach (var m in moved)
            cautions.Add($"Cannot be told apart from {m.Axis}: every try with {changeSaid} had {m.Axis}={m.WithValue}, and every try with {againstSaid} had {m.Axis}={m.WithAgainst}. Telling them apart takes a configuration that changes one and not the other.");
        if (timing.Note is not null) cautions.Add(timing.Note);
        if (power is { CouldConfirm: false }) cautions.Add(power.Note);

        // How many comparisons the exam's report holds beside this one: asked for one at a time, some will look confirmed by chance.
        var others = axes.Def.Where(d => d.Value.Live is not null).SelectMany(d => d.Value.Values!.Keys.Where(v => v != d.Value.Live).Select(v => (Axis: d.Key, Value: v, Against: d.Value.Live!)))
            .Where(o => !(change.Count == 1 && change.GetValueOrDefault(o.Axis) == o.Value && against[o.Axis] == o.Against)).Where(o => Configurations.Compare(exam, byKey, standing, o.Axis, o.Value, o.Against).Cells.Any(c => c.State == "compared"))
            .Select(o => $"{o.Axis}={o.Value} against {o.Axis}={o.Against}").ToList();
        if (others.Count > 0) cautions.Add($"The exam's report holds {others.Count} other comparison{(others.Count == 1 ? "" : "s")} against live with tries on both sides ({string.Join("; ", others)}). At p < 0.05 about one in twenty comparisons of things that do not differ is confirmed by chance.");

        var since = asOf is null ? 0 : all.Where(x => x.Made > at && keys.Contains(x.Attempt.Configuration)).Select(x => x.Id).Distinct().Count();
        // A change of several values at once names the values, not their sum: what each does alone is a comparison of its own.
        if (change.Count > 1)
            cautions.Add($"This compares {change.Count} values changed together ({changeSaid}). It does not say which of them made a difference: ask for each alone.");
        string Listed(Dictionary<string, string> values) => Encoded(string.Join(",", axes.Def.Keys.Where(values.ContainsKey).Select(x => $"{x}={values[x]}")));
        var address = $"/{exam.Id}/compare?change={Listed(change)}&against={Listed(against)}"
            + (within is { Count: > 0 } ? "&within=" + Encoded(string.Join(",", within.Select(w => $"{w.Key}={w.Value}"))) : "") + $"&as_of={Encoded(Iso(at))}";
        // What was predicted of a configuration in this comparison, and how it came out: the predictions whose configuration is one of the value's sides.
        var problems = new List<Problem>();
        var predicted = PredictionOutcomes.Compute(exam, Predictions.Load(root, exam, problems).Where(c => comparison.Cells.Any(k => k.Key == c.Configuration.Key)).ToList(), byKey, rows, current);
        return new ComparisonAnswer(Schema, exam.Id, Iso(at), address, asked, change, against, within is { Count: > 0 } ? new Dictionary<string, string>(within) : null,
            verdict, decided?.Confirmed, comparison.Sentence, cells, comparison.Pooled, comparison.NotRun, power, timing, moved, cautions, others, since,
            "Every number here is proctor's, worked out as its report works it out. A try is one configuration run once through the exam's stages; held is the correctness checks it held, of how many. "
            + "change is compared with against (one value of an axis, or several values changed together when a configuration was asked about) in each setting of the other axes where the exam names or has tries of the change, try against try (Mann-Whitney, exact two-sided p), "
            + "and the settings compared are read as one when they do not point different ways (van Elteren). confirmed means p < 0.05; at the top it is of the one reading there is, "
            + "and absent when there is none (nothing compared, or better here and worse there: then each cell has its own). "
            + "flagged is a try's steps inside any friction point, when friction was run. checks in a cell counts, per correctness check, the tries of each side that held it; it tests nothing. "
            + "together on a configuration more than one value off the reference says whether `proctor build` found it to be its values put together (sum), more than that (more), not to be told (unclear), or never checked (not_checked). "
            + "A setting not run is not run, never no difference. tries_since counts tries made after as_of in these configurations: the numbers are as they stood at as_of. "
            + "by_check and by_stage give each check and each stage on both sides, compared the same way (a check on the stages of a try in which it held, a stage on the share of its correctness checks a try held); friction is flagged steps a try by label. tool_calls, tokens and duration are compared as held and flagged are, try against try, and of each fewer is better; one is null when a try on either side does not carry the number. predictions are those written about a configuration in this comparison: each change one expects, with how it came out against the configuration it named; post hoc means tries of its configuration were made before the prediction was written. "
            + "An address opens the same thing in the viewer; path is its directory under the repository root. Nothing here recommends anything.",
            predicted.Count > 0 ? predicted : null);
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
        var counted = bank.Where(a => a.Counts && Bank.Stale(exam, a) is null).Select(a => (a, Id: a.Entry.IdOf(a.Configuration, a.Attempt))).Where(x => MadeAt(x.Id) <= at)
            .GroupBy(x => x.a.Configuration).ToDictionary(g => g.Key, g => g.Select(x => x.Id!).Distinct().Order(StringComparer.Ordinal).ToList());
        var standing = Combinations.Load(exam).Stand(exam);
        var retired = Retired.Load(exam);
        var ungraded = Configurations.NotGraded(root, exam, bank.Where(a => a.Counts && Bank.Stale(exam, a) is null));
        var liveAxes = axes.Def.Where(d => d.Value.Live is not null).Select(d => d.Key).ToList();

        CoverageRow Row(Configuration c)
        {
            var ids = counted.GetValueOrDefault(c.Key) ?? [];
            var times = ids.Select(id => MadeAt(id)!.Value).ToList();
            return new CoverageRow(c.Key, c.Name, c.Values.ToDictionary(v => v.Axis, v => v.Value), liveAxes.Count > 0 && liveAxes.All(x => c[x] == axes.Def[x].Live),
                liveAxes.Where(x => c[x] != axes.Def[x].Live).Select(x => $"{x}={c[x]}").ToList(), ids.Count, exam.Attempts,
                bank.Where(a => a.Lost && a.Configuration == c.Key && Bank.Stale(exam, a) is null).Select(a => a.Attempt).Distinct().Count(),
                bank.Where(a => a.Configuration == c.Key && Bank.Stale(exam, a) is not null).Select(a => a.Attempt).Distinct().Count(),
                times.Count > 0 ? Iso(times.Min()) : null, times.Count > 0 ? Iso(times.Max()) : null, times.Count > 0 ? Math.Round((at - times.Max()).TotalDays, 1) : null,
                standing.TryGetValue(c.Key, out var together) ? together.State : null, ConfigurationAddress(exam, c.Key), ungraded.Count(u => u.Configuration == c.Key), c.Name is null ? null : retired.GetValueOrDefault(c.Name));
        }
        var rows = axes.Configurations.Select(Row).ToList();

        var values = axes.Def.SelectMany(d => d.Value.Values!.Select(v =>
        {
            var holding = rows.Where(r => r.Values[d.Key] == v.Key && r.Tries > 0).ToList();
            return new ValueCoverage(d.Key, v.Key, v.Value!.Note, d.Value.Live == v.Key, axes.ReferenceOf(d.Key) == v.Key, holding.Count, holding.Sum(r => r.Tries), holding.Select(r => r.Last).Max(StringComparer.Ordinal), v.Value.Why);
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
            axes.Excludes.Select(e => new Dictionary<string, string>(e)).ToList(), bank.Where(a => Bank.Stale(exam, a) is not null).Select(a => (a.Configuration, a.Attempt)).Distinct().Count(),
            "Every number here is proctor's. tries counts whole, current tries as of as_of; wanted is how many the exam asks for of each configuration it makes; a stale try was made before a value or a stage changed and counts nowhere. "
            + "configurations are the ones the exam names or has tries of: any other was never asked for, and nobody spans the rest. "
            + "live on a configuration means it has the live value on every axis that has one; an axis with no live value (one that is not ours to choose) has a reference instead, which is only the value held while others are looked at. "
            + "live_span is every configuration with the live value on each axis that has one, across every value of the axes that have none; a row there with no tries is a setting in use that nothing here has tried. "
            + "It is listed in full unless it is over 200 configurations (listed false), and then only those with tries are given. "
            + "together on a configuration says whether `proctor build` found it to be its values put together (sum), more (more), not to be told (unclear) or never checked (not_checked); absent when it is at most one value off the reference. "
            + "not_graded counts a configuration's tries with no grade under the checks as they are now: they are made, and in no comparison until `proctor grade` reads them. "
            + "A value's note says what it changes and its why says what it is there for (a customer's report, an idea of the author's), both in the author's words. "
            + "A value nobody declared cannot show here at all. retired on a configuration says it was retired, since when and why: its tries are still counted and it can still be compared, and `run` makes no new tries of it. days_since_last is the age of the newest try at as_of: an old try was made on the agent and model as they were then. Nothing here recommends anything.");
    }

    static string P(double p) => p < 0.001 ? "<0.001" : p.ToString("0.###", CultureInfo.InvariantCulture);
}
