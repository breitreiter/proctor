using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Proctor;

// report.html and summary.md: two renderings of one list of blocks built from stats.json, results.jsonl and
// experiment.json. Neither computes a statistic. The structure is project/plans/report-structure.md: define a
// term before using it, one sentence under every heading saying what the section is for, the answer in the
// first six sections and the evidence after. Some readers open this daily and some once a quarter; for the
// latter every time is the first time, which is why the explainers are here.

static class Report
{
    const string Pass = "●", Fail = "○", Undecided = "?", NotCounted = "×";

    // ---- The blocks ----

    abstract record Block;
    record Heading(string Text) : Block;
    record Para(string Text) : Block;                       // inline `code` and **bold** in both renderings
    record Warning(string Text) : Block;
    record Bullets(List<string> Items) : Block;
    record Pairs(List<(string Key, string Value)> Items) : Block;
    record Table(List<string> Header, List<List<Cell>> Rows, HashSet<int> Numeric) : Block;
    /// <summary>A table cell: the text both renderings show, and the HTML to use instead when the page can carry a link or a hover.</summary>
    record Cell(string Text, string? Html = null, string? Anchor = null);

    public static string Html(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges = null) => RenderHtml(Blocks(stats, rows, exp, judges), stats);
    public static string Markdown(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges = null) => RenderMarkdown(Blocks(stats, rows, exp, judges), stats);

