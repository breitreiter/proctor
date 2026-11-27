using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auspex;

/// <summary>An exam of variants as the tries the bank holds, by variant, the live one marked: plain links, nothing
/// counted, since proctor's numbers for such an exam are in its report and not behind Measured.cs. A try is the stage
/// page (TryStagePage.cs). The helpers every page shares are here too.</summary>
public static class Pages
{
    const int ShownChars = 200_000;

    public static string Exam(Exam e)
    {
        var page = Open(e.Id, [("exams", "/"), (e.Id, null)], style: "one");
        var tries = e.Attempts.Select(a => (a.Variant, a.Staged ? "" : a.Task, a.N)).Distinct().Count();
        page.Append("<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">exam of variants</span>")
            .Append($"<h1 class=\"title\">{E(e.Id)}</h1>").Append(e.Description is { } d ? $"<p class=\"desc\">{E(d)}</p>" : "")
            .Append("<dl class=\"facts\">").Append(ExamPage.Fact("Variants", e.Variants.Count().ToString())).Append(ExamPage.Fact("Tries", tries.ToString())).Append("</dl></header>");
        if (e.Attempts.Count == 0) return Close(page.Append("<div class=\"said soft\">No try in this bank yet.</div>"));

        ExamPage.Section(page, "Variants", "A variant is what is under test: an agent, a model and what it is given. Each try is listed as the bank holds it. The numbers are in proctor's report of this exam.");
        string Unfinished(Attempt a) => a.Status is var status && status == "completed" ? "" : $" <wa-badge class=\"tag tag-none\" appearance=\"outlined\">{E(status)}</wa-badge>";
        var first = true;
        foreach (var variant in e.Variants)
        {
            page.Append($"<div class=\"row{(first ? " first" : "")}\" style=\"grid-template-columns:minmax(180px,260px) minmax(0,1fr);padding:14px 0\">")
                .Append($"<span style=\"display:flex;gap:10px;align-items:center;flex-wrap:wrap\"><span class=\"name\">{E(variant)}</span>{(variant == e.Live ? "<wa-badge class=\"light light-live\" appearance=\"outlined\">live</wa-badge>" : "")}</span><span class=\"stack\" style=\"gap:8px\">");
            first = false;
            // An exam of stages: each try, then its stages in order. A stage that did not complete says its status.
            var staged = e.Attempts.Where(a => a.Variant == variant && a.Staged).ToList();
            foreach (var n in staged.Select(a => a.N).Distinct())
                page.Append($"<span style=\"display:flex;gap:6px 14px;flex-wrap:wrap;align-items:baseline\"><a href=\"{U(staged.First(a => a.N == n).ById ? $"/{e.Id}/try/{n}" : $"/{e.Id}/{variant}/{n}")}\">try {E(n)}</a>")
                    .Append(string.Concat(staged.Where(a => a.N == n).Select(a => $"<span class=\"small\"><a class=\"mono\" href=\"{U(a.Path)}\">{E(a.Task)}</a>{Unfinished(a)}</span>"))).Append("</span>");
            foreach (var task in e.Attempts.Where(a => a.Variant == variant && !a.Staged).Select(a => a.Task).Distinct())
                page.Append($"<span style=\"display:flex;gap:6px 14px;flex-wrap:wrap;align-items:baseline\"><span class=\"mono\">{E(task)}</span>")
                    .Append(string.Concat(e.Attempts.Where(a => a.Variant == variant && a.Task == task).Select(a => $"<span class=\"small\"><a href=\"{U(a.Path)}\">try {E(a.N)}</a>{Unfinished(a)}</span>"))).Append("</span>");
            page.Append("</span></div>");
        }
        return Close(page.Append("</div></section>"));
    }

    /// <summary>The text with each cited quote wrapped in a mark, found by the judge rule. A quote not found leaves the text alone.</summary>
    internal static string Marked(string text, List<string>? quotes)
    {
        var spans = (quotes ?? []).Distinct().Select(q => Quote.Find(text, q)).OfType<(int Start, int Length)>().OrderBy(s => s.Start).ToList();
        var html = new StringBuilder();
        var at = 0;
        foreach (var (start, length) in spans)
        {
            if (start < at) continue;
            html.Append(E(text[at..start])).Append("<mark>").Append(E(text.Substring(start, length))).Append("</mark>");
            at = start + length;
        }
        return html.Append(E(text[at..])).ToString();
    }

    internal static string Command(JsonObject call) =>
        call["arguments"] is JsonObject args && args["command"] is JsonValue command && args.Count == 1 ? command.ToString()
        : call["arguments"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "";

    internal static string Content(JsonNode? content) => content is JsonValue v ? v.ToString() : content?.ToJsonString() ?? "";

    internal static string Shown(string text) => text.Length <= ShownChars ? text : text[..ShownChars] + $"\n[{text.Length - ShownChars:N0} more characters not shown]";

    internal static string FirstLine(string text)
    {
        var line = text.AsSpan().Trim();
        var end = line.IndexOfAny('\r', '\n');
        if (end >= 0) line = line[..end];
        return line.Length > 140 ? line[..140].ToString() + "…" : line.ToString();
    }

    internal static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    internal static string Usd(double? value) => value is { } v ? "$" + v.ToString(v < 0.1 ? "0.000" : "0.00", CultureInfo.InvariantCulture) : "";

    /// <summary>A page's head and crumbs, in the one style designed on the canvas (assets/one.css), over Web Awesome's theme and components. The page stays readable with no script; components add the interactions.</summary>
    internal static StringBuilder Open(string title, (string Text, string? Href)[] crumbs, string? style = "one") =>
        new StringBuilder("<!doctype html><html lang=\"en\" class=\"wa-dark\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            // Web Awesome's base and theme first, then ours, unlayered, which sets the theme's tokens and wins wherever both speak.
            .Append($"<title>{E(title)}</title><link rel=\"stylesheet\" href=\"/assets/webawesome/styles/webawesome.css\"><link rel=\"stylesheet\" href=\"/assets/one.css\">")
            .Append("<script type=\"module\" src=\"/assets/components.js\"></script><script type=\"module\" src=\"/assets/page.js\"></script></head><body data-style=\"one\"><div class=\"page\"><nav class=\"crumbs\" aria-label=\"Where this is\">")
            .Append(string.Join("<span>/</span>", crumbs.Select(c => c.Href is null ? $"<span>{E(c.Text)}</span>" : $"<a href=\"{U(c.Href)}\">{E(c.Text)}</a>")))
            .Append("</nav>");

    internal static string Close(StringBuilder page) =>
        page.Append("<footer class=\"note\">Components: Web Awesome, <a href=\"/assets/webawesome/LICENSE.md\">MIT licence</a>. Fonts: Geist, Geist Mono and Space Grotesk, <a href=\"/assets/fonts/README.md\">SIL Open Font Licence</a>.</footer></div></body></html>").ToString();

    static int tips;

    /// <summary>A tag whose meaning is one hover or one tab away: the tag, and Web Awesome's tooltip pointed at it. With no explanation it is the tag alone.</summary>
    internal static string Tagged(string classes, string label, string? says)
    {
        if (says is null) return $"<wa-badge class=\"{classes}\" appearance=\"outlined\">{E(label)}</wa-badge>";
        var id = $"tip{Interlocked.Increment(ref tips)}";
        return $"<wa-badge class=\"{classes}\" appearance=\"outlined\" id=\"{id}\" tabindex=\"0\">{E(label)}</wa-badge><wa-tooltip for=\"{id}\">{E(says)}</wa-tooltip>";
    }

    internal static string E(string text) => WebUtility.HtmlEncode(text);

    // A path as an href: each segment escaped, the slashes and a fragment kept.
    internal static string U(string path) => E(string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
}
