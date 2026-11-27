using System.Text;
using P = Proctor;
using static Auspex.Pages;
using static Auspex.ExamPage;

namespace Auspex;

/// <summary>
/// The pages of an exam with axes (proctor's plans/axes-build.md, slice 3d; project/view/exam-page-axes.md): the exam
/// as the configurations somebody named or ran, one configuration, and one comparison. They are the addresses
/// `proctor compare` and `proctor coverage` put in their answers, so a claim an agent makes can be opened and checked.
/// Every number is proctor's (Measured.cs). The pages hold no prose from an agent and recommend nothing: a judgement is
/// asked of the agent the person works with, and each page says how.
/// </summary>
static class AxesPages
{
    /// <summary>A path with a query, as an href: the path's segments and each value escaped.</summary>
    internal static string Q(string path, params (string Key, string? Value)[] query) =>
        U(path) + (query.Where(q => q.Value is { Length: > 0 }).ToList() is { Count: > 0 } given ? "?" + E(string.Join("&", given.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value!)}"))) : "");

    static string Label(IEnumerable<KeyValuePair<string, string>> values) => string.Join(" × ", values.Select(v => $"{v.Key}={v.Value}"));

    const string Mono = "class=\"mono\"";

    /// <summary>Coverage: the exam's axes, the configurations it names or has tries of, and what is live across the axes that are not ours to choose.</summary>
    public static string Exam(string root, Exam e, string? asOf)
    {
        var page = Open($"coverage · {e.Id}", [("exams", "/"), (e.Id, e.Path), ("coverage", null)]);
        var (exam, problem) = Measured.Load(root, e.Id);
        var (coverage, coverageProblem) = exam is null ? (null, problem) : Measured.Coverage(root, exam, asOf);
        page.Append($"<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">coverage</span><h1 class=\"title\">{E(e.Id)}</h1>");
        if (exam is null || coverage is null)
        {
            // Proctor could not read the exam as it is now: say so in its words, and still list what the bank holds.
            page.Append($"</header><div class=\"said\"><b>Proctor cannot read this exam as it stands</b>, so nothing is counted here.</div><pre class=\"mono small soft\">{E(problem ?? coverageProblem ?? "")}</pre>");
            Tries(page, e, e.Attempts.Select(a => a.Variant).Distinct().ToList());
            return Close(page);
        }
        var axes = exam.Axes!;
        page.Append("<p class=\"desc\">Which configurations of this exam have tries, and which have none.</p><dl class=\"facts\">")
            .Append(Fact("Counted as of", E(coverage.AsOf))).Append(Fact("Configurations", coverage.Configurations.Count.ToString()))
            .Append(coverage.Stale > 0 ? Fact("Stale", $"{coverage.Stale} {(coverage.Stale == 1 ? "try" : "tries")}") : "").Append("</dl></header>");
        if (asOf is not null) page.Append($"<div class=\"said soft\">This is the bank as it stood then. <a href=\"{U($"/{e.Id}/coverage")}\">As it is now</a></div>");
        if (coverage.Stale > 0) page.Append("<div class=\"said soft\">Stale tries are left out: they were made before a value or a stage changed.</div>");

        Section(page, "Axes", "Each value, and the tries of every configuration that holds it.");
        foreach (var ((axis, def), i) in axes.Def.Select((d, i) => (d, i)))
        {
            page.Append($"<div class=\"row axis{(i == 0 ? " first" : "")}\"><span class=\"stack\"><span {Mono}>{E(axis)}</span><span class=\"small soft\">{E(def.About ?? "")}</span></span><span>");
            foreach (var v in coverage.Values.Where(v => v.Axis == axis))
            {
                var mark = v.Live ? " <span class=\"light light-live\">live</span>" : v.Reference ? " <span class=\"tag tag-none\" title=\"Held here when another axis is looked at\">reference</span>" : "";
                var compare = def.Live is { } live && !v.Live
                    ? $" · <a href=\"{Q($"/{e.Id}/compare", ("change", $"{axis}={v.Value}"), ("against", $"{axis}={live}"), ("as_of", coverage.AsOf))}\">{E(v.Value)} vs {E(live)}</a>" : "";
                page.Append($"<div class=\"val\"><span><span {Mono}>{E(v.Value)}</span>{mark}</span><span class=\"stack\"><span class=\"small\">{(v.Tries == 0 ? "<b>no tries</b>" : $"{v.Tries} {(v.Tries == 1 ? "try" : "tries")} in {v.Configurations} configuration{(v.Configurations == 1 ? "" : "s")}")}{compare}</span>")
                    .Append(v.Note is { } note ? $"<span class=\"small soft\">{E(note)}</span>" : "").Append("</span></div>");
            }
            page.Append("</span></div>");
        }
        page.Append("</div></section>");

        Section(page, "Configurations", "The ones this exam names or has tries of. Any other was never asked for: nobody runs every combination.");
        Rows(page, e.Id, coverage.Configurations, coverage.AsOf);
        page.Append("</div></section>");

        if (coverage.LiveSpan is { } span && span.Across.Count > 0)
        {
            Section(page, $"Live, across {string.Join(" and ", span.Across)}", $"{string.Join(", ", span.Fixed)} is what is in use. {string.Join(" and ", span.Across)} {(span.Across.Count == 1 ? "has" : "have")} no live value, so {(span.Across.Count == 1 ? "it is" : "they are")} not ours to choose: each row is a setting some user may be in. "
                + $"{span.WithTries} of {span.Configurations} {(span.Configurations == 1 ? "has" : "have")} tries.{(span.Listed ? "" : " There are too many to list; these are the ones with tries.")}");
            Rows(page, e.Id, span.Rows, coverage.AsOf);
            page.Append("</div></section>");
        }
        page.Append("<section class=\"sec sec-plain\" aria-label=\"How this is counted\"><div class=\"sec-head\"><span class=\"label\">How this is counted</span></div>")
            .Append($"<div class=\"sec-body note\" style=\"display:flex;flex-direction:column;gap:6px;max-width:900px\"><div>{E(coverage.Methods)}</div>")
            .Append(coverage.Excluded.Count > 0 ? $"<div>Declared impossible, and never made: {E(string.Join("; ", coverage.Excluded.Select(Label)))}.</div>" : "");
        Ask(page, e.Id, axes.Def.Where(d => d.Value.Live is not null).SelectMany(d => d.Value.Values!.Keys.Where(v => v != d.Value.Live).Select(v => $"{d.Key}={v}")).FirstOrDefault());
        return Close(page.Append("</div></section>"));
    }

    const string RowColumns = "grid-template-columns:minmax(260px,1.6fr) 110px minmax(150px,.8fr) minmax(200px,1.2fr)";

    static void Rows(StringBuilder page, string exam, List<P.CoverageRow> rows, string asOf)
    {
        page.Append($"<div style=\"min-width:860px\"><div class=\"row row-head\" style=\"{RowColumns}\"><span>Configuration</span><span>Tries</span><span>Newest</span><span></span></div>");
        foreach (var (r, i) in rows.Select((r, i) => (r, i)))
        {
            // How it differs from what is live, which is what a reader scans for; its full label is beneath.
            var differs = r.Live ? "" : string.Join(", ", r.OffLive);
            var what = r.Name ?? (r.Live ? "what is live" : differs.Length > 0 ? differs : Label(r.Values));
            var notes = string.Join(" · ", new[]
            {
                r.Lost > 0 ? $"{r.Lost} lost" : null, r.Stale > 0 ? $"{r.Stale} stale" : null,
                r.Together == P.Combinations.More ? "more than its values put together" : r.Together == P.Combinations.Unclear ? "cannot be told from its values alone" : r.Together == P.Combinations.NotChecked ? "not checked against its values alone" : null,
                !r.Live && r.OffLive.Count > 0 ? $"<a href=\"{Q($"/{exam}/compare", ("configuration", r.Configuration), ("as_of", asOf))}\">vs what is live</a>" : null,
            }.OfType<string>());
            page.Append($"<div class=\"row{(i == 0 ? " first" : "")}\" style=\"{RowColumns}\"><span class=\"stack\"><span style=\"display:flex;gap:10px;align-items:center;flex-wrap:wrap\"><a class=\"name\" href=\"{U($"/{exam}/configuration/{r.Configuration}")}\">{E(what)}</a>{(r.Name is not null && differs.Length > 0 ? $"<span {Mono}>{E(differs)}</span>" : "")}{(r.Live ? "<span class=\"light light-live\">live</span>" : "")}</span>")
                .Append($"<span class=\"small dim mono\">{E(Label(r.Values))}</span></span>")
                .Append(r.Tries == 0 ? $"<span><b>not run</b> <span class=\"small dim\">0 of {r.Wanted}</span></span><span></span>" : $"<span class=\"num\">{r.Tries} of {r.Wanted}</span><span class=\"stack\"><span class=\"small\">{E(r.Last ?? "")}</span><span class=\"small dim\">{Ago(r.DaysSinceLast)}</span></span>")
                .Append($"<span class=\"small soft\">{notes}</span></div>");
        }
        page.Append("</div>");
    }

