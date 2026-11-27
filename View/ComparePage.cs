using System.Globalization;
using System.Text;
using P = Proctor;
using static Auspex.Pages;
using static Auspex.ExamPage;

namespace Auspex;

/// <summary>
/// One configuration against another, as designed on the canvas (2026-11-26 and 27): "X vs Y", where the name before vs
/// is the subject and every number and direction describes it. What was predicted of the pair, the five measures of a
/// try on both sides, each check's rate, each stage as plain statistics, and friction by label. Every number is
/// proctor's answer to `proctor compare` (Measured.cs). Drawn for a comparison in one setting; one that spans several
/// settings of the other axes is still the first pass's page (AxesPages.Compare).
/// </summary>
static class ComparePage
{
    /// <summary>
    /// The designed page for the one setting that is the comparison asked for: the only one compared, or, when a named
    /// configuration was asked about, the setting that is that configuration itself. Proctor compares the change between two
    /// configurations wherever the exam has it, so other settings may be in the answer: they are said, with a link to all
    /// of them. Null when no one setting is the subject: the caller draws every setting.
    /// </summary>
    public static string? Render(string root, Exam e, P.Exam exam, P.ComparisonAnswer answer, Func<string, string?> query)
    {
        var compared = answer.Cells.Where(c => c.State == "compared").ToList();
        var cell = compared.Count == 1 ? compared[0] : compared.FirstOrDefault(c => answer.Configuration is { } asked && (c.Value.Name == asked || c.Value.Configuration == asked));
        if (cell?.Held is not { } held) return null;
        var others = compared.Count - 1;
        var (mine, other) = (cell.Value.Name ?? cell.Value.Configuration, cell.Against.Name ?? cell.Against.Configuration);
        var page = Open($"{mine} vs {other} · {e.Id}", [("exams", "/"), (e.Id, e.Path), ($"{mine} vs {other}", null)], style: "one");

        // The reading is this setting's own, on checks held: with other settings in the answer, proctor's pooled verdict is about more than X and Y.
        var light = held.Direction switch
        {
            "better" or "worse" => $"<span class=\"light {(held.Confirmed ? $"light-{held.Direction}" : "light-same")}\">{E(mine)} is {held.Direction} · {(held.Confirmed ? "confirmed" : cell.CouldConfirm ? "not confirmed" : "cannot confirm")}</span>",
            _ => "<span class=\"light light-same\">no difference</span>",
        };
        var same = exam.Axes!.Of(cell.Value.Configuration)!.Values.Where(v => !answer.Change.ContainsKey(v.Axis)).Select(v => $"{v.Axis}={v.Value}").ToList();
        page.Append("<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">comparison</span>")
            .Append($"<div style=\"display:flex;flex-wrap:wrap;gap:10px 16px;align-items:center\"><h1 class=\"title\"><a href=\"{E(cell.Value.Address)}\" style=\"color:inherit;text-decoration:none\">{E(mine)}</a> <span class=\"dim regular\">vs</span> <a href=\"{E(cell.Against.Address)}\" style=\"color:inherit;text-decoration:none\">{E(other)}</a></h1>{light}</div>");
        // With neither side live there is no given order: the same page, read from the other side.
        var live = exam.Axes.Named.FirstOrDefault(c => c.Values.All(v => exam.Axes.Def[v.Axis].Live is not { } l || l == v.Value));
        if (live?.Key != cell.Against.Configuration && live?.Key != cell.Value.Configuration)
            page.Append($"<a class=\"small\" href=\"{AxesPages.Q($"/{e.Id}/compare", ("configuration", other), ("against", mine))}\">Read it the other way: {E(other)} vs {E(mine)}</a>");
        page.Append("<dl class=\"facts\">")
            .Append(Fact("Differs in", $"<span class=\"mono\">{E(string.Join(", ", answer.Change.Select(c => $"{c.Key}: {c.Value}, not {answer.Against[c.Key]}")))}</span>"))
            .Append(same.Count == 0 ? "" : Fact("The same in", $"<span class=\"mono\">{E(string.Join(", ", same))}</span>"))
            .Append(Fact("Tries", $"{cell.Value.Tries.Count(t => t.Invalid is null)} vs {cell.Against.Tries.Count(t => t.Invalid is null)}"))
            .Append(Fact(query("as_of") is { Length: > 0 } ? "As it stood at" : "As of", E(answer.AsOf))).Append("</dl></header>");
        if (answer.TriesSince > 0)
            page.Append($"<div class=\"said soft\">{answer.TriesSince} {(answer.TriesSince == 1 ? "try has" : "tries have")} been made in these configurations since. <a href=\"{AxesPages.Q($"/{e.Id}/compare", ("configuration", mine), ("against", other))}\">As it is now</a></div>");
        if (others > 0)
            page.Append($"<div class=\"said soft\">The same change was compared in {others} other {(others == 1 ? "setting" : "settings")} of the other axes. <a href=\"{AxesPages.Q($"/{e.Id}/compare", ("configuration", mine), ("against", other), ("as_of", query("as_of")), ("settings", "all"))}\">Every setting</a></div>");
        if (answer.NotRun.Count > 0)
            page.Append($"<div class=\"said soft\">Not run, so not compared: {E(string.Join("; ", answer.NotRun))}.</div>");
        foreach (var caution in answer.Cautions)
            page.Append($"<div class=\"said\"><span class=\"soft\">{E(caution)}</span></div>");

        var predictions = (answer.Predictions ?? []).Where(p => p.Configuration == cell.Value.Configuration && p.Against == cell.Against.Configuration).ToList();
        Prediction(page, e, exam, predictions, mine, other);

        // What was predicted of a measure or a check in this pair, to mark it where it is shown.
        string Predicted(string? metric, string? check)
        {
            var change = predictions.SelectMany(p => p.Changes).FirstOrDefault(c => (metric is not null && c.Metric == metric) || (check is not null && c.Check == check));
            if (change is null) return "";
            var tag = change.Outcome switch { P.PredictionOutcomes.Supported => "tag tag-right", P.PredictionOutcomes.Contradicted => "tag tag-wrong", _ => "tag" };
            return $"<span class=\"{tag}\" title=\"{E(change.Sentence)}\">predicted {(change.Direction == P.Predictions.More ? "higher" : "lower")}</span>";
        }

        Measures(page, cell, mine, other, Predicted);
        Checks(page, cell, mine, other, Predicted);
        Stages(page, exam, cell, mine, other);
        Friction(page, cell, mine, other);

        page.Append("<section class=\"sec sec-plain\" aria-label=\"How this is counted\"><div class=\"sec-head\"><span class=\"label\">How this is counted</span></div>")
            .Append("<div class=\"sec-body note\" style=\"display:flex;flex-direction:column;gap:6px;max-width:900px\">")
            .Append($"<div>Every number and direction describes {E(mine)}, the name before vs. The reading at the top is on checks held.</div>")
            .Append(answer.Power is { } power ? $"<div>{E(power.Note)}</div>" : "")
            .Append($"<div>{E(answer.Methods)}</div><div>Every number here is proctor's. Nothing on this page recommends anything.</div></div></section>");
        return Close(page);
    }

    static void Prediction(StringBuilder page, Exam e, P.Exam exam, List<P.PredictionReport> predictions, string mine, string other)
    {
        if (predictions.Count == 0)
        {
            Section(page, "Prediction", "What we predicted for this match-up and how that turned out.");
            page.Append($"<div class=\"said soft\">No prediction names <span class=\"mono\">{E(mine)}</span> against <span class=\"mono\">{E(other)}</span>.</div></div></section>");
            return;
        }
        // The same block the exam's page draws, for the predictions about this pair.
        var named = exam.Axes!.Named.Select(c => new P.ConfigurationRow(c.Key, c.Label, [], c.Name, false, "", 0, 0, 0, 0, [], 0, null)).ToList();
        Predictions(page, e, predictions, named, "What we predicted for this match-up and how that turned out.");
    }

    /// <summary>The five measures of a try, each side a lane on one scale: a dot a try, the tall mark the median, the subject first.</summary>
    static void Measures(StringBuilder page, P.CellAnswer cell, string mine, string other, Func<string?, string?, string> predicted)
    {
        var (a, b) = (cell.Value.Tries.Where(t => t.Invalid is null).ToList(), cell.Against.Tries.Where(t => t.Invalid is null).ToList());
        Section(page, "Tries", $"Everything we measured across the {a.Count} {mine} {(a.Count == 1 ? "try" : "tries")} and the {b.Count} {other} {(b.Count == 1 ? "try" : "tries")}. A dot is one try and the tall mark is the median.");
        page.Append($"<div style=\"min-width:1040px\"><div class=\"row row-head meas\"><span>Measure</span><span></span><span class=\"r\">{E(mine)}</span><span class=\"r\">{E(other)}</span><span class=\"r\">Change</span><span>Outcome</span></div>");
        (string Name, string Metric, Func<P.TryAddress, double?> Of, Func<double, string> Said, P.Measure? Measure, bool HigherIsBetter)[] rows =
        [
            ("Checks held", "held", t => t.Held, Count, cell.Held, true),
            ("Friction · flagged steps", "flagged_steps", t => t.Flagged, Count, cell.Flagged, false),
            ("Tool calls", "tool_calls", t => t.ToolCalls, Count, cell.ToolCalls, false),
            ("Tokens read", "tokens", t => t.Tokens, Tokens, cell.Tokens, false),
            ("Time", "duration", t => t.DurationMs, Time, cell.Duration, false),
        ];
        foreach (var r in rows)
        {
            var (mineValues, otherValues) = (a.Select(r.Of).OfType<double>().Order().ToList(), b.Select(r.Of).OfType<double>().Order().ToList());
            if (r.Measure is null || mineValues.Count == 0 || otherValues.Count == 0) continue;
            // One scale for both lanes, ending at the most a try can hold (checks) or the highest try on either side.
            var top = r.Metric == "held" ? a.Concat(b).Max(t => t.Of) : Math.Max(mineValues[^1], otherValues[^1]);
            string At(double x) => (top == 0 ? 0 : x / top * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
            string Lane(string name, List<double> values, double median) =>
                $"<span class=\"dist-wrap\"><span class=\"dist-cap lane-name\">{E(name)}</span><span class=\"dist\" role=\"img\" aria-label=\"{E($"{name}: median {r.Said(median)}, {Range(values, r.Said)}")}\">"
                + (values.Count > 1 ? $"<span class=\"dist-range\" style=\"left:{At(values[0])};width:calc({At(values[^1])} - {At(values[0])})\"></span>" : "")
                + string.Concat(values.Select(v => $"<span class=\"dist-dot\" style=\"left:{At(v)}\"></span>")) + (values.Count > 1 ? $"<span class=\"dist-med\" style=\"left:{At(median)}\"></span>" : "")
                + $"</span><span class=\"dist-cap\">{E(r.Said(top))}</span></span>";
            var m = r.Measure;
            var change = m.Direction == "same" ? "" : m.Change is { } c ? (c > 0 ? "+" : "−") + Math.Abs(c).ToString("0%", CultureInfo.InvariantCulture) : "";
            page.Append($"<div class=\"row meas\"><span class=\"stack\"><span>{E(r.Name)}</span><span>{predicted(r.Metric, null)}</span></span><span class=\"lanes\">{Lane(mine, mineValues, m.Median ?? 0)}{Lane(other, otherValues, m.OtherMedian ?? 0)}</span>")
                .Append($"<span class=\"stack r\"><span class=\"num strong\">{E(r.Said(m.Median ?? 0))}</span><span class=\"small dim num\">{E(Range(mineValues, r.Said))}</span></span>")
                .Append($"<span class=\"stack r\"><span class=\"num\">{E(r.Said(m.OtherMedian ?? 0))}</span><span class=\"small dim num\">{E(Range(otherValues, r.Said))}</span></span>")
                .Append($"<span class=\"r {(m.Confirmed ? $"d-{m.Direction}" : "d-none")}\">{change}</span>{Outcome(m, r.HigherIsBetter, cell.CouldConfirm, cell.SmallestP)}</div>");
        }
        page.Append("</div></div></section>");
    }

    static string Range(List<double> values, Func<double, string> said) =>
        values.Count == 1 ? "1 try" : values[0] == values[^1] ? $"in all {values.Count} tries" : $"{said(values[0])} to {said(values[^1])}";

    /// <summary>A measure's outcome in words about the subject: higher or lower, and whether confirmed; or that these counts of tries cannot confirm anything.</summary>
    static string Outcome(P.Measure? m, bool higherIsBetter, bool couldConfirm, double smallestP, bool valence = true)
    {
        if (m is null) return "<span class=\"dim\">not compared</span>";
        if (m.Direction == "same") return "<span class=\"stack\"><span class=\"dim\">No difference</span></span>";
        var word = (m.Direction == "better") == higherIsBetter ? "Higher" : "Lower";
        var strength = m.Confirmed ? "confirmed" : couldConfirm ? "not confirmed" : "cannot confirm";
        var note = couldConfirm ? $"p = {Measured.PValue(m.P)}" : $"p cannot go below {Measured.PValue(smallestP)} with these tries";
        return $"<span class=\"stack\"><span class=\"{(m.Confirmed && valence ? $"d-{m.Direction}" : "dim")}\">{word}, {strength}</span><span class=\"small dim num\">{note}</span></span>";
    }

    /// <summary>Each check's rate on both sides, the subject first: how often it held of the times it was asked, and the outcome.</summary>
    static void Checks(StringBuilder page, P.CellAnswer cell, string mine, string other, Func<string?, string?, string> predicted)
    {
        Section(page, "Checks", $"How often each check held, across all stages, over the {mine} tries and the {other} tries.");
        page.Append($"<div style=\"min-width:900px\"><div class=\"row row-head cchecks\"><span>Check</span><span></span><span class=\"r\">{E(mine)}</span><span class=\"r\">{E(other)}</span><span></span><span>Outcome</span></div>");
        foreach (var k in (cell.ByCheck ?? []).OrderBy(k => Array.IndexOf(RoleOrder, k.Role)))
        {
            var (tag, says) = RoleTag[k.Role];
            page.Append($"<div class=\"row cchecks\"><span class=\"stack\"><span class=\"mono\">{E(k.Check)}</span><span>{predicted(null, k.Check)}</span></span><span>{Tagged(tag, k.Role, says)}</span>")
                .Append($"<span class=\"r num strong\" title=\"{k.Held} of {k.Asked}\">{Share(k.Held, k.Asked)}</span><span class=\"r num soft\" title=\"{k.OtherHeld} of {k.OtherAsked}\">{Share(k.OtherHeld, k.OtherAsked)}</span>")
                .Append($"<span class=\"small dim\">{E(k.About ?? "")}</span>{Outcome(k.Measure, true, cell.CouldConfirm, cell.SmallestP, valence: k.Role != P.Roles.Observation)}</div>");
        }
        page.Append("</div></div></section>");
    }

    /// <summary>Each stage as plain statistics, the subject over the other: the share of its correctness checks a try held, and in how many tries it went right.</summary>
    static void Stages(StringBuilder page, P.Exam exam, P.CellAnswer cell, string mine, string other)
    {
        Section(page, "Stages", "The share of each stage's correctness checks that held in a try: the average across tries, then the lowest and the highest try. A share, so stages with different numbers of checks read alike. Single tries are on each configuration's page.");
        page.Append("<div style=\"min-width:900px\"><div class=\"row row-head cstages\"><span>Stage</span><span></span><span class=\"r\">Average</span><span class=\"r\">Lowest</span><span class=\"r\">Highest</span><span class=\"r\">Right in</span><span>Outcome</span><span>Did not hold</span></div>");
        foreach (var (s, i) in (cell.ByStage ?? []).Select((s, i) => (s, i)))
        {
            var about = exam.Tasks.FirstOrDefault(t => t.Id == s.Stage) is { } stage ? P.Exam.Describe(stage) : "";
            string Two(Func<P.StageTally, string> of, bool strong = false) => $"<span class=\"two r num{(strong ? "" : " soft")}\"><span{(strong ? " class=\"strong\"" : "")}>{of(s.Value)}</span><span class=\"soft\">{of(s.Against)}</span></span>";
            var notHeld = s.Value.DidNotHold is { Count: > 0 } failed
                ? string.Concat(failed.Select(f => $"<span class=\"stack\"><span><span class=\"mono\">{E(f.Check)}</span> <span class=\"soft\">in {f.Tries} of {s.Value.Graded} tries of {E(mine)}</span></span><span class=\"small dim\">{E(f.Reason ?? "")}</span></span>"))
                : "<span class=\"soft\">all held</span>";
            page.Append($"<div class=\"row cstages\" style=\"padding:10px 0\"><span class=\"stack\"><span class=\"mono\">{i + 1}. {E(s.Stage)}</span><span class=\"small dim\">{E(about)} · {s.Value.Checks} correctness checks</span></span>")
                .Append($"<span class=\"two small\"><span>{E(mine)}</span><span class=\"dim\">{E(other)}</span></span>")
                .Append(Two(t => Share(t.Average), strong: true)).Append(Two(t => Share(t.Share?.Min))).Append(Two(t => Share(t.Share?.Max))).Append(Two(t => $"{t.Right} of {t.Of}"))
                .Append(Outcome(s.Measure, true, cell.CouldConfirm, cell.SmallestP)).Append($"<span class=\"stack\" style=\"gap:6px\">{notHeld}</span></div>");
        }
        page.Append("</div></div></section>");
    }

