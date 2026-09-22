using System.Net;
using System.Text;

namespace Proctor;

// report.html and summary.md: two pure functions of stats.json, results.jsonl and experiment.json.
// Neither computes a statistic. Sections follow project/plans/first-slice.md, "The report".

static class Report
{
    const string Pass = "●", Fail = "○", NotAnalysed = "×";

    // ---- HTML ----

    public static string Html(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges = null)
    {
        var arms = stats.Arms.Keys.ToList();
        var first = arms[0];
        var sb = new StringBuilder();
        sb.Append($"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{E(stats.Eval)} — {E(stats.Experiment)}</title>
            <style>
            {Css}
            </style>
            </head>
            <body>
            <main>
            <header>
            <h1>{E(stats.Eval)} <span class="id">{E(stats.Experiment)}</span></h1>
            {(stats.Descriptions.Eval is { } about ? $"<p class=\"summary about\">{E(about)}</p>\n" : "")}<p class="summary">{E(SummarySentence(stats, exp))}</p>
            <p class="summary">{E(stats.Mde.Sentence)}{(stats.Comparisons.Count > 0 ? " " + E(ComparisonSentence(stats)) : "")}{(stats.Baseline is null ? "" : " " + E(BaselineSentence(stats)))}</p>
            </header>

            """);

        if (UnequalLoss(stats) is { } warning)
            sb.Append($"<p class=\"warning\">{E(warning)}</p>\n");

        // 1. Where it fell down: the checks that did not hold, in the author's words
        sb.Append("<section>\n<h2>Where it fell down</h2>\n");
        foreach (var (arm, lead, items) in Failures(stats))
        {
            sb.Append($"<p><span class=\"id\">{E(arm)}</span> {E(lead)}</p>\n");
            if (items.Count > 0) sb.Append("<ul class=\"fell\">\n").Append(string.Join("", items.Select(i => $"<li>{E(i)}</li>\n"))).Append("</ul>\n");
        }
        sb.Append("</section>\n\n");

        // 2. Headline
        sb.Append("<section>\n<h2>Headline</h2>\n<table>\n<thead><tr><th>Arm</th><th class=\"num\">Analysed / attempted</th><th class=\"num\">Pass rate [95% CI] (cells)</th>");
        if (stats.Comparisons.Count > 0) sb.Append($"<th class=\"num\">vs {E(first)} [95% CI]</th><th>Verdict</th>");
        sb.Append("</tr></thead>\n<tbody>\n");
        foreach (var arm in arms)
        {
            var a = stats.Arms[arm];
            var cmp = stats.Comparisons.FirstOrDefault(c => c.Arm == arm);
            sb.Append($"<tr><td class=\"id\">{E(arm)}</td><td class=\"num\">{a.Analysed} / {a.Attempted}</td><td class=\"num\">{E(RateText(a.Pass))}</td>");
            if (stats.Comparisons.Count > 0)
                sb.Append(cmp is null
                    ? "<td class=\"num\">—</td><td>reference</td>"
                    : $"<td class=\"num\">{E(DiffText(cmp))}</td><td>{E(VerdictWord(cmp))}</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody>\n</table>\n</section>\n\n");

        // 2b. Against the baseline
        if (stats.Baseline is { } bl)
        {
            sb.Append($"<section>\n<h2>Against baseline</h2>\n<p class=\"note\">{E(BaselineNote(bl))}</p>\n<table>\n<thead><tr><th>Arm</th><th class=\"num\">vs baseline [95% CI]</th><th class=\"num\">Won / lost / tied</th><th>Verdict</th></tr></thead>\n<tbody>\n");
            foreach (var arm in arms)
                sb.Append(bl.Arms.TryGetValue(arm, out var g)
                    ? $"<tr><td class=\"id\">{E(arm)}</td><td class=\"num\">{E(DiffText(g.DiffPoints, g.Ci95))}</td><td class=\"num\">{g.Won} / {g.Lost} / {g.Tied} of {g.NPairs}</td><td class=\"{(g.Verdict == "regressed" ? "fail" : "pass")}\">{E(g.Verdict)}</td></tr>\n"
                    : $"<tr><td class=\"id\">{E(arm)}</td><td class=\"num\">—</td><td class=\"num\">—</td><td>no shared cases</td></tr>\n");
            sb.Append("</tbody>\n</table>\n<table>\n<thead><tr><th>Case</th><th class=\"num\">baseline</th>");
            foreach (var arm in arms) sb.Append($"<th class=\"num\">{E(arm)}</th>");
            sb.Append("</tr></thead>\n<tbody>\n");
            foreach (var c in stats.Cases)
            {
                sb.Append($"<tr><td class=\"id\">{E(c)}</td><td class=\"num\">{E(BaselineScore(bl, c))}</td>");
                foreach (var arm in arms) sb.Append($"<td class=\"num\">{E(bl.Arms.GetValueOrDefault(arm)?.Cases.GetValueOrDefault(c) is { } bc ? Pct(bc.Arm) : "—")}</td>");
                sb.Append("</tr>\n");
            }
            sb.Append("</tbody>\n</table>\n</section>\n\n");
        }

        // 3. Accounting
        sb.Append("<section>\n<h2>Accounting</h2>\n<table>\n<thead><tr><th>Arm</th><th class=\"num\">Planned</th><th class=\"num\">Attempted</th><th class=\"num\">Completed</th><th class=\"num\">Graded</th><th class=\"num\">Analysed</th></tr></thead>\n<tbody>\n");
        foreach (var arm in arms)
        {
            var a = stats.Arms[arm];
            sb.Append($"<tr><td class=\"id\">{E(arm)}</td><td class=\"num\">{a.Planned}</td><td class=\"num\">{a.Attempted}</td><td class=\"num\">{a.Completed}</td><td class=\"num\">{a.Graded}</td><td class=\"num\">{a.Analysed}</td></tr>\n");
        }
        sb.Append("</tbody>\n</table>\n");
        var excluded = arms.SelectMany(arm => stats.Arms[arm].Excluded).ToList();
        if (excluded.Count > 0)
        {
            sb.Append("<p>Excluded cells, in the accounting and out of the rates:</p>\n<ul class=\"excluded\">\n");
            foreach (var ex in excluded) sb.Append($"<li><span class=\"id\">{E(ex.Cell)}</span> {E(ex.Status)}: {E(ex.Reason)}</li>\n");
            sb.Append("</ul>\n");
        }
        else sb.Append("<p>No cells were excluded.</p>\n");
        sb.Append("</section>\n\n");

        // 4. Matrix
        sb.Append($"<section>\n<h2>Case by arm</h2>\n<p class=\"legend\"><span class=\"pass\">{Pass}</span> pass &nbsp; <span class=\"fail\">{Fail}</span> fail &nbsp; <span class=\"fail\">{NotAnalysed}</span> not analysed (failed, invalid or not graded). One glyph per sample; hover for the reason, click for the run.</p>\n<table class=\"matrix\">\n<thead><tr><th>Case</th><th>What it asks</th>");
        foreach (var arm in arms) sb.Append($"<th>{E(arm)}</th>");
        sb.Append("</tr></thead>\n<tbody>\n");
        foreach (var c in stats.Cases)
        {
            sb.Append($"<tr><td class=\"id\">{E(c)}</td><td class=\"desc\">{E(CaseText(stats, c))}</td>");
            foreach (var arm in arms)
            {
                sb.Append("<td class=\"glyphs\">");
                foreach (var row in rows.Where(r => r.Arm == arm && r.Case == c).OrderBy(r => r.Sample))
                    sb.Append($"<a href=\"#run-{E(row.RunId)}\" class=\"{GlyphClass(row)}\" title=\"{E(GlyphTitle(row))}\">{Glyph(row)}</a>");
                sb.Append("</td>");
            }
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody>\n</table>\n</section>\n\n");

        // 5. Checks
        sb.Append("<section>\n<h2>Checks</h2>\n<table>\n<thead><tr><th>Check</th><th>What it tests</th><th>Role</th>");
        foreach (var arm in arms) sb.Append($"<th class=\"num\">{E(arm)}</th>");
        sb.Append("</tr></thead>\n<tbody>\n");
        foreach (var check in CheckNames(stats))
        {
            sb.Append($"<tr><td class=\"id\">{E(check)}</td><td class=\"desc\">{E(CheckText(stats, check))}</td><td>{Role(stats, check)}</td>");
            foreach (var arm in arms)
                sb.Append($"<td class=\"num\">{E(CheckText(stats.Arms[arm].Checks.GetValueOrDefault(check)))}</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody>\n</table>\n</section>\n\n");

        // 6. Failure breakdown
        sb.Append("<section>\n<h2>Exit reasons</h2>\n<table>\n<thead><tr><th>Exit reason</th>");
        foreach (var arm in arms) sb.Append($"<th class=\"num\">{E(arm)}</th>");
        sb.Append("</tr></thead>\n<tbody>\n");
        foreach (var reason in ExitReasons(stats))
        {
            sb.Append($"<tr><td class=\"id\">{E(reason)}</td>");
            foreach (var arm in arms) sb.Append($"<td class=\"num\">{E(ShareText(stats.Arms[arm], reason))}</td>");
            sb.Append("</tr>\n");
        }
        sb.Append("</tbody>\n</table>\n</section>\n\n");

        // 7. Cost and effort
        var unit = DurationUnit(stats);
        sb.Append($"<section>\n<h2>Cost and effort</h2>\n<table>\n<thead><tr><th>Arm</th><th class=\"num\">Duration, median</th><th class=\"num\">p90</th><th class=\"num\">mean</th><th class=\"num\">range</th><th class=\"num\">Tokens, median</th><th class=\"num\">p90</th><th class=\"num\">mean</th><th class=\"num\">range</th></tr></thead>\n<tbody>\n");
        foreach (var arm in arms)
        {
            var a = stats.Arms[arm];
            sb.Append($"<tr><td class=\"id\">{E(arm)}</td>{SummaryCells(a.DurationMs, v => Duration(v, unit))}{SummaryCells(a.TokensTotal, Tokens)}</tr>\n");
        }
        sb.Append("</tbody>\n</table>\n");
        sb.Append($"<p class=\"note\">Durations in {unit}, wall time of the cell including hooks. {(arms.Any(a => stats.Arms[a].TokensEstimated) ? "Some token counts are nb's estimate, not the provider's report. " : "")}Cost is not shown: nb's trailer does not carry it.</p>\n</section>\n\n");

        // 8. Per-run table
        sb.Append("<section>\n<h2>Runs</h2>\n<p class=\"note\">Failures first.</p>\n<table class=\"runs\">\n<thead><tr><th>Arm</th><th>Case</th><th class=\"num\">Sample</th><th>Status</th><th>Exit</th><th>Pass</th><th class=\"num\">Duration</th><th class=\"num\">Tokens</th><th>Run</th><th>Reason</th></tr></thead>\n<tbody>\n");
        foreach (var r in Ordered(rows))
        {
            sb.Append($"<tr id=\"run-{E(r.RunId)}\"><td class=\"id\">{E(r.Arm)}</td><td class=\"id\">{E(r.Case)}</td><td class=\"num\">{r.Sample}</td><td>{E(r.Status)}</td><td class=\"id\">{E(r.ExitReason ?? "")}</td>");
            sb.Append($"<td class=\"{GlyphClass(r)}\">{Glyph(r)}</td><td class=\"num\">{(r.DurationMs is { } d ? Duration(d, unit) : "")}</td><td class=\"num\">{(r.Usage?.Total is { } t ? Tokens(t) : "")}</td><td class=\"id\">{E(r.RunId)}</td><td>{E(Reason(r))}</td></tr>\n");
        }
        sb.Append("</tbody>\n</table>\n</section>\n\n");

        // 9. Reproducibility
        sb.Append("<section>\n<h2>Reproducibility</h2>\n<dl>\n");
        foreach (var (k, v) in ReproFacts(stats, exp, judges))
            sb.Append($"<dt>{E(k)}</dt><dd class=\"id\">{E(v)}</dd>\n");
        sb.Append($"</dl>\n<p class=\"note\">{E(stats.Methods)}</p>\n</section>\n</main>\n</body>\n</html>\n");
        return sb.ToString();
    }

    const string Css = """
        :root { --ink: #1b1b1b; --muted: #6b6b6b; --rule: #d8d8d8; --accent: #1f6f43; --band: #fff4d6; --mark: #eef4f0; }
        html { background: #fff; }
        body { margin: 0; color: var(--ink); font: 15px/1.45 -apple-system, "Segoe UI", Helvetica, Arial, sans-serif; }
        main { max-width: 64rem; margin: 0 auto; padding: 2rem 1.25rem 4rem; }
        h1 { font-size: 1.5rem; font-weight: 600; margin: 0 0 .5rem; }
        h1 .id { font-weight: 400; color: var(--muted); }
        h2 { font-size: 1.1rem; font-weight: 600; margin: 2.5rem 0 .6rem; padding-top: .6rem; border-top: 1px solid var(--rule); }
        p { margin: .4rem 0; }
        .summary { max-width: 52rem; }
        .about { font-size: 1.05rem; }
        .desc { max-width: 28rem; }
        .warning { background: var(--band); border-left: 4px solid #d9a400; padding: .5rem .75rem; margin: 1rem 0; }
        .note, .legend { color: var(--muted); font-size: .9rem; }
        table { border-collapse: collapse; margin: .5rem 0; font-variant-numeric: tabular-nums; }
        th, td { padding: .3rem .7rem; border-bottom: 1px solid var(--rule); text-align: left; vertical-align: top; }
        th { font-weight: 600; font-size: .85rem; color: var(--muted); }
        .num { text-align: right; white-space: nowrap; }
        .id { font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; font-size: .9em; white-space: nowrap; }
        .pass { color: var(--accent); }
        .fail { color: var(--muted); }
        .glyphs a { text-decoration: none; letter-spacing: .2em; font-size: 1.25em; }
        .runs td { font-size: .9rem; }
        tr:target { background: var(--mark); }
        ul.excluded, ul.fell { margin: .3rem 0 .6rem 1.2rem; padding: 0; max-width: 52rem; }
        dl { display: grid; grid-template-columns: max-content 1fr; gap: .2rem 1rem; }
        dt { color: var(--muted); }
        dd { margin: 0; overflow-wrap: anywhere; }
        """;

    // ---- Markdown ----

    public static string Markdown(StatsFile stats, List<ResultRow> rows, Experiment exp, List<JudgeUse>? judges = null)
    {
        var arms = stats.Arms.Keys.ToList();
        var first = arms[0];
        var sb = new StringBuilder();
        sb.Append($"# {stats.Eval} — {stats.Experiment}\n\n{(stats.Descriptions.Eval is { } about ? about + "\n\n" : "")}{SummarySentence(stats, exp)}\n\n{stats.Mde.Sentence}");
        if (stats.Comparisons.Count > 0) sb.Append(' ').Append(ComparisonSentence(stats));
        if (stats.Baseline is not null) sb.Append(' ').Append(BaselineSentence(stats));
        sb.Append("\n\n");
        if (UnequalLoss(stats) is { } warning) sb.Append($"> **Warning.** {warning}\n\n");
        sb.Append("## Where it fell down\n\n");
        foreach (var (arm, lead, items) in Failures(stats))
            sb.Append($"`{arm}` {lead}\n\n").Append(string.Join("", items.Select(i => $"- {Md(i)}\n"))).Append(items.Count > 0 ? "\n" : "");

        sb.Append("## Headline\n\n");
        var head = new List<string> { "Arm", "Analysed / attempted", "Pass rate [95% CI] (cells)" };
        if (stats.Comparisons.Count > 0) head.AddRange([$"vs {first} [95% CI]", "Verdict"]);
        sb.Append(Table(head, arms.Select(arm =>
        {
            var a = stats.Arms[arm];
            var cmp = stats.Comparisons.FirstOrDefault(c => c.Arm == arm);
            var cells = new List<string> { $"`{arm}`", $"{a.Analysed} / {a.Attempted}", RateText(a.Pass) };
            if (stats.Comparisons.Count > 0) cells.AddRange(cmp is null ? ["—", "reference"] : [DiffText(cmp), VerdictWord(cmp)]);
            return cells;
        })));

        if (stats.Baseline is { } bl)
        {
            sb.Append($"\n## Against baseline\n\n{BaselineNote(bl)}\n\n");
            sb.Append(Table(["Arm", "vs baseline [95% CI]", "Won / lost / tied", "Verdict"], arms.Select(arm =>
                bl.Arms.TryGetValue(arm, out var g)
                    ? new List<string> { $"`{arm}`", DiffText(g.DiffPoints, g.Ci95), $"{g.Won} / {g.Lost} / {g.Tied} of {g.NPairs}", g.Verdict }
                    : new List<string> { $"`{arm}`", "—", "—", "no shared cases" })));
            sb.Append('\n');
            sb.Append(Table(["Case", "baseline", .. arms.Select(a => $"`{a}`")], stats.Cases.Select(c =>
                (string[])[$"`{c}`", BaselineScore(bl, c), .. arms.Select(arm => bl.Arms.GetValueOrDefault(arm)?.Cases.GetValueOrDefault(c) is { } bc ? Pct(bc.Arm) : "—")])));
        }

        sb.Append("\n## Accounting\n\n");
        sb.Append(Table(["Arm", "Planned", "Attempted", "Completed", "Graded", "Analysed"],
            arms.Select(arm => { var a = stats.Arms[arm]; return new List<string> { $"`{arm}`", $"{a.Planned}", $"{a.Attempted}", $"{a.Completed}", $"{a.Graded}", $"{a.Analysed}" }; })));
        var excluded = arms.SelectMany(arm => stats.Arms[arm].Excluded).ToList();
        sb.Append(excluded.Count == 0 ? "\nNo cells were excluded.\n" : "\nExcluded cells, in the accounting and out of the rates:\n\n" + string.Join("", excluded.Select(ex => $"- `{ex.Cell}` {ex.Status}: {ex.Reason}\n")));

        sb.Append($"\n## Case by arm\n\n{Pass} pass, {Fail} fail, {NotAnalysed} not analysed (failed, invalid or not graded); one glyph per sample. Reasons are in the runs table.\n\n");
        sb.Append(Table(["Case", "What it asks", .. arms.Select(a => $"`{a}`")], stats.Cases.Select(c =>
            (string[])[$"`{c}`", Md(CaseText(stats, c)), .. arms.Select(arm => string.Join(" ", rows.Where(r => r.Arm == arm && r.Case == c).OrderBy(r => r.Sample).Select(Glyph)))])));

        sb.Append("\n## Checks\n\n");
        sb.Append(Table(["Check", "What it tests", "Role", .. arms.Select(a => $"`{a}`")], CheckNames(stats).Select(check =>
            (string[])[$"`{check}`", Md(CheckText(stats, check)), Role(stats, check), .. arms.Select(arm => CheckText(stats.Arms[arm].Checks.GetValueOrDefault(check)))])));

        sb.Append("\n## Exit reasons\n\n");
        sb.Append(Table(["Exit reason", .. arms.Select(a => $"`{a}`")], ExitReasons(stats).Select(reason =>
            (string[])[$"`{reason}`", .. arms.Select(arm => ShareText(stats.Arms[arm], reason))])));

        var unit = DurationUnit(stats);
        sb.Append($"\n## Cost and effort\n\nDurations in {unit}, wall time of the cell including hooks. {(arms.Any(a => stats.Arms[a].TokensEstimated) ? "Some token counts are nb's estimate, not the provider's report. " : "")}Cost is not shown: nb's trailer does not carry it.\n\n");
        sb.Append(Table(["Arm", "Duration median", "p90", "mean", "range", "Tokens median", "p90", "mean", "range"], arms.Select(arm =>
        {
            var a = stats.Arms[arm];
            return (string[])[$"`{arm}`", .. SummaryText(a.DurationMs, v => Duration(v, unit)), .. SummaryText(a.TokensTotal, Tokens)];
        })));

        sb.Append("\n## Runs\n\nFailures first.\n\n");
        sb.Append(Table(["Arm", "Case", "Sample", "Status", "Exit", "Pass", "Duration", "Tokens", "Run", "Reason"], Ordered(rows).Select(r =>
            new List<string> { $"`{r.Arm}`", $"`{r.Case}`", $"{r.Sample}", r.Status, r.ExitReason ?? "", Glyph(r), r.DurationMs is { } d ? Duration(d, unit) : "", r.Usage?.Total is { } t ? Tokens(t) : "", $"`{r.RunId}`", Md(Reason(r)) })));

        sb.Append("\n## Reproducibility\n\n");
        foreach (var (k, v) in ReproFacts(stats, exp, judges)) sb.Append($"- {k}: `{v}`\n");
        sb.Append($"\n{stats.Methods}\n");
        return sb.ToString();
    }

    static string Table(IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
    {
        var h = header.ToList();
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", h)).Append(" |\n");
        sb.Append("|").Append(string.Join("|", h.Select(_ => "---"))).Append("|\n");
        foreach (var r in rows) sb.Append("| ").Append(string.Join(" | ", r)).Append(" |\n");
        return sb.ToString();
    }

    // ---- Shared text ----

    static string SummarySentence(StatsFile stats, Experiment exp)
    {
        var armText = string.Join("; ", stats.Arms.Keys.Select(id =>
        {
            var def = exp.EvalDef["arms"]?.AsArray().FirstOrDefault(a => a?["id"]?.GetValue<string>() == id);
            var samples = def?["samples"]?.GetValue<int>() ?? 1;
            var about = stats.Descriptions.Arms.TryGetValue(id, out var d) ? $"{d}: " : "";
            return $"{id} ({about}{def?["provider"]?.GetValue<string>() ?? "?"} / {def?["model"]?.GetValue<string>() ?? "?"} through {def?["harness"]?.GetValue<string>() ?? "?"}, {samples} sample{(samples == 1 ? "" : "s")} per case)";
        }));
        var analysed = stats.Arms.Values.Sum(a => a.Analysed);
        var planned = stats.Arms.Values.Sum(a => a.Planned);
        return $"Eval {stats.Eval}, run {exp.Created[..10]} on {exp.Host}. {stats.Arms.Count} arm{(stats.Arms.Count == 1 ? "" : "s")}: {armText}. {stats.NCases} case{(stats.NCases == 1 ? "" : "s")}, {planned} cell{(planned == 1 ? "" : "s")} planned, {analysed} analysed.";
    }

    /// <summary>Per arm, a lead sentence and one line per check that did not pass in an analysed cell: what it tests, how often it did not hold, and in which cases.</summary>
    static IEnumerable<(string Arm, string Lead, List<string> Items)> Failures(StatsFile stats)
    {
        foreach (var (arm, a) in stats.Arms)
        {
            if (a.Analysed == 0) { yield return (arm, "no cell was analysed.", []); continue; }
            if (a.Failures.Count == 0) { yield return (arm, $"every check held in all {a.Analysed} analysed cell{(a.Analysed == 1 ? "" : "s")}.", []); continue; }
            var items = a.Failures.Select(f =>
                $"{CheckText(stats, f.Check)} ({f.Check}, {Role(stats, f.Check)}) did not hold in {f.Cells} of {f.Of} cell{(f.Of == 1 ? "" : "s")}: "
                + string.Join(", ", f.Cases.Select(c => c.Value == 1 ? c.Key : $"{c.Key} ×{c.Value}"))).ToList();
            yield return (arm, $"fell short on {a.Failures.Count} check{(a.Failures.Count == 1 ? "" : "s")}:", items);
        }
    }

    static string CheckText(StatsFile stats, string check) => stats.Descriptions.Checks.GetValueOrDefault(check, check);
    static string CaseText(StatsFile stats, string c) => stats.Descriptions.Cases.GetValueOrDefault(c, c);
    static string Md(string s) => s.Replace("|", "\\|");

    static string ComparisonSentence(StatsFile stats) =>
        string.Join(" ", stats.Comparisons.Select(c => $"{c.Arm} vs {c.Vs}: {DiffText(c)}, {VerdictWord(c)} (won {c.Won}, lost {c.Lost}, tied {c.Tied} of {c.NPairs})."));

    static string? UnequalLoss(StatsFile stats)
    {
        var analysed = stats.Arms.Values.Select(a => a.Analysed).Distinct().Count();
        if (analysed <= 1) return null;
        return "Arms lost runs unequally: " + string.Join(", ", stats.Arms.Select(a => $"{a.Key} analysed {a.Value.Analysed} of {a.Value.Planned}")) + ". Rates are over the analysed cells; see Accounting for what was excluded and why.";
    }

    static string RateText(RateJson? r) => r is null ? "—" : $"{Pct(r.Rate)} [{Points(r.Ci95.Lo)}, {Points(r.Ci95.Hi)}] ({r.KCells}/{r.NCells})";

    static string CheckText(RateJson? r)
    {
        if (r is null) return "—";
        var extra = new[] { r.Errors is > 0 ? $"{r.Errors} error" : null, r.NeedsJudge is > 0 ? $"{r.NeedsJudge} needs judge" : null }.Where(s => s is not null);
        return RateText(r) + (extra.Any() ? $" · {string.Join(", ", extra)}" : "");
    }

    static string DiffText(Comparison c) => DiffText(c.DiffPoints, c.Ci95);
    static string DiffText(int points, int[] ci) => $"{Signed(points)} pts [{Signed(ci[0])}, {Signed(ci[1])}]";

    static string BaselineSentence(StatsFile stats) =>
        string.Join(" ", stats.Baseline!.Arms.Select(a => $"{a.Key} vs baseline: {DiffText(a.Value.DiffPoints, a.Value.Ci95)}, {a.Value.Verdict} at a tolerance of {stats.Baseline.TolerancePoints} points."));

    static string BaselineNote(BaselineStats bl) =>
        $"Baseline set {bl.Set}; scores {bl.Scores}. Verdict: the point estimate against a tolerance of {bl.TolerancePoints} point{(bl.TolerancePoints == 1 ? "" : "s")}; the interval is the same paired method as between arms.";

    static string BaselineScore(BaselineStats bl, string c) =>
        bl.Arms.Values.Select(a => a.Cases.GetValueOrDefault(c)).FirstOrDefault(x => x is not null) is { } bc ? Pct(bc.Baseline) : "—";

    static string VerdictWord(Comparison c) => c.Verdict switch
    {
        "better" => $"better than {c.Vs}",
        "worse" => $"worse than {c.Vs}",
        _ => "no detectable difference",
    };

    static string ShareText(ArmStats a, string reason)
    {
        var n = a.ExitReasons.GetValueOrDefault(reason);
        return a.Completed == 0 ? "—" : $"{n} ({Pct((double)n / a.Completed)})";
    }

    static IEnumerable<string> CheckNames(StatsFile stats) =>
        stats.PassChecks.Concat(stats.ValidityChecks).Concat(stats.Arms.Values.SelectMany(a => a.Checks.Keys)).Distinct();

    static string Role(StatsFile stats, string check) =>
        stats.PassChecks.Contains(check) ? "headline" : stats.ValidityChecks.Contains(check) ? "validity" : "guardrail";

    static IEnumerable<string> ExitReasons(StatsFile stats) =>
        stats.Arms.Values.SelectMany(a => a.ExitReasons).GroupBy(k => k.Key).OrderByDescending(g => g.Sum(k => k.Value)).ThenBy(g => g.Key).Select(g => g.Key);

    static IEnumerable<ResultRow> Ordered(List<ResultRow> rows) =>
        rows.OrderBy(r => !r.Analysed ? 1 : r.Pass == true ? 2 : 0).ThenBy(r => r.Arm).ThenBy(r => r.Case).ThenBy(r => r.Sample);

    static string Reason(ResultRow r) => r.Invalid is not null ? $"invalid, {r.Invalid}" : Detail(r);
    static string Detail(ResultRow r) =>
        r.StatusReason ?? r.Invalid ?? (r.Reasons is null ? "" : string.Join("; ", r.Reasons.Select(k => $"{k.Key}: {k.Value}")));

    static string Outcome(ResultRow r) => r.Invalid is not null ? "invalid" : r.Pass switch { true => "pass", false => "fail", null => r.Status };
    static string Glyph(ResultRow r) => !r.Analysed ? NotAnalysed : r.Pass == true ? Pass : Fail;
    static string GlyphClass(ResultRow r) => r.Analysed && r.Pass == true ? "pass" : "fail";
    static string GlyphTitle(ResultRow r) => $"sample {r.Sample}: {Outcome(r)}{(Detail(r).Length > 0 ? " — " + Detail(r) : "")} ({r.RunId})";

    static IEnumerable<(string, string)> ReproFacts(StatsFile stats, Experiment exp, List<JudgeUse>? judges)
    {
        yield return ("proctor", exp.Versions.GetValueOrDefault("proctor", "unknown"));
        yield return ("nb", $"{exp.Versions.GetValueOrDefault("nb", "unknown")} at {exp.Nb.Path}");
        foreach (var j in judges ?? [])
            yield return ($"judge {j.Judge}", $"{j.Kind}{(j.Model is null ? "" : " " + j.Model)} at {j.Endpoint}; graded {string.Join(", ", j.Checks)}");
        yield return ("eval hash", exp.EvalHash);
        foreach (var (arm, bundle) in exp.Bundles ?? []) yield return ($"bundle {arm}", $"{bundle.Source} {bundle.Hash}");
        yield return ("repository", exp.Repo is null ? "not a git repository" : $"{exp.Repo.Commit}{(exp.Repo.Dirty ? " (dirty)" : "")}");
        yield return ("command", exp.CommandLine);
        yield return ("created", exp.Created);
    }

    static string DurationUnit(StatsFile stats) =>
        stats.Arms.Values.Any(a => a.DurationMs is { Median: >= 120_000 }) ? "min" : "s";

    static string Duration(double ms, string unit) => unit == "min" ? $"{ms / 60_000:F1}" : $"{ms / 1000:F1}";
    static string Tokens(double n) => ((long)n).ToString("N0");
    static string Pct(double p) => $"{Points(p)}%";
    static string Points(double p) => $"{Math.Round(100 * p, MidpointRounding.AwayFromZero):F0}";
    static string Signed(int points) => points > 0 ? $"+{points}" : points < 0 ? $"−{-points}" : "0";

    static string SummaryCells(Summary? s, Func<double, string> f) =>
        s is null ? "<td class=\"num\">—</td><td class=\"num\">—</td><td class=\"num\">—</td><td class=\"num\">—</td>"
                  : $"<td class=\"num\">{f(s.Median)}</td><td class=\"num\">{f(s.P90)}</td><td class=\"num\">{f(s.Mean)}</td><td class=\"num\">{f(s.Min)}–{f(s.Max)}</td>";

    static IEnumerable<string> SummaryText(Summary? s, Func<double, string> f) =>
        s is null ? ["—", "—", "—", "—"] : [f(s.Median), f(s.P90), f(s.Mean), $"{f(s.Min)}–{f(s.Max)}"];

    static string E(string s) => WebUtility.HtmlEncode(s);
}
