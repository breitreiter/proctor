using System.Globalization;
using System.Text;
using P = Proctor;
using static Auspex.Pages;
using static Auspex.ExamPage;

namespace Auspex;

/// <summary>
/// The page of one named configuration, as designed on the canvas (2026-11-26 and 27): against what is live on five
/// measures, what was predicted of it, each stage as outcomes and plain statistics, each check's rate, what recurs as
/// friction, and every try with the shape of its stages. Every number is proctor's (Measured.cs). A try's squares open
/// that stage of that try, which is how a person gets from here to what happened.
/// </summary>
static class ConfigurationPage
{
    public static string Render(string root, Exam e, string key)
    {
        var (exam, problem) = Measured.Load(root, e.Id);
        var c = exam?.Axes!.Of(key) ?? exam?.Axes!.Named.FirstOrDefault(k => k.Name == key);
        var (report, reportProblem) = exam is null ? (null, problem) : Measured.Report(root, exam);
        var row = c is null ? null : report?.Stats.Configurations.FirstOrDefault(r => r.Key == c.Key);
        var page = Open($"{c?.Name ?? key} · {e.Id}", [("exams", "/"), (e.Id, e.Path), (c?.Name ?? "configuration", null)], style: "one");
        if (exam is null || c is null || report is null || row is null)
        {
            page.Append($"<header><span class=\"kind\">configuration</span><h1 class=\"title\">{E(key)}</h1></header><div class=\"said\"><b>")
                .Append(exam is null || report is null ? "Proctor cannot read this exam as it stands." : "Not a configuration this exam names or has tries of now.")
                .Append($"</b></div><pre class=\"mono small soft\">{E(problem ?? reportProblem ?? "")}</pre>");
            return Close(page);
        }
        var stats = report.Stats;
        var named = stats.Configurations.Where(k => k.Name is not null).ToList();
        var live = named.FirstOrDefault(k => k.Live && k.Tries is { Tries: > 0 });
        var tries = row.Tries;
        var counted = tries?.Rows.Where(t => t.Invalid is null).ToList() ?? [];
        var name = c.Name ?? c.Label;
        var (answer, refused) = row.Live || live is null || counted.Count == 0 ? (null, null) : Measured.Compare(root, exam, q => q == "configuration" ? c.Name ?? c.Key : null, now: true);
        var cell = ExamPage.Own(answer, c.Key);
        var against = cell?.Against.Name ?? live?.Name ?? "live";

        var mark = row.Live ? "<wa-badge class=\"light light-live\" appearance=\"outlined\">live</wa-badge>"
            : refused is not null ? $"<wa-badge class=\"tag tag-none\" appearance=\"outlined\" title=\"{E(refused)}\">not compared{(row.NotGraded > 0 ? $" · {row.NotGraded} not yet graded" : "")}</wa-badge>"
            : answer is null ? "" : ExamPage.Light(cell, against);
        var made = (tries?.Rows ?? []).Select(t => t.Made).Where(m => m is not null).Select(m => m![..Math.Min(10, m!.Length)]).Distinct().Order().ToList();
        page.Append("<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">configuration</span>")
            .Append($"<div style=\"display:flex;flex-wrap:wrap;gap:10px 16px;align-items:center\"><h1 class=\"title\">{E(name)}</h1>{mark}{(row.Retired is null ? "" : "<wa-badge class=\"tag tag-none\" appearance=\"outlined\">retired</wa-badge>")}</div>")
            .Append($"<p class=\"desc\"><span class=\"mono\">{E(c.Label)}</span></p><dl class=\"facts\">")
            .Append(Fact("Tries", $"{row.Made} of {row.Wanted}{(row.Lost > 0 ? $" <span class=\"dim regular\">· {row.Lost} lost</span>" : "")}{(row.NotGraded > 0 ? $" <span class=\"dim regular\">· {row.NotGraded} not yet graded</span>" : "")}"))
            .Append(tries is null ? "" : Fact("Tries passed", $"{tries.Passed} of {tries.Tries}"))
            .Append(made.Count == 0 ? "" : Fact("Made", E(made.Count == 1 ? made[0] : $"{made[0]} to {made[^1]}")))
            .Append("</dl></header>");
        if (row.Retired is { } retired)
            page.Append($"<div class=\"said\"><b>Retired {E(retired.Since)}.</b> <span class=\"soft\">{E(retired.Reason)} Its tries, predictions and comparisons are kept, and proctor makes no new tries of it until it is brought back.</span></div>");
        foreach (var move in (stats.StandardMoved ?? []).Where(m => m.Configurations.Contains(c.Key)))
            page.Append($"<div class=\"said\"><b>The standard moved.</b> <span class=\"soft\">{E(move.Sentence)}</span></div>");

        if (counted.Count == 0)
        {
            page.Append($"<div class=\"said soft\">{(row.NotGraded > 0 ? "Its tries are not yet graded under the checks as they are now, so nothing is counted. Grading the exam brings them in; nothing has to be run again." : "No tries yet.")}</div>");
            Values(page, exam, c);
            return Close(page);
        }

        // Against what is live, or alone when this is live or nothing is.
        var compare = AxesPages.Q($"/{e.Id}/compare", ("configuration", c.Name ?? c.Key));
        page.Append($"<section class=\"sec\" aria-label=\"Compared with live\"><div class=\"sec-head\"><h2>{(row.Live ? "Measures" : live is null ? "Measures" : $"vs {E(against)}")}</h2><span class=\"note\">")
            .Append(row.Live ? "This configuration is live: the others are compared with it."
                : live is null ? "Nothing is live with tries of its own, so nothing is compared."
                : $"<a href=\"{U($"/{e.Id}/configuration/{live.Key}")}\">{E(against)}</a> is live. <a href=\"{compare}\">Open {E(name)} vs {E(against)}</a>")
            .Append("</span></div><div class=\"sec-body\"><div class=\"five\" style=\"min-width:820px\">")
            .Append(string.Concat(Columns.Select(column => Cell(column, counted, live, row.Live, cell, against, labelled: true))))
            .Append("</div></div></section>");

        Predictions(page, e, (stats.Predictions ?? []).Where(p => p.Configuration == c.Key).ToList(), named, "What was predicted for this configuration, and how that turned out.");
        Stages(page, stats, tries!);
        Checks(page, exam, tries!, row.Live ? null : live, name);
        Friction(page, e, report, c.Key, tries!);
        Tries(page, e, report, c.Key, tries!, stats);
        Values(page, exam, c);
        return Close(page);
    }

