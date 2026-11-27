using System.Text;
using P = Proctor;
using static Auspex.Pages;

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

    /// <summary>The exam: its axes, the configurations it names or has tries of, and what is live across the axes that are not ours to choose.</summary>
    public static string Exam(string root, Exam e, string? asOf)
    {
        var page = Open(e.Id, [("exams", "/"), (e.Id, null)]);
        page.Append($"<h1><code>{E(e.Id)}</code></h1>");
        if (e.Description is not null) page.Append($"<p>{E(e.Description)}</p>");
        var (exam, problem) = Measured.Load(root, e.Id);
        var (coverage, coverageProblem) = exam is null ? (null, problem) : Measured.Coverage(root, exam, asOf);
        if (exam is null || coverage is null)
        {
            // Proctor could not read the exam as it is now: say so in its words, and still list what the bank holds.
            page.Append($"<p class=\"stage-status\"><b>Proctor cannot read this exam as it stands</b>, so nothing is counted here.</p><pre>{E(problem ?? coverageProblem ?? "")}</pre>");
            Tries(page, e, e.Attempts.Select(a => a.Variant).Distinct().ToList());
            return Close(page);
        }
        var axes = exam.Axes!;
        page.Append($"<p class=\"meta\">Counted as of {E(coverage.AsOf)}. {(asOf is null ? "" : $"<a href=\"{U(e.Path)}\">As it is now</a>. ")}{(coverage.Stale > 0 ? $"{coverage.Stale} stale {(coverage.Stale == 1 ? "try" : "tries")} left out: made before a value or a stage changed." : "")}</p>");
        Ask(page, e.Id, axes.Def.Where(d => d.Value.Live is not null).SelectMany(d => d.Value.Values!.Keys.Where(v => v != d.Value.Live).Select(v => $"{d.Key}={v}")).FirstOrDefault());

        page.Append("<h2>Axes</h2><table class=\"axes\"><tr><th>Axis</th><th>Values, and the tries of every configuration holding each</th></tr>");
        foreach (var (axis, def) in axes.Def)
        {
            page.Append($"<tr><td><code>{E(axis)}</code><div class=\"meta\">{E(def.About ?? "")}</div></td><td>");
            foreach (var v in coverage.Values.Where(v => v.Axis == axis))
            {
                var mark = v.Live ? " <span class=\"meta\">live</span>" : v.Reference ? " <span class=\"meta\">held here when another axis is looked at</span>" : "";
                var compare = def.Live is { } live && !v.Live
                    ? $" · <a href=\"{Q($"/{e.Id}/compare", ("change", $"{axis}={v.Value}"), ("against", $"{axis}={live}"), ("as_of", coverage.AsOf))}\">against {E(live)}</a>" : "";
                page.Append($"<div class=\"value\"><code>{E(v.Value)}</code>{mark} <span class=\"meta\">{(v.Tries == 0 ? "<b>no tries</b>" : $"{v.Tries} {(v.Tries == 1 ? "try" : "tries")} in {v.Configurations} configuration{(v.Configurations == 1 ? "" : "s")}")}{compare}</span>")
                    .Append(v.Note is { } note ? $"<div class=\"says\">{E(note)}</div>" : "").Append("</div>");
            }
            page.Append("</td></tr>");
        }
        page.Append("</table>");

        page.Append("<h2>Configurations</h2><p class=\"meta\">The ones this exam names or has tries of. Any other was never asked for: nobody runs every combination.</p>");
        Rows(page, e.Id, coverage.Configurations, coverage.AsOf);

        if (coverage.LiveSpan is { } span && span.Across.Count > 0)
        {
            page.Append($"<h2>What is live, across {E(string.Join(" and ", span.Across))}</h2>")
                .Append($"<p class=\"meta\">{E(string.Join(", ", span.Fixed))} is what is in use. {E(string.Join(" and ", span.Across))} {(span.Across.Count == 1 ? "has" : "have")} no live value, so {(span.Across.Count == 1 ? "it is" : "they are")} not ours to choose: each row is a setting some user may be in. ")
                .Append($"{span.WithTries} of {span.Configurations} {(span.Configurations == 1 ? "has" : "have")} tries.{(span.Listed ? "" : " There are too many to list; these are the ones with tries.")}</p>");
            Rows(page, e.Id, span.Rows, coverage.AsOf);
        }
        if (coverage.Excluded.Count > 0)
            page.Append($"<p class=\"meta\">Declared impossible, and never made: {E(string.Join("; ", coverage.Excluded.Select(Label)))}.</p>");
        page.Append($"<details class=\"methods\"><summary>How these are counted</summary><p>{E(coverage.Methods)}</p></details>");
        return Close(page);
    }

    static void Rows(StringBuilder page, string exam, List<P.CoverageRow> rows, string asOf)
    {
        page.Append("<table class=\"configurations\"><tr><th>Configuration</th><th>Tries</th><th>Newest</th><th></th></tr>");
        foreach (var r in rows)
        {
            var name = r.Name is { } n ? $"<b>{E(n)}</b> " : "";
            var what = r.Live ? "what is live" : r.OffLive.Count > 0 ? string.Join(", ", r.OffLive) : "";
            var notes = string.Join(" · ", new[]
            {
                r.Lost > 0 ? $"{r.Lost} lost" : null, r.Stale > 0 ? $"{r.Stale} stale" : null,
                r.Together == P.Combinations.More ? "more than its values put together" : r.Together == P.Combinations.Unclear ? "cannot be told from its values alone" : r.Together == P.Combinations.NotChecked ? "not checked against its values alone" : null,
                !r.Live && r.OffLive.Count > 0 ? $"<a href=\"{Q($"/{exam}/compare", ("configuration", r.Configuration), ("as_of", asOf))}\">against what is live</a>" : null,
            }.OfType<string>());
            page.Append($"<tr><td><a href=\"{U($"/{exam}/configuration/{r.Configuration}")}\">{name}<code>{E(what.Length > 0 ? what : Label(r.Values))}</code></a><div class=\"meta\">{E(Label(r.Values))}</div></td>")
                .Append(r.Tries == 0 ? $"<td><b>not run</b> <span class=\"meta\">0 of {r.Wanted}</span></td><td></td>" : $"<td>{r.Tries} of {r.Wanted}</td><td>{E(r.Last ?? "")} <span class=\"meta\">{Ago(r.DaysSinceLast)}</span></td>")
                .Append($"<td class=\"meta\">{notes}</td></tr>");
        }
        page.Append("</table>");
    }

    static string Ago(double? days) => days is not { } d ? "" : d < 1 ? "today" : $"{d:0} day{(d < 1.5 ? "" : "s")} before";

    /// <summary>The tries the bank holds of these configurations, newest first, each with its stages; nothing counted.</summary>
    static void Tries(StringBuilder page, Exam e, IReadOnlyCollection<string> configurations)
    {
        var tries = e.Attempts.Where(a => a.ById && configurations.Contains(a.Variant)).GroupBy(a => a.N).OrderByDescending(g => g.Key, StringComparer.Ordinal).ToList();
        if (tries.Count == 0) { page.Append("<p class=\"empty\">No try of it in the bank.</p>"); return; }
        page.Append("<ul class=\"tries\">");
        foreach (var t in tries)
            page.Append($"<li><a href=\"{U($"/{e.Id}/try/{t.Key}")}\"><code>{E(t.Key)}</code></a> ")
                .Append(string.Join(" · ", t.Select(a => $"<a href=\"{U(a.Path)}\">{E(a.Task)}</a>{(a.Status == "completed" ? "" : $" <span class=\"meta\">{E(a.Status)}</span>")}")))
                .Append(configurations.Count > 1 ? $" <span class=\"meta\">{E(t.First().Variant)}</span>" : "").Append("</li>");
        page.Append("</ul>");
    }

    /// <summary>One comparison, at the address proctor's answer gave: the numbers as they stood then, and as they are now when tries have been added since.</summary>
    public static string Compare(string root, Exam e, Func<string, string?> query)
    {
        var (exam, problem) = Measured.Load(root, e.Id);
        var (answer, why) = exam is null ? (null, problem) : Measured.Compare(root, exam, query);
        var page = Open($"compare · {e.Id}", [("exams", "/"), (e.Id, e.Path), ("comparison", null)]);
        if (answer is null)
            return Close(page.Append($"<h1>Comparison</h1><p class=\"stage-status\"><b>Proctor did not answer this.</b></p><pre>{E(why ?? "")}</pre>"));

        var (change, against) = (Label(answer.Change), Label(answer.Against));
        page.Append($"<h1><code>{E(change)}</code> against <code>{E(against)}</code></h1>");
        if (answer.Configuration is { } asked) page.Append($"<p class=\"meta\">Asked of the configuration <a href=\"{U($"/{e.Id}/configuration/{exam!.Axes!.Named.FirstOrDefault(c => c.Name == asked)?.Key ?? asked}")}\">{E(asked)}</a>: everything it changes, together.</p>");
        if (answer.Within is { Count: > 0 } within) page.Append($"<p class=\"meta\">Held: {E(Label(within))}.</p>");

        // The address says as of when. A claim quoted last week opens on the number it quoted; what has been made since is said beside it.
        var pinned = query("as_of") is { Length: > 0 };
        page.Append($"<p class=\"asof\">{(pinned ? "As it stood at" : "As of")} <b>{E(answer.AsOf)}</b>.");
        if (answer.TriesSince > 0 && Measured.Compare(root, exam!, query, now: true).Answer is { } now)
            page.Append($" {answer.TriesSince} {(answer.TriesSince == 1 ? "try has" : "tries have")} been made in these configurations since. <a href=\"{E(now.Address)}\">As it is now</a>: {E(now.Sentence)}");
        page.Append($"</p><p class=\"verdict\">{E(answer.Sentence)}</p>");
        // Proctor's sentence already ends with the cautions about its configurations; the rest are listed under it.
        if (answer.Cautions.Where(c => !answer.Sentence.Contains(c)).ToList() is { Count: > 0 } cautions)
            page.Append("<ul class=\"cautions\">").Append(string.Join("", cautions.Select(c => $"<li>{E(c)}</li>"))).Append("</ul>");

        page.Append($"<h2>Setting by setting</h2><table class=\"cells\"><tr><th>Every other axis held at</th><th><code>{E(change)}</code><div class=\"meta\">checks held, each try</div></th><th><code>{E(against)}</code><div class=\"meta\">checks held, each try</div></th><th>Found</th></tr>");
        foreach (var cell in answer.Cells)
        {
            string Side(P.SideAnswer side) =>
                $"<a href=\"{E(side.Address)}\">{E(side.Name ?? "configuration")}</a><div>" + (side.Tries.Count == 0 ? "<b>not run</b>"
                    : string.Join(" ", side.Tries.Select(t => $"<a class=\"held{(t.Invalid is null ? "" : " invalid")}\" href=\"{E(t.Address)}\" title=\"{E(t.Id)}, made {E(t.Made)}{(t.Invalid is { } i ? $"; not counted: {E(i)}" : "")}\">{t.Held}/{t.Of}</a>"))) + "</div>";
            var found = cell.Held is { } m
                ? $"<b>{E(m.Direction)}</b> · p = {Measured.PValue(m.P)} · {(m.Confirmed ? "confirmed" : cell.CouldConfirm ? "not confirmed" : "this many tries cannot confirm anything")}"
                : cell.State switch { "not_run" => $"<b>not run</b>: no try of {E(change)} here", "live_not_run" => $"<b>not run</b>: no try of {E(against)} here", _ => "<b>not run</b>: neither side" };
            page.Append($"<tr><td><code>{E(cell.Holding.Count == 0 ? "the one configuration" : Label(cell.Holding))}</code></td><td>{Side(cell.Value)}</td><td>{Side(cell.Against)}</td><td>{found}");
            if (cell.Checks is { Count: > 0 } checks)
                page.Append("<details class=\"per-check\"><summary>By check</summary><table>")
                    .Append(string.Join("", checks.Select(k => $"<tr><td><code{(k.About is { } about ? $" title=\"{E(about)}\"" : "")}>{E(k.Check)}</code></td><td>{k.Value} of {k.ValueTries}</td><td>{k.Against} of {k.AgainstTries}</td></tr>")))
                    .Append("</table><div class=\"meta\">tries that held it, each side; counts only, nothing is tested per check</div></details>");
            page.Append("</td></tr>");
        }
        page.Append("</table>");
        if (answer.ReadAsOne is { } one)
            page.Append($"<p><b>Read as one</b>, over {one.Covered} of {one.Of} settings{(one.Across.Count > 0 ? $", across {E(string.Join(" and ", one.Across))}" : "")}: {E(one.Held.Direction)} · p = {Measured.PValue(one.Held.P)} · {(one.Held.Confirmed ? "confirmed" : one.CanConfirm ? "not confirmed" : "this many tries cannot confirm anything")}.</p>");
        if (answer.Power is { } power) page.Append($"<p class=\"meta\">{E(power.Note)}</p>");
        if (answer.Timing is { ValueFirst: not null } timing)
            page.Append($"<p class=\"meta\">Tries made: {E(change)} {E(timing.ValueFirst!)} to {E(timing.ValueLast!)}; {E(against)} {E(timing.AgainstFirst ?? "none")} to {E(timing.AgainstLast ?? "none")}.</p>");
        Ask(page, e.Id, answer.Configuration is null && answer.Change.Count == 1 ? change : null, answer.Configuration);
        page.Append($"<details class=\"methods\"><summary>How this is worked out</summary><p>{E(answer.Methods)}</p></details>");
        return Close(page);
    }

    /// <summary>
    /// What to ask the agent you work with. The page measures and stops there: a judgement (is it better, is there
    /// enough evidence, what to ship) is the agent's to make from proctor's answers, with links back here.
    /// </summary>
    static void Ask(StringBuilder page, string exam, string? value, string? configuration = null)
    {
        var about = configuration is not null ? $"the {configuration} configuration" : value ?? "a value";
        var command = configuration is not null ? $"proctor compare {exam} --configuration {configuration}" : $"proctor compare {exam} {value ?? "<axis>=<value>"}";
        page.Append("<aside class=\"ask\"><b>This page shows what was measured, and no more.</b> For a judgement, ask the agent you work with, for example: ")
            .Append($"<q>What does {E(about)} change, and how much evidence is there for it?</q> or <q>Where has nothing been tried?</q> ")
            .Append($"It answers from <code>{E(command)}</code> and <code>proctor coverage {E(exam)}</code>, and can link each claim to a page here. The skill for it is <code>skills/proctor-answers</code>.</aside>");
    }
}
