using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Proctor;

// report.html and summary.md: two renderings of one list of blocks built from stats.json, results.jsonl and
// the batch the report stands for (the latest its attempts came from, with the exam's variants as they are now).
// Neither computes a statistic. The structure is project/plans/report-structure.md: define a
// term before using it, one sentence under every heading saying what the section is for, the answer in the
// first six sections and the evidence after. Some readers open this daily and some once a quarter; for the
// latter every time is the first time, which is why the explainers are here, muted, so the findings stand out.
// The page is built as the variants side by side, the live one the reference when one is live, until the two report layouts exist. Shape inherited from experiments; tolerated, not endorsed. See project/plans/exam-variant-attempt.md.

static class Report
{
    const string Pass = "●", Fail = "○", Undecided = "?", NotCounted = "×";

    // ---- The blocks ----

    abstract record Block;
    record Title(string Exam, string Date, string Time) : Block;                  // the page's h1 and when it ran
    record Heading(string Text, string Id, bool Flagged = false) : Block;          // a section; Flagged marks it in the rail
    record Para(string Text, string? Class = null) : Block;                        // "explain": skippable once known; "caution": a warning inside the card
    record Warning(string Text) : Block;
    record Bullets(List<string> Items, string? Class = null) : Block;
    record Pairs(List<(string Key, string Value)> Items, string? Class = null) : Block;  // "roles": the keys are check roles
    record Table(List<string> Header, List<List<Td>> Rows, HashSet<int> Numeric, string? Class = null) : Block;
    record Label(string Text) : Block;                                             // a small heading inside a task card
    record Card(string Title, string? DiffTitle, List<CardVariant> Variants, List<Block> Lines, List<Block> Foot) : Block;  // the verdict: a chart, then what moved
    record CardVariant(string Variant, bool Live, RateJson? Pass, CardDiff? Diff);                                         // one chart row; Live marks the reference
    record CardDiff(int Points, int Lo, int Hi, string Word, string? Tone, bool Confirmed);
    record TaskCard(string Name, string Prompt, List<string> Facts, List<Block> Body) : Block;
    /// <summary>A table cell: the text both renderings show, and the HTML to use instead when the page can carry a link or a hover.</summary>
    record Td(string Text, string? Html = null, string? Anchor = null);

    // Inline forms both renderings share: `id`, ``code``, **bold**, *italic* and [text](#anchor). Markdown writes
    // ``code`` as `code` (it has one code style, and doubled backticks read as noise raw), keeps the rest as they
    // are and drops the link to its text, since summary.md has no anchors.

    public static string Html(StatsFile stats, List<ResultRow> rows, Batch batch, List<JudgeUse>? judges = null) => RenderHtml(Blocks(stats, rows, batch, judges), stats, batch);
    public static string Markdown(StatsFile stats, List<ResultRow> rows, Batch batch, List<JudgeUse>? judges = null) => RenderMarkdown(Blocks(stats, rows, batch, judges));

