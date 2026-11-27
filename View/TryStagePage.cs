using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using P = Proctor;
using static Auspex.Pages;
using static Auspex.ExamPage;

namespace Auspex;

/// <summary>
/// One stage of a try, as designed on the canvas (2026-11-26 and 27): the stages of the try as
/// tabs, each right or wrong on an exam with axes; its checks by role, each with the steps its evidence points at as pills; its friction
/// points likewise; and the transcript, a step a row that opens. It shows what the stage's files hold (checks.json,
/// friction.json, trajectory.json, the diff) and what proctor says of the stage (right or wrong). A pill opens its
/// step, which the browser marks as the target, so nothing here needs script. A try of an exam of variants is the
/// same page with no word from proctor on right or wrong, since proctor works that out for exams with axes only; a try
/// of one task has one stage, and so no tabs.
/// </summary>
static class TryStagePage
{
    public static string Render(string root, Attempt a, IReadOnlyList<Attempt>? stagesOfTry, string? show)
    {
        IReadOnlyList<Attempt> stages = stagesOfTry is { Count: > 1 } ? stagesOfTry : [];
        var (exam, _) = Measured.Load(root, a.Exam);
        var report = exam?.Axes is null ? null : Measured.Report(root, exam).Report;
        var configuration = exam?.Axes?.Of(a.Variant);
        var name = configuration?.Name ?? a.Variant;
        // Proctor's word for each stage of this try: right, wrong, or why it is neither.
        var number = report?.TryIds.FirstOrDefault(t => t.Key.Configuration == a.Variant && t.Value == a.N).Key.Try;
        var row = report?.Stats.Configurations.FirstOrDefault(c => c.Key == a.Variant)?.Tries?.Rows.FirstOrDefault(r => r.N == number);
        string? Result(string stage) => row?.Stages.FirstOrDefault(s => s.Id == stage)?.Result;

        var checks = (a.Checks ?? []).Select(c => new Check(c.Key, c.Value as JsonObject ?? [])).ToList();
        var friction = a.Friction;
        var points = (friction?["points"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var steps = (a.Trajectory?["steps"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var ids = steps.Select(s => Bank.Str(s["step_id"])).ToList();
        var place = stages.ToList().FindIndex(s => s.Task == a.Task) + 1;

        // A try under its own id belongs to a configuration, which has a page; on an exam of variants a try is a variant's, by number.
        (string, string?)[] crumbs = a.ById ? [("exams", "/"), (a.Exam, $"/{a.Exam}"), (name, $"/{a.Exam}/configuration/{a.Variant}"), (a.N, $"/{a.Exam}/try/{a.N}"), (a.Task, null)]
            : a.Staged ? [("exams", "/"), (a.Exam, $"/{a.Exam}"), ($"{a.Variant} · try {a.N}", $"/{a.Exam}/{a.Variant}/{a.N}"), (a.Task, null)]
            : [("exams", "/"), (a.Exam, $"/{a.Exam}"), ($"{a.Variant} · {a.Task} · try {a.N}", null)];
        var page = Open($"{a.Task} · {a.N}", crumbs, style: "one");
        var core = a.Core;
        var exit = ExitOf(a);
        var answers = steps.Count(s => Bank.Str(s["extra"]?["source"]) == "oracle");
        var flagged = points.SelectMany(p => Span(p, ids)).Distinct().Count();
        var about = exam?.Tasks.FirstOrDefault(t => t.Id == a.Task) is { } def ? P.Exam.Describe(def) : null;
        page.Append($"<header style=\"display:flex;flex-direction:column;gap:10px\"><span class=\"kind\">{(stages.Count > 0 ? $"stage {place} of {stages.Count} · " : "")}try {(a.ById ? "" : E(a.N) + " ")}of {E(name)}</span>")
            .Append($"<div style=\"display:flex;flex-wrap:wrap;gap:10px 16px;align-items:center\"><h1 class=\"title\">{E(a.Task)}</h1>{Light(Result(a.Task), a.Status)}</div>")
            .Append(about is null ? "" : $"<p class=\"desc\">{E(about)}</p>").Append("<dl class=\"facts\">")
            .Append(Bank.Str(a.Manifest?["started"]) is { Length: > 0 } started ? Fact("Made", E(When(started))) : "")
            .Append(Bank.Str(a.Manifest?["model"]) is { Length: > 0 } model ? Fact("Model", E(model)) : "")
            .Append(exit.Length > 0 && exit != "ok" ? Fact("Ended", $"<span class=\"mono\">{E(exit)}</span>") : "")
            .Append(Fact("Steps", steps.Count.ToString()))
            .Append(answers > 0 ? Fact("Oracle", $"{answers} {(answers == 1 ? "answer" : "answers")}") : "")
            .Append(Number(core?["tool_calls"]) is { } calls ? Fact("Tool calls", calls.ToString("0", CultureInfo.InvariantCulture)) : "")
            .Append(friction is null ? "" : Fact("Friction", $"<span class=\"fr\">{flagged} flagged {(flagged == 1 ? "step" : "steps")}</span>"))
            .Append(Number(core?["input_tokens"]) is { } tokens ? Fact("Tokens read", E(Tokens(tokens))) : "")
            .Append((Number(core?["duration_ms"]) ?? Number(a.Manifest?["duration_ms"])) is { } ms ? Fact("Time", E(Time(ms))) : "")
            .Append(Number(core?["cost_usd"]) is { } cost ? Fact("Cost", E(Usd(cost))) : "")
            .Append(Fact("Address", $"<span class=\"mono\">{E(a.Path.TrimStart('/'))}</span>")).Append("</dl></header>");
        // A stage that did not complete: proctor's status and its own sentence for why, as written.
        if (a.Status != "completed" && Bank.Text(a.Manifest?["status_reason"]) is { } reason)
            page.Append($"<div class=\"said\"><b>{E(a.Status)}</b> <span class=\"soft\">{E(reason)}</span></div>");

        if (stages.Count > 0) page.Append("<div class=\"tabs\" role=\"tablist\" aria-label=\"Stages of this try\">");
        foreach (var s in stages)
        {
            var result = Result(s.Task);
            // Without proctor's word on a stage, one that did not complete still says its status.
            var tag = result switch { "right" => "<span class=\"tag tag-right\">right</span>", "wrong" => "<span class=\"tag tag-wrong\">wrong</span>", null => s.Status is var status && status != "completed" ? $"<span class=\"tag tag-none\">{E(status)}</span>" : "", _ => $"<span class=\"tag tag-none\">{E(result.Replace('_', ' '))}</span>" };
            page.Append($"<a class=\"tab stab{(s.Task == a.Task ? " tab-on" : "")}\" role=\"tab\" aria-selected=\"{(s.Task == a.Task ? "true" : "false")}\" href=\"{U(s.Path)}\"><span class=\"dim num\">{s.Place}</span><span>{E(s.Task)}</span>{tag}</a>");
        }
        page.Append($"{(stages.Count > 0 ? "</div>" : "")}<div class=\"cols\"><aside style=\"display:flex;flex-direction:column;gap:32px\">");
        Checks(page, checks, ids, a.Status == "completed");
        Friction(page, friction, points, ids);
        page.Append("</aside>");

        var diff = Bank.Read(System.IO.Path.Combine(a.Dir, P.Layout.DiffFile)) ?? "";
        page.Append("<section id=\"steps\" class=\"blk blk-top\" aria-label=\"Transcript\"><div class=\"tabs sub\" role=\"tablist\" aria-label=\"What to read\">");
        foreach (var (key, label) in new[] { ((string?)null, "Transcript"), ("prompt", "Prompt"), ("files", $"Files{(diff.Trim().Length == 0 ? " <span class=\"dim regular\">· no change</span>" : "")}") })
            page.Append($"<a class=\"tab{(show == key ? " tab-on" : "")}\" role=\"tab\" aria-selected=\"{(show == key ? "true" : "false")}\" href=\"{AxesPages.Q(a.Path, ("show", key))}#steps\">{label}</a>");
        // Search is the component's input and a few lines of script (page.js): it leaves the steps that hold the words and says how many.
        if (show is null && steps.Count > 0)
            page.Append("<span class=\"search\"><span id=\"search-count\" class=\"small dim num\" aria-live=\"polite\"></span><wa-input id=\"search\" type=\"search\" size=\"small\" placeholder=\"Search this stage\" aria-label=\"Search this stage\" with-clear></wa-input></span>");
        page.Append("</div>");
        if (show == "prompt")
            page.Append(steps.FirstOrDefault(s => Bank.Str(s["source"]) == "user") is { } first ? $"<div class=\"say\" style=\"padding-top:12px\">{E(Shown(Bank.Str(first["message"])))}</div>" : "<div class=\"said soft\">This stage's trajectory holds no prompt.</div>");
        else if (show == "files")
            page.Append(diff.Trim().Length == 0 ? "<div class=\"said soft\" style=\"padding-top:12px\">The stage left the workspace as it found it.</div>" : $"<pre class=\"full\" style=\"padding-top:12px;max-height:none\">{E(Shown(diff))}</pre>");
        else Transcript(page, steps, ids, points, exit);

        var next = place < stages.Count ? stages[place] : null;
        if (stages.Count == 0) return Close(page.Append("</section></div>"));
        page.Append("<div class=\"row\" style=\"display:flex;justify-content:space-between;gap:12px;border-top-color:var(--rule)\">")
            .Append($"<span class=\"dim small\">{(place == 1 ? "First stage of this try" : $"<a href=\"{U(stages[place - 2].Path)}\">Previous stage: {stages[place - 2].Place}. {E(stages[place - 2].Task)}</a>")}</span>")
            .Append(next is null ? "<span class=\"dim small\">Last stage of this try</span>" : $"<a href=\"{U(next.Path)}\">Next stage: {next.Place}. {E(next.Task)}{(Result(next.Task) is { } r ? $", {E(r.Replace('_', ' '))}" : "")}</a>")
            .Append("</div></section></div>");
        return Close(page);
    }

    static string Light(string? result, string status) => result switch
    {
        "right" => "<span class=\"light light-better\">right</span>",
        "wrong" => "<span class=\"light light-worse\">wrong</span>",
        null => status == "completed" ? "" : $"<span class=\"light light-same\">{E(status)}</span>",
        _ => $"<span class=\"light light-same\">{E(result.Replace('_', ' '))}</span>",
    };

    /// <summary>The verdicts by role, as graded: held or did not hold where there is a preferred answer, yes or no for an observation; and the steps each points at.</summary>
    static void Checks(StringBuilder page, List<Check> checks, List<string> ids, bool completed)
    {
        page.Append("<div class=\"blk blk-top\" aria-label=\"Checks\"><h2>Checks</h2>");
        // A stage that did not complete has nothing to grade, so nothing is asked for.
        if (checks.Count == 0) page.Append(!completed ? "<div class=\"said soft\">None: this stage did not complete.</div>" : "<div class=\"said soft\">This stage has no grade yet. Grading the exam writes it.</div>");
        foreach (var role in RoleOrder.Append(""))
        {
            var mine = checks.Where(c => (c.Role ?? "") == role || (role == "" && !RoleOrder.Contains(c.Role ?? ""))).ToList();
            if (mine.Count == 0) continue;
            var (tag, says) = RoleTag.GetValueOrDefault(role, ("tag tag-none", "Graded before proctor recorded a check's role: grade the exam again to see it."));
            page.Append($"<div class=\"group\">{Tagged(tag, role.Length > 0 ? role : "role not recorded", says)}</div>");
            foreach (var c in mine)
            {
                var observed = role == P.Roles.Observation;
                var (word, cls) = c.Result switch
                {
                    "pass" => (observed ? "yes" : "held", observed ? "soft" : "right"),
                    "fail" => (observed ? "no" : "did not hold", observed ? "soft" : "wrong"),
                    "not-applicable" => ("does not apply", "dim"),
                    var other => (other, "dim"),
                };
                page.Append($"<div class=\"ck\" id=\"check-{E(c.Name)}\"><span class=\"{cls} strong\">{E(word)}</span><span class=\"stack\">")
                    .Append($"<span class=\"mono\"{(c.Describes is { } d ? $" title=\"{E(d)}\"" : "")}>{E(c.Name)}</span><span class=\"small soft\">{E(c.Reason)}</span>")
                    .Append(c.Why is { } why ? $"<span class=\"small dim\">Why it is checked: {E(why)}</span>" : "")
                    .Append(c.Script is { } script ? $"<span class=\"small dim mono\">{E(script)}</span>" : "")
                    .Append(Pills(c.Steps, ids)).Append(c.Files.Count > 0 ? $"<span class=\"small dim mono\">{E(string.Join(" ", c.Files))}</span>" : "").Append("</span></div>");
            }
        }
        page.Append("</div>");
    }

    static void Friction(StringBuilder page, JsonObject? friction, List<JsonObject> points, List<string> ids)
    {
        page.Append("<div class=\"blk\" aria-label=\"Friction\"><h2>Friction</h2>");
        if (friction is null) page.Append("<div class=\"said soft\">Friction has not been read on this stage.</div>");
        else if (points.Count == 0) page.Append("<div class=\"said soft\">None flagged.</div>");
        var describes = Describes(friction);
        foreach (var p in points)
        {
            var label = Bank.Str(p["label"]);
            var detail = (p["detail"] as JsonArray)?.Select(Bank.Str).Where(s => s.Length > 0).ToList() ?? [];
            var because = (p["evidence"] as JsonArray ?? []).OfType<JsonObject>().Select(e => Bank.Str(e["because"])).FirstOrDefault(s => s.Length > 0)
                ?? string.Join(" · ", (p["notes"] as JsonArray)?.Select(Bank.Str).Where(s => s.Length > 0) ?? []);
            page.Append($"<div class=\"ck\" style=\"grid-template-columns:minmax(0,1fr)\" id=\"{E(Bank.Str(p["id"]))}\"><span style=\"display:flex;gap:10px;align-items:baseline;flex-wrap:wrap\">")
                .Append($"{Tagged("tag tag-fr", label, describes.GetValueOrDefault(label))}{Pills(Span(p, ids), ids)}<span class=\"mono small dim\">{E(string.Join("; ", detail))}</span></span>")
                .Append($"<span class=\"small soft\">{E(because)}</span></div>");
        }
        page.Append("</div>");
    }

    /// <summary>Steps as pills, one per step or unbroken run of steps in the trajectory's order; each opens its first step.</summary>
    static string Pills(List<string> steps, List<string> ids)
    {
        var at = steps.Select(s => ids.IndexOf(s)).Where(i => i >= 0).Distinct().Order().ToList();
        if (at.Count == 0) return "";
        var runs = new List<(int From, int To)>();
        foreach (var i in at)
            if (runs.Count > 0 && runs[^1].To == i - 1) runs[^1] = (runs[^1].From, i); else runs.Add((i, i));
        return "<span class=\"pills\">" + string.Concat(runs.Select(r => r.From == r.To
            ? $"<wa-button class=\"pill\" size=\"small\" pill appearance=\"outlined\" href=\"#s{U(ids[r.From])}\" title=\"Step {E(ids[r.From])}\">{E(ids[r.From])}</wa-button>"
            : $"<wa-button class=\"pill\" size=\"small\" pill appearance=\"outlined\" href=\"#s{U(ids[r.From])}\" title=\"Steps {E(ids[r.From])} to {E(ids[r.To])}\">{E(ids[r.From])}–{E(ids[r.To])}</wa-button>")) + "</span>";
    }

    static void Transcript(StringBuilder page, List<JsonObject> steps, List<string> ids, List<JsonObject> points, string exit)
    {
        if (steps.Count == 0) { page.Append("<div class=\"said soft\" style=\"padding-top:12px\">This stage has no trajectory.</div>"); return; }
        var covering = ids.ToDictionary(id => id, _ => new List<string>());
        var quotes = new Dictionary<string, List<string>>();
        foreach (var p in points)
        {
            foreach (var id in Span(p, ids)) if (!covering[id].Contains(Bank.Str(p["label"]))) covering[id].Add(Bank.Str(p["label"]));
            foreach (var ev in (p["evidence"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var call = Bank.Str(ev["call"]);
                if (!quotes.TryGetValue(call, out var list)) quotes[call] = list = [];
                list.Add(Bank.Str(ev["quote"]));
            }
        }
        foreach (var step in steps)
        {
            var id = Bank.Str(step["step_id"]);
            var calls = (step["tool_calls"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var results = (step["observation"]?["results"] as JsonArray ?? []).OfType<JsonObject>().ToList();
            var message = Bank.Str(step["message"]);
            // A user turn the program did not write says who did: an oracle's answer is the oracle's, with the sheet entries it answered from.
            var extra = step["extra"] as JsonObject;
            var wrote = Bank.Str(extra?["source"]) is { Length: > 0 } other ? other : Bank.Str(step["source"]);
            var keys = (extra?["keys"] as JsonArray ?? []).Select(Bank.Str).Where(k => k.Length > 0).ToList();
            var who = wrote + (calls.Count > 0 ? $" · {string.Join(", ", calls.Select(c => Bank.Str(c["function_name"])).Distinct())}" : "") + (keys.Count > 0 ? $" · answered from {string.Join(", ", keys)}" : "");
            var labels = covering[id];
            string ResultOf(JsonObject call) => results.FirstOrDefault(r => Bank.Str(r["source_call_id"]) == Bank.Str(call["tool_call_id"])) is { } r ? Content(r["content"]) : "";

            // Folded, a step is who spoke and its first line or command with the first line of what came back; open, the whole of each.
            page.Append($"<wa-details class=\"step{(labels.Count > 0 ? " step-fr" : "")}\" id=\"s{E(id)}\" appearance=\"plain\"><div slot=\"summary\" class=\"step-sum\"><span class=\"mono dim\">{E(id)}</span>")
                .Append($"<div style=\"display:flex;flex-direction:column;gap:6px;min-width:0\"><span class=\"who\">{E(who)}</span>");
            if (calls.Count == 0) page.Append($"<div class=\"out out-say\">{E(FirstLine(message))}</div>");
            else page.Append($"<div class=\"cmd\">{E(FirstLine(Gist(calls[0])))}</div><div class=\"out\">{E(FirstLine(ResultOf(calls[0])))}</div>");
            page.Append("</div><span style=\"display:flex;flex-direction:column;gap:4px;align-items:flex-end\">")
                .Append(string.Concat(labels.Select(l => $"<span class=\"tag tag-fr\">{E(l)}</span>"))).Append("</span></div><div class=\"step-body\">");
            if (message.Length > 0) page.Append($"<div class=\"say\">{E(Shown(message))}</div>");
            for (var n = 0; n < calls.Count; n++)
            {
                quotes.TryGetValue($"s{id}.{n + 1}", out var marks);
                page.Append($"<pre class=\"cmd\">{Marked(Shown(Command(calls[n])), marks)}</pre>");
                if (ResultOf(calls[n]) is { Length: > 0 } result) page.Append($"<pre class=\"full\">{Marked(Shown(result), marks)}</pre>");
            }
            page.Append("</div></wa-details>");
        }
        // An oracle that did not answer leaves no turn behind, so the ending is said where the turn would have been.
        if (exit == "oracle_miss") page.Append("<div class=\"said\" style=\"padding:12px 0\"><b>The oracle had no answer</b> <span class=\"soft\">to the agent's last message: the answer sheet does not cover it, and the stage ended there.</span></div>");
        else if (exit == "oracle_budget") page.Append("<div class=\"said\" style=\"padding:12px 0\"><b>The oracle's answers ran out</b><span class=\"soft\">: the agent's last message went unanswered.</span></div>");
    }

    static string ExitOf(Attempt a) =>
        Bank.Str(a.Core?["exit_reason"]) is { Length: > 0 } counted ? counted : Bank.Str(a.Trajectory?["extra"]?["result"]?["exit_reason"]);

    static string Gist(JsonObject call) =>
        call["arguments"] is JsonObject args && args.Select(kv => kv.Value).OfType<JsonValue>().Select(v => v.ToString()).FirstOrDefault(v => v.Length > 0) is { } first
            ? (args["command"] is JsonValue c ? c.ToString() : first)
            : Command(call);

    /// <summary>The step ids a point covers, in the trajectory's order.</summary>
    static List<string> Span(JsonObject p, List<string> ids)
    {
        var span = p["steps"] as JsonArray;
        var (a, b) = (ids.IndexOf(Bank.Str(span?[0])), ids.IndexOf(Bank.Str(span?[1])));
        return a < 0 || b < a ? [] : ids.GetRange(a, b - a + 1);
    }

    static Dictionary<string, string> Describes(JsonObject? friction)
    {
        var map = new Dictionary<string, string>();
        foreach (var list in new[] { friction?["detector"]?["labels"], friction?["reader"]?["labels"] })
            foreach (var label in (list as JsonArray ?? []).OfType<JsonObject>())
                if (Bank.Str(label["name"]) is { Length: > 0 } name && Bank.Str(label["describes"]) is { Length: > 0 } d) map[name] = d;
        return map;
    }

    static string When(string made) =>
        DateTime.TryParse(made, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at) ? at.ToString("d MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture) : made;

    /// <summary>A verdict from checks.json, as proctor wrote it.</summary>
    sealed record Check(string Name, JsonObject V)
    {
        public string Result => Bank.Str(V["result"]);
        public string Reason => Bank.Str(V["reason"]);
        public string? Role => Bank.Text(V["role"]);
        public string? Describes => Bank.Text(V["describes"]);
        public string? Why => Bank.Text(V["why"]);
        /// <summary>A script check's script, relative to proctor's root, so a person can open what decided.</summary>
        public string? Script => Bank.Text(V["script"]);
        public List<string> Steps => [.. (V["evidence"] as JsonArray ?? []).OfType<JsonObject>().Select(e => Bank.Str(e["step"])).Where(s => s.Length > 0).Distinct()];
        public List<string> Files => [.. (V["evidence"] as JsonArray ?? []).OfType<JsonObject>().Select(e => Bank.Str(e["file"])).Where(s => s.Length > 0).Distinct()];
    }
}
