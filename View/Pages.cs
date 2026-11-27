using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Auspex;

/// <summary>The pages, rendered on the server from proctor's bank: one exam of variants' tries as plain links (a try
/// is the stage page, StagePage.cs; an exam of stages lists each try's stages). Helpers here are shared with it. Nothing is grouped, counted across tries or
/// worded here that proctor didn't write; the exam page that summarises tries waits on stages (project/build-plan.md).
/// On screen proctor's variant is a setup and its attempt a try; until stages exist, a try is one task's attempt.</summary>
public static class Pages
{
    const int ShownChars = 200_000;

    public static string Exam(Exam e)
    {
        var page = Open(e.Id, [("exams", "/"), (e.Id, null)]);
        page.Append($"<h1><code>{E(e.Id)}</code></h1>");
        if (e.Description is not null) page.Append($"<p>{E(e.Description)}</p>");
        if (e.Attempts.Count == 0) return Close(page.Append("<p class=\"empty\">No try in this bank yet.</p>"));

        foreach (var variant in e.Variants)
        {
            page.Append($"<h2>{E(variant)}{(variant == e.Live ? " <span class=\"meta\">live</span>" : "")}</h2>");
            // An exam of stages: each try, then its stages in order. A stage that did not complete says its status.
            var staged = e.Attempts.Where(a => a.Variant == variant && a.Staged).ToList();
            if (staged.Count > 0)
            {
                page.Append("<ul class=\"tries\">");
                foreach (var tryN in staged.Select(a => a.N).Distinct())
                    page.Append($"<li><a href=\"{U(staged.First(a => a.N == tryN).ById ? $"/{e.Id}/try/{tryN}" : $"/{e.Id}/{variant}/{tryN}")}\">try {E(tryN)}</a> ")
                        .Append(string.Join(" · ", staged.Where(a => a.N == tryN).Select(a => $"<a href=\"{U(a.Path)}\">{E(a.Task)}</a>{(a.Status == "completed" ? "" : $" <span class=\"meta\">{E(a.Status)}</span>")}")))
                        .Append("</li>");
                page.Append("</ul>");
            }
            foreach (var task in e.Attempts.Where(a => a.Variant == variant && !a.Staged).Select(a => a.Task).Distinct())
            {
                page.Append($"<h3><code>{E(task)}</code></h3><ul class=\"tries\">");
                foreach (var a in e.Attempts.Where(a => a.Variant == variant && a.Task == task))
                    page.Append($"<li><a href=\"{U(a.Path)}\">try {E(a.N)}</a> <span class=\"meta\">{E(a.Status)}</span></li>");
                page.Append("</ul>");
            }
        }
        return Close(page);
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

    internal static string Chip(string label, bool model) => $"<span class=\"chip l-{E(label)}{(model ? " model" : "")}\">{E(label)}</span>";

    internal static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    internal static string Usd(double? value) => value is { } v ? "$" + v.ToString(v < 0.1 ? "0.000" : "0.00", CultureInfo.InvariantCulture) : "";

    /// <summary>A page's head and crumbs. Style "one" is the pages designed on the canvas (assets/one.css); without it, the first pass's stylesheet and Web Awesome's theme.</summary>
    internal static StringBuilder Open(string title, (string Text, string? Href)[] crumbs, string? script = null, string? style = null)
    {
        if (style == "one")
            return new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
                .Append($"<title>{E(title)}</title><link rel=\"stylesheet\" href=\"/assets/one.css\"></head><body data-style=\"one\"><div class=\"page\"><nav class=\"crumbs\" aria-label=\"Where this is\">")
                .Append(string.Join("<span>/</span>", crumbs.Select(c => c.Href is null ? $"<span>{E(c.Text)}</span>" : $"<a href=\"{U(c.Href)}\">{E(c.Text)}</a>")))
                .Append("</nav>");
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append($"<title>{E(title)}</title>")
            // Web Awesome's theme goes dark by a class, not by the system setting; set it before the first paint.
            .Append("<script>(function(){var m=matchMedia('(prefers-color-scheme: dark)'),r=document.documentElement;function a(){r.classList.toggle('wa-dark',m.matches)}a();m.addEventListener('change',a)})()</script>")
            .Append("<link rel=\"stylesheet\" href=\"/assets/webawesome/styles/webawesome.css\"><link rel=\"stylesheet\" href=\"/assets/auspex.css\">")
            .Append("<script type=\"module\" src=\"/assets/components.js\"></script>")
            .Append(script is null ? "" : $"<script type=\"module\" src=\"{U(script)}\"></script>")
            .Append("</head><body><nav>");
        page.Append(string.Join(" / ", crumbs.Select(c => c.Href is null ? E(c.Text) : $"<a href=\"{U(c.Href)}\">{E(c.Text)}</a>")));
        return page.Append("</nav><main>");
    }

    internal static string Close(StringBuilder page) => page.ToString().Contains("<body data-style=\"one\">")
        ? page.Append("<footer class=\"note\">Fonts: Geist, Geist Mono and Space Grotesk, <a href=\"/assets/fonts/README.md\">SIL Open Font Licence</a>.</footer></div></body></html>").ToString()
        : page.Append("</main><footer class=\"meta\">Components: Web Awesome, <a href=\"/assets/webawesome/LICENSE.md\">MIT licence</a>.</footer></body></html>").ToString();

    internal static string E(string text) => WebUtility.HtmlEncode(text);

    // A path as an href: each segment escaped, the slashes and a fragment kept.
    internal static string U(string path) => E(string.Join('/', path.Split('/').Select(Uri.EscapeDataString)));
}