    static List<Block> Blocks(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges)
    {
        var arms = stats.Arms.Keys.ToList();
        var first = arms[0];
        var d = stats.Descriptions;
        var b = new List<Block>();
        Cell T(string s) => new(s);
        Cell Id(string s) => new($"`{s}`");

        // 1. About
        if (d.Suite is { } about) b.Add(new Para(about));
        var samplesPerTask = arms.Select(a => Samples(exp, a)).Distinct().ToList();
        b.Add(new Pairs(
        [
            ("Suite", $"`{stats.Suite}`"),
            ("Run", $"{exp.Created[..10]} on `{exp.Host}`"),
            ("Arms", $"{arms.Count}: " + string.Join(", ", arms.Select(a => a == first && arms.Count > 1 ? $"`{a}` (the reference)" : $"`{a}`"))),
            ("Tasks", $"{stats.NTasks}, each run {(samplesPerTask.Count == 1 ? Times(samplesPerTask[0]) : string.Join(" or ", samplesPerTask.Select(Times)))} per arm"),
            ("Runs", $"{stats.Arms.Values.Sum(a => a.Planned)} planned, {stats.Arms.Values.Sum(a => a.Analysed)} counted"),
            ("Result", ResultLine(stats)),
        ]));
        foreach (var u in stats.UndecidedChecks)
            b.Add(new Warning($"The check `{u.Check}` ({CheckText(stats, u.Check)}) could not decide {u.Runs} of the {u.Of} runs it applies to. Its pass rates below are over the {u.Of - u.Runs} runs it decided, and the check needs rewriting before this experiment is conclusive."));

        // 2. Arms
        b.Add(new Heading("Arms"));
        b.Add(new Para("An arm is one configuration under test: a harness, a provider and a model, run over every task." + (arms.Count > 1 ? " The first arm is the reference the others are compared with." : "")));
        var bundles = exp.Bundles is { Count: > 0 };
        b.Add(new Table(["Arm", "What it is", "Harness", "Provider", "Model", "Runs per task", .. bundles ? ["Bundle"] : Array.Empty<string>()],
            arms.Select(a => (List<Cell>)[Id(a), T(d.Arms.GetValueOrDefault(a, "")), T(ArmField(exp, a, "harness")), T(ArmField(exp, a, "provider")), T(ArmField(exp, a, "model")), T(Samples(exp, a).ToString()),
                .. bundles ? [T(exp.BundleOf(a) is { } bu ? $"`{bu.Source}`" : "")] : Array.Empty<Cell>()]).ToList(),
            [5]));

        // 3. Tasks
        b.Add(new Heading("Tasks"));
        var shared = SharedChecks(stats);
        var anyFixture = stats.Tasks.Any(c => FixtureOf(stats, c) is not null);
        b.Add(new Para($"A task is one input and one desired outcome, given to every arm: a prompt against a fixture repository, with the checks that say whether the outcome was reached. Each task is run {(samplesPerTask.Count == 1 ? Times(samplesPerTask[0]) : "several times")} per arm, so a score is not one lucky or unlucky attempt."
            + (shared.Count > 0 ? $" Every task's runs carry the {shared.Count} check{(shared.Count == 1 ? "" : "s")} the suite declares, listed under Checks; the last column is what a task's runs are checked for beyond those, from its fixture or its own file, in that task's own words." : " The last column is what each task's runs are checked for, in that task's own words.")));
        b.Add(new Table(["Task", "What it asks", .. anyFixture ? ["Fixture"] : Array.Empty<string>(), "How it is measured"],
            stats.Tasks.Select(c => (List<Cell>)[Id(c), T(TaskText(stats, c)), .. anyFixture ? [T(FixtureOf(stats, c) is { } f ? $"`{f}`" : "—")] : Array.Empty<Cell>(), T(OwnChecks(stats, c))]).ToList(), []));

        // 4. Checks
        b.Add(new Heading("Checks"));
        b.Add(new Para("A check is one yes-or-no test over a finished run. The headline checks together decide whether a run passed. A validity check decides whether a run counts at all: a run that fails one is left out of every rate, not counted as a failure. Any other check is a guardrail: reported, not part of the pass. A check that cannot decide a run leaves that run out of its rate on both sides. A check is declared by the suite, by a fixture or by a task, and its rate is over the runs of the tasks it is on."));
        b.Add(new Table(["Check", "What it tests", "Role", "On"], CheckNames(stats).Select(c => (List<Cell>)[Id(c), T(CheckText(stats, c)), T(Role(stats, c)), T(On(stats, c))]).ToList(), []));

        // 5. Results
        b.Add(new Heading("Results"));
        b.Add(new Para($"Pass rate is the share of counted runs in which every headline check held, averaged task by task so that one task with many runs does not outweigh another. The 95% interval says how far the true rate could plausibly sit from the measured one; with {stats.NTasks} task{(stats.NTasks == 1 ? "" : "s")} it is {(stats.NTasks < 10 ? "wide" : "what it is")}."));
        b.Add(new Table(["Arm", "Pass rate", "95% interval", "Passed / decided runs"],
            arms.Select(a => (List<Cell>)[Id(a), T(RatePct(stats.Arms[a].Pass)), T(IntervalText(stats.Arms[a].Pass)), T(stats.Arms[a].Pass is { } p ? $"{p.KCells} / {p.NCells}" : "—")]).ToList(),
            [1, 2, 3]));
        foreach (var c in stats.Comparisons) b.Add(new Para(ComparisonParagraph(c)));
        b.Add(new Para(stats.Mde.Sentence));
        if (stats.Baseline is { } bl)
        {
            b.Add(new Para($"**Against the baseline.** The baseline is the score pinned for each task on {bl.Set[..10]}, scores {bl.Scores}. The verdict compares each arm's score with it and calls anything more than {bl.TolerancePoints} point{(bl.TolerancePoints == 1 ? "" : "s")} either way a change; the interval is shown so a small number of tasks cannot hide behind the verdict."));
            b.Add(new Table(["Arm", "Difference from baseline", "95% interval", "Tasks better / worse / same", "Verdict"],
                arms.Select(a => bl.Arms.TryGetValue(a, out var g)
                    ? (List<Cell>)[Id(a), T(SignedPoints(g.DiffPoints)), T($"{Signed(g.Ci95[0])} to {Signed(g.Ci95[1])}"), T($"{g.Won} / {g.Lost} / {g.Tied}"), new Cell(g.Verdict, $"<span class=\"{(g.Verdict == "regressed" ? "fail" : "pass")}\">{E(g.Verdict)}</span>")]
                    : [Id(a), T("—"), T("—"), T("—"), T("no shared tasks")]).ToList(),
                [1, 2, 3]));
            b.Add(new Table(["Task", "Baseline", .. arms.Select(a => $"`{a}`")],
                stats.Tasks.Select(c => (List<Cell>)[Id(c), T(BaselineScore(bl, c)), .. arms.Select(a => T(bl.Arms.GetValueOrDefault(a)?.Tasks.GetValueOrDefault(c) is { } bc ? Pct(bc.Arm) : "—"))]).ToList(),
                Enumerable.Range(1, arms.Count + 1).ToHashSet()));
        }

        // 6. Failures
        b.Add(new Heading("Failures"));
        b.Add(new Para("For each arm, the checks that failed in at least one counted run, most frequent first, with the tasks they failed on. A check that could not decide a run is listed apart: that is the check's weakness, not the arm's."));
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
            b.Add(new Bullets(undecided.Select(u => $"`{u.Cell}`: {u.Reason}").ToList()));
        }
        else if (undecided.Count > 5)
            b.Add(new Para($"{undecided.Count} runs were undecided and are out of the pass rates; see the warning above. They are marked {Undecided} in the tables below."));