    static List<Block> Blocks(StatsFile stats, List<ResultRow> rows, Batch batch, List<JudgeUse>? judges)
    {
        var variants = stats.Variants.Keys.ToList();
        var live = Reference(stats);
        var d = stats.Descriptions;
        var b = new List<Block>();
        Td T(string s) => new(s);
        Td Id(string s) => new($"`{s}`");

        // 1. Summary
        var created = When(batch.Created);
        b.Add(new Title(stats.Exam, created.ToString("dddd, d MMMM yyyy"), created.ToString("HH:mm")));
        if (d.Exam is { } about) b.Add(new Para(about));
        b.Add(VerdictCard(stats));
        var tasksLine = $"{stats.NTasks}, each attempted {Times(Attempts(batch))} per variant";
        var attemptsPerVariant = $"Attempted **{Times(Attempts(batch))}** per variant";
        b.Add(new Pairs(
        [
            ("Host", batch.Host),
            ("Variants", $"{variants.Count}: " + string.Join(", ", variants.Select(a => a == live ? $"`{a}` (live)" : $"`{a}`"))),
            ("Tasks", tasksLine),
            ("Attempts", $"{stats.Variants.Values.Sum(a => a.Planned)} planned, {stats.Variants.Values.Sum(a => a.Analysed)} counted"),
        ]));
        foreach (var u in stats.UndecidedChecks)
            b.Add(new Warning($"The check `{u.Check}` ({CheckText(stats, u.Check)}) could not decide {u.Attempts} of the {u.Of} attempts it applies to. Its pass rates below are over the {u.Of - u.Attempts} attempts it decided, and the check needs rewriting before this report is conclusive."));
        foreach (var x in stats.ErroredChecks ?? [])
        {
            var headline = stats.PassChecks.Contains(x.Check);
            var where = x.Variants.Count == variants.Count && variants.Count > 1 ? "on every variant" : $"on {Join(x.Variants.Keys.Select(a => $"`{a}`"))}";
            b.Add(new Warning($"The check `{x.Check}` ({CheckText(stats, x.Check)}) could not grade {x.Attempts} of the {x.Of} attempts it applies to, {where} ({string.Join(", ", x.Variants.Select(a => $"`{a.Key}` {a.Value}"))}). "
                + (headline ? "It is a headline check, so each of those attempts counts as failed and the pass rates are lower by as much. " : $"Its rates below are over the {x.Of - x.Attempts} attempts it graded. ")
                + "Each attempt's reason says why; the check needs fixing before its result means much."));
        }
        foreach (var o in stats.OutdatedChecks ?? [])
        {
            var where = o.Variants.Count == variants.Count && variants.Count > 1 ? "on every variant" : $"on {Join(o.Variants.Keys.Select(a => $"`{a}`"))}";
            b.Add(new Warning($"The check `{o.Check}` ({CheckText(stats, o.Check)}) has changed or been added since {o.Attempts} of the {o.Of} attempts it applies to were made, {where} ({string.Join(", ", o.Variants.Select(a => $"`{a.Key}` {a.Value}"))}). "
                + "On an agent variant a script check runs when the attempt is made, so their verdicts are outdated: left out of its rates" + (stats.PassChecks.Contains(o.Check) ? " and, as a headline check, out of the pass rates" : "") + ". "
                + $"Make those attempts again: move {(o.Batches.Count == 1 ? "batch" : "batches")} {Join(o.Batches.Select(id => $"``{Layout.AttemptsDir}/{id}``"))} out of ``{Layout.AttemptsDir}/`` and run ``proctor run {stats.Exam}``."));
        }
        foreach (var (variant, mixed) in stats.BuiltFrom?.Mixed ?? [])
            b.Add(new Warning($"`{variant}`'s attempts were made under mixed conditions ({string.Join("; ", mixed)}), so part of any difference in its results may be the conditions and not the variant."));
        if (stats.Live is { NotReference: { } why } mark)
            b.Add(new Warning($"`{mark.Variant}` is marked live, but {why}, so no variant is compared with it."));

        // 2. Variants
        b.Add(new Heading("Variants", "variants"));
        b.Add(new Para("A variant is what is under test: an agent, a model and, when it has one, a bundle, given every task." + (variants.Count < 2 ? ""
            : live is not null ? $" The live variant, `{live}`, is the one in use now and the reference the others are compared with." : " None is live, so the variants stand side by side and none is compared with another."), "explain"));
        var bundles = batch.Bundles is { Count: > 0 };
        b.Add(new Table(["Variant", "What it is", "Harness", "Provider", "Model", "Attempts per task", .. bundles ? ["Bundle"] : Array.Empty<string>()],
            variants.Select(a => (List<Td>)[Id(a), T(d.Variants.GetValueOrDefault(a, "")), T(Harness(batch, a)), T(VariantField(batch, a, "provider", "—")), T(VariantField(batch, a, "model")), T(Attempts(batch).ToString()),
                .. bundles ? [T(batch.BundleOf(a) is { } bu ? $"``{bu.Source}``" : "")] : Array.Empty<Td>()]).ToList(),
            [5]));

        // 3. Tasks: what each asks, what it is checked for, and how every variant did on it
        b.Add(new Heading("Tasks", "tasks"));
        b.Add(new Para($"A task is one input and one desired outcome, given to every variant: a prompt against a fixture repository, with the checks that say whether the outcome was reached. {(Attempts(batch) == 1 ? "Each task is attempted once per variant, so one attempt decides its score." : $"Each task is attempted {Times(Attempts(batch))} per variant, so a score is not one lucky or unlucky attempt.")}", "explain"));
        b.Add(new Para("A check is one yes-or-no test over a finished attempt. Every task carries the checks the exam declares; a fixture or the task itself can add more. A check's role decides what it does to an attempt:", "explain"));
        b.Add(new Pairs(
        [
            ("headline", "Together, the headline checks decide whether an attempt passed."),
            ("validity", "Decides whether an attempt counts at all: an attempt that fails one is left out of every rate, not counted as a failure."),
            ("guardrail", "Reported, not part of the pass."),
        ], "roles"));
        b.Add(new Para("A check that cannot decide an attempt leaves that attempt out of its rate on both sides.", "explain"));
        var examChecks = (stats.TaskDetails ?? []).Values.SelectMany(td => td.Checks).Where(k => k.Level == "exam").DistinctBy(k => k.Name).ToList();
        if (examChecks.Count > 0)
        {
            b.Add(new Label("Checks the exam declares, carried by every task"));
            b.Add(new Table(["Check", "What it tests", "Role"],
                examChecks.Select(k => (List<Td>)[new(k.Name), new(CheckText(stats, k.Name)), RoleTd(stats, k.Name)]).ToList(), [], "checks"));
        }
        b.Add(new Para($"Each task's results show one mark per attempt: {Pass} passed, {Fail} failed, {Undecided} undecided, {NotCounted} not counted. Hover a mark for the reason; click it for the attempt. A task scores its mean over its counted, decided attempts"
            + ".", "explain"));
        foreach (var task in stats.Tasks) b.Add(TaskBlock(stats, rows, task, attemptsPerVariant));

        // 4. Results: only the numbers across all tasks
        b.Add(new Heading("Results", "results"));
        b.Add(new Para($"Pass rate is the share of counted attempts in which every headline check held, averaged task by task so that one task with many attempts does not outweigh another. The 95% interval says how far the true rate could plausibly sit from the measured one; with {stats.NTasks} task{(stats.NTasks == 1 ? "" : "s")} it is {(stats.NTasks < 10 ? "wide" : "what it is")}.", "explain"));
        b.Add(new Table(["Variant", "Pass rate", "95% interval", "Passed / decided attempts"],
            variants.Select(a => (List<Td>)[Id(a), T(RatePct(stats.Variants[a].Pass)), T(IntervalText(stats.Variants[a].Pass)), T(stats.Variants[a].Pass is { } p ? $"{p.KAttempts} / {p.NAttempts}" : "—")]).ToList(),
            [1, 2, 3]));
        if (stats.Comparisons.Count > 0)
        {
            b.Add(new Para($"**Against the live variant, `{live}`.** Each variant's pass rate minus `{live}`'s, task by task. The verdict is the direction of the difference, confirmed only when the whole 95% interval is above or below zero; unconfirmed, with {stats.Mde.NPairs} task{(stats.Mde.NPairs == 1 ? "" : "s")}, it could be noise.", "explain"));
            b.Add(new Table(["Variant", $"Difference from `{live}`", "95% interval", "Tasks better / worse / same", "Verdict"],
                stats.Comparisons.Select(c => (List<Td>)[Id(c.Variant), T(SignedPoints(c.DiffPoints)), T($"{Signed(c.Ci95[0])} to {Signed(c.Ci95[1])}"), T($"{c.Won} / {c.Lost} / {c.Tied}"),
                    ComparisonVerdict(c)]).ToList(),
                [1, 2, 3]));
        }
        if (stats.Comparisons.Count > 0) b.Add(new Para(stats.Mde.Sentence));
        b.Add(new Para("How each task scored, and why attempts failed, is shown with the task under [Tasks](#tasks).", "explain"));

        // 5. Failures
        b.Add(new Heading("Failures", "failures"));
        b.Add(new Para("For each variant, the checks that failed in at least one counted attempt, most frequent first, with the tasks they failed on. A check that could not decide an attempt, or could not grade it at all, is listed apart: that is the check's weakness, not the variant's.", "explain"));
        foreach (var a in variants)
        {
            var s = stats.Variants[a];
            var who = d.Variants.TryGetValue(a, out var ad) ? $"**`{a}`** ({ad})" : $"**`{a}`**";
            if (s.Analysed == 0) { b.Add(new Para($"{who}: no attempt was counted.")); continue; }
            b.Add(new Para(s.Failures.Count == 0 ? $"{who} failed no check in any of its {s.Analysed} counted attempt{(s.Analysed == 1 ? "" : "s")}." : $"{who} failed {s.Failures.Count} check{(s.Failures.Count == 1 ? "" : "s")}:"));
            if (s.Failures.Count > 0)
                b.Add(new Bullets(s.Failures.Select(f => FailureText(stats, f)).ToList()));
            var warned = (stats.ErroredChecks ?? []).Select(x => x.Check).ToHashSet();
            if (s.Errored?.Where(f => !warned.Contains(f.Check)).ToList() is { Count: > 0 } errored)
            {
                b.Add(new Para($"Apart from that, {(errored.Count == 1 ? "a check" : $"{errored.Count} checks")} could not grade some of {a}'s attempts; each attempt's reason says why:"));
                b.Add(new Bullets(errored.Select(f => FailureText(stats, f, "could not grade")).ToList()));
            }
        }
        if (stats.ErroredChecks is { Count: > 0 } erroredChecks)
            b.Add(new Para($"{Join(erroredChecks.Select(x => $"`{x.Check}`"))} could not grade {(erroredChecks.Count == 1 ? "many attempts" : "many attempts each")}, and {(erroredChecks.Count == 1 ? "is" : "are")} left out of the lists above; see the warning at the top."));
        var undecided = variants.SelectMany(a => stats.Variants[a].Undecided).Where(u => u.Status != Verdict.Outdated).ToList();
        if (variants.Sum(a => stats.Variants[a].Undecided.Count) - undecided.Count is > 0 and var outdated)
            b.Add(new Para($"{outdated} attempt{(outdated == 1 ? " is" : "s are")} undecided because a headline check's verdict is outdated; see the warning at the top. {(outdated == 1 ? "It is" : "They are")} marked {Undecided} outdated in the tables below."));
        if (undecided.Count > 0 && undecided.Count <= 5)
        {
            b.Add(new Para($"{undecided.Count} attempt{(undecided.Count == 1 ? " was" : "s were")} undecided, and can be reviewed top to bottom:"));
            b.Add(new Bullets(undecided.Select(u => $"{AttemptLink(rows, u.Attempt)}: {u.Reason}").ToList()));
        }
        else if (undecided.Count > 5)
            b.Add(new Para($"{undecided.Count} attempts were undecided and are out of the pass rates; see the warning above. They are marked {Undecided} in the tables below."));

        // 6. Attempt summary
        var unequal = UnequalLoss(stats);
        b.Add(new Heading("Attempt summary", "attempt-summary", Flagged: unequal is not null));
        b.Add(new Para("This is how far each attempt got before terminating. *Started* attempts began; *completed* attempts ended with a transcript; *graded* attempts have their checks; *counted* attempts are graded and passed every validity check; *decided* attempts are counted attempts whose headline checks all reached a verdict. Pass rates are over decided attempts only.", "explain"));
        b.Add(new Table(["Variant", "Planned", "Started", "Completed", "Graded", "Counted", "Decided"],
            variants.Select(a => { var s = stats.Variants[a]; return (List<Td>)[Id(a), T($"{s.Planned}"), T($"{s.Started}"), T($"{s.Completed}"), T($"{s.Graded}"), T($"{s.Analysed}"), T($"{s.Decided}")]; }).ToList(),
            [1, 2, 3, 4, 5, 6]));
        if (unequal is not null) b.Add(new Warning(unequal));
        var excluded = variants.SelectMany(a => stats.Variants[a].Excluded).ToList();
        if (excluded.Count == 0) b.Add(new Para("No attempt was left out."));
        else
        {
            b.Add(new Para("Attempts left out, and why:"));
            b.Add(new Bullets(excluded.Select(ex => $"{AttemptLink(rows, ex.Attempt)}: {ExclusionText(ex)}").ToList()));
        }
        b.Add(new Para("How each completed attempt ended, per variant. `ok` is a normal finish; anything else is the harness stopping the attempt (a limit, an error from the provider), which the checks then grade like any other.", "explain"));
        var reasons = ExitReasons(stats).ToList();
        b.Add(new Table(["Variant", .. reasons.Select(r => $"`{r}`")],
            variants.Select(a => (List<Td>)[Id(a), .. reasons.Select(r => T(ExitTd(stats.Variants[a], rows, a, r)))]).ToList(),
            Enumerable.Range(1, reasons.Count).ToHashSet()));

        // 7. Check pass rates
        b.Add(new Heading("Check pass rates", "check-rates"));
        b.Add(new Para("How often each check held, per variant, over the attempts it applies to. Headline and guardrail checks are over counted attempts; a validity check is over every graded attempt, because it says how many were counted. An attempt the check could not decide is out of its rate and counted beside it.", "explain"));
        b.Add(new Table(["Check", "What it tests", "Role", .. variants.Select(a => $"`{a}`")],
            CheckNames(stats).Select(c => (List<Td>)[Id(c), T(CheckText(stats, c)), T(Role(stats, c)), .. variants.Select(a => T(CheckRateText(stats.Variants[a].Checks.GetValueOrDefault(c))))]).ToList(),
            Enumerable.Range(3, variants.Count).ToHashSet()));

        // 8. Cost and effort
        var unit = DurationUnit(stats);
        b.Add(new Heading("Cost and effort", "cost"));
        var costed = variants.Any(a => stats.Variants[a].CostUsd is not null);
        b.Add(new Para($"Per completed attempt. Duration is wall time in {(unit == "min" ? "minutes" : "seconds")}, hooks included; tokens are the attempt's own count{(variants.Any(a => stats.Variants[a].TokensEstimated) ? ", and some counts are the harness's estimate rather than the provider's" : "")}. "
            + (costed ? "Cost is what the agent reported for its attempts (Claude Code its own count, Codex as Harbor priced its tokens); a variant whose harness reports none shows none." : "Cost is not shown: no variant's harness reports one."), "explain"));
        b.Add(new Table(["Variant", $"Duration ({unit}): median", "p90", "mean", "range", "Tokens: median", "p90", "mean", "range", .. costed ? ["Cost: median", "total"] : Array.Empty<string>()],
            variants.Select(a => { var s = stats.Variants[a]; return (List<Td>)[Id(a), .. SummaryText(s.DurationMs, v => Duration(v, unit)).Select(T), .. SummaryText(s.TokensTotal, Tokens).Select(T),
                .. costed ? [T(s.CostUsd is { } c ? Dollars(c.Median) : ""), T(s.CostTotalUsd is { } total ? Dollars(total) : "")] : Array.Empty<Td>()]; }).ToList(),
            Enumerable.Range(1, costed ? 10 : 8).ToHashSet()));

        // 9. Every attempt
        b.Add(new Heading("Every attempt", "every-attempt"));
        b.Add(new Para("Every attempt, failures first. *Reason* is the first check that did not hold and what it saw, or why the attempt never completed.", "explain"));
        b.Add(new Table(["Variant", "Task", "Attempt", "Result", "Ended", $"Duration ({unit})", "Tokens", "Reason"],
            Ordered(rows).Select(r => (List<Td>)[
                new Td($"`{r.Variant}`", Anchor: $"attempt-{r.Id}"), Id(r.Task), T($"{r.Attempt}"),
                new Td($"{Glyph(r)} {Outcome(r)}", $"<span class=\"{GlyphClass(r)}\" title=\"{E(r.Id)}\">{Glyph(r)} {E(Outcome(r))}</span>"),
                T(r.ExitReason is null ? "" : $"`{r.ExitReason}`"), T(r.DurationMs is { } dm ? Duration(dm, unit) : ""), T(r.Usage?.Total is { } t ? Tokens(t) : ""), T(Paths(Reason(r)))]).ToList(),
            [2, 5, 6]));

        // 10. Reproducibility and method
        b.Add(new Heading("Reproducibility and method", "method"));
        b.Add(new Para("What produced this report, so it can be run again, and how the numbers were computed.", "explain"));
        b.Add(new Pairs(ReproFacts(stats, batch, judges).ToList()));
        b.Add(new Para(stats.Methods, "explain"));
        return b;
    }

