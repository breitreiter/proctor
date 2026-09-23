using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Proctor;

// report.html and summary.md: two renderings of one list of blocks built from stats.json, results.jsonl and
// experiment.json. Neither computes a statistic. The structure is project/plans/report-structure.md: define a
// term before using it, one sentence under every heading saying what the section is for, the answer in the
// first six sections and the evidence after. Some readers open this daily and some once a quarter; for the
// latter every time is the first time, which is why the explainers are here, muted, so the findings stand out.

static class Report
{
    const string Pass = "●", Fail = "○", Undecided = "?", NotCounted = "×";

    // ---- The blocks ----

    abstract record Block;
    record Title(string Suite, string Date, string Time) : Block;                  // the page's h1 and when it ran
    record Heading(string Text, string Id, bool Flagged = false) : Block;          // a section; Flagged marks it in the rail
    record Para(string Text, string? Class = null) : Block;                        // "explain": skippable once known; "caution": a warning inside the card
    record Warning(string Text) : Block;
    record Bullets(List<string> Items, string? Class = null) : Block;
    record Pairs(List<(string Key, string Value)> Items, string? Class = null) : Block;  // "roles": the keys are check roles
    record Table(List<string> Header, List<List<Cell>> Rows, HashSet<int> Numeric, string? Class = null) : Block;
    record Label(string Text) : Block;                                             // a small heading inside a task card
    record Card(string Title, List<CardArm> Arms, List<Block> Foot) : Block;       // the verdict
    record CardArm(string Arm, bool Reference, string Word, string? Tone, bool Confirmed, string Points, string Interval, string Drivers);
    record TaskCard(string Name, string Prompt, List<string> Facts, List<Block> Body) : Block;
    /// <summary>A table cell: the text both renderings show, and the HTML to use instead when the page can carry a link or a hover.</summary>
    record Cell(string Text, string? Html = null, string? Anchor = null);

    // Inline forms both renderings share: `id`, ``code``, **bold**, *italic* and [text](#anchor). Markdown writes
    // ``code`` as `code` (it has one code style, and doubled backticks read as noise raw), keeps the rest as they
    // are and drops the link to its text, since summary.md has no anchors.

    public static string Html(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges = null) => RenderHtml(Blocks(stats, rows, exp, judges), stats, exp);
    public static string Markdown(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges = null) => RenderMarkdown(Blocks(stats, rows, exp, judges));