        // 7. What ran
        b.Add(new Heading("What ran"));
        b.Add(new Para("Every run planned by the matrix, and how far it got. *Attempted* runs started; *completed* runs ended with a transcript from nb; *graded* runs have their checks; *counted* runs are graded and passed every validity check; *decided* runs are counted runs whose headline checks all reached a verdict. Pass rates are over decided runs only."));
        b.Add(new Table(["Arm", "Planned", "Attempted", "Completed", "Graded", "Counted", "Decided"],
            arms.Select(a => { var s = stats.Arms[a]; return (List<Cell>)[Id(a), T($"{s.Planned}"), T($"{s.Attempted}"), T($"{s.Completed}"), T($"{s.Graded}"), T($"{s.Analysed}"), T($"{s.Decided}")]; }).ToList(),
            [1, 2, 3, 4, 5, 6]));
        if (UnequalLoss(stats) is { } warning) b.Add(new Warning(warning));
        var excluded = arms.SelectMany(a => stats.Arms[a].Excluded).ToList();
        if (excluded.Count == 0) b.Add(new Para("No run was left out."));
        else
        {
            b.Add(new Para("Runs left out, and why:"));
            b.Add(new Bullets(excluded.Select(ex => $"`{ex.Cell}`: {ExclusionText(ex)}").ToList()));
        }
        b.Add(new Para("How nb ended each completed run, per arm. `ok` is a normal finish; anything else is nb stopping the run, which the checks then grade like any other."));
        var reasons = ExitReasons(stats).ToList();
        b.Add(new Table(["Arm", .. reasons.Select(r => $"`{r}`")],
            arms.Select(a => (List<Cell>)[Id(a), .. reasons.Select(r => T(ExitCell(stats.Arms[a], rows, a, r)))]).ToList(),
            Enumerable.Range(1, reasons.Count).ToHashSet()));

        // 8. Results by task
        b.Add(new Heading("Results by task"));
        b.Add(new Para($"One row per task, one column per arm, one mark per run: {Pass} passed, {Fail} failed, {Undecided} undecided, {NotCounted} not counted. Hover a mark for the reason; click it for the run."));
        b.Add(new Table(["Task", "What it asks", .. arms.Select(a => $"`{a}`")],
            stats.Tasks.Select(c => (List<Cell>)[Id(c), T(TaskText(stats, c)), .. arms.Select(a =>
            {
                var runs = rows.Where(r => r.Arm == a && r.Task == c).OrderBy(r => r.Sample).ToList();
                return new Cell(string.Join(" ", runs.Select(Glyph)),
                    string.Join("", runs.Select(r => $"<a href=\"#run-{E(r.RunId)}\" class=\"{GlyphClass(r)}\" title=\"{E(GlyphTitle(r))}\">{Glyph(r)}</a>")));
            })]).ToList(),
            []));

        // 9. Check pass rates
        b.Add(new Heading("Check pass rates"));
        b.Add(new Para("How often each check held, per arm, over the runs it applies to. Headline and guardrail checks are over counted runs; a validity check is over every graded run, because it says how many were counted. A run the check could not decide is out of its rate and counted beside it."));
        b.Add(new Table(["Check", "What it tests", "Role", .. arms.Select(a => $"`{a}`")],
            CheckNames(stats).Select(c => (List<Cell>)[Id(c), T(CheckText(stats, c)), T(Role(stats, c)), .. arms.Select(a => T(CheckRateText(stats.Arms[a].Checks.GetValueOrDefault(c))))]).ToList(),
            Enumerable.Range(3, arms.Count).ToHashSet()));