    // ---- The verdict card ----

    /// <summary>
    /// The chart (each variant's pass rate with its interval; with a live variant among several, each other variant's change
    /// against it with its interval), then a line for each variant that moved, one for every variant that did not, and the
    /// foot. With nothing live, the variants side by side and no change.
    /// </summary>
    static Card VerdictCard(StatsFile stats)
    {
        var variants = stats.Variants.Keys.ToList();
        var live = Reference(stats);
        var foot = new List<Block>();
        var lines = new List<Block>();
        var same = new List<string>();
        var confirmedAll = true;
        string title;
        string? diffTitle = null;
        var rows = new List<CardVariant>();

        void Moved(string variant, string drivers, string reach, bool allHeld)
        {
            if (allHeld && reach.Length == 0) { same.Add(variant); return; }
            var text = drivers.Length > 0 ? char.ToLowerInvariant(drivers[0]) + drivers[1..] : "";
            lines.Add(new Para(Sentences($"**{variant}**: {text}", reach)));
        }

        if (variants.Count > 1 && live is not null)
        {
            title = $"Against the live variant, {live}";
            diffTitle = $"Change against {live}";
            var byVariant = stats.Comparisons.ToDictionary(c => c.Variant);
            foreach (var a in variants)
            {
                if (!byVariant.TryGetValue(a, out var c))
                {
                    rows.Add(new CardVariant(a, a == live, stats.Variants[a].Pass, null));
                    if (a != live) lines.Add(new Para($"**{a}**: no task decided on both sides."));
                    continue;
                }
                var verdict = Direction(c.DiffPoints);
                var confirmed = verdict != "held" && c.Verdict != "no-detectable-difference";
                rows.Add(new CardVariant(a, false, stats.Variants[a].Pass, new CardDiff(c.DiffPoints, c.Ci95[0], c.Ci95[1], TaskWord(verdict), VariantTone(verdict), confirmed)));
                confirmedAll &= confirmed;
                Moved(a, Drivers(c.Tasks.Select(t => (t.Key, t.Value, Direction(t.Value))), verdict),
                    ShortReach(c.NPairs, c.MdePoints, stats.Mde.NPairs), c.Tasks.Values.All(v => v == 0));
            }
            if (same.Count > 0) lines.Add(new Para($"{Join(same.Select(s => $"**{s}**"))}: same as **{live}** on every task."));
        }
        else if (variants.Count > 1)
        {
            title = $"{Capital(Number(variants.Count))} variants, side by side";
            foreach (var a in variants) rows.Add(new CardVariant(a, false, stats.Variants[a].Pass, null));
            if (stats.Live is null) lines.Add(new Para("None is live yet."));
            lines.Add(new Para("Picking the best of several flatters it: part of any lead is luck."));
            lines.Add(new Para(stats.Mde.Sentence));
        }
        else
        {
            title = "Result";
            rows.Add(new CardVariant(variants[0], false, stats.Variants[variants[0]].Pass, null));
            if (stats.Variants[variants[0]].Pass is null) lines.Add(new Para($"**{variants[0]}** has no decided attempt."));
        }

        if (!confirmedAll)
        {
            var m = stats.Mde;
            var why = m.NPairs == 0 ? m.Sentence
                : $"with {m.NPairs} task{(m.NPairs == 1 ? "" : "s")} this report can only confirm a change of about {m.Points} points. Confirming a 10-point change takes about {m.TasksFor10Points} tasks.";
            foot.Add(new Para($"**Why unconfirmed:** {why}"));
        }
        foreach (var a in Careful(stats))
            foot.Add(new Para(a, "caution"));
        return new Card(title, diffTitle, rows, lines, foot);
    }

    /// <summary>The variant the others are compared with: the live one, when its mark holds and it has attempts here.</summary>
    static string? Reference(StatsFile stats) => stats.Live is { NotReference: null } live ? live.Variant : null;

