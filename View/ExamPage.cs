using System.Globalization;
using System.Text;
using P = Auspex;
using static Auspex.View.Pages;

namespace Auspex.View;

/// <summary>
/// The page of an exam with axes, as designed on the canvas (2026-11-26 and 27): the configurations the exam names, each
/// against what is live on five measures; its predictions and how each change came out; its axes, stages and checks.
/// Every number is proctor's (Measured.cs): this page places and words them, and where a dot sits on a strip is layout,
/// not a count. Nothing here changes anything: a write is asked of the CLI or of the agent the person works with.
/// </summary>
static class ExamPage
{
    /// <summary>A measure of a try as a column: its name, the try's number, how the number is written, and what a comparison's cell holds for it.</summary>
    internal sealed record Column(string Name, Func<P.TryRow, double?> Of, Func<double, string> Said, Func<P.CellAnswer, P.Measure?> In);

    internal static readonly Column[] Columns =
    [
        new("Checks held", t => t.Held, Count, c => c.Held),
        new("Friction · flagged steps", t => t.Flagged, Count, c => c.Flagged),
        new("Tool calls", t => t.ToolCalls, Count, c => c.ToolCalls),
        new("Tokens read", t => t.TokensIn, Tokens, c => c.Tokens),
        new("Time", t => t.DurationMs, Time, c => c.Duration),
    ];

    internal static readonly string[] RoleOrder = [P.Roles.Correctness, P.Roles.Integrity, P.Roles.Quality, P.Roles.Observation];

    internal static readonly Dictionary<string, (string Class, string Says)> RoleTag = new()
    {
        [P.Roles.Correctness] = ("tag tag-live", "Correctness: was the task completed in a way that satisfies our intent. Decides right or wrong."),
        [P.Roles.Integrity] = ("tag tag-fr", "Integrity: can the result be trusted. A failure takes the try out of every number."),
        [P.Roles.Quality] = ("tag", "Quality: something we prefer and do not require. Reported as better or worse, and decides nothing."),
        [P.Roles.Observation] = ("tag tag-none", "Observation: something we are interested in, with no preferred answer. Counted, never judged."),
    };

    public static string Render(string root, Exam e)
    {
        var page = Open(e.Id, [("exams", "/"), (e.Id, null)], style: "one");
        var (exam, problem) = Measured.Load(root, e.Id);
        var (stats, statsProblem) = exam is null ? (null, problem) : Measured.Stats(root, exam);
        if (exam is null || stats is null)
        {
            // Proctor could not read the exam as it is now: say so in its words.
            page.Append($"<header><span class=\"kind\">exam</span><h1 class=\"title\">{E(e.Id)}</h1></header>")
                .Append($"<div class=\"said\"><b>Proctor cannot read this exam as it stands</b>, so nothing is counted here.</div><pre class=\"mono small soft\">{E(problem ?? statsProblem ?? "")}</pre>")
                .Append(EarlierLink(e));
            return Close(page);
        }
        var named = stats.Configurations.Where(c => c.Name is not null).ToList();
        var asOf = DateTime.UtcNow.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);
        var (tries, lost) = (named.Sum(c => c.Tries?.Tries ?? 0), stats.Lost.Count);

        page.Append("<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">exam</span>")
            .Append($"<h1 class=\"title\">{E(e.Id)}</h1>")
            .Append(e.Description is { } d ? $"<p class=\"desc\">{E(d)}</p>" : "")
            .Append("<dl class=\"facts\">")
            .Append(Fact("Configurations", named.Count(c => c.Retired is null) + (named.Count(c => c.Retired is not null) is > 0 and var gone ? $" <span class=\"dim regular\">· {gone} retired</span>" : ""))).Append(Fact("Predictions", (stats.Predictions?.Count ?? 0).ToString()))
            .Append(Fact("Axes", stats.Axes.Count.ToString())).Append(Fact("Stages", stats.Stages.Count.ToString()))
            .Append(Fact("Tries", tries + (lost > 0 ? $" <span class=\"dim regular\">· {lost} lost</span>" : "")))
            .Append(Fact("As of", asOf)).Append("</dl></header>");

        foreach (var move in stats.StandardMoved ?? [])
            page.Append($"<div class=\"said\"><b>The standard moved.</b> <span class=\"soft\">{E(move.Sentence)}</span></div>");

        Configurations(page, root, e, exam, named);
        Predictions(page, e, stats.Predictions ?? [], named, "A prediction captures how you expect a new configuration to behave. Using them helps keep your experimentation focused and honest.");
        Axes(page, exam);
        Stages(page, stats);
        Checks(page, exam, stats);

        page.Append("<section class=\"sec sec-plain\" aria-label=\"How this is counted\"><div class=\"sec-head\"><span class=\"label\">How this is counted</span></div>")
            .Append($"<div class=\"sec-body note\" style=\"display:flex;flex-direction:column;gap:6px;max-width:900px\"><div>{E(stats.Methods)}</div>");
        foreach (var l in stats.Lost)
            page.Append($"<div>Try {l.N} of {E(named.FirstOrDefault(c => c.Key == l.Configuration)?.Name ?? l.Configuration)} is lost and counted nowhere, at {E(l.At)}: {E(l.Why)}</div>");
        page.Append("<div>Every number here is proctor's. Nothing on this page recommends anything.</div></div></section>");
        return Close(page);
    }

