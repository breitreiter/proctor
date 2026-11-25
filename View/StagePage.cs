using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using static Auspex.Pages;

namespace Auspex;

/// <summary>
/// The page for one stage of a try (project/stage-page.md). Until proctor builds stages, a try is one task's attempt,
/// so it has one stage, named by its task, and no stage tabs: the page is its stage.
///
/// It shows what proctor's files hold and makes up nothing. Before proctor writes a verdict's `why` (check-evidence C1)
/// a failure card has no "Why it is checked" line; before `evidence` (C3) a check points at no step; before a friction
/// point carries `because` (C5.1) its row shows the reader's notes or the rule's detail, which is what the file holds.
/// The page works without script: the first failed check is selected on the server. Script adds selecting another
/// check or friction point, and search.
/// </summary>
public static class StagePage
{
    /// <summary>One stage of a try. <paramref name="stages"/> is every stage of that try in order, on an exam of stages; none on an exam of tasks.</summary>
    /// <remarks><paramref name="check"/> is the check the address selects (?check=name), so a link can point at one verdict; without it the first failed check is selected.</remarks>
    public static string Render(Attempt a, IReadOnlyList<Attempt>? stages = null, string? check = null)
    {
        var checks = (a.Checks ?? []).Select(c => new Check(c.Key, c.Value as JsonObject ?? [])).ToList();
        var failed = checks.Where(c => c.Result == "fail").ToList();
        // A check that does not apply in this try's configuration is listed last, as neither: it counts toward nothing.
        var passed = checks.Where(c => c.Result is not ("fail" or "not-applicable")).ToList();
        var apart = checks.Where(c => c.Result == "not-applicable").ToList();
        var friction = a.Friction;
        var points = (friction?["points"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var steps = (a.Trajectory?["steps"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var ids = steps.Select(s => Bank.Str(s["step_id"])).ToList();
        var selected = failed.FirstOrDefault(c => c.Name == check) ?? failed.FirstOrDefault();

        // A try under its own id belongs to a configuration, which has a page; before axes a try is a setup's, by number.
        (string, string?)[] crumbs = a.ById
            ? [("exams", "/"), (a.Exam, $"/{a.Exam}"), ("configuration", $"/{a.Exam}/configuration/{a.Variant}"), ($"try {a.N}", null)]
            : [("exams", "/"), (a.Exam, $"/{a.Exam}"), ($"{a.Variant} / try {a.N}", null)];
        var page = Open($"{a.Task} · try {a.N}", crumbs, "/assets/stage.js");
        Header(page, a, steps.Count);
        if (stages is { Count: > 0 }) StageStrip(page, a, stages);

        // The stage: its label, its counts, its flagged steps in friction's own colour, beside the result and never tinting it.
        var summary = friction?["summary"] as JsonObject;
        var flagged = Bank.Str(summary?["flagged_steps"]) is { Length: > 0 } f ? f : Bank.Str(summary?["steps_in_a_point"]);
        page.Append("<section class=\"stage\"><div class=\"stage-head\">")
            .Append($"<h1><code>{E(a.Task)}</code></h1><span class=\"counts\">")
            .Append(failed.Count > 0 ? $"<span class=\"bad\">✕ {failed.Count} failed</span> · " : "")
            .Append($"<span class=\"good\">✓ {passed.Count} passed</span></span>")
            .Append(flagged.Length > 0 ? $"<span class=\"flagged\">{E(flagged)} flagged step{(flagged == "1" ? "" : "s")}</span>" : "")
            .Append("</div>");
        // A stage that did not complete: proctor's status and its own sentence for why, as written.
        if (a.Status != "completed" && Bank.Text(a.Manifest?["status_reason"]) is { } reason)
            page.Append($"<p class=\"stage-status\"><b>{E(a.Status)}</b> · {E(reason)}</p>");

        page.Append($"<div class=\"panes{(failed.Count == 0 ? " quiet" : "")}\"><aside class=\"checks\" aria-label=\"Checks\">");
        foreach (var c in failed) FailureCard(page, c, c == selected);
        FrictionRows(page, friction, points, ids);
        if (failed.Count > 0) PassedList(page, passed);
        else if (passed.Count > 0)
        {
            // A green stage opens on the transcript: friction is the way in, and the checks are one click away.
            page.Append("<wa-details summary=\"Show the checks\" class=\"show-checks\">");
            PassedList(page, passed);
            page.Append("</wa-details>");
        }
        if (apart.Count > 0)
            page.Append("<div class=\"passed\"><div class=\"lbl\">Does not apply here</div>")
                .Append(string.Join("", apart.Select(c => $"<div class=\"pass-row na\"><span class=\"meta\">–</span> <code{(c.Describes is { } d ? $" title=\"{E(d)}\"" : "")}>{E(c.Name)}</code> <span class=\"meta\">{E(c.Reason)}</span></div>")))
                .Append("</div>");
        if (checks.Count == 0 && a.Status == "completed") page.Append("<p class=\"empty\">This try has no <code>checks.json</code>. <code>proctor grade</code> writes it.</p>");
        page.Append("</aside>");

        Transcript(page, steps, ids, points, checks, selected);
        page.Append("</div></section>");
        return Close(page);
    }

    /// <summary>The try around the stage: setup, model, tool calls, tokens, time, cost and status, from proctor's manifest and core.json.</summary>
    static void Header(StringBuilder page, Attempt a, int stepCount)
    {
        var core = a.Core;
        var facts = new List<string> { a.ById ? $"configuration <a href=\"{U($"/{a.Exam}/configuration/{a.Variant}")}\"><b>{E(a.Variant.Replace(",", " × "))}</b></a>" : $"setup <b>{E(a.Variant)}</b>" };
        if (Bank.Str(a.Manifest?["model"]) is { Length: > 0 } model) facts.Add(E(model));
        facts.Add(E(a.Status) + (Bank.Str(core?["exit_reason"]) is { Length: > 0 } exit && exit != "ok" ? $" ({E(exit)})" : ""));
        facts.Add(Number(core?["tool_calls"]) is { } calls ? $"{calls:N0} tool calls" : $"{stepCount} steps");
        if (Number(core?["input_tokens"]) is { } tokens) facts.Add($"{tokens:N0} tokens read");
        if ((Number(core?["duration_ms"]) ?? Number(a.Manifest?["duration_ms"])) is { } ms) facts.Add(Duration(ms));
        if (Number(core?["cost_usd"]) is { } cost) facts.Add(Usd(cost));
        page.Append($"<p class=\"try-head meta\">{string.Join(" · ", facts)}</p>");
    }

    /// <summary>A failed check: its name, what happened, why it is checked (when proctor wrote it), and the steps it points at.</summary>
    static void FailureCard(StringBuilder page, Check c, bool selected)
    {
        page.Append($"<article class=\"card{(selected ? " sel" : "")}\" data-check=\"{E(c.Name)}\" data-steps=\"{E(string.Join(' ', c.Steps))}\" tabindex=\"0\" aria-pressed=\"{(selected ? "true" : "false")}\">")
            .Append($"<div class=\"card-name\"><span class=\"bad\">✕</span> <code{(c.Describes is { } d ? $" title=\"{E(d)}\"" : "")}>{E(c.Name)}</code></div>")
            .Append($"<p class=\"reason\">{E(c.Reason)}</p>");
        if (c.Script is { } script) page.Append($"<div class=\"meta\">script <code>{E(script)}</code></div>");
        if (c.Why is { } why) page.Append($"<div class=\"why\"><span class=\"lbl\">Why it is checked</span><p>{E(why)}</p></div>");
        if (c.Steps.Count > 0)
            page.Append("<div class=\"steps-line\"><span class=\"lbl\">Steps</span> ").Append(string.Join(" ", c.Steps.Select(s => $"<a href=\"#s{U(s)}\">{E(s)}</a>"))).Append("</div>");
        if (c.Files.Count > 0)
            page.Append("<div class=\"steps-line\"><span class=\"lbl\">Files</span> ").Append(string.Join(" ", c.Files.Select(f => $"<code>{E(f)}</code>"))).Append("</div>");
        page.Append("</article>");
    }

    /// <summary>One row per friction point, as proctor friction wrote it: label (its sentence on hover, when the file has one), span, finder, and why.</summary>
    static void FrictionRows(StringBuilder page, JsonObject? friction, List<JsonObject> points, List<string> ids)
    {
        if (friction is null) { page.Append("<p class=\"empty\">No <code>friction.json</code>: <code>proctor friction</code> writes it.</p>"); return; }
        if (points.Count == 0) return;
        var describes = Describes(friction);
        page.Append("<div class=\"friction\"><div class=\"lbl\">Friction</div>");
        foreach (var p in points)
        {
            var label = Bank.Str(p["label"]);
            var model = Bank.Str(p["source"]?["kind"]) == "model";
            var span = Span(p, ids);
            var (first, last) = (Bank.Str((p["steps"] as JsonArray)?[0]), Bank.Str((p["steps"] as JsonArray)?[1]));
            var finder = model ? $"reader {Bank.Str(p["source"]?["model"])}" : $"rule {string.Join(", ", (p["rules"] as JsonArray ?? []).Select(Bank.Str))}";
            page.Append($"<button type=\"button\" class=\"fric\" id=\"{E(Bank.Str(p["id"]))}\" data-steps=\"{E(string.Join(' ', span))}\" aria-pressed=\"false\">")
                .Append($"<span class=\"fric-head\"><span class=\"flabel\"{(describes.TryGetValue(label, out var d) ? $" title=\"{E(d)}\"" : "")}>{E(label)}</span>")
                .Append($" <span>{(first == last ? $"step {E(first)}" : $"steps {E(first)}–{E(last)}")}</span> <span class=\"meta\">{E(finder)}</span></span>");
            if (Because(p) is { Length: > 0 } because) page.Append($"<span class=\"because\">{because}</span>");
            page.Append("</button>");
        }
        page.Append("</div>");
    }