    static string Number(int n) => n switch { 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight", 9 => "nine", _ => n.ToString() };

    static string Capital(string s) => char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>The tasks behind a variant's verdict: the ones that moved, the variant's own direction first, then the ones that held.</summary>
    static string Drivers(IEnumerable<(string Task, int Diff, string Verdict)> tasks, string variantVerdict)
    {
        var list = tasks.ToList();
        string Moved(string verdict) => Join(list.Where(t => t.Verdict == verdict).Select(t => $"{TaskLink(t.Task)} ({Signed(t.Diff)})"));
        var lower = list.Any(t => t.Verdict == "regressed") ? $"lower on {Moved("regressed")}" : null;
        var higher = list.Any(t => t.Verdict == "improved") ? $"higher on {Moved("improved")}" : null;
        var same = list.Any(t => t.Verdict == "held") ? $"same on {Join(list.Where(t => t.Verdict == "held").Select(t => TaskLink(t.Task)))}" : null;
        var parts = (variantVerdict == "improved" ? [higher, lower, same] : new[] { lower, higher, same }).OfType<string>().ToList();
        if (parts.Count == 0) return "";
        var text = string.Join("; ", parts) + ".";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A variant paired on fewer tasks than the report reaches less far than the card's foot says; its row says how far.</summary>
    static string ShortReach(int nPairs, int? mdePoints, int runPairs) => mdePoints is { } p
        ? $"Paired on {nPairs} of {runPairs} tasks, so it can only confirm a change of about {p} points."
        : "";

    static string Sentences(params string[] parts) => string.Join(" ", parts.Where(p => p.Length > 0));

    /// <summary>A variant that lost more of its attempts than another rests on fewer of them; the card says so and points at Attempt summary.</summary>
    static IEnumerable<string> Careful(StatsFile stats)
    {
        if (UnequalLoss(stats) is null) yield break;
        var lost = stats.Variants.ToDictionary(a => a.Key, a => a.Value.Planned - a.Value.Decided);
        var least = lost.Values.Min();
        foreach (var (variant, n) in lost.Where(l => l.Value > least))
        {
            var others = Join(lost.Where(o => o.Key != variant).Select(o => $"{o.Key} lost {(o.Value == 0 ? "none" : o.Value.ToString())}"));
            yield return $"**Read {variant} with care:** it lost {n} of its {stats.Variants[variant].Planned} attempts and {others}, so {variant}'s rate rests on fewer attempts. See [Attempt summary](#attempt-summary).";
        }
    }

    static string Direction(int diffPoints) => diffPoints < 0 ? "regressed" : diffPoints > 0 ? "improved" : "held";
    static string? VariantTone(string verdict) => verdict switch { "regressed" => "bad", "improved" => "good", _ => null };
    static string TaskWord(string verdict) => verdict switch { "regressed" => "worse", "improved" => "better", _ => "same" };
    static string ToneClass(string? tone) => tone is null ? "vsame" : $"v{tone}";
    static string Confirmation(bool confirmed) => confirmed ? "(confirmed)" : "(unconfirmed)";

    // ---- A task card ----

    static TaskCard TaskBlock(StatsFile stats, List<ResultRow> rows, string task, string attemptsPerVariant)
    {
        var variants = stats.Variants.Keys.ToList();
        var checks = (stats.TaskDetails?.GetValueOrDefault(task)?.Checks ?? [])
            .OrderBy(k => k.Level switch { "task" => 0, "fixture" => 1, _ => 2 }).ToList();
        var fromExam = checks.Count(k => k.Level == "exam");
        var fromFixture = checks.Count(k => k.Level == "fixture");
        var own = checks.Count(k => k.Level == "task");
        var where = new List<string>();
        if (fromExam > 0) where.Add($"{fromExam} from the exam");
        if (fromFixture > 0) where.Add($"{fromFixture} from its fixture");
        where.Add(own == 0 ? "none of its own" : $"{own} of its own");

        var facts = new List<string>();
        if (FixtureOf(stats, task) is { } fixture) facts.Add($"Fixture **{fixture}**");
        facts.Add(attemptsPerVariant);
        facts.Add($"**{checks.Count} check{(checks.Count == 1 ? "" : "s")}**: {string.Join(", ", where)}");

        var body = new List<Block>();
        var particular = checks.Where(k => k.Level != "exam").ToList();
        if (particular.Count > 0)
        {
            body.Add(new Label(fromExam > 0 ? "Checks beyond the exam's" : "Checks"));
            body.Add(new Table(["Check", "What it tests", "Role", "Declared by"],
                particular.Select(k => (List<Td>)[new(k.Name), new(k.Description), RoleTd(stats, k.Name),
                    new(Declared(k.Level), $"<span class=\"src\">{Declared(k.Level)}</span>")]).ToList(),
                [], "checks"));
        }

        body.Add(new Label("Results"));
        body.Add(new Table(["Variant", "Attempts", "Score", "Passed / decided"],
            variants.Select(a =>
            {
                var attempts = rows.Where(r => r.Variant == a && r.Task == task).OrderBy(r => r.Attempt).ToList();
                var score = stats.Variants[a].Tasks.GetValueOrDefault(task);
                return (List<Td>)[
                    new(a),
                    new(string.Join(" ", attempts.Select(Glyph)), "<span class=\"marks\">" + string.Join("", attempts.Select(r => $"<a href=\"#attempt-{E(r.Id)}\" class=\"{GlyphClass(r)}\" title=\"{E(GlyphTitle(r))}\">{Glyph(r)}</a>")) + "</span>"),
                    new(score?.Score is { } s ? $"**{Pct(s)}**" : "—"),
                    new(score is { Decided: > 0 } ? $"{score.Passed} of {score.Decided}" : "—"),
                ];
            }).ToList(),
            [2, 3], "results"));

        var missed = variants.SelectMany(a => rows.Where(r => r.Variant == a && r.Task == task && !(r.Analysed && r.Pass == true)).OrderBy(r => r.Attempt)).ToList();
        if (missed.Count > 0)
            body.Add(new Bullets(missed.Select(r => $"[{r.Variant} attempt {r.Attempt}](#attempt-{r.Id}) {Outcome(r)}: {Paths(FirstReason(r))}").ToList(), "why"));
        return new TaskCard(task, TaskText(stats, task), facts, body);
    }

    /// <summary>A variant against the reference in the card's words: the direction, and whether the interval confirms it.</summary>
    static Td ComparisonVerdict(Comparison c)
    {
        var verdict = Direction(c.DiffPoints);
        if (verdict == "held") return new("same");
        var word = TaskWord(verdict);
        var confirmed = Confirmation(c.Verdict != "no-detectable-difference");
        return new($"**{word}** {confirmed}", $"<b class=\"{ToneClass(VariantTone(verdict))}\">{word}</b> {confirmed}");
    }

    static Td RoleTd(StatsFile stats, string check) => new(Role(stats, check), $"<span class=\"role role-{Role(stats, check)}\">{Role(stats, check)}</span>");

    static string Declared(string level) => level switch { "task" => "this task", "fixture" => "its fixture", _ => "exam" };

    // ---- Sentences ----

    static string? UnequalLoss(StatsFile stats)
    {
        // A share, not a count: variants with different attempt counts lose nothing by deciding different numbers of attempts.
        if (stats.Variants.Values.Select(a => (double)a.Decided / Math.Max(1, a.Planned)).Distinct().Count() <= 1) return null;
        return "Variants lost attempts unequally: " + string.Join(", ", stats.Variants.Select(a => $"`{a.Key}` decided {a.Value.Decided} of {a.Value.Planned}")) + ". The rates are over decided attempts; the attempts lost are listed here.";
    }

    static string ExclusionText(Exclusion ex) => ex.Status switch
    {
        "invalid" => $"not counted, failed a validity check. {Paths(ex.Reason)}",
        "failed" => $"never completed. {Paths(ex.Reason)}",
        _ => $"{ex.Status}. {Paths(ex.Reason)}".TrimEnd('.', ' '),
    };

    static string ExitTd(VariantStats a, List<ResultRow> rows, string variant, string reason)
    {
        var n = a.ExitReasons.GetValueOrDefault(reason);
        if (a.Completed == 0) return "—";
        if (n == 0 || n > 3 || reason == "ok") return $"{n}";
        var which = rows.Where(r => r.Variant == variant && r.ExitReason == reason).Select(r => $"`{r.Task}` attempt {r.Attempt}");
        return $"{n} ({string.Join(", ", which)})";
    }

    /// <summary>An exclusion's attempt (variant/task/n) as a link to its row under Every attempt.</summary>
    static string AttemptLink(List<ResultRow> rows, string attempt) =>
        rows.FirstOrDefault(r => $"{r.Variant}/{r.Task}/{r.Attempt}" == attempt) is { } r ? $"[{attempt}](#attempt-{r.Id})" : $"`{attempt}`";

    static string TaskLink(string task) => $"[{task}](#task-{task})";

    static string Reason(ResultRow r) =>
        r.StatusReason ?? r.Invalid ?? r.Undecided ?? (r.Reasons is null ? "" : string.Join("; ", r.Reasons.Select(k => $"{k.Key}: {k.Value}")));

    static string FirstReason(ResultRow r) =>
        r.StatusReason ?? r.Invalid ?? r.Undecided ?? (r.Reasons?.FirstOrDefault() is { Key: not null } k ? $"{k.Key}: {k.Value}" : "");

    static string Outcome(ResultRow r) =>
        r.Invalid is not null ? "not counted" : r.Outdated is not null ? "outdated" : r.Undecided is not null ? "undecided" : r.Pass switch { true => "passed", false => "failed", null => r.Status == AttemptStatus.Completed ? "not graded" : r.Status == AttemptStatus.Failed ? "never completed" : r.Status };

    static string Glyph(ResultRow r) => !r.Analysed ? NotCounted : r.Undecided is not null ? Undecided : r.Pass == true ? Pass : Fail;
    static string GlyphClass(ResultRow r) => !r.Analysed ? "skip" : r.Undecided is not null ? "undecided" : r.Pass == true ? "pass" : "fail";
    static string GlyphTitle(ResultRow r) => $"attempt {r.Attempt}: {Outcome(r)}{(Reason(r).Length > 0 ? " — " + Reason(r) : "")} ({r.Id})";

    static IEnumerable<ResultRow> Ordered(List<ResultRow> rows) =>
        rows.OrderBy(r => !r.Analysed ? 1 : r.Undecided is not null ? 2 : r.Pass == true ? 3 : 0).ThenBy(r => r.Variant).ThenBy(r => r.Task).ThenBy(r => r.Attempt);

    /// <summary>A file path in free text (a hook, a script) set as code: `dir/name.ext`, at least one slash and an extension.</summary>
    static string Paths(string text) => Regex.Replace(text, @"(?<![\w/`.'""-])(?:[\w.-]+/)+[\w-]+\.\w+(?![\w/`])", "``$0``");

    /// <summary>What a check tests when it says one thing everywhere; a check each task describes in its own words points at the Tasks section.</summary>
    static string CheckText(StatsFile stats, string check) =>
        stats.Descriptions.Checks.GetValueOrDefault(check) ?? (PerTask(stats, check) ? "per task; see Tasks" : check);

    /// <summary>A check with no one description, so the report has a sentence for it only per task.</summary>
    static bool PerTask(StatsFile stats, string check) =>
        !stats.Descriptions.Checks.ContainsKey(check) && stats.TaskDetails is { Count: > 0 } td && td.Values.Any(t => t.Checks.Any(k => k.Name == check));

    static string? TaskCheckText(StatsFile stats, string task, string check) =>
        stats.TaskDetails?.GetValueOrDefault(task)?.Checks.FirstOrDefault(k => k.Name == check)?.Description;

    /// <summary>A failed check with the tasks it failed on; a check described per task carries each task's sentence beside the task.</summary>
    static string FailureText(StatsFile stats, CheckFailure f, string verb = "failed in")
    {
        var perTask = PerTask(stats, f.Check);
        var head = perTask ? $"**`{f.Check}`** ({Role(stats, f.Check)})" : $"**{CheckText(stats, f.Check)}** (`{f.Check}`, {Role(stats, f.Check)})";
        var on = f.Tasks.OrderByDescending(c => c.Value).Select(c =>
            $"{Times(c.Value)} on {TaskLink(c.Key)}" + (perTask && TaskCheckText(stats, c.Key, f.Check) is { } text ? $" ({text})" : ""));
        return $"{head} {verb} {f.Attempts} of {f.Of} attempt{(f.Of == 1 ? "" : "s")}: {string.Join(", ", on)}.";
    }

    static string TaskText(StatsFile stats, string c) => stats.Descriptions.Tasks.GetValueOrDefault(c, c);

    static IEnumerable<string> CheckNames(StatsFile stats) =>
        stats.PassChecks.Concat(stats.ValidityChecks).Concat(stats.Variants.Values.SelectMany(a => a.Checks.Keys)).Distinct();

    static string? FixtureOf(StatsFile stats, string task) => stats.TaskDetails?.GetValueOrDefault(task)?.Fixture;

    static string Role(StatsFile stats, string check) =>
        stats.PassChecks.Contains(check) ? "headline" : stats.ValidityChecks.Contains(check) ? "validity" : "guardrail";

    static IEnumerable<string> ExitReasons(StatsFile stats) =>
        stats.Variants.Values.SelectMany(a => a.ExitReasons).GroupBy(k => k.Key).OrderByDescending(g => g.Sum(k => k.Value)).ThenBy(g => g.Key).Select(g => g.Key);

    static string VariantField(Batch batch, string variant, string field, string missing = "?") =>
        batch.Variants.FirstOrDefault(a => a.Id == variant)?.Def[field]?.GetValue<string>() ?? missing;

    /// <summary>nb's harness on an nb variant; a vendor agent at its pin, which is its harness. Its provider is the machine's route, which the batch does not record.</summary>
    static string Harness(Batch batch, string variant) =>
        VariantField(batch, variant, "harness", "") is { Length: > 0 } h ? h : $"{VariantField(batch, variant, "agent")} {VariantField(batch, variant, "agent_version", "")}".TrimEnd();

    static int Attempts(Batch batch) => batch.ExamDef["attempts"]?.GetValue<int>() ?? 1;

    /// <summary>Monospace only for what is typed or opened: the command, paths, endpoints.</summary>
    static IEnumerable<(string, string)> ReproFacts(StatsFile stats, Batch batch, List<JudgeUse>? judges)
    {
        if (stats.BuiltFrom is { } built)
        {
            yield return ("built from", $"{Count(built.Current, "current attempt")} from {Count(built.Batches.Count, "batch", "batches")}: {string.Join(", ", built.Batches.Select(id => $"``{id}``"))}");
            if (built.Narrowed is { } narrowed) yield return ("narrowed to", $"``{narrowed}``");
            if (built.Stale > 0)
                yield return ("stale", $"{Count(built.Stale, "attempt")} not counted, made under a variant or task that has changed: "
                    + string.Join("; ", (built.StaleAttempts ?? []).GroupBy(x => x.Why).Select(g => $"{g.Key} ({g.Count()})")));
            foreach (var (variant, hash) in built.Variants) yield return ($"variant {variant}", hash);
            foreach (var (task, hash) in built.Tasks) yield return ($"task {task}", hash);
        }
        else yield return ("batch", batch.Id);
        yield return ("proctor", batch.Versions.GetValueOrDefault("proctor", "unknown"));
        yield return ("nb", $"{batch.Versions.GetValueOrDefault("nb", "unknown")} at ``{batch.Nb.Path}``");
        yield return ("exam hash", batch.ExamHash);
        foreach (var (variant, bundle) in batch.Bundles ?? []) yield return ($"bundle {variant}", $"``{bundle.Source}`` {bundle.Hash}");
        foreach (var j in judges ?? [])
            yield return ($"judge {j.Judge}", $"{j.Kind}{(j.Model is null ? "" : " " + j.Model)} at ``{j.Endpoint}``; graded {string.Join(", ", j.Checks)}");
        yield return ("repository", batch.Repo is null ? "not a git repository" : $"{batch.Repo.Commit}{(batch.Repo.Dirty ? " (dirty)" : "")}");
        yield return ("command", $"``{batch.CommandLine}``");
        yield return ("created", batch.Created);
    }

    // ---- Numbers and dates as text (formatting only) ----

    static string RatePct(RateJson? r) => r is null ? "—" : Pct(r.Rate);
    static string IntervalText(RateJson? r) => r is null ? "—" : $"{Points(r.Ci95.Lo)}% to {Points(r.Ci95.Hi)}%";

    static string CheckRateText(RateJson? r)
    {
        if (r is null) return "—";
        var extra = new[] { r.NeedsJudge is > 0 ? $"{r.NeedsJudge} undecided" : null, r.Errors is > 0 ? $"{r.Errors} error" : null, r.Outdated is > 0 ? $"{r.Outdated} outdated" : null }.Where(s => s is not null);
        return $"{Pct(r.Rate)} ({r.KAttempts}/{r.NAttempts})" + (extra.Any() ? $" · {string.Join(", ", extra)}" : "");
    }

    static string DurationUnit(StatsFile stats) =>
        stats.Variants.Values.Any(a => a.DurationMs is { Median: >= 120_000 }) ? "min" : "s";

    static string Duration(double ms, string unit) => unit == "min" ? $"{ms / 60_000:F1}" : $"{ms / 1000:F1}";
    static string Tokens(double n) => ((long)n).ToString("N0");
    static string Dollars(double d) => d < 0.01 ? $"${d:0.0000}" : $"${d:0.00}";
    static string Pct(double p) => $"{Points(p)}%";
    static string Points(double p) => $"{Math.Round(100 * p, MidpointRounding.AwayFromZero):F0}";
    static string Signed(int points) => points > 0 ? $"+{points}" : points < 0 ? $"−{-points}" : "0";
    static string SignedPoints(int points) => points == 0 ? "±0 points" : $"{Signed(points)} points";
    static string Count(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";
    static string Times(int n) => n switch { 1 => "once", 2 => "twice", _ => $"{n} times" };
    static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list[..^1]) + " and " + list[^1];
    }

    static IEnumerable<string> SummaryText(Summary? s, Func<double, string> f) =>
        s is null ? ["—", "—", "—", "—"] : [f(s.Median), f(s.P90), f(s.Mean), $"{f(s.Min)}–{f(s.Max)}"];

    static DateTimeOffset When(string iso) =>
        DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    // ---- HTML ----

    // ---- The chart ----

    /// <summary>
    /// The verdict card's chart as inline SVG: a row per variant, its pass rate as a dot on its 95% interval over 0–100%
    /// (the live variant labelled), and beside it the change in points with its interval over
    /// −100 to +100, filled when confirmed. It draws what stats.json holds and computes nothing; the hover is a title.
    /// </summary>
    static string ChartSvg(Card c)
    {
        const int Row = 34, Rate = 250, Gap = 70, Diff = 200, Value = 96;
        var diff = c.DiffTitle is not null;
        var anyLive = c.Variants.Any(a => a.Live);
        var label = Math.Clamp(c.Variants.Max(a => a.Variant.Length) * 8 + (anyLive ? 40 : 0) + 16, 90, 240);
        const int top = 56;
        var bottom = top + Row * c.Variants.Count;
        var width = diff ? label + Rate + Gap + Diff + Value : label + Rate + 60;
        var height = bottom + 30;
        double X1(double r) => label + r * Rate;
        double X2(double p) => label + Rate + Gap + (Math.Clamp(p, -100, 100) + 100) / 200 * Diff;
        static string F(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        static string Tone(string? tone) => tone switch { "bad" => "#a52d15", "good" => "#17603a", _ => "#1b1b1b" };

        var sb = new StringBuilder();
        var aria = diff ? $"Pass rate per variant with its 95% interval, and each variant's {c.DiffTitle!.ToLowerInvariant()} in points" : "Pass rate with its 95% interval";
        sb.Append($"<svg class=\"chart\" viewBox=\"0 0 {width} {height}\" width=\"{width}\" height=\"{height}\" role=\"img\" aria-label=\"{E(aria)}\">\n");
        sb.Append($"<text x=\"{label}\" y=\"16\" class=\"ch\">Pass rate</text><text x=\"{label}\" y=\"33\" class=\"cs\">dot: share of attempts passed · bar: 95% interval</text>\n");
        if (diff)
            sb.Append($"<text x=\"{F(X2(-100))}\" y=\"16\" class=\"ch\">{E(c.DiffTitle!)}</text><text x=\"{F(X2(-100))}\" y=\"33\" class=\"cs\">points · hollow: not confirmed</text>\n");
        foreach (var r in new[] { 0, .25, .5, .75, 1 })
            sb.Append($"<line x1=\"{F(X1(r))}\" x2=\"{F(X1(r))}\" y1=\"{top - 8}\" y2=\"{bottom}\" class=\"grid\"/><text x=\"{F(X1(r))}\" y=\"{bottom + 17}\" class=\"ax\" text-anchor=\"middle\">{F(r * 100)}%</text>\n");
        if (diff)
            foreach (var p in new[] { -100, -50, 0, 50, 100 })
                sb.Append($"<line x1=\"{F(X2(p))}\" x2=\"{F(X2(p))}\" y1=\"{top - 8}\" y2=\"{bottom}\" class=\"{(p == 0 ? "zero" : "grid")}\"/><text x=\"{F(X2(p))}\" y=\"{bottom + 17}\" class=\"ax\" text-anchor=\"middle\">{Signed(p)}</text>\n");

        foreach (var (a, i) in c.Variants.Select((a, i) => (a, i)))
        {
            var y = top + Row * i + Row / 2;
            sb.Append($"<text x=\"0\" y=\"{y + 5}\" class=\"variant\">{E(a.Variant)}{(a.Live ? " <tspan class=\"cs\">live</tspan>" : "")}</text>\n");
            if (a.Pass is { } p)
            {
                sb.Append($"<g><title>{E(a.Variant)}: {Pct(p.Rate)} of attempts passed ({p.KAttempts} of {p.NAttempts}); 95% interval {E(IntervalText(p))} over {p.N} task{(p.N == 1 ? "" : "s")}</title>");
                sb.Append($"<rect x=\"{F(X1(p.Ci95.Lo) - 6)}\" y=\"{y - 13}\" width=\"{F(X1(p.Ci95.Hi) - X1(p.Ci95.Lo) + 12)}\" height=\"26\" fill=\"transparent\"/>");
                sb.Append($"<line x1=\"{F(X1(p.Ci95.Lo))}\" x2=\"{F(X1(p.Ci95.Hi))}\" y1=\"{y}\" y2=\"{y}\" class=\"bar\"/>");
                sb.Append($"<circle cx=\"{F(X1(p.Rate))}\" cy=\"{y}\" r=\"5\" class=\"dot\"/></g>");
                sb.Append($"<text x=\"{F(X1(1) + 10)}\" y=\"{y + 5}\" class=\"val\">{Pct(p.Rate)}</text>\n");
            }
            else sb.Append($"<text x=\"{F(X1(0))}\" y=\"{y + 5}\" class=\"cs\">no decided attempt</text>\n");
            if (a.Diff is { } d)
            {
                var colour = Tone(d.Tone);
                sb.Append($"<g><title>{E(a.Variant)}: {E(SignedPoints(d.Points))}; 95% interval {Signed(d.Lo)} to {Signed(d.Hi)}; {d.Word}, {(d.Confirmed ? "confirmed" : "not confirmed")}</title>");
                sb.Append($"<rect x=\"{F(X2(d.Lo) - 6)}\" y=\"{y - 13}\" width=\"{F(X2(d.Hi) - X2(d.Lo) + 12)}\" height=\"26\" fill=\"transparent\"/>");
                sb.Append($"<line x1=\"{F(X2(d.Lo))}\" x2=\"{F(X2(d.Hi))}\" y1=\"{y}\" y2=\"{y}\" class=\"bar\" style=\"stroke:{colour}\"/>");
                sb.Append($"<circle cx=\"{F(X2(d.Points))}\" cy=\"{y}\" r=\"5\" class=\"ddot\" style=\"stroke:{colour};fill:{(d.Confirmed ? colour : "#fff")}\"/></g>");
                sb.Append($"<text x=\"{F(X2(100) + 10)}\" y=\"{y + 5}\" class=\"val\" style=\"fill:{colour}\">{(d.Points == 0 ? "±0" : Signed(d.Points))} {d.Word}</text>\n");
            }
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }


    // ---- HTML ----

    static string RenderHtml(List<Block> blocks, StatsFile stats, Batch batch)
    {
        var created = When(batch.Created);
        var sb = new StringBuilder();
        sb.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(stats.Exam)} — {created:d MMM yyyy} — proctor</title>
            <link rel="icon" href="{Logo}">
            <style>
            {Css}
            </style>
            </head>
            <body>
            <div class="page">

            """);
        sb.Append(Rail(blocks, stats.Exam, created));
        sb.Append("<main class=\"rpt\" id=\"top\">\n<section id=\"summary\">\n");
        foreach (var block in blocks) Html(sb, block);
        sb.Append("</section>\n");
        sb.Append($"""
            <footer>
            <img src="{Logo}" alt="" width="16" height="16">
            <span>Run by proctor {E(batch.Versions.GetValueOrDefault("proctor", "unknown"))} on {created:d MMM yyyy}. Icon adapted from <a href="https://game-icons.net/1x1/lorc/pointy-hat.html">Pointy hat</a> by Lorc, <a href="https://creativecommons.org/licenses/by/3.0/">CC BY 3.0</a>.</span>
            </footer>
            </main>
            </div>
            <script>
            {Scrollspy}
            </script>
            </body>
            </html>

            """);
        return sb.ToString();
    }

    static void Html(StringBuilder sb, Block block)
    {
        switch (block)
        {
            case Title t:
                sb.Append($"""
                    <header>
                    <div class="kicker">Exam</div>
                    <h1>{E(t.Exam)}</h1>
                    <div class="when">{E(t.Date)}</div>
                    <div class="time">Started {E(t.Time)} UTC</div>
                    </header>

                    """);
                break;
            case Heading h:
                sb.Append($"</section>\n\n<section id=\"{E(h.Id)}\">\n<h2>{E(h.Text)}</h2>\n");
                break;
            case Para p:
                sb.Append($"<p{Class(p.Class)}>{Inline(p.Text)}</p>\n");
                break;
            case Warning w:
                sb.Append($"<p class=\"warning\">{Inline(w.Text)}</p>\n");
                break;
            case Bullets l:
                sb.Append($"<ul{Class(l.Class)}>\n").Append(string.Concat(l.Items.Select(i => $"<li>{Inline(i)}</li>\n"))).Append("</ul>\n");
                break;
            case Pairs kv:
                sb.Append(kv.Class == "roles" ? "<dl class=\"explain\">\n" : "<dl>\n");
                foreach (var (k, v) in kv.Items)
                    sb.Append(kv.Class == "roles" ? $"<dt><span class=\"role role-{E(k)}\">{E(k)}</span></dt>" : $"<dt>{E(k)}</dt>").Append($"<dd>{Inline(v)}</dd>\n");
                sb.Append("</dl>\n");
                break;
            case Label l:
                sb.Append($"<h4>{E(l.Text)}</h4>\n");
                break;
            case Table t:
                // A wide table (many variants) scrolls within the column rather than running off the page.
                sb.Append($"<div class=\"table-wrap\"><table{Class(t.Class)}>\n<thead><tr>");
                foreach (var (h, i) in t.Header.Select((h, i) => (h, i))) sb.Append($"<th{(t.Numeric.Contains(i) ? " class=\"num\"" : "")}>{Inline(h)}</th>");
                sb.Append("</tr></thead>\n<tbody>\n");
                foreach (var row in t.Rows)
                {
                    var anchor = row.Select(c => c.Anchor).FirstOrDefault(a => a is not null);
                    sb.Append(anchor is null ? "<tr>" : $"<tr id=\"{E(anchor)}\">");
                    foreach (var (c, i) in row.Select((c, i) => (c, i)))
                        sb.Append($"<td{(t.Numeric.Contains(i) ? " class=\"num\"" : "")}>{c.Html ?? Inline(c.Text)}</td>");
                    sb.Append("</tr>\n");
                }
                sb.Append("</tbody>\n</table></div>\n");
                break;
            case Card c:
                sb.Append($"<div class=\"verdict\" role=\"region\" aria-label=\"Verdict\">\n<div class=\"kicker\">{E(c.Title)}</div>\n");
                sb.Append(ChartSvg(c));
                if (c.Lines.Count > 0)
                {
                    sb.Append("<div class=\"verdict-lines\">\n");
                    foreach (var l in c.Lines) Html(sb, l);
                    sb.Append("</div>\n");
                }
                if (c.Foot.Count > 0)
                {
                    sb.Append("<div class=\"verdict-foot\">\n");
                    foreach (var f in c.Foot) Html(sb, f);
                    sb.Append("</div>\n");
                }
                sb.Append("</div>\n");
                break;
            case TaskCard tc:
                sb.Append($"<section id=\"task-{E(tc.Name)}\" class=\"task\">\n<h3>{E(tc.Name)}</h3>\n<p class=\"prompt\">{Inline(tc.Prompt)}</p>\n");
                sb.Append("<div class=\"facts\">").Append(string.Concat(tc.Facts.Select(f => $"<span>{Inline(f)}</span>"))).Append("</div>\n");
                foreach (var inner in tc.Body) Html(sb, inner);
                sb.Append("</section>\n");
                break;
        }
    }

    /// <summary>The sticky table of contents: logo, exam and date, every section with the tasks under Tasks, and back to top.</summary>
    static string Rail(List<Block> blocks, string exam, DateTimeOffset created)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            <nav class="toc-rail" aria-label="Report sections">
            <div class="brand"><img src="{Logo}" alt="" width="22" height="22"><span>proctor</span></div>
            <div class="rail-exam"><div class="name">{E(exam)}</div><div class="date">{created:d MMM yyyy}</div></div>
            <div class="rail-toc">
            <div class="kicker">On this page</div>
            <div class="toc">
            <a href="#summary"><span>Summary</span></a>

            """);
        foreach (var block in blocks)
        {
            if (block is Heading h)
                sb.Append($"<a href=\"#{E(h.Id)}\"><span>{E(h.Text)}</span>{(h.Flagged ? "<span class=\"flag\" title=\"Needs care\" aria-label=\"Needs care\"></span>" : "")}</a>\n");
            else if (block is TaskCard tc)
                sb.Append($"<a href=\"#task-{E(tc.Name)}\" class=\"is-sub\"><span>{E(tc.Name)}</span></a>\n");
        }
        sb.Append("""
            </div>
            </div>
            <a class="to-top" href="#top"><svg width="12" height="12" viewBox="0 0 12 12" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M6 10V2M2.5 5.5L6 2l3.5 3.5"></path></svg>Back to top</a>
            </nav>

            """);
        return sb.ToString();
    }