    /// <summary>Each stage across the tries: the share of its correctness checks a try held, as average, lowest and highest, in how many tries it went right, and what did not hold.</summary>
    static void Stages(StringBuilder page, P.ConfigurationsFile stats, P.TryVariant tries)
    {
        Section(page, "Stages", "Stages are defined by the exam. A try is one attempt at the exam by this configuration. Right means that all the stage's correctness checks passed. Each percentage is the share of the stage's correctness checks that held in a try: the average across tries, then the lowest and the highest try.");
        page.Append("<div style=\"min-width:860px\"><div class=\"row row-head grid\"><span>Stage</span><span class=\"r\">Checks</span><span class=\"r\">Average</span><span class=\"r\">Lowest</span><span class=\"r\">Highest</span><span class=\"r\">Right in</span><span>Did not hold</span></div>");
        foreach (var s in tries.Stages)
        {
            var info = stats.Stages.FirstOrDefault(i => i.Id == s.Id);
            var notHeld = s.DidNotHold is { Count: > 0 } failed
                ? string.Concat(failed.Select(f => $"<span class=\"stack\"><span><span class=\"mono\">{E(f.Check)}</span> <span class=\"soft\">in {f.Tries} of {s.Graded} tries</span></span><span class=\"small dim\">{E(f.Reason ?? "")}</span></span>"))
                : "<span class=\"soft\">all held</span>";
            page.Append($"<div class=\"row grid\" style=\"padding:10px 0\"><span class=\"stack\"><span class=\"mono\">{info?.Place}. {E(s.Id)}</span><span class=\"small dim\">{E(info?.About ?? "")}</span></span>")
                .Append($"<span class=\"r num soft\">{s.Checks}</span>")
                .Append($"<span class=\"r num{(s.Right == 0 && s.Of > 0 ? " wrong" : "")} strong\">{Share(s.Average)}</span><span class=\"r num soft\">{Share(s.Share?.Min)}</span><span class=\"r num soft\">{Share(s.Share?.Max)}</span>")
                .Append($"<span class=\"r num soft\">{s.Right} of {s.Of} tries</span><span class=\"stack\" style=\"gap:6px\">{notHeld}</span></div>");
        }
        page.Append("</div></div></section>");
    }