    static List<Block> Blocks(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges)
    {
        var arms = stats.Arms.Keys.ToList();
        var first = arms[0];
        var d = stats.Descriptions;
        var bl = stats.Baseline;
        var b = new List<Block>();
        Cell T(string s) => new(s);
        Cell Id(string s) => new($"`{s}`");

        // 1. Summary
        var created = When(exp.Created);
        b.Add(new Title(stats.Suite, created.ToString("dddd, d MMMM yyyy"), created.ToString("HH:mm")));
        if (d.Suite is { } about) b.Add(new Para(about));
        if (VerdictCard(stats) is { } card) b.Add(card);
        var samplesPerTask = arms.Select(a => Samples(exp, a)).Distinct().ToList();
        var runsPerArm = samplesPerTask.Count == 1 ? Times(samplesPerTask[0]) : string.Join(" or ", samplesPerTask.Select(Times));
        b.Add(new Pairs(
        [
            ("Host", exp.Host),
            ("Arms", $"{arms.Count}: " + string.Join(", ", arms.Select(a => a == first && arms.Count > 1 ? $"`{a}` (the reference)" : $"`{a}`"))),
            ("Tasks", $"{stats.NTasks}, each run {runsPerArm} per arm"),
            ("Runs", $"{stats.Arms.Values.Sum(a => a.Planned)} planned, {stats.Arms.Values.Sum(a => a.Analysed)} counted"),
            .. arms.Count == 1 && bl is null ? [("Result", stats.Arms[first].Pass is { } p ? $"`{first}` passed {Pct(p.Rate)} of its runs" : $"`{first}` has no decided run")] : Array.Empty<(string, string)>(),
        ]));
        foreach (var u in stats.UndecidedChecks)
            b.Add(new Warning($"The check `{u.Check}` ({CheckText(stats, u.Check)}) could not decide {u.Runs} of the {u.Of} runs it applies to. Its pass rates below are over the {u.Of - u.Runs} runs it decided, and the check needs rewriting before this experiment is conclusive."));

        // 2. Arms
        b.Add(new Heading("Arms", "arms"));
        b.Add(new Para("An arm is one configuration under test: a harness, a provider and a model, run over every task." + (arms.Count > 1 ? " The first arm is the reference the others are compared with." : ""), "explain"));
        var bundles = exp.Bundles is { Count: > 0 };
        b.Add(new Table(["Arm", "What it is", "Harness", "Provider", "Model", "Runs per task", .. bundles ? ["Bundle"] : Array.Empty<string>()],
            arms.Select(a => (List<Cell>)[Id(a), T(d.Arms.GetValueOrDefault(a, "")), T(ArmField(exp, a, "harness")), T(ArmField(exp, a, "provider")), T(ArmField(exp, a, "model")), T(Samples(exp, a).ToString()),
                .. bundles ? [T(exp.BundleOf(a) is { } bu ? $"``{bu.Source}``" : "")] : Array.Empty<Cell>()]).ToList(),
            [5]));

        // 3. Tasks: what each asks, what it is checked for, and how every arm did on it
        b.Add(new Heading("Tasks", "tasks"));
        b.Add(new Para($"A task is one input and one desired outcome, given to every arm: a prompt against a fixture repository, with the checks that say whether the outcome was reached. Each task is run {(samplesPerTask.Count == 1 ? Times(samplesPerTask[0]) : "several times")} per arm, so a score is not one lucky or unlucky attempt.", "explain"));
        b.Add(new Para("A check is one yes-or-no test over a finished run. Every task carries the checks the suite declares; a fixture or the task itself can add more. A check's role decides what it does to a run:", "explain"));
        b.Add(new Pairs(
        [
            ("headline", "Together, the headline checks decide whether a run passed."),
            ("validity", "Decides whether a run counts at all: a run that fails one is left out of every rate, not counted as a failure."),
            ("guardrail", "Reported, not part of the pass."),
        ], "roles"));
        b.Add(new Para("A check that cannot decide a run leaves that run out of its rate on both sides.", "explain"));
        b.Add(new Para($"Each task's results show one mark per run: {Pass} passed, {Fail} failed, {Undecided} undecided, {NotCounted} not counted. Hover a mark for the reason; click it for the run. A task scores its mean over its counted, decided runs"
            + (bl is null ? "." : bl.TolerancePoints == 0 ? ", and is called better or worse than its baseline on any difference." : $", and is called better or worse than its baseline beyond {bl.TolerancePoints} points either way."), "explain"));
        foreach (var task in stats.Tasks) b.Add(TaskBlock(stats, rows, task, runsPerArm));

        // 4. Results: only the numbers across all tasks
        b.Add(new Heading("Results", "results"));
        b.Add(new Para($"Pass rate is the share of counted runs in which every headline check held, averaged task by task so that one task with many runs does not outweigh another. The 95% interval says how far the true rate could plausibly sit from the measured one; with {stats.NTasks} task{(stats.NTasks == 1 ? "" : "s")} it is {(stats.NTasks < 10 ? "wide" : "what it is")}.", "explain"));
        b.Add(new Table(["Arm", "Pass rate", "95% interval", "Passed / decided runs"],
            arms.Select(a => (List<Cell>)[Id(a), T(RatePct(stats.Arms[a].Pass)), T(IntervalText(stats.Arms[a].Pass)), T(stats.Arms[a].Pass is { } p ? $"{p.KCells} / {p.NCells}" : "—")]).ToList(),
            [1, 2, 3]));
        foreach (var c in stats.Comparisons) b.Add(new Para(ComparisonParagraph(c)));
        b.Add(new Para(stats.Mde.Sentence));
        if (bl is not null)
        {
            b.Add(new Para($"**Against the baseline.** The baseline is the score pinned for each task on {bl.Set[..10]}, scores {bl.Scores}. The verdict compares each arm's score with it and calls anything more than {bl.TolerancePoints} point{(bl.TolerancePoints == 1 ? "" : "s")} either way a change; it is confirmed only when the whole 95% interval agrees, so a small number of tasks cannot hide behind the verdict.", "explain"));
            b.Add(new Table(["Arm", "Difference from baseline", "95% interval", "Tasks better / worse / same", "Verdict"],
                arms.Select(a => bl.Arms.TryGetValue(a, out var g)
                    ? (List<Cell>)[Id(a), T(SignedPoints(g.DiffPoints)), T($"{Signed(g.Ci95[0])} to {Signed(g.Ci95[1])}"), T($"{g.Won} / {g.Lost} / {g.Tied}"),
                        new Cell($"**{ArmWord(g.Verdict)}** {Confirmation(g.Confirmed)}", $"<b class=\"{ToneClass(ArmTone(g.Verdict))}\">{ArmWord(g.Verdict)}</b> {Confirmation(g.Confirmed)}")]
                    : [Id(a), T("—"), T("—"), T("—"), T("no shared tasks")]).ToList(),
                [1, 2, 3]));
        }
        b.Add(new Para($"How each task scored{(bl is null ? "" : " against its baseline")}, and why runs failed, is shown with the task under [Tasks](#tasks).", "explain"));

        // 5. Failures
        b.Add(new Heading("Failures", "failures"));
        b.Add(new Para("For each arm, the checks that failed in at least one counted run, most frequent first, with the tasks they failed on. A check that could not decide a run is listed apart: that is the check's weakness, not the arm's.", "explain"));
        foreach (var a in arms)
        {
            var s = stats.Arms[a];
            var who = d.Arms.TryGetValue(a, out var ad) ? $"**`{a}`** ({ad})" : $"**`{a}`**";
            if (s.Analysed == 0) { b.Add(new Para($"{who}: no run was counted.")); continue; }
            b.Add(new Para(s.Failures.Count == 0 ? $"{who} failed no check in any of its {s.Analysed} counted run{(s.Analysed == 1 ? "" : "s")}." : $"{who} failed {s.Failures.Count} check{(s.Failures.Count == 1 ? "" : "s")}:"));
            if (s.Failures.Count > 0)
                b.Add(new Bullets(s.Failures.Select(f => FailureText(stats, f)).ToList()));
        }
        var undecided = arms.SelectMany(a => stats.Arms[a].Undecided).ToList();
        if (undecided.Count > 0 && undecided.Count <= 5)
        {
            b.Add(new Para($"{undecided.Count} run{(undecided.Count == 1 ? " was" : "s were")} undecided, and can be reviewed top to bottom:"));
            b.Add(new Bullets(undecided.Select(u => $"{RunLink(rows, u.Cell)}: {u.Reason}").ToList()));
        }
        else if (undecided.Count > 5)
            b.Add(new Para($"{undecided.Count} runs were undecided and are out of the pass rates; see the warning above. They are marked {Undecided} in the tables below."));

        // 6. What ran
        var unequal = UnequalLoss(stats);
        b.Add(new Heading("What ran", "what-ran", Flagged: unequal is not null));
        b.Add(new Para("Every run planned by the matrix, and how far it got. *Attempted* runs started; *completed* runs ended with a transcript from nb; *graded* runs have their checks; *counted* runs are graded and passed every validity check; *decided* runs are counted runs whose headline checks all reached a verdict. Pass rates are over decided runs only.", "explain"));
        b.Add(new Table(["Arm", "Planned", "Attempted", "Completed", "Graded", "Counted", "Decided"],
            arms.Select(a => { var s = stats.Arms[a]; return (List<Cell>)[Id(a), T($"{s.Planned}"), T($"{s.Attempted}"), T($"{s.Completed}"), T($"{s.Graded}"), T($"{s.Analysed}"), T($"{s.Decided}")]; }).ToList(),
            [1, 2, 3, 4, 5, 6]));
        if (unequal is not null) b.Add(new Warning(unequal));
        var excluded = arms.SelectMany(a => stats.Arms[a].Excluded).ToList();
        if (excluded.Count == 0) b.Add(new Para("No run was left out."));
        else
        {
            b.Add(new Para("Runs left out, and why:"));
            b.Add(new Bullets(excluded.Select(ex => $"{RunLink(rows, ex.Cell)}: {ExclusionText(ex)}").ToList()));
        }
        b.Add(new Para("How nb ended each completed run, per arm. `ok` is a normal finish; anything else is nb stopping the run, which the checks then grade like any other.", "explain"));
        var reasons = ExitReasons(stats).ToList();
        b.Add(new Table(["Arm", .. reasons.Select(r => $"`{r}`")],
            arms.Select(a => (List<Cell>)[Id(a), .. reasons.Select(r => T(ExitCell(stats.Arms[a], rows, a, r)))]).ToList(),
            Enumerable.Range(1, reasons.Count).ToHashSet()));

        // 7. Check pass rates
        b.Add(new Heading("Check pass rates", "check-rates"));
        b.Add(new Para("How often each check held, per arm, over the runs it applies to. Headline and guardrail checks are over counted runs; a validity check is over every graded run, because it says how many were counted. A run the check could not decide is out of its rate and counted beside it.", "explain"));
        b.Add(new Table(["Check", "What it tests", "Role", .. arms.Select(a => $"`{a}`")],
            CheckNames(stats).Select(c => (List<Cell>)[Id(c), T(CheckText(stats, c)), T(Role(stats, c)), .. arms.Select(a => T(CheckRateText(stats.Arms[a].Checks.GetValueOrDefault(c))))]).ToList(),
            Enumerable.Range(3, arms.Count).ToHashSet()));

        // 8. Cost and effort
        var unit = DurationUnit(stats);
        b.Add(new Heading("Cost and effort", "cost"));
        b.Add(new Para($"Per completed run. Duration is wall time in {(unit == "min" ? "minutes" : "seconds")}, hooks included; tokens are what nb reported{(arms.Any(a => stats.Arms[a].TokensEstimated) ? ", and some counts are nb's estimate rather than the provider's" : "")}. Cost is not shown: nb does not report it.", "explain"));
        b.Add(new Table(["Arm", $"Duration ({unit}): median", "p90", "mean", "range", "Tokens: median", "p90", "mean", "range"],
            arms.Select(a => { var s = stats.Arms[a]; return (List<Cell>)[Id(a), .. SummaryText(s.DurationMs, v => Duration(v, unit)).Select(T), .. SummaryText(s.TokensTotal, Tokens).Select(T)]; }).ToList(),
            Enumerable.Range(1, 8).ToHashSet()));

        // 9. Every run
        b.Add(new Heading("Every run", "every-run"));
        b.Add(new Para("Every run, failures first. *Reason* is the first check that did not hold and what it saw, or why the run never completed.", "explain"));
        b.Add(new Table(["Arm", "Task", "Run", "Result", "nb ended", $"Duration ({unit})", "Tokens", "Reason"],
            Ordered(rows).Select(r => (List<Cell>)[
                new Cell($"`{r.Arm}`", Anchor: $"run-{r.RunId}"), Id(r.Task), T($"{r.Sample}"),
                new Cell($"{Glyph(r)} {Outcome(r)}", $"<span class=\"{GlyphClass(r)}\" title=\"{E(r.RunId)}\">{Glyph(r)} {E(Outcome(r))}</span>"),
                T(r.ExitReason is null ? "" : $"`{r.ExitReason}`"), T(r.DurationMs is { } dm ? Duration(dm, unit) : ""), T(r.Usage?.Total is { } t ? Tokens(t) : ""), T(Paths(Reason(r)))]).ToList(),
            [2, 5, 6]));

        // 10. Reproducibility and method
        b.Add(new Heading("Reproducibility and method", "method"));
        b.Add(new Para("What produced this report, so it can be run again, and how the numbers were computed.", "explain"));
        b.Add(new Pairs(ReproFacts(exp, judges).ToList()));
        b.Add(new Para(stats.Methods, "explain"));
        return b;
    }