    static string Ago(double? days) => days is not { } d ? "" : d < 1 ? "today" : $"{d:0} day{(d < 1.5 ? "" : "s")} before";

    /// <summary>The tries the bank holds of these configurations, newest first, each with its stages; nothing counted.</summary>
    static void Tries(StringBuilder page, Exam e, IReadOnlyCollection<string> configurations)
    {
        var tries = e.Attempts.Where(a => a.ById && configurations.Contains(a.Variant)).GroupBy(a => a.N).OrderByDescending(g => g.Key, StringComparer.Ordinal).ToList();
        if (tries.Count == 0) { page.Append("<div class=\"said soft\">No try of it in the bank.</div>"); return; }
        foreach (var t in tries)
            page.Append($"<div class=\"row\" style=\"display:flex;gap:6px 14px;flex-wrap:wrap;align-items:baseline\"><a {Mono} href=\"{U($"/{e.Id}/try/{t.Key}")}\">{E(t.Key)}</a>")
                .Append(string.Concat(t.Select(a => $"<span class=\"small\"><a href=\"{U(a.Path)}\">{E(a.Task)}</a>{(a.Status == "completed" ? "" : $" <span class=\"tag tag-none\">{E(a.Status)}</span>")}</span>")))
                .Append(configurations.Count > 1 ? $"<span class=\"small dim\">{E(t.First().Variant)}</span>" : "").Append("</div>");
    }

    /// <summary>One comparison, at the address proctor's answer gave: the numbers as they stood then, and as they are now when tries have been added since.</summary>
    public static string Compare(string root, Exam e, Func<string, string?> query)
    {
        var (exam, problem) = Measured.Load(root, e.Id);
        var (answer, why) = exam is null ? (null, problem) : Measured.Compare(root, exam, query);
        if (answer is null)
            return Close(Open($"comparison · {e.Id}", [("exams", "/"), (e.Id, e.Path), ("comparison", null)])
                .Append($"<header><span class=\"kind\">comparison</span><h1 class=\"title\">{E(e.Id)}</h1></header><div class=\"said\"><b>Proctor did not answer this.</b></div><pre class=\"mono small soft\">{E(why ?? "")}</pre>"));

        // One setting compared is the page designed on the canvas; every setting of a change is drawn here.
        if (query("settings") != "all" && ComparePage.Render(root, e, exam!, answer, query) is { } designed) return designed;

        var (change, against) = (Label(answer.Change), Label(answer.Against));
        var page = Open($"{change} vs {against} · {e.Id}", [("exams", "/"), (e.Id, e.Path), ($"{change} vs {against}", null)]);
        page.Append($"<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">comparison · every setting</span><h1 class=\"title\">{E(change)} vs {E(against)}</h1>");
        if (answer.Configuration is { } asked)
            page.Append($"<p class=\"desc\">Asked of the configuration <a href=\"{U($"/{e.Id}/configuration/{exam!.Axes!.Named.FirstOrDefault(c => c.Name == asked)?.Key ?? asked}")}\">{E(asked)}</a>: everything it changes, together, in every setting of the other axes that has tries of it. A setting on another value of an axis is another configuration.</p>");
        // The address says as of when. A claim quoted last week opens on the number it quoted; what has been made since is said beside it.
        var pinned = query("as_of") is { Length: > 0 };
        page.Append("<dl class=\"facts\">").Append(Fact(pinned ? "As it stood at" : "As of", E(answer.AsOf)))
            .Append(Fact("Settings", $"{answer.Cells.Count(c => c.State == "compared")} compared of {answer.Cells.Count}"))
            .Append(answer.Within is { Count: > 0 } within ? Fact("Held", $"<span {Mono}>{E(Label(within))}</span>") : "").Append("</dl></header>");
        if (answer.TriesSince > 0 && Measured.Compare(root, exam!, query, now: true).Answer is { } now)
            page.Append($"<div class=\"said soft\">{answer.TriesSince} {(answer.TriesSince == 1 ? "try has" : "tries have")} been made in these configurations since. <a href=\"{E(now.Address)}\">As it is now</a>: {E(now.Sentence)}</div>");
        page.Append($"<div class=\"said\">{E(answer.Sentence)}</div>");
        // Proctor's sentence already ends with the cautions about its configurations; the rest are listed under it.
        foreach (var caution in answer.Cautions.Where(c => !answer.Sentence.Contains(c)))
            page.Append($"<div class=\"said soft\">{E(caution)}</div>");

        const string columns = "grid-template-columns:minmax(200px,1fr) minmax(200px,1.2fr) minmax(200px,1.2fr) minmax(220px,1.2fr)";
        Section(page, "Settings", "One row for each setting of the other axes. The two sides are compared try against try within a setting, never across settings.");
        page.Append($"<div style=\"min-width:940px\"><div class=\"row row-head\" style=\"{columns}\"><span>Every other axis held at</span><span>{E(change)}<br>checks held, each try</span><span>{E(against)}<br>checks held, each try</span><span>Found</span></div>");
        foreach (var (cell, i) in answer.Cells.Select((c, i) => (c, i)))
        {
            string Side(P.SideAnswer side) =>
                $"<span class=\"stack\" style=\"gap:6px\"><a href=\"{E(side.Address)}\">{E(side.Name ?? "configuration")}</a><span class=\"num\" style=\"display:flex;gap:4px 10px;flex-wrap:wrap\">" + (side.Tries.Count == 0 ? "<b>not run</b>"
                    : string.Concat(side.Tries.Select(t => $"<a class=\"{(t.Invalid is null ? "" : "dim")}\" href=\"{E(t.Address)}\" title=\"{E(t.Id)}, made {E(t.Made)}{(t.Invalid is { } invalid ? $"; not counted: {E(invalid)}" : "")}\">{t.Held}/{t.Of}</a>"))) + "</span></span>";
            var found = cell.Held is { } m
                ? $"<span class=\"strong{(m.Confirmed ? $" d-{m.Direction}" : "")}\">{E(m.Direction is "better" or "worse" ? m.Direction : "no difference")}</span><span class=\"small soft num\">p = {Measured.PValue(m.P)} · {(m.Confirmed ? "confirmed" : cell.CouldConfirm ? "not confirmed" : "this many tries cannot confirm anything")}</span>"
                : $"<b>not run</b><span class=\"small soft\">{cell.State switch { "not_run" => $"no try of {E(change)} here", "live_not_run" => $"no try of {E(against)} here", _ => "neither side" }}</span>";
            page.Append($"<div class=\"row{(i == 0 ? " first" : "")}\" style=\"{columns}\"><span {Mono}>{E(cell.Holding.Count == 0 ? "the one configuration" : Label(cell.Holding))}</span>{Side(cell.Value)}{Side(cell.Against)}<span class=\"stack\" style=\"gap:4px\">{found}");
            if (cell.Checks is { Count: > 0 } checks)
                page.Append("<wa-details class=\"quiet\" appearance=\"plain\" summary=\"By check\"><div class=\"small\">")
                    .Append(string.Concat(checks.Select(k => $"<div style=\"display:grid;grid-template-columns:minmax(0,1fr) 70px 70px;gap:8px;padding:3px 0\"><span class=\"mono\"{(k.About is { } about ? $" title=\"{E(about)}\"" : "")}>{E(k.Check)}</span><span class=\"num\">{k.Value} of {k.ValueTries}</span><span class=\"num soft\">{k.Against} of {k.AgainstTries}</span></div>")))
                    .Append("<div class=\"dim\">Tries that held it, each side. Counts only: nothing is tested per check here.</div></div></wa-details>");
            page.Append("</span></div>");
        }
        page.Append("</div>");
        if (answer.ReadAsOne is { } one)
            page.Append($"<div class=\"row said\" style=\"display:block;max-width:none\"><b>Read as one</b>, over {one.Covered} of {one.Of} settings{(one.Across.Count > 0 ? $", across {E(string.Join(" and ", one.Across))}" : "")}: {E(one.Held.Direction)} · <span class=\"num\">p = {Measured.PValue(one.Held.P)}</span> · {(one.Held.Confirmed ? "confirmed" : one.CanConfirm ? "not confirmed" : "this many tries cannot confirm anything")}.</div>");
        page.Append("</div></section>");