    /// <summary>Each check's rate here and, beside it, in what is live: how often it held of the times it was asked.</summary>
    static void Checks(StringBuilder page, P.Exam exam, P.TryVariant tries, P.ConfigurationRow? live, string name)
    {
        Section(page, "Checks", "Checks are small tests that verify the agent's work. Correctness checks fail the stage, integrity invalidates the run. Each number is how often the check held, of the times it was asked.");
        page.Append($"<div style=\"min-width:760px\"><div class=\"row row-head rates\"><span>Check</span><span></span><span class=\"r\">{E(name)}</span><span class=\"r\">{E(live?.Name ?? "")}</span><span></span></div>");
        foreach (var k in (tries.Checks ?? []).OrderBy(k => Array.IndexOf(RoleOrder, k.Role)))
        {
            var (tag, says) = RoleTag[k.Role];
            var other = live?.Tries?.Checks?.FirstOrDefault(o => o.Check == k.Check);
            var spec = exam.Tasks.Select(s => exam.ChecksFor(s).GetValueOrDefault(k.Check)).FirstOrDefault(s => s is not null);
            // Only a correctness check that fell short is coloured: an observation has no preferred answer.
            var fell = k.Role == P.Roles.Correctness && k.Held < k.Asked;
            page.Append($"<div class=\"row rates\"><span class=\"mono\">{E(k.Check)}</span><span>{Tagged(tag, k.Role, says)}</span>")
                .Append($"<span class=\"r num{(fell ? " wrong" : "")} strong\" title=\"{k.Held} of {k.Asked}\">{Share((double)k.Held / k.Asked)}</span>")
                .Append(other is null ? "<span></span>" : $"<span class=\"r num soft\" title=\"{other.Held} of {other.Asked}\">{Share((double)other.Held / other.Asked)}</span>")
                .Append($"<span class=\"small dim\">{E(spec is null ? "" : P.Checks.Describe(spec.Spec) ?? "")}</span></div>");
        }
        page.Append("</div></div></section>");
    }