    static void PassedList(StringBuilder page, List<Check> passed)
    {
        if (passed.Count == 0) return;
        page.Append("<div class=\"passed\"><div class=\"lbl\">Passed</div>");
        foreach (var c in passed)
            page.Append($"<button type=\"button\" class=\"pass-row\" data-check=\"{E(c.Name)}\" data-steps=\"{E(string.Join(' ', c.Steps))}\" aria-pressed=\"false\">")
                .Append($"<span class=\"good\">{(c.Result == "pass" ? "✓" : "?")}</span> <code{(c.Describes is { } d ? $" title=\"{E(d)}\"" : "")}>{E(c.Name)}</code> <span class=\"meta\">{E(c.Reason)}</span></button>");
        page.Append("</div>");
    }

    /// <summary>The try's stages in the order they ran, each by its id, the one shown marked; a stage that did not complete says its status.</summary>
    static void StageStrip(StringBuilder page, Attempt shown, IReadOnlyList<Attempt> stages)
    {
        page.Append("<nav class=\"stages\" aria-label=\"Stages of this try\">");
        foreach (var s in stages)
        {
            var status = s.Status;
            page.Append($"<a href=\"{U(s.Path)}\"{(s.Task == shown.Task ? " aria-current=\"page\"" : "")}{(status == "completed" ? "" : " class=\"unfinished\"")}>")
                .Append($"<span class=\"place\">{s.Place}</span> {E(s.Task)}{(status == "completed" ? "" : $" <span class=\"meta\">{E(status)}</span>")}</a>");
        }
        page.Append("</nav>");
    }

    static void Transcript(StringBuilder page, List<JsonObject> steps, List<string> ids, List<JsonObject> points, List<Check> checks, Check? selected)
    {
        page.Append("<section class=\"transcript\" aria-label=\"Transcript\">")
            .Append("<div class=\"transcript-bar\"><wa-input id=\"search\" type=\"search\" placeholder=\"Search this stage\" with-clear size=\"small\" aria-label=\"Search this stage\"></wa-input></div>");
        if (steps.Count == 0) { page.Append("<p class=\"empty\">This stage has no <code>trajectory.json</code>.</p></section>"); return; }

        var covering = ids.ToDictionary(id => id, _ => new List<JsonObject>());
        var quotes = new Dictionary<string, List<string>>();
        foreach (var p in points)
        {
            foreach (var id in Span(p, ids)) covering[id].Add(p);
            foreach (var ev in (p["evidence"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var call = Bank.Str(ev["call"]);
                if (!quotes.TryGetValue(call, out var list)) quotes[call] = list = [];
                list.Add(Bank.Str(ev["quote"]));
            }
        }
        var lit = selected?.Steps.ToHashSet() ?? [];
        foreach (var step in steps)
        {
            var id = Bank.Str(step["step_id"]);
            var cited = checks.Where(c => c.Steps.Contains(id)).ToList();
            Step(page, step, id, covering[id], cited, quotes, lit.Contains(id));
        }
        page.Append("</section>");
    }

    static void Step(StringBuilder page, JsonObject step, string id, List<JsonObject> points, List<Check> cited, Dictionary<string, List<string>> quotes, bool lit)
    {
        var calls = (step["tool_calls"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var results = (step["observation"]?["results"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var message = Bank.Str(step["message"]);
        var headline = calls.Count > 0 ? $"{Bank.Str(calls[0]["function_name"])} {FirstLine(Gist(calls[0]))}" : FirstLine(message);
        var open = lit || points.Count > 0 || cited.Any(c => c.Result == "fail");

        page.Append($"<details class=\"step{(points.Count > 0 ? " in-point" : "")}{(lit ? " lit" : "")}\" id=\"s{E(id)}\"{(open ? " open" : "")}><summary>")
            .Append("<span class=\"gutter\">");
        foreach (var c in cited)
            page.Append($"<a class=\"gm {(c.Result == "fail" ? "gm-fail" : "gm-pass")}\" href=\"#\" data-check=\"{E(c.Name)}\" title=\"{E(c.Name)} {(c.Result == "fail" ? "failed" : "passed")} here\">{(c.Result == "fail" ? "●" : "○")}</a>");
        page.Append($"</span><b>{E(id)}</b> <span class=\"meta\">{E(Bank.Str(step["source"]))}</span> ");
        foreach (var p in points)
            page.Append($"<a class=\"tag\" href=\"#{U(Bank.Str(p["id"]))}\"><span class=\"flabel\">{E(Bank.Str(p["label"]))}</span></a> ");
        page.Append($"<span class=\"headline\">{E(headline)}</span></summary>");

        if (message.Length > 0) page.Append($"<div class=\"message\">{E(Shown(message))}</div>");
        for (var n = 0; n < calls.Count; n++)
        {
            var callId = $"s{id}.{n + 1}";
            quotes.TryGetValue(callId, out var marks);
            var result = results.FirstOrDefault(r => Bank.Str(r["source_call_id"]) == Bank.Str(calls[n]["tool_call_id"]));
            page.Append($"<div class=\"call\"><div class=\"meta\">{E(callId)} · {E(Bank.Str(calls[n]["function_name"]))}</div><pre class=\"command\">{Marked(Shown(Command(calls[n])), marks)}</pre>");
            if (result is not null) page.Append($"<pre class=\"result\">{Marked(Shown(Content(result["content"])), marks)}</pre>");
            page.Append("</div>");
        }
        page.Append("</details>");
    }

    /// <summary>A call in a line: a shell command as written, else its first text argument (a path, a pattern).</summary>
    static string Gist(JsonObject call) =>
        call["arguments"] is JsonObject args && args.Select(kv => kv.Value).OfType<JsonValue>().Select(v => v.ToString()).FirstOrDefault(v => v.Length > 0) is { } first
            ? (args["command"] is JsonValue c ? c.ToString() : first)
            : Command(call);

    /// <summary>The step ids a point covers, in the trajectory's order.</summary>
    static List<string> Span(JsonObject p, List<string> ids)
    {
        var span = p["steps"] as JsonArray;
        var a = ids.IndexOf(Bank.Str(span?[0]));
        var b = ids.IndexOf(Bank.Str(span?[1]));
        return a < 0 || b < a ? [] : ids.GetRange(a, b - a + 1);
    }

    /// <summary>Why the point was marked, from what the file holds: each evidence item's `because` (C5.1); before that, a reader's notes or a rule's detail.</summary>
    static string Because(JsonObject p)
    {
        var because = (p["evidence"] as JsonArray ?? []).OfType<JsonObject>().Select(e => Bank.Str(e["because"])).Where(s => s.Length > 0).Distinct().ToList();
        if (because.Count > 0) return E(string.Join(" ", because));
        if ((p["notes"] as JsonArray)?.Select(Bank.Str).Where(s => s.Length > 0).ToList() is { Count: > 0 } notes) return E(string.Join(" · ", notes));
        if ((p["detail"] as JsonArray)?.Select(Bank.Str).Where(s => s.Length > 0).ToList() is { Count: > 0 } detail) return string.Join(" ", detail.Select(d => $"<code>{E(d)}</code>"));
        return "";
    }

    /// <summary>Each label's sentence, when the file carries labels as objects (C5.1): detector.labels and reader.labels, [{name, describes}].</summary>
    static Dictionary<string, string> Describes(JsonObject friction)
    {
        var map = new Dictionary<string, string>();
        foreach (var list in new[] { friction["detector"]?["labels"], friction["reader"]?["labels"] })
            foreach (var label in (list as JsonArray ?? []).OfType<JsonObject>())
                if (Bank.Str(label["name"]) is { Length: > 0 } name && Bank.Str(label["describes"]) is { Length: > 0 } d) map[name] = d;
        return map;
    }

    static string Duration(double ms) => ms >= 120_000 ? (ms / 60_000).ToString("0.0", CultureInfo.InvariantCulture) + " min" : (ms / 1000).ToString("0", CultureInfo.InvariantCulture) + " s";

    /// <summary>A verdict from checks.json, with what C2 and C3 add when proctor has written them.</summary>
    sealed record Check(string Name, JsonObject V)
    {
        public string Result => Bank.Str(V["result"]);
        public string Reason => Bank.Str(V["reason"]);
        public string? Why => Bank.Text(V["why"]);
        public string? Describes => Bank.Text(V["describes"]);
        /// <summary>A script check's script, relative to proctor's root (C2), so a person can open what decided.</summary>
        public string? Script => Bank.Text(V["script"]);
        /// <summary>The step ids the verdict's evidence points at ({step} or {stage, step}); files and anything else are not steps.</summary>
        public List<string> Steps => [.. (V["evidence"] as JsonArray ?? []).OfType<JsonObject>().Select(e => Bank.Str(e["step"])).Where(s => s.Length > 0).Distinct()];
        /// <summary>The diff paths the verdict's evidence names ({file}): a check on the workspace points here, not at a step.</summary>
        public List<string> Files => [.. (V["evidence"] as JsonArray ?? []).OfType<JsonObject>().Select(e => Bank.Str(e["file"])).Where(s => s.Length > 0).Distinct()];
    }
}