        // 10. Cost and effort
        var unit = DurationUnit(stats);
        b.Add(new Heading("Cost and effort"));
        b.Add(new Para($"Per completed run. Duration is wall time in {(unit == "min" ? "minutes" : "seconds")}, hooks included; tokens are what nb reported{(arms.Any(a => stats.Arms[a].TokensEstimated) ? ", and some counts are nb's estimate rather than the provider's" : "")}. Cost is not shown: nb does not report it."));
        b.Add(new Table(["Arm", $"Duration ({unit}): median", "p90", "mean", "range", "Tokens: median", "p90", "mean", "range"],
            arms.Select(a => { var s = stats.Arms[a]; return (List<Cell>)[Id(a), .. SummaryText(s.DurationMs, v => Duration(v, unit)).Select(T), .. SummaryText(s.TokensTotal, Tokens).Select(T)]; }).ToList(),
            Enumerable.Range(1, 8).ToHashSet()));

        // 11. Every run
        b.Add(new Heading("Every run"));
        b.Add(new Para("Every run, failures first. *Reason* is the first check that did not hold and what it saw, or why the run never completed."));
        b.Add(new Table(["Arm", "Task", "Run", "Result", "nb ended", $"Duration ({unit})", "Tokens", "Reason"],
            Ordered(rows).Select(r => (List<Cell>)[
                new Cell($"`{r.Arm}`", Anchor: $"run-{r.RunId}"), Id(r.Task), T($"{r.Sample}"),
                new Cell($"{Glyph(r)} {Outcome(r)}", $"<span class=\"{GlyphClass(r)}\" title=\"{E(r.RunId)}\">{Glyph(r)} {E(Outcome(r))}</span>"),
                T(r.ExitReason is null ? "" : $"`{r.ExitReason}`"), T(r.DurationMs is { } dm ? Duration(dm, unit) : ""), T(r.Usage?.Total is { } t ? Tokens(t) : ""), T(Reason(r))]).ToList(),
            [2, 5, 6]));

