using System.Globalization;
using System.Text;
using P = Auspex;
using static Auspex.View.Pages;
using static Auspex.View.ExamPage;

namespace Auspex.View;

/// <summary>
/// The exams, as designed on the canvas (2026-11-27): one row an exam, the one tried most recently first. An exam with
/// axes shows its named configurations with the mark each carries on the exam's own page, which is the headline read:
/// what is live, what might replace it, how that is going; then its predictions as "X vs Y" with their count, its tries
/// and when the last was made. An exam proctor cannot read is a plain row. Every mark and count
/// is proctor's (Measured.cs).
/// </summary>
static class IndexPage
{
    sealed record Row(Exam Exam, string Html, string? Last, int Tries);

    public static string Render(string root)
    {
        var page = Open("auspex", [("exams", null)], style: "one");
        var exams = Bank.Exams(root).Select(id => Bank.FindExam(root, id)!).ToList();
        var rows = exams.Select(e => e.Axes ? WithAxes(root, e) : Plain(e, "exams/" + e.Id + "/exam.json declares no axes")).OrderByDescending(r => r.Last ?? "", StringComparer.Ordinal).ThenBy(r => r.Exam.Id, StringComparer.Ordinal).ToList();

        page.Append("<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">bank</span><h1 class=\"title\">Exams</h1><dl class=\"facts\">")
            .Append(Fact("Exams", exams.Count.ToString())).Append(Fact("Tries", rows.Sum(r => r.Tries).ToString()))
            .Append(Fact("As of", DateTime.UtcNow.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture))).Append("</dl></header>");
        if (exams.Count == 0)
            return Close(page.Append($"<div class=\"said soft\">No exam has a bank under <span class=\"mono\">{E(root)}</span>. auspex reads proctor's root: the directory holding <span class=\"mono\">bank/</span> and <span class=\"mono\">exams/</span>.</div>"));

        Section(page, "Exams", "An exam is a question, the stages that ask it and the checks that grade them. The one tried most recently is first.");
        page.Append("<div style=\"min-width:1040px\"><div class=\"row row-head ex\"><span>Exam</span><span>Configurations</span><span>Predictions</span><span class=\"r\">Tries</span><span class=\"r\">Last try</span></div>")
            .Append(string.Concat(rows.Select((r, i) => i == 0 ? r.Html.Replace("class=\"row ex\"", "class=\"row ex first\"") : r.Html)))
            .Append("</div></div></section>");
        return Close(page);
    }

    static string Head(Exam e, string shape) =>
        $"<span class=\"stack\" style=\"gap:6px\"><a class=\"name\" href=\"{U(e.Path)}\">{E(e.Id)}</a>{(e.Description is { } d ? $"<span class=\"soft\" style=\"line-height:1.45\">{E(d)}</span>" : "")}<span class=\"small dim\">{E(shape)}</span></span>";

    /// <summary>An exam as proctor reads it: its named configurations and their marks, its predictions, its tries.</summary>
    static Row WithAxes(string root, Exam e)
    {
        var (exam, problem) = Measured.Load(root, e.Id);
        var (report, reportProblem) = exam is null ? (null, problem) : Measured.Report(root, exam);
        if (exam is null || report is null) return Plain(e, problem ?? reportProblem);
        var stats = report.Stats;
        var named = stats.Configurations.Where(c => c.Name is not null).ToList();
        var live = named.FirstOrDefault(c => c.Live && c.Tries is { Tries: > 0 });

        var row = new StringBuilder("<div class=\"row ex\" style=\"padding:18px 0\">")
            .Append(Head(e, $"{Counted(stats.Axes.Count, "axis", "axes")} · {Counted(stats.Stages.Count, "stage", "stages")}")).Append("<span class=\"list\">");
        foreach (var c in named.Where(c => c.Retired is null))
            row.Append($"<span class=\"cfg\"><a href=\"{U($"/{e.Id}/configuration/{c.Key}")}\">{E(c.Name!)}</a>{Standing(root, exam, c, live).Mark}</span>");
        if (live is null && named.Any(c => c.Tries is { Tries: > 0 })) row.Append("<span class=\"small dim\">Nothing is live, so nothing is compared.</span>");
        if (named.Count(c => c.Retired is not null) is > 0 and var retired) row.Append($"<a class=\"small\" href=\"{U($"/{e.Id}")}#retired\">{retired} retired</a>");
        row.Append("</span><span class=\"list\">");
        string Name(string key) => named.FirstOrDefault(c => c.Key == key)?.Name ?? key;
        foreach (var p in stats.Predictions ?? [])
        {
            var (mine, other) = (Name(p.Configuration), Name(p.Against));
            var supported = p.Changes.Count(c => c.Outcome == P.PredictionOutcomes.Supported);
            row.Append($"<span class=\"stack\"><a href=\"{AxesPages.Q($"/{e.Id}/compare", ("configuration", mine), ("against", other))}\">{E(mine)} vs {E(other)}</a>")
                .Append($"<span class=\"small soft\">{supported} of {Counted(p.Changes.Count, "change", "changes")} supported{(p.PostHoc ? " <wa-badge class=\"tag\" appearance=\"outlined\">post hoc</wa-badge>" : "")}</span></span>");
        }
        if (stats.Predictions is not { Count: > 0 }) row.Append("<span class=\"small dim\">None</span>");

        var tries = named.Sum(c => c.Made);
        var notes = string.Join(" · ", new[] { stats.Lost.Count > 0 ? $"{stats.Lost.Count} lost" : null, named.Sum(c => c.NotGraded) is > 0 and var n ? $"{n} not yet graded" : null }.OfType<string>());
        var last = named.SelectMany(c => c.Tries?.Rows ?? []).Select(t => t.Made).Where(m => m is not null).Max(StringComparer.Ordinal);
        row.Append($"</span><span class=\"stack r\"><span class=\"num strong\">{tries}</span><span class=\"small dim num\">{E(notes)}</span></span>")
            .Append($"<span class=\"r small soft num\">{E(last is null ? "never" : When(last))}</span></div>");
        return new Row(e, row.ToString(), last, tries);
    }

    /// <summary>An exam proctor cannot read as it stands (one still written as tasks and variants, say): what the bank holds, nothing counted.</summary>
    static Row Plain(Exam e, string? problem)
    {
        var tries = e.Attempts.Select(a => (a.Configuration, a.Staged ? "" : a.Stage, a.N)).Distinct().Count();
        var row = new StringBuilder("<div class=\"row ex\" style=\"padding:18px 0\">").Append(Head(e, "not read")).Append("<span class=\"list\">");
        row.Append($"<span class=\"small soft\" title=\"{E(problem ?? "")}\">Proctor cannot read this exam as it stands, so nothing is counted.</span>");
        row.Append("</span><span class=\"small dim\">None</span>")
            .Append($"<span class=\"stack r\"><span class=\"num strong\">{tries}</span></span><span class=\"r small dim\"></span></div>");
        return new Row(e, row.ToString(), null, tries);
    }

    static string Counted(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    static string When(string made) =>
        DateTime.TryParse(made, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at.ToString("d MMM, HH:mm 'UTC'", CultureInfo.InvariantCulture) : made;
}
