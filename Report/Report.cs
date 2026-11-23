using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Proctor;

// summary.md: a list of blocks built from stats.json, results.jsonl and the conditions the report stands for
// (the log entry of the latest run its attempts came from, with the exam's variants as they are now), rendered as
// markdown. It computes no statistic. There is no HTML page: auspex is the viewer (removed 2026-11-22). The structure is project/plans/report-structure.md: define a
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
    record Heading(string Text) : Block;
    record Para(string Text) : Block;
    record Warning(string Text) : Block;
    record Bullets(List<string> Items) : Block;
    record Pairs(List<(string Key, string Value)> Items) : Block;
    record Table(List<string> Header, List<List<Td>> Rows) : Block;
    record Label(string Text) : Block;                                             // a small heading inside a task card
    record Card(string Title, string? DiffTitle, List<CardVariant> Variants, List<Block> Lines, List<Block> Foot) : Block;  // the verdict: each variant's rate as a table, then what moved
    record CardVariant(string Variant, bool Live, RateJson? Pass, CardDiff? Diff);                                         // one chart row; Live marks the reference
    record CardDiff(int Points, int Lo, int Hi, string Word, bool Confirmed);
    record TaskCard(string Name, string Prompt, List<string> Facts, List<Block> Body) : Block;
    record Td(string Text);

    // Inline forms in the text: `id`, ``code``, **bold**, *italic* and [text](#anchor). Markdown writes ``code`` as
    // `code` (it has one code style, and doubled backticks read as noise raw), keeps the rest as they are and drops
    // the link to its text, since summary.md has no anchors.

    public static string Markdown(StatsFile stats, List<ResultRow> rows, LogEntry latest, List<JudgeUse>? judges = null) => RenderMarkdown(Blocks(stats, rows, latest, judges));

    static List<Block> Blocks(StatsFile stats, List<ResultRow> rows, LogEntry latest, List<JudgeUse>? judges)
    {
        var attempts = stats.Attempts;
        var variants = stats.Variants.Keys.ToList();
        var live = Reference(stats);
        var d = stats.Descriptions;
        var b = new List<Block>();
        Td T(string s) => new(s);
        Td Id(string s) => new($"`{s}`");

        // 1. Summary
        var created = When(latest.Created);
        b.Add(new Title(stats.Exam, created.ToString("dddd, d MMMM yyyy"), created.ToString("HH:mm")));
        if (d.Exam is { } about) b.Add(new Para(about));
        b.Add(VerdictCard(stats));
        var tasksLine = $"{stats.NTasks}, each attempted {Times(attempts)} per variant";
        var attemptsPerVariant = $"Attempted **{Times(attempts)}** per variant";
        b.Add(new Pairs(
        [
            ("Host", latest.Host),
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
                + $"Make those attempts again: ``proctor grade {stats.Exam}`` names them; move their directories out of ``{Layout.BankDir}/{stats.Exam}/`` and run ``proctor run {stats.Exam}``."));
        }
        foreach (var (variant, mixed) in stats.BuiltFrom?.Mixed ?? [])
            b.Add(new Warning($"`{variant}`'s attempts were made under mixed conditions ({string.Join("; ", mixed)}), so part of any difference in its results may be the conditions and not the variant."));
        if (stats.Live is { NotReference: { } why } mark)
            b.Add(new Warning($"`{mark.Variant}` is marked live, but {why}, so no variant is compared with it."));

        // 2. Variants
        b.Add(new Heading("Variants"));
        b.Add(new Para("A variant is what is under test: an agent, a model and, when it has one, a bundle, given every task." + (variants.Count < 2 ? ""
            : live is not null ? $" The live variant, `{live}`, is the one in use now and the reference the others are compared with." : " None is live, so the variants stand side by side and none is compared with another.")));
        var bundles = latest.Bundles is { Count: > 0 };
        b.Add(new Table(["Variant", "What it is", "Harness", "Provider", "Model", "Attempts per task", .. bundles ? ["Bundle"] : Array.Empty<string>()],
            variants.Select(a => (List<Td>)[Id(a), T(d.Variants.GetValueOrDefault(a, "")), T(Harness(latest, a)), T(VariantField(latest, a, "provider", "—")), T(VariantField(latest, a, "model")), T(attempts.ToString()),
                .. bundles ? [T(latest.BundleOf(a) is { } bu ? $"``{bu.Source}``" : "")] : Array.Empty<Td>()]).ToList()));

        // 3. Tasks: what each asks, what it is checked for, and how every variant did on it
        b.Add(new Heading("Tasks"));
        b.Add(new Para($"A task is one input and one desired outcome, given to every variant: a prompt against a fixture repository, with the checks that say whether the outcome was reached. {(attempts == 1 ? "Each task is attempted once per variant, so one attempt decides its score." : $"Each task is attempted {Times(attempts)} per variant, so a score is not one lucky or unlucky attempt.")}"));
        b.Add(new Para("A check is one yes-or-no test over a finished attempt. Every task carries the checks the exam declares; a fixture or the task itself can add more. A check's role decides what it does to an attempt:"));
        b.Add(new Pairs(
        [
            ("headline", "Together, the headline checks decide whether an attempt passed."),
            ("validity", "Decides whether an attempt counts at all: an attempt that fails one is left out of every rate, not counted as a failure."),
            ("guardrail", "Reported, not part of the pass."),
        ]));
        b.Add(new Para("A check that cannot decide an attempt leaves that attempt out of its rate on both sides."));
        var examChecks = (stats.TaskDetails ?? []).Values.SelectMany(td => td.Checks).Where(k => k.Level == "exam").DistinctBy(k => k.Name).ToList();
        if (examChecks.Count > 0)
        {
            b.Add(new Label("Checks the exam declares, carried by every task"));
            b.Add(new Table(["Check", "What it tests", "Role"],
                examChecks.Select(k => (List<Td>)[new(k.Name), new(CheckText(stats, k.Name)), RoleTd(stats, k.Name)]).ToList()));
        }
        b.Add(new Para($"Each task's results show one mark per attempt: {Pass} passed, {Fail} failed, {Undecided} undecided, {NotCounted} not counted. Each attempt's reason is under Every attempt. A task scores its mean over its counted, decided attempts"
            + "."));
        foreach (var task in stats.Tasks) b.Add(TaskBlock(stats, rows, task, attemptsPerVariant));

        // 4. Results: only the numbers across all tasks
        b.Add(new Heading("Results"));
        b.Add(new Para($"Pass rate is the share of counted attempts in which every headline check held, averaged task by task so that one task with many attempts does not outweigh another. The 95% interval says how far the true rate could plausibly sit from the measured one; with {stats.NTasks} task{(stats.NTasks == 1 ? "" : "s")} it is {(stats.NTasks < 10 ? "wide" : "what it is")}."));
        b.Add(new Table(["Variant", "Pass rate", "95% interval", "Passed / decided attempts"],
            variants.Select(a => (List<Td>)[Id(a), T(RatePct(stats.Variants[a].Pass)), T(IntervalText(stats.Variants[a].Pass)), T(stats.Variants[a].Pass is { } p ? $"{p.KAttempts} / {p.NAttempts}" : "—")]).ToList()));
        if (stats.Comparisons.Count > 0)
        {
            b.Add(new Para($"**Against the live variant, `{live}`.** Each variant's pass rate minus `{live}`'s, task by task. The verdict is the direction of the difference, confirmed only when the whole 95% interval is above or below zero; unconfirmed, with {stats.Mde.NPairs} task{(stats.Mde.NPairs == 1 ? "" : "s")}, it could be noise."));
            b.Add(new Table(["Variant", $"Difference from `{live}`", "95% interval", "Tasks better / worse / same", "Verdict"],
                stats.Comparisons.Select(c => (List<Td>)[Id(c.Variant), T(SignedPoints(c.DiffPoints)), T($"{Signed(c.Ci95[0])} to {Signed(c.Ci95[1])}"), T($"{c.Won} / {c.Lost} / {c.Tied}"),
                    ComparisonVerdict(c)]).ToList()));
        }
        if (stats.Comparisons.Count > 0) b.Add(new Para(stats.Mde.Sentence));
        b.Add(new Para("How each task scored, and why attempts failed, is shown with the task under [Tasks](#tasks)."));

        // 5. Failures
        b.Add(new Heading("Failures"));
        b.Add(new Para("For each variant, the checks that failed in at least one counted attempt, most frequent first, with the tasks they failed on. A check that could not decide an attempt, or could not grade it at all, is listed apart: that is the check's weakness, not the variant's."));
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
        b.Add(new Heading("Attempt summary"));
        b.Add(new Para("This is how far each attempt got before terminating. *Started* attempts began; *completed* attempts ended with a transcript; *graded* attempts have their checks; *counted* attempts are graded and passed every validity check; *decided* attempts are counted attempts whose headline checks all reached a verdict. Pass rates are over decided attempts only."));
        b.Add(new Table(["Variant", "Planned", "Started", "Completed", "Graded", "Counted", "Decided"],
            variants.Select(a => { var s = stats.Variants[a]; return (List<Td>)[Id(a), T($"{s.Planned}"), T($"{s.Started}"), T($"{s.Completed}"), T($"{s.Graded}"), T($"{s.Analysed}"), T($"{s.Decided}")]; }).ToList()));
        if (unequal is not null) b.Add(new Warning(unequal));
        var excluded = variants.SelectMany(a => stats.Variants[a].Excluded).ToList();
        if (excluded.Count == 0) b.Add(new Para("No attempt was left out."));
        else
        {
            b.Add(new Para("Attempts left out, and why:"));
            b.Add(new Bullets(excluded.Select(ex => $"{AttemptLink(rows, ex.Attempt)}: {ExclusionText(ex)}").ToList()));
        }
        b.Add(new Para("How each completed attempt ended, per variant. `ok` is a normal finish; anything else is the harness stopping the attempt (a limit, an error from the provider), which the checks then grade like any other."));
        var reasons = ExitReasons(stats).ToList();
        b.Add(new Table(["Variant", .. reasons.Select(r => $"`{r}`")],
            variants.Select(a => (List<Td>)[Id(a), .. reasons.Select(r => T(ExitTd(stats.Variants[a], rows, a, r)))]).ToList()));

        // 7. Check pass rates
        b.Add(new Heading("Check pass rates"));
        b.Add(new Para("How often each check held, per variant, over the attempts it applies to. Headline and guardrail checks are over counted attempts; a validity check is over every graded attempt, because it says how many were counted. An attempt the check could not decide is out of its rate and counted beside it."));
        b.Add(new Table(["Check", "What it tests", "Role", .. variants.Select(a => $"`{a}`")],
            CheckNames(stats).Select(c => (List<Td>)[Id(c), T(CheckText(stats, c)), T(Role(stats, c)), .. variants.Select(a => T(CheckRateText(stats.Variants[a].Checks.GetValueOrDefault(c))))]).ToList()));

        // 8. Cost and effort
        var unit = DurationUnit(stats);
        b.Add(new Heading("Cost and effort"));
        var costed = variants.Any(a => stats.Variants[a].CostUsd is not null);
        b.Add(new Para($"Per completed attempt. Duration is wall time in {(unit == "min" ? "minutes" : "seconds")}, hooks included; tokens are the attempt's own count{(variants.Any(a => stats.Variants[a].TokensEstimated) ? ", and some counts are the harness's estimate rather than the provider's" : "")}. "
            + (costed ? "Cost is what the agent reported for its attempts (Claude Code its own count, Codex as Harbor priced its tokens); a variant whose harness reports none shows none." : "Cost is not shown: no variant's harness reports one.")));
        b.Add(new Table(["Variant", $"Duration ({unit}): median", "p90", "mean", "range", "Tokens: median", "p90", "mean", "range", .. costed ? ["Cost: median", "total"] : Array.Empty<string>()],
            variants.Select(a => { var s = stats.Variants[a]; return (List<Td>)[Id(a), .. SummaryText(s.DurationMs, v => Duration(v, unit)).Select(T), .. SummaryText(s.TokensTotal, Tokens).Select(T),
                .. costed ? [T(s.CostUsd is { } c ? Dollars(c.Median) : ""), T(s.CostTotalUsd is { } total ? Dollars(total) : "")] : Array.Empty<Td>()]; }).ToList()));

        // 9. Every attempt
        b.Add(new Heading("Every attempt"));
        b.Add(new Para("Every attempt, failures first. *Reason* is the first check that did not hold and what it saw, or why the attempt never completed."));
        b.Add(new Table(["Variant", "Task", "Attempt", "Result", "Ended", $"Duration ({unit})", "Tokens", "Reason"],
            Ordered(rows).Select(r => (List<Td>)[
                new Td($"`{r.Variant}`"), Id(r.Task), T($"{r.Attempt}"),
                new Td($"{Glyph(r)} {Outcome(r)}"),
                T(r.ExitReason is null ? "" : $"`{r.ExitReason}`"), T(r.DurationMs is { } dm ? Duration(dm, unit) : ""), T(r.Usage?.Total is { } t ? Tokens(t) : ""), T(Paths(Reason(r)))]).ToList()));

        // 10. Reproducibility and method
        b.Add(new Heading("Reproducibility and method"));
        b.Add(new Para("What produced this report, so it can be run again, and how the numbers were computed."));
        b.Add(new Pairs(ReproFacts(stats, latest, judges).ToList()));
        b.Add(new Para(stats.Methods));
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
                rows.Add(new CardVariant(a, false, stats.Variants[a].Pass, new CardDiff(c.DiffPoints, c.Ci95[0], c.Ci95[1], TaskWord(verdict), confirmed)));
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
            foot.Add(new Para(a));
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
    static string TaskWord(string verdict) => verdict switch { "regressed" => "worse", "improved" => "better", _ => "same" };
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
                    new(Declared(k.Level))]).ToList()));
        }

        body.Add(new Label("Results"));
        body.Add(new Table(["Variant", "Attempts", "Score", "Passed / decided"],
            variants.Select(a =>
            {
                var attempts = rows.Where(r => r.Variant == a && r.Task == task).OrderBy(r => r.Attempt).ToList();
                var score = stats.Variants[a].Tasks.GetValueOrDefault(task);
                return (List<Td>)[
                    new(a),
                    new(string.Join(" ", attempts.Select(Glyph))),
                    new(score?.Score is { } s ? $"**{Pct(s)}**" : "—"),
                    new(score is { Decided: > 0 } ? $"{score.Passed} of {score.Decided}" : "—"),
                ];
            }).ToList()));

        var missed = variants.SelectMany(a => rows.Where(r => r.Variant == a && r.Task == task && !(r.Analysed && r.Pass == true)).OrderBy(r => r.Attempt)).ToList();
        if (missed.Count > 0)
            body.Add(new Bullets(missed.Select(r => $"[{r.Variant} attempt {r.Attempt}](#attempt-{r.Id}) {Outcome(r)}: {Paths(FirstReason(r))}").ToList()));
        return new TaskCard(task, TaskText(stats, task), facts, body);
    }

    /// <summary>A variant against the reference in the card's words: the direction, and whether the interval confirms it.</summary>
    static Td ComparisonVerdict(Comparison c)
    {
        var verdict = Direction(c.DiffPoints);
        if (verdict == "held") return new("same");
        var word = TaskWord(verdict);
        var confirmed = Confirmation(c.Verdict != "no-detectable-difference");
        return new($"**{word}** {confirmed}");
    }

    static Td RoleTd(StatsFile stats, string check) => new(Role(stats, check));

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

    static string VariantField(LogEntry latest, string variant, string field, string missing = "?") =>
        latest.Variants.FirstOrDefault(a => a.Id == variant)?.Def[field]?.GetValue<string>() ?? missing;

    /// <summary>nb's harness on an nb variant; a vendor agent at its pin, which is its harness. Its provider is the machine's route, which the log entry does not record.</summary>
    static string Harness(LogEntry latest, string variant) =>
        VariantField(latest, variant, "harness", "") is { Length: > 0 } h ? h : $"{VariantField(latest, variant, "agent")} {VariantField(latest, variant, "agent_version", "")}".TrimEnd();

    /// <summary>Monospace only for what is typed or opened: the command, paths, endpoints.</summary>
    static IEnumerable<(string, string)> ReproFacts(StatsFile stats, LogEntry latest, List<JudgeUse>? judges)
    {
        if (stats.BuiltFrom is { } built)
        {
            yield return ("built from", $"{Count(built.Current, "current attempt")}, made by {Count(built.Log.Count, "run")} logged as {string.Join(", ", built.Log.Select(id => $"``{Layout.LogDir}/{id}``"))}");
            if (built.Narrowed is { } narrowed) yield return ("narrowed to", $"``{narrowed}``");
            if (built.Stale > 0)
                yield return ("stale", $"{Count(built.Stale, "attempt")} not counted, made under a variant or task that has changed: "
                    + string.Join("; ", (built.StaleAttempts ?? []).GroupBy(x => x.Why).Select(g => $"{g.Key} ({g.Count()})")));
            foreach (var (variant, hash) in built.Variants) yield return ($"variant {variant}", hash);
            foreach (var (task, hash) in built.Tasks) yield return ($"task {task}", hash);
        }
        else yield return ("log entry", latest.Id);
        yield return ("proctor", latest.Versions.GetValueOrDefault("proctor", "unknown"));
        yield return ("nb", $"{latest.Versions.GetValueOrDefault("nb", "unknown")} at ``{latest.Nb.Path}``");
        yield return ("exam hash", latest.ExamHash);
        foreach (var (variant, bundle) in latest.Bundles ?? []) yield return ($"bundle {variant}", $"``{bundle.Source}`` {bundle.Hash}");
        foreach (var j in judges ?? [])
            yield return ($"judge {j.Judge}", $"{j.Kind}{(j.Model is null ? "" : " " + j.Model)} at ``{j.Endpoint}``; graded {string.Join(", ", j.Checks)}");
        yield return ("repository", latest.Repo is null ? "not a git repository" : $"{latest.Repo.Commit}{(latest.Repo.Dirty ? " (dirty)" : "")}");
        yield return ("command", $"``{latest.CommandLine}``");
        yield return ("created", latest.Created);
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