    // ---- The verdict card ----

    /// <summary>
    /// One block per arm: direction, whether the interval confirms it, and the tasks that drove it. Against the
    /// baseline when there is one, every arm; otherwise every arm against the reference. None with one arm and no baseline.
    /// </summary>
    static Card? VerdictCard(StatsFile stats)
    {
        var arms = stats.Arms.Keys.ToList();
        var first = arms[0];
        var foot = new List<Block>();
        List<CardArm> rows;
        string title;
        if (stats.Baseline is { } bl)
        {
            title = $"Against the baseline pinned {ShortDate(bl.Set)}";
            rows = arms.Select(a => bl.Arms.TryGetValue(a, out var g)
                ? new CardArm(a, a == first && arms.Count > 1, ArmWord(g.Verdict), ArmTone(g.Verdict), g.Confirmed, SignedPoints(g.DiffPoints), $"{Signed(g.Ci95[0])} to {Signed(g.Ci95[1])}",
                    Drivers(g.Tasks.Select(t => (t.Key, t.Value.DiffPoints, t.Value.Verdict)), g.Verdict))
                : new CardArm(a, a == first && arms.Count > 1, "No shared tasks", null, true, "", "", "")).ToList();
        }
        else if (stats.Comparisons.Count > 0)
        {
            title = $"Against the reference arm, {first}";
            rows = stats.Comparisons.Select(c =>
            {
                var verdict = GuardVerdict(c.DiffPoints);
                return new CardArm(c.Arm, false, ArmWord(verdict), ArmTone(verdict), verdict != "held" && c.Verdict != "no-detectable-difference", SignedPoints(c.DiffPoints),
                    $"{Signed(c.Ci95[0])} to {Signed(c.Ci95[1])}", Drivers(c.Tasks.Select(t => (t.Key, t.Value, GuardVerdict(t.Value))), verdict));
            }).ToList();
        }
        else return null;

        var between = stats.Baseline is null ? [] : stats.Comparisons;
        if (rows.Any(r => !r.Confirmed))
        {
            var m = stats.Mde;
            var why = m.NPairs == 0 ? m.Sentence
                : $"with {m.NPairs} task{(m.NPairs == 1 ? "" : "s")} this run can only confirm a change of about {m.Points} points. Confirming a 10-point change takes about {m.TasksFor10Points} tasks.";
            foot.Add(new Para($"**Why unconfirmed:** {why}" + string.Concat(between.Select(c => " " + Between(c, sameReason: true)))));
        }
        else if (between.Count > 0)
            foot.Add(new Para(string.Join(" ", between.Select(c => Between(c, sameReason: false)))));
        foreach (var a in Careful(stats))
            foot.Add(new Para(a, "caution"));
        return new Card(title, rows, foot);
    }