    static string Class(string? c) => c is null ? "" : $" class=\"{c}\"";

    /// <summary>Escape, then the inline forms both renderings share.</summary>
    static string Inline(string text)
    {
        var s = E(text);
        s = Regex.Replace(s, "``(.+?)``", "<code class=\"code\">$1</code>");
        s = Regex.Replace(s, "`([^`]+)`", "<span class=\"id\">$1</span>");
        s = Regex.Replace(s, @"\[([^\]]+)\]\((#[^)\s]+)\)", "<a href=\"$2\">$1</a>");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
        s = Regex.Replace(s, @"(?<![\w*])\*([^*\s][^*]*?)\*(?![\w*])", "<i>$1</i>");
        return s;
    }

    static string E(string s) => WebUtility.HtmlEncode(s);

    /// <summary>The wizard hat, embedded from assets/pointy-hat.svg (CC BY 3.0; the credit is in the footer).</summary>
    static readonly string Logo = LoadLogo();

    static string LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("pointy-hat.svg")!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(buffer.ToArray());
    }

    // Colour is semantic only: green passed or better, red failed or worse, amber not counted or a warning,
    // purple a link. Monospace is for what is typed or opened; every id is in the body font.
    const string Css = """
        html { scroll-behavior: smooth; background: #fff; }
        body { margin: 0; color: #1b1b1b; font: 15px/1.45 -apple-system, "Segoe UI", Helvetica, Arial, sans-serif; background: #fff; }
        a { color: #5a3e9e; } a:hover { color: #3f2a78; }
        b { font-weight: 600; }
        .page { width: 100%; min-height: 100vh; box-sizing: border-box; display: flex; justify-content: center; gap: 48px; padding: 0 24px; }
        .rpt { flex-grow: 1; min-width: 0; max-width: 64rem; padding: 2rem 0 4rem; }
        .kicker { font-size: 12px; font-weight: 600; letter-spacing: .08em; text-transform: uppercase; color: #595959; }
        .rpt header { display: flex; flex-direction: column; gap: 10px; margin-bottom: 20px; }
        .rpt header .when { font-size: 20px; font-weight: 500; margin-top: 4px; }
        .rpt header .time { font-size: 14px; color: #595959; margin-top: -6px; }
        .rpt h1 { font-size: 2.5rem; line-height: 1.1; font-weight: 650; letter-spacing: -.01em; margin: 0; }
        .rpt h2 { font-size: 1.625rem; line-height: 1.2; font-weight: 650; letter-spacing: -.005em; margin: 3rem 0 .75rem; padding-top: 1rem; border-top: 1px solid #d8d8d8; }
        .rpt h3 { font-size: 1.0625rem; line-height: 1.3; font-weight: 600; margin: 0; }
        .rpt h4 { font-size: .8rem; font-weight: 600; letter-spacing: .08em; text-transform: uppercase; color: #595959; margin: 1.1rem 0 0; }
        .rpt section, .rpt tr { scroll-margin-top: 24px; }
        .rpt p { margin: .5rem 0; max-width: 52rem; }
        .rpt .explain { color: #6b6b6b; font-size: 14px; }
        .rpt table { border-collapse: collapse; margin: .75rem 0; font-variant-numeric: tabular-nums; }
        .rpt .table-wrap { max-width: 100%; overflow-x: auto; }
        .rpt th, .rpt td { padding: .3rem .7rem; border-bottom: 1px solid #d8d8d8; text-align: left; vertical-align: top; }
        .rpt th { font-weight: 600; font-size: .85rem; color: #595959; }
        .rpt td { max-width: 32rem; }
        .rpt .num { text-align: right; white-space: nowrap; }
        .rpt .marks { white-space: nowrap; }
        .rpt .marks a { text-decoration: none; letter-spacing: .2em; font-size: 1.25em; }
        .rpt tr:target { background: #eef4f0; }
        .rpt ul { margin: .3rem 0 .6rem 1.2rem; padding: 0; max-width: 52rem; }
        .rpt li { margin: .15rem 0; }
        .rpt dl { display: grid; grid-template-columns: max-content 1fr; gap: .3rem 1rem; max-width: 52rem; margin: .75rem 0; }
        .rpt dt { color: #595959; } .rpt dd { margin: 0; overflow-wrap: anywhere; }
        .id { white-space: nowrap; }
        .code { font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; font-size: .88em; background: #f4f4f2; padding: 1px 4px; border-radius: 3px; overflow-wrap: anywhere; }
        .pass { color: #1f6f43; } .fail { color: #c2361b; } .skip { color: #9a5b00; } .undecided { color: #595959; }
        .vbad { color: #a52d15; } .vgood { color: #17603a; } .vsame { color: #595959; }
        .role { font-size: 12.5px; font-weight: 600; white-space: nowrap; }
        .role-headline { color: #1b1b1b; } .role-validity { color: #9a5b00; } .role-guardrail { color: #595959; }
        .src { color: #595959; white-space: nowrap; }
        .warning { background: #fff4d6; color: #5c3d00; padding: .5rem .75rem; margin: 1rem 0; border-radius: 4px; }
        .verdict { margin: 1.25rem 0 1rem; max-width: 52rem; box-sizing: border-box; border: 1px solid #d8d8d8; border-radius: 10px; padding: 24px 28px 20px; display: flex; flex-direction: column; gap: 20px; background: #fbfbf9; }
        .chart { display: block; max-width: 100%; height: auto; overflow: visible; }
        .chart text { font-family: inherit; }
        .chart .ch { font-size: 13.5px; font-weight: 600; fill: #1b1b1b; }
        .chart .cs { font-size: 12px; font-weight: 400; fill: #595959; }
        .chart .ax { font-size: 11.5px; fill: #595959; font-variant-numeric: tabular-nums; }
        .chart .variant { font-size: 13.5px; font-weight: 600; fill: #1b1b1b; }
        .chart .val { font-size: 13px; font-weight: 600; fill: #1b1b1b; font-variant-numeric: tabular-nums; }
        .chart .grid { stroke: #e4e4e0; } .chart .zero { stroke: #9a9a9a; }
        .chart .bar { stroke: #595959; stroke-width: 2; stroke-linecap: round; }
        .chart .dot { fill: #1b1b1b; stroke: #fff; stroke-width: 2; }
        .chart .ddot { stroke-width: 2; }
        .verdict-lines { display: flex; flex-direction: column; gap: 6px; }
        .rpt .verdict-lines p { margin: 0; max-width: 44rem; }
        .verdict-foot { border-top: 1px solid #e4e4e0; padding-top: 16px; }
        .rpt .verdict-foot p { margin: 0 0 .5rem; }
        .rpt .verdict .caution { color: #5c3d00; }
        .rpt .task { margin-top: 1.75rem; padding: 1rem 1.25rem 1.1rem; border: 1px solid #d8d8d8; border-radius: 8px; max-width: 52rem; box-sizing: border-box; }
        .rpt .task .prompt { margin: .25rem 0 .5rem; }
        .rpt .task .facts { display: flex; flex-wrap: wrap; gap: 4px 20px; font-size: 13.5px; color: #595959; }
        .rpt .task .facts b { color: #1b1b1b; }
        .rpt .task table { width: 100%; margin: .35rem 0 0; }
        .rpt .task th { white-space: nowrap; }
        .rpt .task tr:last-child td { border-bottom: 0; }
        .rpt ul.why { margin: .6rem 0 0 1.1rem; }
        .rpt dl.explain .role { font-size: 12.5px; }
        .rpt footer { margin-top: 3rem; padding-top: 1rem; border-top: 1px solid #d8d8d8; display: flex; align-items: center; gap: 8px; font-size: 14px; color: #6b6b6b; }
        .rpt footer img, .brand img { display: block; }
        .toc-rail { position: sticky; top: 0; align-self: flex-start; flex-shrink: 0; width: 220px; max-height: 100vh; overflow-y: auto; box-sizing: border-box; padding: 32px 0 24px; display: flex; flex-direction: column; gap: 16px; }
        .brand { display: flex; align-items: center; gap: 8px; padding-bottom: 14px; border-bottom: 1px solid #e4e4e0; }
        .brand span { font-weight: 600; color: #3b2f6e; letter-spacing: .01em; }
        .rail-exam { display: flex; flex-direction: column; gap: 2px; }
        .rail-exam .name { font-size: 15px; font-weight: 650; }
        .rail-exam .date { font-size: 12.5px; color: #595959; }
        .rail-toc { display: flex; flex-direction: column; gap: 6px; }
        .rail-toc .kicker { font-size: 11px; }
        .toc { display: flex; flex-direction: column; border-left: 1px solid #d8d8d8; }
        .toc a { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 5px 12px; margin-left: -1px; border-left: 2px solid transparent; color: #595959; text-decoration: none; font-size: 13.5px; line-height: 1.35; }
        .toc a:hover { color: #1b1b1b; border-left-color: #bdbdbd; }
        .toc a.is-active { color: #1b1b1b; font-weight: 600; border-left-color: #5a3e9e; }
        .toc a.is-sub { padding-left: 26px; font-size: 13px; }
        .toc a:focus-visible { outline: 2px solid #5a3e9e; outline-offset: 2px; border-radius: 2px; }
        .toc .flag { flex-shrink: 0; width: 7px; height: 7px; border-radius: 50%; background: #d9a400; }
        .to-top { font-size: 12.5px; color: #595959; text-decoration: none; display: flex; align-items: center; gap: 6px; min-height: 28px; }
        @media (max-width: 900px) { .toc-rail { display: none; } }
        """;

    /// <summary>Highlights the rail entry for the section being read. The page works without it.</summary>
    const string Scrollspy = """
        (function () {
          var links = Array.prototype.slice.call(document.querySelectorAll('.toc a'));
          var ids = links.map(function (a) { return a.getAttribute('href').slice(1); });
          function update() {
            var active = ids[0];
            ids.forEach(function (id) {
              var el = document.getElementById(id);
              if (el && el.getBoundingClientRect().top <= 120) active = id;
            });
            var se = document.scrollingElement;
            if (se.scrollHeight > innerHeight && se.scrollTop + innerHeight >= se.scrollHeight - 4) active = ids[ids.length - 1];
            links.forEach(function (a, i) {
              var on = ids[i] === active;
              a.classList.toggle('is-active', on);
              if (on) a.setAttribute('aria-current', 'true'); else a.removeAttribute('aria-current');
            });
          }
          addEventListener('scroll', update, { passive: true });
          addEventListener('resize', update);
          update();
        })();
        """;

    // ---- Markdown ----

    static string RenderMarkdown(List<Block> blocks)
    {
        var sb = new StringBuilder();
        foreach (var block in blocks) Markdown(sb, block);
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    static void Markdown(StringBuilder sb, Block block)
    {
        switch (block)
        {
            case Title t: sb.Append($"# {t.Exam}\n\n{t.Date}, started {t.Time} UTC\n\n"); break;
            case Heading h: sb.Append($"## {h.Text}\n\n"); break;
            case Label l: sb.Append($"**{l.Text}**\n\n"); break;
            case Para p: sb.Append($"{Md(p.Text)}\n\n"); break;
            case Warning w: sb.Append($"> **Warning.** {Md(w.Text)}\n\n"); break;
            case Bullets l: sb.Append(string.Concat(l.Items.Select(i => $"- {Md(i)}\n"))).Append('\n'); break;
            case Pairs kv:
                sb.Append("| | |\n|---|---|\n").Append(string.Concat(kv.Items.Select(i => $"| {i.Key} | {MdTd(i.Value)} |\n"))).Append('\n');
                break;
            case Table t:
                sb.Append("| ").Append(string.Join(" | ", t.Header)).Append(" |\n");
                sb.Append("|").Append(string.Join("|", t.Header.Select(_ => "---"))).Append("|\n");
                foreach (var row in t.Rows) sb.Append("| ").Append(string.Join(" | ", row.Select(c => MdTd(c.Text)))).Append(" |\n");
                sb.Append('\n');
                break;
            case Card c:
                sb.Append($"**{c.Title}**\n\n");
                var diffs = c.DiffTitle is not null;
                sb.Append(diffs ? $"| Variant | Passed | 95% interval | {c.DiffTitle} | 95% interval |\n|---|---|---|---|---|\n" : "| Variant | Passed | 95% interval |\n|---|---|---|\n");
                foreach (var a in c.Variants)
                {
                    sb.Append($"| {a.Variant}{(a.Live ? " (live)" : "")} | {RatePct(a.Pass)} | {IntervalText(a.Pass)} |");
                    if (diffs)
                        sb.Append(a.Diff is { } d
                            ? $" {(d.Points == 0 ? "±0" : Signed(d.Points))} points, **{d.Word}** {Confirmation(d.Confirmed)} | {Signed(d.Lo)} to {Signed(d.Hi)} |"
                            : " — | — |");
                    sb.Append('\n');
                }
                sb.Append('\n');
                foreach (var l in c.Lines) Markdown(sb, l);
                foreach (var f in c.Foot) Markdown(sb, f);
                break;
            case TaskCard tc:
                sb.Append($"### {tc.Name}\n\n{Md(tc.Prompt)}\n\n{Md(string.Join(" · ", tc.Facts))}\n\n");
                foreach (var inner in tc.Body) Markdown(sb, inner);
                break;
        }
    }

    static string Md(string s) => Regex.Replace(Regex.Replace(s, @"\[([^\]]+)\]\(#[^)\s]+\)", "$1"), "``([^`]+?)``", "`$1`");
    static string MdTd(string s) => Md(s).Replace("|", "\\|").ReplaceLineEndings("<br>");
}