    /// <summary>What recurs as friction across the tries, by kind, most flagged steps a try first; each opens a stage where it happened.</summary>
    static void Friction(StringBuilder page, Exam e, Measured.Reported report, string key, P.TryVariant tries)
    {
        Section(page, "Friction", "Instances where the agent performed fruitless work, back-tracked, or otherwise appeared to be struggling with the environment or available tools.");
        var friction = report.Friction.Variants.FirstOrDefault(v => v.Id == key);
        if (friction is null || tries.Flagged is null)
        {
            page.Append("<div class=\"said soft\">Friction has not been read on every stage of these tries.</div></div></section>");
            return;
        }
        if (friction.Kinds.Count == 0) { page.Append("<div class=\"said soft\">None flagged.</div></div></section>"); return; }
        page.Append($"<div style=\"min-width:780px\"><div class=\"row row-head kinds\"><span>What happened</span><span class=\"r\">in tries</span><span class=\"r\">min</span><span class=\"r\">median</span><span class=\"r\">max</span><span class=\"r\">Per try, over all {friction.Tries}</span></div>");
        // The kinds worth a row each are those that cost a flagged step a try or more; the rest fold under one line.
        var ordered = friction.Kinds.OrderByDescending(k => k.PerTry).ToList();
        var minor = ordered.Count(k => k.PerTry < 1);
        foreach (var (k, i) in ordered.Select((k, i) => (k, i)))
        {
            if (minor > 1 && i == ordered.Count - minor) page.Append($"<div class=\"row\" style=\"display:block\"><wa-details class=\"quiet\" appearance=\"plain\" summary=\"{minor} more, each under 1 flagged step a try\">");
            // Example is try/stage: the try's number, which the bank knows by its id.
            var parts = k.Example.Split('/', 2);
            var href = parts.Length == 2 && int.TryParse(parts[0], out var n) && report.TryIds.TryGetValue((key, n), out var id) ? U($"/{e.Id}/try/{id}/{parts[1]}") : "";
            var where = k.Stages.Count == report.Stats.Stages.Count ? $"all {k.Stages.Count} stages" : string.Join(", ", k.Stages);
            page.Append($"<a class=\"row kinds\"{(href.Length > 0 ? $" href=\"{href}\"" : "")}><span class=\"stack\"><span><wa-badge class=\"tag tag-fr\" appearance=\"outlined\">{E(k.Label)}</wa-badge> <span class=\"mono\">{E(k.Detail ?? "")}</span></span><span class=\"small dim\">{E(where)}</span></span>")
                .Append($"<span class=\"r num\">{k.In} of {k.Of}</span><span class=\"r num\">{Count(k.When.Min)}</span><span class=\"r num\">{Count(k.When.Median)}</span><span class=\"r num\">{Count(k.When.Max)}</span><span class=\"r fr\">{Number(k.PerTry)}</span></a>");
        }
        if (minor > 1) page.Append("</wa-details></div>");
        page.Append("<div class=\"row kinds row-sum\"><span class=\"stack\"><span>Flagged steps per try</span><span class=\"small dim\">each step once, however many labels it carries</span></span><span></span>")
            .Append($"<span class=\"r num\">{Count(tries.Flagged.Min)}</span><span class=\"r num\">{Count(tries.Flagged.Median)}</span><span class=\"r num\">{Count(tries.Flagged.Max)}</span><span class=\"r fr\">{Number(friction.PerTry)}</span></div>")
            .Append("</div></div></section>");
    }

    /// <summary>Every try, with its stages in order as squares: filled is right, hollow is wrong, dashed did not run. Each square opens that stage of that try.</summary>
    static void Tries(StringBuilder page, Exam e, Measured.Reported report, string key, P.TryVariant tries, P.ConfigurationsFile stats)
    {
        Section(page, "Tries", "Each recorded attempt at this exam, for this configuration. A filled square is a stage that went right, a hollow one went wrong. Each opens that stage.");
        page.Append("<div style=\"min-width:900px\"><div class=\"row row-head everytry\"><span>Try</span><span>Made</span><span>Stages, in order</span><span class=\"r\">Checks held</span><span class=\"r\">Friction</span><span class=\"r\">Tool calls</span><span class=\"r\">Tokens read</span><span class=\"r\">Time</span></div>");
        foreach (var t in tries.Rows)
        {
            var id = report.TryIds.GetValueOrDefault((key, t.N));
            string Href(string? stage) => id is null ? "" : $" href=\"{U(stage is null ? $"/{e.Id}/try/{id}" : $"/{e.Id}/try/{id}/{stage}")}\"";
            var squares = string.Concat(t.Stages.Select((s, i) =>
            {
                var (kind, word) = s.Result switch { "right" => ("sq-right", "right"), "wrong" => ("sq-wrong", "wrong"), "invalid" => ("sq-wrong", "not counted"), "not_reached" => ("sq-lost", "not reached"), _ => ("sq-lost", "undecided") };
                var label = $"Try {t.N}, stage {i + 1}, {s.Id}: {word}";
                return $"<a class=\"sq {kind}\"{Href(s.Id)} title=\"{E(label)}\" aria-label=\"{E(label)}\"></a>";
            }));
            var right = t.Stages.Count(s => s.Result == "right");
            page.Append($"<div class=\"row everytry\"><a{Href(null)} class=\"mono strong\">{E(id ?? $"try {t.N}")}</a>")
                .Append($"<span class=\"soft\">{E(When(t.Made))}</span>")
                .Append($"<span style=\"display:flex;gap:10px;align-items:center\"><span class=\"sqs\">{squares}</span><span class=\"small soft num\">{right} of {t.Stages.Count} right{(t.Invalid is { } why ? $" · not counted: {E(why)}" : "")}</span></span>")
                .Append($"<span class=\"r num\">{t.Held} of {t.Of}</span><span class=\"r fr\">{(t.Flagged is { } f ? f.ToString() : "–")}</span>")
                .Append($"<span class=\"r num\">{(t.ToolCalls is { } calls ? calls.ToString() : "–")}</span><span class=\"r num\">{(t.TokensIn is { } tokens ? E(Tokens(tokens)) : "–")}</span><span class=\"r num\">{(t.DurationMs is { } ms ? E(Time(ms)) : "–")}</span></div>");
        }
        foreach (var l in stats.Lost.Where(l => l.Variant == key))
            page.Append($"<div class=\"row small dim\" style=\"display:block\">Try {l.N} is lost and counted nowhere, at {E(l.At)}: {E(l.Why)}</div>");
        page.Append("</div></div></section>");
    }

    /// <summary>What the configuration is made of: each axis's value, what its author says it changes, and what it carries.</summary>
    static void Values(StringBuilder page, P.Exam exam, P.Configuration c)
    {
        var axes = exam.Axes!;
        Section(page, "Axes", "The value this configuration has on each axis. What a value changes is its author's account, not proctor's finding.");
        page.Append("<div style=\"min-width:760px\"><div class=\"row row-head values\"><span>Axis</span><span>Value</span><span>What its author says it changes</span><span>What it carries</span></div>");
        foreach (var (axis, value) in c.Values)
        {
            var def = axes.Value(axis, value);
            var carries = new List<string>();
            if (def.Files?.Path is { } path) carries.Add($"files from {path}");
            if (def.Files?.Git is { } git) carries.Add($"files from {git} at {def.Files.Rev}");
            foreach (var (param, given) in def.Set ?? []) carries.Add($"sets {param} = {(given is System.Text.Json.Nodes.JsonValue ? given.ToString() : given?.ToJsonString() ?? "")}");
            foreach (var (field, given) in def.Own()) if (given is not null) carries.Add($"{field} {given}");
            page.Append($"<div class=\"row values\"><span class=\"mono\">{E(axis)}</span><span><span class=\"mono\">{E(value)}</span>{(axes.Def[axis].Live == value ? " <wa-badge class=\"tag tag-live\" appearance=\"outlined\">live</wa-badge>" : "")}</span>")
                .Append($"<span class=\"soft\">{E(def.Note ?? "")}</span><span class=\"small dim mono\">{E(string.Join("; ", carries))}</span></div>");
        }
        page.Append("</div></div></section>");
    }

    static string Share(double? fraction) => fraction is { } f ? f.ToString("0%", CultureInfo.InvariantCulture) : "–";

    static string When(string? made) =>
        made is not null && DateTime.TryParse(made, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at.ToString("d MMM, HH:mm 'UTC'", CultureInfo.InvariantCulture) : made ?? "";
}