    /// <summary>The tasks behind an arm's verdict: the ones that moved, the arm's own direction first, then the ones that held.</summary>
    static string Drivers(IEnumerable<(string Task, int Diff, string Verdict)> tasks, string armVerdict)
    {
        var list = tasks.ToList();
        string Moved(string verdict) => Join(list.Where(t => t.Verdict == verdict).Select(t => $"{TaskLink(t.Task)} ({Signed(t.Diff)})"));
        var lower = list.Any(t => t.Verdict == "regressed") ? $"lower on {Moved("regressed")}" : null;
        var higher = list.Any(t => t.Verdict == "improved") ? $"higher on {Moved("improved")}" : null;
        var same = list.Any(t => t.Verdict == "held") ? $"same on {Join(list.Where(t => t.Verdict == "held").Select(t => TaskLink(t.Task)))}" : null;
        var parts = (armVerdict == "improved" ? [higher, lower, same] : new[] { lower, higher, same }).OfType<string>().ToList();
        if (parts.Count == 0) return "";
        var text = string.Join("; ", parts) + ".";
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>A between-arms difference in the card's foot, which says why the verdicts above cannot be confirmed.</summary>
    static string Between(Comparison c, bool sameReason)
    {
        if (c.DiffPoints == 0) return $"**{c.Arm}** and **{c.Vs}** passed the same share of their runs.";
        var moved = $"**{c.Arm}** passing {Math.Abs(c.DiffPoints)} points {(c.DiffPoints > 0 ? "more" : "fewer")} than **{c.Vs}**";
        return c.Verdict == "no-detectable-difference"
            ? $"{(sameReason ? "For the same reason, " : "")}{moved} is not a detectable difference."
            : $"{char.ToUpperInvariant(moved[0])}{moved[1..]} is a detectable difference.";
    }

    /// <summary>An arm that lost more of its runs than another rests on fewer of them; the card says so and points at What ran.</summary>
    static IEnumerable<string> Careful(StatsFile stats)
    {
        if (UnequalLoss(stats) is null) yield break;
        var lost = stats.Arms.ToDictionary(a => a.Key, a => a.Value.Planned - a.Value.Decided);
        var least = lost.Values.Min();
        foreach (var (arm, n) in lost.Where(l => l.Value > least))
        {
            var others = Join(lost.Where(o => o.Key != arm).Select(o => $"{o.Key} lost {(o.Value == 0 ? "none" : o.Value.ToString())}"));
            yield return $"**Read {arm} with care:** it lost {n} of its {stats.Arms[arm].Planned} runs and {others}, so {arm}'s rate rests on fewer runs. See [What ran](#what-ran).";
        }
    }

    static string GuardVerdict(int diffPoints) => diffPoints < 0 ? "regressed" : diffPoints > 0 ? "improved" : "held";
    static string ArmWord(string verdict) => verdict switch { "regressed" => "Worse", "improved" => "Better", _ => "Unchanged" };
    static string? ArmTone(string verdict) => verdict switch { "regressed" => "bad", "improved" => "good", _ => null };
    static string TaskWord(string verdict) => verdict switch { "regressed" => "worse", "improved" => "better", _ => "same" };
    static string ToneClass(string? tone) => tone is null ? "vsame" : $"v{tone}";
    static string Confirmation(bool confirmed) => confirmed ? "(confirmed)" : "(unconfirmed)";

    // ---- A task card ----

    static TaskCard TaskBlock(StatsFile stats, List<ResultRow> rows, string task, string runsPerArm)
    {
        var arms = stats.Arms.Keys.ToList();
        var bl = stats.Baseline;
        var checks = (stats.TaskDetails?.GetValueOrDefault(task)?.Checks ?? [])
            .OrderBy(k => k.Level switch { "task" => 0, "fixture" => 1, _ => 2 }).ToList();
        var fromSuite = checks.Count(k => k.Level == "suite");
        var fromFixture = checks.Count(k => k.Level == "fixture");
        var own = checks.Count(k => k.Level == "task");
        var where = new List<string>();
        if (fromSuite > 0) where.Add($"{fromSuite} from the suite");
        if (fromFixture > 0) where.Add($"{fromFixture} from its fixture");
        where.Add(own == 0 ? "none of its own" : $"{own} of its own");

        var facts = new List<string>();
        if (FixtureOf(stats, task) is { } fixture) facts.Add($"Fixture **{fixture}**");
        facts.Add($"Run **{runsPerArm}** per arm");
        facts.Add($"**{checks.Count} check{(checks.Count == 1 ? "" : "s")}**: {string.Join(", ", where)}");

        var body = new List<Block> { new Label("Checks") };
        body.Add(new Table(["Check", "What it tests", "Role", "Declared by"],
            checks.Select(k => (List<Cell>)[
                new(k.Level == "suite" ? k.Name : $"**{k.Name}**"), new(k.Description),
                new(Role(stats, k.Name), $"<span class=\"role role-{Role(stats, k.Name)}\">{Role(stats, k.Name)}</span>"),
                new(Declared(k.Level), $"<span class=\"src\">{Declared(k.Level)}</span>")]).ToList(),
            [], "checks"));

        var baseline = bl?.Arms.Values.Select(a => a.Tasks.GetValueOrDefault(task)).FirstOrDefault(x => x is not null)?.Baseline;
        body.Add(new Label("Results"));
        body.Add(new Table(["Arm", "Runs", "Score", "Passed / decided", .. bl is null ? Array.Empty<string>() : [baseline is { } p ? $"vs baseline ({Pct(p)})" : "vs baseline", "Verdict"]],
            arms.Select(a =>
            {
                var runs = rows.Where(r => r.Arm == a && r.Task == task).OrderBy(r => r.Sample).ToList();
                var score = stats.Arms[a].Tasks.GetValueOrDefault(task);
                var against = bl?.Arms.GetValueOrDefault(a)?.Tasks.GetValueOrDefault(task);
                return (List<Cell>)[
                    new(a),
                    new(string.Join(" ", runs.Select(Glyph)), "<span class=\"marks\">" + string.Join("", runs.Select(r => $"<a href=\"#run-{E(r.RunId)}\" class=\"{GlyphClass(r)}\" title=\"{E(GlyphTitle(r))}\">{Glyph(r)}</a>")) + "</span>"),
                    new(score?.Score is { } s ? $"**{Pct(s)}**" : "—"),
                    new(score is { Decided: > 0 } ? $"{score.Passed} of {score.Decided}" : "—"),
                    .. bl is null ? Array.Empty<Cell>() : against is null ? [new("—"), new("—")] :
                    [
                        new(SignedPoints(against.DiffPoints)),
                        new(TaskWord(against.Verdict), $"<b class=\"{ToneClass(ArmTone(against.Verdict))}\">{TaskWord(against.Verdict)}</b>"),
                    ],
                ];
            }).ToList(),
            [2, 3, 4], "results"));

        var missed = arms.SelectMany(a => rows.Where(r => r.Arm == a && r.Task == task && !(r.Analysed && r.Pass == true)).OrderBy(r => r.Sample)).ToList();
        if (missed.Count > 0)
            body.Add(new Bullets(missed.Select(r => $"[{r.Arm} run {r.Sample}](#run-{r.RunId}) {Outcome(r)}: {Paths(FirstReason(r))}").ToList(), "why"));
        return new TaskCard(task, TaskText(stats, task), facts, body);
    }

    static string Declared(string level) => level switch { "task" => "this task", "fixture" => "its fixture", _ => "suite" };

    // ---- Sentences ----

    static string ComparisonParagraph(Comparison c)
    {
        var sb = new StringBuilder($"**`{c.Arm}` against `{c.Vs}`.** ");
        sb.Append(c.DiffPoints switch
        {
            > 0 => $"`{c.Arm}` passed {c.DiffPoints} points more of its runs than `{c.Vs}`. ",
            < 0 => $"`{c.Arm}` passed {-c.DiffPoints} points fewer of its runs than `{c.Vs}`. ",
            _ => $"`{c.Arm}` and `{c.Vs}` passed the same share of their runs. ",
        });
        sb.Append($"The 95% interval on that difference runs from {Signed(c.Ci95[0])} to {Signed(c.Ci95[1])} points, so ");
        sb.Append(c.Verdict switch
        {
            "better" => $"`{c.Arm}` is better: the whole interval is above zero. ",
            "worse" => $"`{c.Arm}` is worse: the whole interval is below zero. ",
            _ => $"with {c.NPairs} task{(c.NPairs == 1 ? "" : "s")} the difference could be noise: no detectable difference. ",
        });
        sb.Append($"Task by task, `{c.Arm}` did better on {c.Won}, worse on {c.Lost}, the same on {c.Tied}.");
        return sb.ToString();
    }

    static string? UnequalLoss(StatsFile stats)
    {
        if (stats.Arms.Values.Select(a => a.Decided).Distinct().Count() <= 1) return null;
        return "Arms lost runs unequally: " + string.Join(", ", stats.Arms.Select(a => $"`{a.Key}` decided {a.Value.Decided} of {a.Value.Planned}")) + ". The rates are over decided runs; the runs lost are listed here.";
    }

    static string ExclusionText(Exclusion ex) => ex.Status switch
    {
        "invalid" => $"not counted, failed a validity check. {Paths(ex.Reason)}",
        "failed" => $"never completed. {Paths(ex.Reason)}",
        _ => $"{ex.Status}. {Paths(ex.Reason)}".TrimEnd('.', ' '),
    };

    static string ExitCell(ArmStats a, List<ResultRow> rows, string arm, string reason)
    {
        var n = a.ExitReasons.GetValueOrDefault(reason);
        if (a.Completed == 0) return "—";
        if (n == 0 || n > 3 || reason == "ok") return $"{n}";
        var which = rows.Where(r => r.Arm == arm && r.ExitReason == reason).Select(r => $"`{r.Task}` run {r.Sample}");
        return $"{n} ({string.Join(", ", which)})";
    }

    /// <summary>An exclusion's cell (arm/task/sample) as a link to its row under Every run.</summary>
    static string RunLink(List<ResultRow> rows, string cell) =>
        rows.FirstOrDefault(r => $"{r.Arm}/{r.Task}/{r.Sample}" == cell) is { } r ? $"[{cell}](#run-{r.RunId})" : $"`{cell}`";

    static string TaskLink(string task) => $"[{task}](#task-{task})";

    static string Reason(ResultRow r) =>
        r.StatusReason ?? r.Invalid ?? r.Undecided ?? (r.Reasons is null ? "" : string.Join("; ", r.Reasons.Select(k => $"{k.Key}: {k.Value}")));

    static string FirstReason(ResultRow r) =>
        r.StatusReason ?? r.Invalid ?? r.Undecided ?? (r.Reasons?.FirstOrDefault() is { Key: not null } k ? $"{k.Key}: {k.Value}" : "");

    static string Outcome(ResultRow r) =>
        r.Invalid is not null ? "not counted" : r.Undecided is not null ? "undecided" : r.Pass switch { true => "passed", false => "failed", null => r.Status == CellStatus.Completed ? "not graded" : r.Status == CellStatus.Failed ? "never completed" : r.Status };

    static string Glyph(ResultRow r) => !r.Analysed ? NotCounted : r.Undecided is not null ? Undecided : r.Pass == true ? Pass : Fail;
    static string GlyphClass(ResultRow r) => !r.Analysed ? "skip" : r.Undecided is not null ? "undecided" : r.Pass == true ? "pass" : "fail";
    static string GlyphTitle(ResultRow r) => $"run {r.Sample}: {Outcome(r)}{(Reason(r).Length > 0 ? " — " + Reason(r) : "")} ({r.RunId})";

    static IEnumerable<ResultRow> Ordered(List<ResultRow> rows) =>
        rows.OrderBy(r => !r.Analysed ? 1 : r.Undecided is not null ? 2 : r.Pass == true ? 3 : 0).ThenBy(r => r.Arm).ThenBy(r => r.Task).ThenBy(r => r.Sample);

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
    static string FailureText(StatsFile stats, CheckFailure f)
    {
        var perTask = PerTask(stats, f.Check);
        var head = perTask ? $"**`{f.Check}`** ({Role(stats, f.Check)})" : $"**{CheckText(stats, f.Check)}** (`{f.Check}`, {Role(stats, f.Check)})";
        var on = f.Tasks.OrderByDescending(c => c.Value).Select(c =>
            $"{Times(c.Value)} on {TaskLink(c.Key)}" + (perTask && TaskCheckText(stats, c.Key, f.Check) is { } text ? $" ({text})" : ""));
        return $"{head} failed in {f.Cells} of {f.Of} run{(f.Of == 1 ? "" : "s")}: {string.Join(", ", on)}.";
    }

    static string TaskText(StatsFile stats, string c) => stats.Descriptions.Tasks.GetValueOrDefault(c, c);

    static IEnumerable<string> CheckNames(StatsFile stats) =>
        stats.PassChecks.Concat(stats.ValidityChecks).Concat(stats.Arms.Values.SelectMany(a => a.Checks.Keys)).Distinct();

    static string? FixtureOf(StatsFile stats, string task) => stats.TaskDetails?.GetValueOrDefault(task)?.Fixture;

    static string Role(StatsFile stats, string check) =>
        stats.PassChecks.Contains(check) ? "headline" : stats.ValidityChecks.Contains(check) ? "validity" : "guardrail";

    static IEnumerable<string> ExitReasons(StatsFile stats) =>
        stats.Arms.Values.SelectMany(a => a.ExitReasons).GroupBy(k => k.Key).OrderByDescending(g => g.Sum(k => k.Value)).ThenBy(g => g.Key).Select(g => g.Key);

    static string ArmField(Experiment exp, string arm, string field) =>
        exp.SuiteDef["arms"]?.AsArray().FirstOrDefault(a => a?["id"]?.GetValue<string>() == arm)?[field]?.GetValue<string>() ?? "?";

    static int Samples(Experiment exp, string arm) =>
        exp.SuiteDef["arms"]?.AsArray().FirstOrDefault(a => a?["id"]?.GetValue<string>() == arm)?["samples"]?.GetValue<int>() ?? 1;

    /// <summary>Monospace only for what is typed or opened: the command, paths, endpoints.</summary>
    static IEnumerable<(string, string)> ReproFacts(Experiment exp, List<JudgeUse>? judges)
    {
        yield return ("run id", exp.Id);
        yield return ("proctor", exp.Versions.GetValueOrDefault("proctor", "unknown"));
        yield return ("nb", $"{exp.Versions.GetValueOrDefault("nb", "unknown")} at ``{exp.Nb.Path}``");
        yield return ("suite hash", exp.SuiteHash);
        foreach (var (arm, bundle) in exp.Bundles ?? []) yield return ($"bundle {arm}", $"``{bundle.Source}`` {bundle.Hash}");
        foreach (var j in judges ?? [])
            yield return ($"judge {j.Judge}", $"{j.Kind}{(j.Model is null ? "" : " " + j.Model)} at ``{j.Endpoint}``; graded {string.Join(", ", j.Checks)}");
        yield return ("repository", exp.Repo is null ? "not a git repository" : $"{exp.Repo.Commit}{(exp.Repo.Dirty ? " (dirty)" : "")}");
        yield return ("command", $"``{exp.CommandLine}``");
        yield return ("created", exp.Created);
    }

    // ---- Numbers and dates as text (formatting only) ----

    static string RatePct(RateJson? r) => r is null ? "—" : Pct(r.Rate);
    static string IntervalText(RateJson? r) => r is null ? "—" : $"{Points(r.Ci95.Lo)}% to {Points(r.Ci95.Hi)}%";

    static string CheckRateText(RateJson? r)
    {
        if (r is null) return "—";
        var extra = new[] { r.NeedsJudge is > 0 ? $"{r.NeedsJudge} undecided" : null, r.Errors is > 0 ? $"{r.Errors} error" : null }.Where(s => s is not null);
        return $"{Pct(r.Rate)} ({r.KCells}/{r.NCells})" + (extra.Any() ? $" · {string.Join(", ", extra)}" : "");
    }

    static string DurationUnit(StatsFile stats) =>
        stats.Arms.Values.Any(a => a.DurationMs is { Median: >= 120_000 }) ? "min" : "s";

    static string Duration(double ms, string unit) => unit == "min" ? $"{ms / 60_000:F1}" : $"{ms / 1000:F1}";
    static string Tokens(double n) => ((long)n).ToString("N0");
    static string Pct(double p) => $"{Points(p)}%";
    static string Points(double p) => $"{Math.Round(100 * p, MidpointRounding.AwayFromZero):F0}";
    static string Signed(int points) => points > 0 ? $"+{points}" : points < 0 ? $"−{-points}" : "0";
    static string SignedPoints(int points) => points == 0 ? "±0 points" : $"{Signed(points)} points";
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

    static string ShortDate(string iso) => When(iso).ToString("d MMM yyyy");

    // ---- HTML ----

    static string RenderHtml(List<Block> blocks, StatsFile stats, Experiment exp)
    {
        var created = When(exp.Created);
        var sb = new StringBuilder();
        sb.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(stats.Suite)} — {created:d MMM yyyy} — proctor</title>
            <link rel="icon" href="{Logo}">
            <style>
            {Css}
            </style>
            </head>
            <body>
            <div class="page">

            """);
        sb.Append(Rail(blocks, stats.Suite, created));
        sb.Append("<main class=\"rpt\" id=\"top\">\n<section id=\"summary\">\n");
        foreach (var block in blocks) Html(sb, block);
        sb.Append("</section>\n");
        sb.Append($"""
            <footer>
            <img src="{Logo}" alt="" width="16" height="16">
            <span>Run by proctor {E(exp.Versions.GetValueOrDefault("proctor", "unknown"))} on {created:d MMM yyyy}. Icon adapted from <a href="https://game-icons.net/1x1/lorc/pointy-hat.html">Pointy hat</a> by Lorc, <a href="https://creativecommons.org/licenses/by/3.0/">CC BY 3.0</a>.</span>
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
                    <div class="kicker">Suite</div>
                    <h1>{E(t.Suite)}</h1>
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
                sb.Append($"<table{Class(t.Class)}>\n<thead><tr>");
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
                sb.Append("</tbody>\n</table>\n");
                break;
            case Card c:
                sb.Append($"<div class=\"verdict\" role=\"region\" aria-label=\"Verdict\">\n<div class=\"kicker\">{E(c.Title)}</div>\n<div class=\"verdict-arms\">\n");
                foreach (var a in c.Arms)
                {
                    sb.Append($"<div class=\"varm\">\n<div class=\"arm\">{E(a.Arm)}{(a.Reference ? " <span class=\"ref\">(reference)</span>" : "")}</div>\n");
                    sb.Append(a.Interval.Length == 0
                        ? $"<div class=\"vline\">{E(a.Word)}</div>\n"
                        : $"<div class=\"vline {ToneClass(a.Tone)}\">{E(a.Word)} <span class=\"unc\" title=\"The 95% interval runs from {E(a.Interval)} points\">{Confirmation(a.Confirmed)}</span> {E(a.Points)}</div>\n");
                    if (a.Drivers.Length > 0) sb.Append($"<p>{Inline(a.Drivers)}</p>\n");
                    sb.Append("</div>\n");
                }
                sb.Append("</div>\n");
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