    static void Friction(StringBuilder page, P.CellAnswer cell, string mine, string other)
    {
        Section(page, "Friction", "Flagged steps per try, by label. A step can carry more than one label, so the labels add up to more than the total.");
        if (cell.Friction is null || cell.FlaggedPerTry is null || cell.OtherFlaggedPerTry is null)
        {
            page.Append("<div class=\"said soft\">Friction has not been read on every stage of these tries.</div></div></section>");
            return;
        }
        string Change(double a, double b) => a == b ? "" : (a > b ? "+" : "−") + Number(Math.Abs(a - b));
        page.Append($"<div style=\"min-width:760px\"><div class=\"row row-head fric\"><span>Label</span><span class=\"r\">{E(mine)}</span><span class=\"r\">{E(other)}</span><span class=\"r\">Change</span><span></span></div>");
        foreach (var l in cell.Friction)
            page.Append($"<div class=\"row fric\"><span><span class=\"tag tag-fr\">{E(l.Label)}</span></span><span class=\"r fr\">{Number(l.PerTry)}</span><span class=\"r num soft\">{Number(l.OtherPerTry)}</span><span class=\"r num dim\">{Change(l.PerTry, l.OtherPerTry)}</span><span></span></div>");
        var total = cell.Flagged;
        page.Append($"<div class=\"row fric row-sum\"><span>Flagged steps per try</span><span class=\"r fr\">{Number(cell.FlaggedPerTry.Value)}</span><span class=\"r num soft\">{Number(cell.OtherFlaggedPerTry.Value)}</span>")
            .Append($"<span class=\"r num {(total is { Confirmed: true } ? $"d-{total.Direction}" : "dim")}\">{Change(cell.FlaggedPerTry.Value, cell.OtherFlaggedPerTry.Value)}</span><span class=\"small dim\">each step once</span></div></div></div></section>");
    }

    static string Share(int held, int asked) => asked == 0 ? "–" : ((double)held / asked).ToString("0%", CultureInfo.InvariantCulture);
    static string Share(double? fraction) => fraction is { } f ? f.ToString("0%", CultureInfo.InvariantCulture) : "–";
}