    static void Configurations(StringBuilder page, string root, Exam e, P.Exam exam, List<P.ConfigurationRow> named)
    {
        Section(page, "Configurations", "A configuration is one value on each axis, under a name. It's something you ship or something you want to test.");
        page.Append("<div style=\"min-width:1040px\">");
        var live = named.FirstOrDefault(c => c.Live && c.Tries is { Tries: > 0 });
        if (live is null && named.Any(c => c.Tries is { Tries: > 0 }))
            page.Append("<div class=\"said soft\" style=\"padding-bottom:12px\">Nothing is live with tries of its own, so nothing is compared.</div>");
        page.Append("<div class=\"row row-head ra\"><span>Configuration</span>").Append(string.Concat(Columns.Select(c => $"<span>{E(c.Name)}</span>"))).Append("</div>");

        foreach (var (c, i) in named.Where(c => c.Retired is null).Select((c, i) => (c, i)))
        {
            var counted = c.Tries?.Rows.Where(t => t.Invalid is null).ToList() ?? [];
            var (mark, answer, cell, against) = Standing(root, exam, c, live);
            var made = $"{c.Made} of {c.Wanted} {(c.Wanted == 1 ? "try" : "tries")}{(c.Lost > 0 ? $" · {c.Lost} lost" : "")}{(c.NotGraded > 0 && counted.Count > 0 ? $" · {c.NotGraded} not yet graded" : "")}";
            page.Append($"<div class=\"row ra{(i == 0 ? " first" : "")}\" style=\"padding:14px 0\"><span class=\"who\">")
                // Ticked, a configuration is one side of a comparison of the reader's choosing (page.js); one with no counted try has nothing to compare.
                .Append($"<span class=\"pick\">{(counted.Count > 0 ? $"<wa-checkbox size=\"small\" data-pick=\"{E(c.Name!)}\"{(c.Live ? " data-live" : "")} aria-label=\"Compare {E(c.Name!)}\"></wa-checkbox>" : "")}<a class=\"name\" href=\"{U($"/{e.Id}/configuration/{c.Key}")}\">{E(c.Name!)}</a></span>{mark}<span class=\"small dim\">{E(made)}</span>")
                .Append(answer is null ? "" : $"<a class=\"small\" href=\"{AxesPages.Q($"/{e.Id}/compare", ("configuration", c.Name))}\">{E(c.Name!)} vs {E(against)}</a>")
                .Append("</span>");
            foreach (var column in Columns)
                page.Append(Cell(column, counted, live, c.Live, cell, against));
            page.Append("</div>");
        }
        if (named.Count(c => c.Retired is null && c.Tries?.Rows.Any(t => t.Invalid is null) == true) > 1)
            page.Append($"<div class=\"row\" style=\"display:flex;gap:14px;align-items:center;border-top-color:var(--rule)\"><wa-button id=\"compare\" size=\"small\" appearance=\"outlined\" disabled data-to=\"{U($"/{e.Id}/compare")}\">Compare</wa-button><span id=\"compare-hint\" class=\"small dim\" aria-live=\"polite\">Tick two configurations to compare them.</span></div>");
        if (live is not null)
            page.Append($"<div class=\"row note\" style=\"border-top-color:var(--rule)\">Each cell is the change vs {E(live.Name!)}, which is live, then the median. The strip is centred on live's median: a dot is one try, placed by how far it is from that median, from 200% less to 200% more, and a hollow dot is further out than that. The band is live's own spread. Colour means the change is confirmed.</div>");
        // The retired ones are out of the main list and one click away, each with its reason: nothing about them is deleted.
        if (named.Where(c => c.Retired is not null).ToList() is { Count: > 0 } retired)
        {
            page.Append($"<div class=\"row\" style=\"display:block\"><wa-details id=\"retired\" class=\"quiet\" appearance=\"plain\" summary=\"{retired.Count} retired\">")
                .Append("<div class=\"note\" style=\"padding:10px 0 4px\">A retired configuration keeps its tries, its predictions and its comparisons. Proctor makes no new tries of it until it is brought back.</div>");
            foreach (var c in retired)
                page.Append($"<div style=\"display:flex;flex-wrap:wrap;gap:6px 16px;align-items:baseline;padding:6px 0\"><a class=\"name\" href=\"{U($"/{e.Id}/configuration/{c.Key}")}\">{E(c.Name!)}</a>")
                    .Append($"<span class=\"small dim\">retired {E(c.Retired!.Since)} · {c.Made} {(c.Made == 1 ? "try" : "tries")}</span><span class=\"soft\">{E(c.Retired.Reason)}</span></div>");
            page.Append("</wa-details></div>");
        }
        if (EarlierLink(e) is { Length: > 0 } earlier) page.Append($"<div class=\"row note\" style=\"display:block\">{earlier}</div>");
        page.Append("</div></div></section>");
    }

    /// <summary>A line to what the bank still holds from before the exam was stages and axes; empty when there is nothing.</summary>
    static string EarlierLink(Exam e)
    {
        var tries = e.Earlier.Select(a => (a.Configuration, a.Staged ? "" : a.Stage, a.N)).Distinct().Count();
        return tries == 0 ? "" : $"<a id=\"earlier\" href=\"{U($"/{e.Id}/earlier")}\">{tries} earlier {(tries == 1 ? "attempt" : "attempts")}</a> <span class=\"dim\">made by an earlier proctor. In the bank, and in no number here.</span>";
    }

    /// <summary>
    /// Of proctor's answer about a configuration, the one setting that is that configuration itself. Proctor compares
    /// the change wherever the exam has it, so its pooled verdict may rest on tries of other settings (another model,
    /// say); a mark beside a configuration's name is about its own tries and nothing else (the user, 2026-11-27).
    /// </summary>
    internal static P.CellAnswer? Own(P.ComparisonAnswer? answer, string key) => answer?.Cells.FirstOrDefault(k => k.State == "compared" && k.Value.Configuration == key);

    /// <summary>The mark of a configuration against another, from its own setting on correctness checks held, as the comparison page words it.</summary>
    internal static string Light(P.CellAnswer? cell, string against) => cell?.Held is not { } held ? "<wa-badge class=\"tag tag-none\" appearance=\"outlined\">nothing to compare</wa-badge>" : held.Direction switch
    {
        "better" or "worse" => $"<wa-badge class=\"light {(held.Confirmed ? $"light-{held.Direction}" : "light-same")}\" appearance=\"accent\">{held.Direction} vs {E(against)} · {(held.Confirmed ? "confirmed" : cell.CouldConfirm ? "not confirmed" : "cannot confirm")}</wa-badge>",
        _ => $"<wa-badge class=\"light light-same\" appearance=\"accent\">no difference vs {E(against)}</wa-badge>",
    };

    /// <summary>
    /// A named configuration's standing against what is live, as proctor compares them: the mark (live, better or worse
    /// and whether confirmed, no difference, or why it is not compared), proctor's answer, the one setting compared, and live's name.
    /// </summary>
    internal static (string Mark, P.ComparisonAnswer? Answer, P.CellAnswer? Cell, string Against) Standing(string root, P.Exam exam, P.ConfigurationRow c, P.ConfigurationRow? live)
    {
        var counted = c.Tries?.Rows.Where(t => t.Invalid is null).ToList() ?? [];
        // Proctor's own comparison of this configuration with what is live: the verdict, and each measure.
        var (answer, refused) = c.Live || live is null || counted.Count == 0 ? (null, null) : Measured.Compare(root, exam, key => key == "configuration" ? c.Name : null, now: true);
        var cell = Own(answer, c.Key);
        var against = cell?.Against.Name ?? live?.Name ?? "live";

        var mark = c.Live ? "<wa-badge class=\"light light-live\" appearance=\"outlined\">live</wa-badge>"
        : counted.Count == 0 ? $"<wa-badge class=\"tag tag-none\" appearance=\"outlined\">{(c.NotGraded > 0 ? "not yet graded" : "no tries yet")}</wa-badge>"
        : refused is not null ? $"<wa-badge class=\"tag tag-none\" appearance=\"outlined\" title=\"{E(refused)}\">not compared{(c.NotGraded > 0 ? $" · {c.NotGraded} not yet graded" : "")}</wa-badge>"
        : answer is null ? ""
        : Light(cell, against);
        return (mark, answer, cell, against);
    }

    /// <summary>One measure of one configuration: the change against live and the median, over a strip centred on live's median; with nothing live, the median and the range.</summary>
    internal static string Cell(Column column, List<P.TryRow> counted, P.ConfigurationRow? live, bool isLive, P.CellAnswer? cell, string against, bool labelled = false)
    {
        var head = labelled ? $"<span class=\"label\">{E(column.Name)}</span>" : "";
        var values = counted.Select(column.Of).ToList();
        if (values.Count == 0 || values.Any(v => v is null)) return $"<span class=\"stat\">{head}<span class=\"dim\">–</span></span>";
        var sorted = values.Select(v => v!.Value).Order().ToList();
        var median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        var range = sorted[0] == sorted[^1] ? $"in all {sorted.Count} {(sorted.Count == 1 ? "try" : "tries")}" : $"{column.Said(sorted[0])} to {column.Said(sorted[^1])}";

        var reference = live?.Tries!.Rows.Where(t => t.Invalid is null).Select(column.Of).ToList();
        var liveSorted = reference is not null && reference.All(v => v is not null) ? reference.Select(v => v!.Value).Order().ToList() : null;
        var centre = liveSorted is null ? (double?)null : liveSorted.Count % 2 == 1 ? liveSorted[liveSorted.Count / 2] : (liveSorted[liveSorted.Count / 2 - 1] + liveSorted[liveSorted.Count / 2]) / 2;
        // No strip without a centre: nothing live, live lacks this number, or live's median is zero and "how far from it" has no scale.
        if (centre is not > 0)
            return $"<span class=\"stack\">{head}<span class=\"num strong\">{E(column.Said(median))}</span><span class=\"small dim num\">{E(range)}</span></span>";

        string At(double x) => (50 + Math.Clamp((x / centre.Value - 1) * 100, -200, 200) / 4).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        var measure = cell is null ? null : column.In(cell);
        var (delta, deltaClass, how) = ("reference", "delta d-none", "This is live: nothing to compare it with.");
        if (!isLive)
        {
            (delta, deltaClass, how) = measure switch
            {
                null => ("not compared", "delta d-none", $"Not compared with {against}."),
                { Direction: "same" } => ("no difference", "delta d-none", How(measure, against)),
                { Change: { } change } => ((change > 0 ? "+" : "−") + Math.Abs(change).ToString("0%", CultureInfo.InvariantCulture), measure.Confirmed ? $"delta d-{measure.Direction}" : "delta d-none", How(measure, against)),
                _ => (measure.Direction, measure.Confirmed ? $"delta d-{measure.Direction}" : "delta d-none", How(measure, against)),
            };
        }
        var label = $"{column.Name}: median {column.Said(median)}, {range}{(isLive ? "" : $", {delta} against {against}")}";
        var band = $"left:{At(liveSorted![0])};width:calc({At(liveSorted[^1])} - {At(liveSorted[0])})";
        var dots = string.Concat(sorted.Select(x => $"<span class=\"div-dot{(Math.Abs(x / centre.Value - 1) > 2 ? " div-out" : "")}\" style=\"left:{At(x)}\"></span>"));
        return $"<span class=\"stat\" title=\"{E(how)}\">{head}<span class=\"stat-line\"><span class=\"{deltaClass}\">{E(delta)}</span><span class=\"small dim num\">{E(column.Said(median))}</span></span>"
            + $"<span class=\"div\" role=\"img\" aria-label=\"{E(label)}\"><span class=\"div-band\" style=\"{band}\"></span><span class=\"div-zero\"></span>{dots}</span>{(labelled ? $"<span class=\"small dim num\">{E(range)}</span>" : "")}</span>";
    }

    static string How(P.Measure m, string against) =>
        $"Against {against}: Mann–Whitney, try against try, exact two-sided p = {Measured.PValue(m.P)}, {(m.Confirmed ? "confirmed" : "not confirmed")}; higher in {m.Higher}, lower in {m.Lower}, tied in {m.Tied} of {m.Pairings} pairings";

    internal static void Predictions(StringBuilder page, Exam e, List<P.PredictionReport> predictions, List<P.ConfigurationRow> named, string note)
    {
        Section(page, "Predictions", note, "display:flex;flex-direction:column;gap:22px");
        if (predictions.Count == 0)
            page.Append($"<div class=\"said soft\">None. A prediction is a file, <span class=\"mono\">exams/{E(e.Id)}/predictions/&lt;name&gt;.json</span>.</div>");
        string Name(string key) => named.FirstOrDefault(c => c.Key == key)?.Name ?? key;
        foreach (var p in predictions)
        {
            var (mine, other) = (Name(p.Configuration), Name(p.Against));
            var compare = AxesPages.Q($"/{e.Id}/compare", ("configuration", mine), ("against", other));
            var supported = p.Changes.Count(c => c.Outcome == P.PredictionOutcomes.Supported);
            page.Append("<div style=\"min-width:760px\"><div style=\"display:flex;flex-direction:column;gap:4px;padding-bottom:10px\">")
                .Append($"<span class=\"claim\">{E(p.Note ?? p.Name)}</span>")
                .Append("<span style=\"display:flex;flex-wrap:wrap;gap:6px 12px;align-items:baseline\" class=\"small\">")
                .Append($"<span class=\"soft\"><a href=\"{compare}\">{E(mine)} vs {E(other)}</a></span>")
                .Append($"<span class=\"soft\">{supported} of {p.Changes.Count} {(p.Changes.Count == 1 ? "change" : "changes")} supported</span>")
                .Append(p.PostHoc ? $"<wa-badge class=\"tag\" appearance=\"outlined\" title=\"{E(p.Labels.FirstOrDefault(l => l.StartsWith("Post hoc")) ?? "")}\">post hoc</wa-badge>" : "")
                .Append(p.StandardChanged ? $"<wa-badge class=\"tag\" appearance=\"outlined\" title=\"{E(p.Labels.FirstOrDefault(l => l.StartsWith("The standard")) ?? "")}\">standard changed</wa-badge>" : "")
                .Append($"<span class=\"dim\">written {E(p.Written)}</span></span></div>")
                .Append("<div class=\"row row-head pred\"><span>Change predicted</span><span>Outcome</span><span>Evidence</span><span>The author's reason</span></div>");
            foreach (var c in p.Changes)
            {
                var what = c.Check ?? P.Predictions.Metrics.GetValueOrDefault(c.Metric ?? "", c.Metric ?? "");
                var (word, tag) = c.Outcome switch
                {
                    P.PredictionOutcomes.Supported => ("supported", "tag tag-right"),
                    P.PredictionOutcomes.Contradicted => ("contradicted", "tag tag-wrong"),
                    P.PredictionOutcomes.NoEffect => ("no effect", "tag"),
                    P.PredictionOutcomes.Inconclusive => ("inconclusive", "tag tag-none"),
                    _ => ("not run", "tag tag-none"),
                };
                var evidence = c.Measure is { Median: { } a, OtherMedian: { } b } m
                    ? m.Direction == "same" && a == b ? $"{Number(a)}{(c.Check is null ? "" : " stages")} on both sides, in {c.Tries} against {c.AgainstTries} tries"
                        : $"{(c.Check is null ? "median" : "held in a median of")} {Number(a)} vs {Number(b)}{(c.Check is null ? "" : " stages")}, p = {Measured.PValue(m.P)}"
                    : c.Outcome == P.PredictionOutcomes.NotRun ? "a side has no graded try" : "";
                page.Append($"<div class=\"row pred\"><span><span class=\"mono\">{E(what)}</span> {(c.Direction == P.Predictions.More ? "higher" : "lower")}</span>")
                    .Append($"<span><a href=\"{compare}\" style=\"text-decoration:none\" title=\"{E(c.Sentence)}\"><wa-badge class=\"{tag}\" appearance=\"outlined\">{word}</wa-badge></a></span>")
                    .Append($"<span class=\"soft num\">{E(evidence)}</span><span class=\"small dim\">{E(c.Because ?? "")}</span></div>");
            }
            page.Append("</div>");
        }
        page.Append("</div></section>");
    }

    static void Axes(StringBuilder page, P.Exam exam)
    {
        Section(page, "Axes", "An axis is one named thing that can vary, like model or a prompt. Axes are defined as part of the exam. They may be very broad or extremely narrow tweaks.");
        page.Append("<div style=\"min-width:620px\">");
        foreach (var ((axis, def), i) in exam.Axes!.Def.Select((a, i) => (a, i)))
        {
            page.Append($"<div class=\"row axis{(i == 0 ? " first" : "")}\"><span class=\"stack\"><span class=\"mono strong\">{E(axis)}</span><span class=\"small dim\">{E(def.About ?? "")}</span></span><div>");
            foreach (var (value, v) in def.Values!)
                page.Append($"<div class=\"val\"><span><span class=\"mono\">{E(value)}</span>{(value == def.Live ? " <wa-badge class=\"tag tag-live\" appearance=\"outlined\">live</wa-badge>" : "")}</span><span class=\"soft\">{E(v?.Note ?? "")}</span></div>");
            page.Append("</div></div>");
        }
        page.Append("</div></div></section>");
    }

    static void Stages(StringBuilder page, P.ConfigurationsFile stats)
    {
        var how = stats.Chained
            ? "This exam's stages are chained: they run in order in one workspace, so a stage meets what the stages before it left."
            : "Each stage starts from the workspace as the try began, so no stage sees another's files.";
        Section(page, "Stages", $"Stages divide the exam into chunks. Use stages to break a large exam into manageable pieces or combine several small tasks into a single exam. {how} The agent starts every stage with a fresh context.");
        page.Append("<div style=\"min-width:620px\">");
        foreach (var s in stats.Stages)
            page.Append($"<div class=\"row stages{(s.Place == 1 ? " first" : "")}\"><span class=\"dim num\">{s.Place}</span><span class=\"mono\">{E(s.Id)}</span><span class=\"soft\">{E(s.About)}</span><span class=\"small dim r\">{s.Checks?.Count ?? 0} checks</span></div>");
        page.Append("</div></div></section>");
    }

    static void Checks(StringBuilder page, P.Exam exam, P.ConfigurationsFile stats)
    {
        Section(page, "Checks", "A check is one yes-or-no question asked of a finished stage. Its role says what the answer is used for.");
        page.Append("<div style=\"min-width:760px\"><div class=\"row row-head checks\"><span>Check</span><span></span><span>What it asks</span><span class=\"r\">Asked of</span></div>");
        var checks = stats.Stages.SelectMany(s => (s.Checks ?? []).Select(k => (Name: k.Key, Role: k.Value, Stage: s.Id))).GroupBy(k => k.Name)
            .Select(g => (Name: g.Key, g.First().Role, Stages: g.Count())).OrderBy(k => Array.IndexOf(RoleOrder, k.Role)).ToList();
        foreach (var k in checks)
        {
            var spec = exam.Stages.Select(s => exam.ChecksFor(s).GetValueOrDefault(k.Name)).FirstOrDefault(c => c is not null);
            var (tag, says) = RoleTag[k.Role];
            page.Append($"<div class=\"row checks\"><span class=\"mono\">{E(k.Name)}</span><span>{Tagged(tag, k.Role, says)}</span>")
                .Append($"<span class=\"soft\">{E(spec is null ? "" : P.Checks.Describe(spec.Spec) ?? "")}</span>")
                .Append($"<span class=\"small dim r\">{(k.Stages == stats.Stages.Count ? "every stage" : $"{k.Stages} of {stats.Stages.Count} stages")}</span></div>");
        }
        page.Append("</div></div></section>");
    }

    internal static void Section(StringBuilder page, string heading, string note, string? bodyStyle = null) =>
        page.Append($"<section class=\"sec\" aria-label=\"{E(heading)}\"><div class=\"sec-head\"><h2>{E(heading)}</h2><span class=\"note\">{E(note)}</span></div><div class=\"sec-body\"{(bodyStyle is null ? "" : $" style=\"{bodyStyle}\"")}>");

    internal static string Fact(string term, string html) => $"<div><dt>{E(term)}</dt><dd>{html}</dd></div>";

    internal static string Number(double x) => x.ToString("0.##", CultureInfo.InvariantCulture);
    internal static string Count(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
    internal static string Tokens(double x) => x >= 1e6 ? (x / 1e6).ToString("0.00", CultureInfo.InvariantCulture) + "M" : x >= 1e3 ? (x / 1e3).ToString("0", CultureInfo.InvariantCulture) + "k" : Count(x);
    internal static string Time(double ms) => Math.Round(ms / 1000) is var s && s >= 60 ? $"{(int)(s / 60)}m {(int)(s % 60):00}s" : $"{s:0}s";
}