    /// <summary>The sticky table of contents: logo, suite and date, every section with the tasks under Tasks, and back to top.</summary>
    static string Rail(List<Block> blocks, string suite, DateTimeOffset created)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            <nav class="toc-rail" aria-label="Report sections">
            <div class="brand"><img src="{Logo}" alt="" width="22" height="22"><span>proctor</span></div>
            <div class="rail-suite"><div class="name">{E(suite)}</div><div class="date">{created:d MMM yyyy}</div></div>
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
        .verdict-arms { display: flex; flex-direction: column; gap: 22px; padding: 4px 0; }
        .varm { display: flex; flex-direction: column; gap: 2px; }
        .varm .arm { font-weight: 600; }
        .varm .ref { font-weight: 400; color: #595959; }
        .varm .vline { font-weight: 600; font-variant-numeric: tabular-nums; }
        .varm .vsame { color: #1b1b1b; }
        .varm .unc { font-weight: 400; }
        .rpt .varm p { margin: 0; max-width: 40rem; }
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
        .rail-suite { display: flex; flex-direction: column; gap: 2px; }
        .rail-suite .name { font-size: 15px; font-weight: 650; }
        .rail-suite .date { font-size: 12.5px; color: #595959; }
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
            case Title t: sb.Append($"# {t.Suite}\n\n{t.Date}, started {t.Time} UTC\n\n"); break;
            case Heading h: sb.Append($"## {h.Text}\n\n"); break;
            case Label l: sb.Append($"**{l.Text}**\n\n"); break;
            case Para p: sb.Append($"{Md(p.Text)}\n\n"); break;
            case Warning w: sb.Append($"> **Warning.** {Md(w.Text)}\n\n"); break;
            case Bullets l: sb.Append(string.Concat(l.Items.Select(i => $"- {Md(i)}\n"))).Append('\n'); break;
            case Pairs kv:
                sb.Append("| | |\n|---|---|\n").Append(string.Concat(kv.Items.Select(i => $"| {i.Key} | {MdCell(i.Value)} |\n"))).Append('\n');
                break;
            case Table t:
                sb.Append("| ").Append(string.Join(" | ", t.Header)).Append(" |\n");
                sb.Append("|").Append(string.Join("|", t.Header.Select(_ => "---"))).Append("|\n");
                foreach (var row in t.Rows) sb.Append("| ").Append(string.Join(" | ", row.Select(c => MdCell(c.Text)))).Append(" |\n");
                sb.Append('\n');
                break;
            case Card c:
                sb.Append($"**{c.Title}**\n\n");
                foreach (var a in c.Arms)
                {
                    var who = $"**{a.Arm}**{(a.Reference ? " (reference)" : "")}";
                    sb.Append(a.Interval.Length == 0
                        ? $"- {who}: {a.Word}\n"
                        : $"- {who}: **{a.Word}** {Confirmation(a.Confirmed)} {a.Points}; 95% interval {a.Interval} points. {Md(a.Drivers)}".TrimEnd() + "\n");
                }
                sb.Append('\n');
                foreach (var f in c.Foot) Markdown(sb, f);
                break;
            case TaskCard tc:
                sb.Append($"### {tc.Name}\n\n{Md(tc.Prompt)}\n\n{Md(string.Join(" · ", tc.Facts))}\n\n");
                foreach (var inner in tc.Body) Markdown(sb, inner);
                break;
        }
    }

    static string Md(string s) => Regex.Replace(Regex.Replace(s, @"\[([^\]]+)\]\(#[^)\s]+\)", "$1"), "``([^`]+?)``", "`$1`");
    static string MdCell(string s) => Md(s).Replace("|", "\\|");
}