        // 12. Reproducibility and method
        b.Add(new Heading("Reproducibility and method"));
        b.Add(new Para("What produced this report, so it can be run again, and how the numbers were computed."));
        b.Add(new Pairs(ReproFacts(stats, exp, judges).Select(f => (f.Item1, $"`{f.Item2}`")).ToList()));
        b.Add(new Para(stats.Methods));
        return b;
    }

    // ---- Sentences ----

    /// <summary>The verdict in one line, from verdicts stats.json already holds: rates, comparisons, the baseline, the undecided band.</summary>
    static string ResultLine(StatsFile stats)
    {
        var arms = stats.Arms.Keys.ToList();
        var first = arms[0];
        var parts = new List<string>();
        parts.Add(string.Join("; ", arms.Select(a => stats.Arms[a].Pass is not { } p ? $"`{a}` has no decided run"
            : a == first ? $"`{a}`{(arms.Count > 1 ? " (the reference)" : "")} passed {Pct(p.Rate)} of its runs"
            : $"`{a}` {Pct(p.Rate)}")) + ".");
        foreach (var c in stats.Comparisons)
            parts.Add(c.Verdict switch
            {
                "better" => $"`{c.Arm}` is better than `{c.Vs}` by {c.DiffPoints} points, and with {c.NPairs} task{(c.NPairs == 1 ? "" : "s")} that difference is statistically detectable.",
                "worse" => $"`{c.Arm}` is worse than `{c.Vs}` by {-c.DiffPoints} points, and with {c.NPairs} task{(c.NPairs == 1 ? "" : "s")} that difference is statistically detectable.",
                _ => $"With {c.NPairs} task{(c.NPairs == 1 ? "" : "s")} the difference between `{c.Arm}` and `{c.Vs}` ({SignedPoints(c.DiffPoints)}) is not statistically detectable.",
            });
        if (stats.Baseline is { } bl && bl.Arms.Count > 0)
            parts.Add($"Against the pinned baseline, {Join(bl.Arms.Select(a => $"`{a.Key}` {a.Value.Verdict}"))} at a tolerance of {bl.TolerancePoints} points.");
        if (stats.UndecidedChecks.Count > 0)
            parts.Add($"{(stats.UndecidedChecks.Count == 1 ? "One check" : $"{stats.UndecidedChecks.Count} checks")} could not decide {stats.UndecidedChecks.Sum(u => u.Runs)} runs; see the warning below.");
        if (UnequalLoss(stats) is not null)
            parts.Add("The arms lost runs unequally; see What ran.");
        return string.Join(" ", parts);
    }

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
        "invalid" => $"not counted, failed a validity check. {ex.Reason}",
        "failed" => $"never completed. {ex.Reason}",
        _ => $"{ex.Status}. {ex.Reason}".TrimEnd('.', ' '),
    };

    static string ExitCell(ArmStats a, List<ResultRow> rows, string arm, string reason)
    {
        var n = a.ExitReasons.GetValueOrDefault(reason);
        if (a.Completed == 0) return "—";
        if (n == 0 || n > 3 || reason == "ok") return $"{n}";
        var which = rows.Where(r => r.Arm == arm && r.ExitReason == reason).Select(r => $"`{r.Task}` run {r.Sample}");
        return $"{n} ({string.Join(", ", which)})";
    }

    static string Reason(ResultRow r) =>
        r.StatusReason ?? r.Invalid ?? r.Undecided ?? (r.Reasons is null ? "" : string.Join("; ", r.Reasons.Select(k => $"{k.Key}: {k.Value}")));

    static string Outcome(ResultRow r) =>
        r.Invalid is not null ? "not counted" : r.Undecided is not null ? "undecided" : r.Pass switch { true => "passed", false => "failed", null => r.Status == CellStatus.Completed ? "not graded" : r.Status == CellStatus.Failed ? "never completed" : r.Status };

    static string Glyph(ResultRow r) => !r.Analysed ? NotCounted : r.Undecided is not null ? Undecided : r.Pass == true ? Pass : Fail;
    static string GlyphClass(ResultRow r) => r.Analysed && r.Pass == true ? "pass" : "fail";
    static string GlyphTitle(ResultRow r) => $"run {r.Sample}: {Outcome(r)}{(Reason(r).Length > 0 ? " — " + Reason(r) : "")} ({r.RunId})";

    static IEnumerable<ResultRow> Ordered(List<ResultRow> rows) =>
        rows.OrderBy(r => !r.Analysed ? 1 : r.Undecided is not null ? 2 : r.Pass == true ? 3 : 0).ThenBy(r => r.Arm).ThenBy(r => r.Task).ThenBy(r => r.Sample);

    /// <summary>What a check tests when it says one thing everywhere; a check each task describes in its own words points at the Tasks table.</summary>
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
            $"{Times(c.Value)} on `{c.Key}`" + (perTask && TaskCheckText(stats, c.Key, f.Check) is { } text ? $" ({text})" : ""));
        return $"{head} failed in {f.Cells} of {f.Of} run{(f.Of == 1 ? "" : "s")}: {string.Join(", ", on)}.";
    }
    static string TaskText(StatsFile stats, string c) => stats.Descriptions.Tasks.GetValueOrDefault(c, c);

    static IEnumerable<string> CheckNames(StatsFile stats) =>
        stats.PassChecks.Concat(stats.ValidityChecks).Concat(stats.Arms.Values.SelectMany(a => a.Checks.Keys)).Distinct();

    static string? FixtureOf(StatsFile stats, string task) => stats.TaskDetails?.GetValueOrDefault(task)?.Fixture;

    /// <summary>The checks the suite declares, which every task carries with one meaning. Every task carrying a check does not make it shared: a headline each task declares in its own words is the task's.</summary>
    static List<string> SharedChecks(StatsFile stats) =>
        stats.TaskDetails is { Count: > 0 } td ? CheckNames(stats).Where(c => td.Values.Any(t => t.Checks.Any(k => k.Name == c && k.Level == "suite"))).ToList() : CheckNames(stats).ToList();

    /// <summary>How a task is measured beyond the suite's checks: its fixture's and its own, headline first, each in the task's words.</summary>
    static string OwnChecks(StatsFile stats, string task)
    {
        var own = (stats.TaskDetails?.GetValueOrDefault(task)?.Checks ?? []).Where(k => k.Level != "suite")
            .OrderBy(k => Role(stats, k.Name) switch { "headline" => 0, "validity" => 1, _ => 2 }).ToList();
        return own.Count == 0 ? "—" : string.Join("; ", own.Select(k => $"`{k.Name}` ({Role(stats, k.Name)}): {k.Description}"));
    }

    /// <summary>Which tasks' runs carry a check: every task, or the ones that do.</summary>
    static string On(StatsFile stats, string check)
    {
        if (stats.TaskDetails is not { Count: > 0 } td) return "every task";
        var on = stats.Tasks.Where(t => td.GetValueOrDefault(t)?.Checks.Any(k => k.Name == check) == true).ToList();
        return on.Count == stats.Tasks.Count ? "every task" : on.Count == 0 ? "—" : string.Join(", ", on.Select(t => $"`{t}`"));
    }

    static string Role(StatsFile stats, string check) =>
        stats.PassChecks.Contains(check) ? "headline" : stats.ValidityChecks.Contains(check) ? "validity" : "guardrail";

    static IEnumerable<string> ExitReasons(StatsFile stats) =>
        stats.Arms.Values.SelectMany(a => a.ExitReasons).GroupBy(k => k.Key).OrderByDescending(g => g.Sum(k => k.Value)).ThenBy(g => g.Key).Select(g => g.Key);

    static string BaselineScore(BaselineStats bl, string c) =>
        bl.Arms.Values.Select(a => a.Tasks.GetValueOrDefault(c)).FirstOrDefault(x => x is not null) is { } bc ? Pct(bc.Baseline) : "—";

    static string ArmField(Experiment exp, string arm, string field) =>
        exp.SuiteDef["arms"]?.AsArray().FirstOrDefault(a => a?["id"]?.GetValue<string>() == arm)?[field]?.GetValue<string>() ?? "?";

    static int Samples(Experiment exp, string arm) =>
        exp.SuiteDef["arms"]?.AsArray().FirstOrDefault(a => a?["id"]?.GetValue<string>() == arm)?["samples"]?.GetValue<int>() ?? 1;

    static IEnumerable<(string, string)> ReproFacts(StatsFile stats, Experiment exp, List<JudgeUse>? judges)
    {
        yield return ("proctor", exp.Versions.GetValueOrDefault("proctor", "unknown"));
        yield return ("nb", $"{exp.Versions.GetValueOrDefault("nb", "unknown")} at {exp.Nb.Path}");
        yield return ("suite hash", exp.SuiteHash);
        foreach (var (arm, bundle) in exp.Bundles ?? []) yield return ($"bundle {arm}", $"{bundle.Source} {bundle.Hash}");
        foreach (var j in judges ?? [])
            yield return ($"judge {j.Judge}", $"{j.Kind}{(j.Model is null ? "" : " " + j.Model)} at {j.Endpoint}; graded {string.Join(", ", j.Checks)}");
        yield return ("repository", exp.Repo is null ? "not a git repository" : $"{exp.Repo.Commit}{(exp.Repo.Dirty ? " (dirty)" : "")}");
        yield return ("command", exp.CommandLine);
        yield return ("created", exp.Created);
    }

    // ---- Numbers as text (formatting only) ----

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
    static string SignedPoints(int points) => $"{Signed(points)} points";
    static string Times(int n) => n switch { 1 => "once", 2 => "twice", _ => $"{n} times" };
    static string Join(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count <= 1 ? string.Concat(list) : string.Join(", ", list[..^1]) + " and " + list[^1];
    }

    static IEnumerable<string> SummaryText(Summary? s, Func<double, string> f) =>
        s is null ? ["—", "—", "—", "—"] : [f(s.Median), f(s.P90), f(s.Mean), $"{f(s.Min)}–{f(s.Max)}"];

    // ---- HTML ----

    static string RenderHtml(List<Block> blocks, StatsFile stats)
    {
        var sb = new StringBuilder();
        sb.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(stats.Suite)} — {E(stats.Experiment)}</title>
            <style>
            {Css}
            </style>
            </head>
            <body>
            <main>
            <h1>{E(stats.Suite)} <span class="id">{E(stats.Experiment)}</span></h1>

            """);
        var open = false;
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Heading h:
                    if (open) sb.Append("</section>\n\n");
                    sb.Append($"<section>\n<h2>{E(h.Text)}</h2>\n");
                    open = true;
                    break;
                case Para p:
                    sb.Append($"<p>{Inline(p.Text)}</p>\n");
                    break;
                case Warning w:
                    sb.Append($"<p class=\"warning\">{Inline(w.Text)}</p>\n");
                    break;
                case Bullets l:
                    sb.Append("<ul>\n").Append(string.Concat(l.Items.Select(i => $"<li>{Inline(i)}</li>\n"))).Append("</ul>\n");
                    break;
                case Pairs kv:
                    sb.Append("<dl>\n").Append(string.Concat(kv.Items.Select(i => $"<dt>{E(i.Key)}</dt><dd>{Inline(i.Value)}</dd>\n"))).Append("</dl>\n");
                    break;
                case Table t:
                    sb.Append("<table>\n<thead><tr>");
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
            }
        }
        if (open) sb.Append("</section>\n");
        sb.Append("</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    /// <summary>Escape, then the two inline forms both renderings share: `code` and **bold**, plus *italic* for the column words.</summary>
    static string Inline(string text)
    {
        var s = E(text);
        s = Regex.Replace(s, "`([^`]+)`", "<span class=\"id\">$1</span>");
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<b>$1</b>");
        s = Regex.Replace(s, @"(?<![\w*])\*([^*\s][^*]*?)\*(?![\w*])", "<i>$1</i>");
        return s;
    }

    const string Css = """
        :root { --ink: #1b1b1b; --muted: #6b6b6b; --rule: #d8d8d8; --accent: #1f6f43; --band: #fff4d6; --mark: #eef4f0; }
        html { background: #fff; }
        body { margin: 0; color: var(--ink); font: 15px/1.45 -apple-system, "Segoe UI", Helvetica, Arial, sans-serif; }
        main { max-width: 64rem; margin: 0 auto; padding: 2rem 1.25rem 4rem; }
        h1 { font-size: 1.5rem; font-weight: 600; margin: 0 0 .75rem; }
        h1 .id { font-weight: 400; color: var(--muted); }
        h2 { font-size: 1.1rem; font-weight: 600; margin: 2.5rem 0 .6rem; padding-top: .6rem; border-top: 1px solid var(--rule); }
        p { margin: .5rem 0; max-width: 52rem; }
        .warning { background: var(--band); border-left: 4px solid #d9a400; padding: .5rem .75rem; margin: 1rem 0; }
        table { border-collapse: collapse; margin: .75rem 0; font-variant-numeric: tabular-nums; }
        th, td { padding: .3rem .7rem; border-bottom: 1px solid var(--rule); text-align: left; vertical-align: top; }
        th { font-weight: 600; font-size: .85rem; color: var(--muted); }
        td { max-width: 32rem; }
        .num { text-align: right; white-space: nowrap; }
        .id { font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; font-size: .9em; white-space: nowrap; }
        .pass { color: var(--accent); }
        .fail { color: var(--muted); }
        td a { text-decoration: none; letter-spacing: .2em; font-size: 1.25em; }
        tr:target { background: var(--mark); }
        ul { margin: .3rem 0 .6rem 1.2rem; padding: 0; max-width: 52rem; }
        li { margin: .15rem 0; }
        dl { display: grid; grid-template-columns: max-content 1fr; gap: .3rem 1rem; max-width: 52rem; margin: .75rem 0; }
        dt { color: var(--muted); }
        dd { margin: 0; overflow-wrap: anywhere; }
        """;

    static string E(string s) => WebUtility.HtmlEncode(s);

    // ---- Markdown ----

    static string RenderMarkdown(List<Block> blocks, StatsFile stats)
    {
        var sb = new StringBuilder($"# {stats.Suite} — {stats.Experiment}\n\n");
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Heading h: sb.Append($"## {h.Text}\n\n"); break;
                case Para p: sb.Append($"{p.Text}\n\n"); break;
                case Warning w: sb.Append($"> **Warning.** {w.Text}\n\n"); break;
                case Bullets l: sb.Append(string.Concat(l.Items.Select(i => $"- {i}\n"))).Append('\n'); break;
                case Pairs kv:
                    sb.Append("| | |\n|---|---|\n").Append(string.Concat(kv.Items.Select(i => $"| {i.Key} | {Md(i.Value)} |\n"))).Append('\n');
                    break;
                case Table t:
                    sb.Append("| ").Append(string.Join(" | ", t.Header)).Append(" |\n");
                    sb.Append("|").Append(string.Join("|", t.Header.Select(_ => "---"))).Append("|\n");
                    foreach (var row in t.Rows) sb.Append("| ").Append(string.Join(" | ", row.Select(c => Md(c.Text)))).Append(" |\n");
                    sb.Append('\n');
                    break;
            }
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    static string Md(string s) => s.Replace("|", "\\|");
}