        page.Append("<section class=\"sec sec-plain\" aria-label=\"How this is counted\"><div class=\"sec-head\"><span class=\"label\">How this is counted</span></div>")
            .Append($"<div class=\"sec-body note\" style=\"display:flex;flex-direction:column;gap:6px;max-width:900px\"><div>{E(answer.Methods)}</div>")
            .Append(answer.Power is { } power ? $"<div>{E(power.Note)}</div>" : "")
            .Append(answer.Timing is { ValueFirst: not null } timing ? $"<div>Tries made: {E(change)} {E(timing.ValueFirst!)} to {E(timing.ValueLast!)}; {E(against)} {E(timing.AgainstFirst ?? "none")} to {E(timing.AgainstLast ?? "none")}.</div>" : "");
        Ask(page, e.Id, answer.Configuration is null && answer.Change.Count == 1 ? change : null, answer.Configuration);
        return Close(page.Append("</div></section>"));
    }

    /// <summary>
    /// What to ask the agent you work with. The page measures and stops there: a judgement (is it better, is there
    /// enough evidence, what to ship) is the agent's to make from proctor's answers, with links back here.
    /// </summary>
    static void Ask(StringBuilder page, string exam, string? value, string? configuration = null)
    {
        var about = configuration is not null ? $"the {configuration} configuration" : value ?? "a value";
        var command = configuration is not null ? $"proctor compare {exam} --configuration {configuration}" : $"proctor compare {exam} {value ?? "<axis>=<value>"}";
        page.Append("<div>This page shows what was measured, and no more. For a judgement, ask the agent you work with, for example: ")
            .Append($"<q>What does {E(about)} change, and how much evidence is there for it?</q> or <q>Where has nothing been tried?</q> ")
            .Append($"It answers from <span class=\"mono\">{E(command)}</span> and <span class=\"mono\">proctor coverage {E(exam)}</span>, and can link each claim to a page here. The skill for it is <span class=\"mono\">skills/proctor-answers</span>.</div>");
    }
}
