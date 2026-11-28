using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Auspex;

/// <summary>
/// The reader stage of `proctor friction`: a chat model reads one attempt and names the friction rules cannot see (a
/// repair, a lost search, a re-read, a dead end, a work-around). It chooses from a fixed list and must quote the call;
/// nothing it says is written until the call exists, the label is in the list and the quote is in that call. Its
/// one-line reason is kept as a note and is prose, never counted. What was sent and what came back is kept in the
/// attempt, and is the cache: the same attempt, prompt and model are not asked twice.
/// </summary>
static class FrictionReader
{
    /// <summary>The names the prompt asks the model for. The prompt keeps its measured wording, so its hash, and every reading kept in an attempt, stays valid.</summary>
    public static readonly string[] Labels = ["repair", "lost", "reread", "dead-end", "workaround"];

    /// <summary>
    /// What each asked-for name is written as in a file, and its sentence. Renamed 2026-11-22 so each reads bare
    /// (check-evidence.md, C5.1): "lost" is a try or stage that died, "repair" says nothing of what, and "dead-end" hid
    /// that the instruction was wrong. Mapped here, not in the prompt, so no attempt is read again.
    /// </summary>
    public static readonly (string Asked, string Name, string Describes)[] LabelDefs =
    [
        ("repair", "fixed-own-work", "a step where the agent fixed its own earlier work after something it ran failed"),
        ("lost", "wasted-read", "a step where a search or a read led to nothing of later use"),
        ("reread", "reread", "a step where the agent read again something an earlier call already showed"),
        ("dead-end", "bad-instruction", "a step where the agent followed an instruction, document or path that could not work as given"),
        ("workaround", "workaround", "a step where the agent built a way round something instead of using it"),
    ];

    static string AsWritten(string asked) => LabelDefs.First(l => l.Asked == asked).Name;

    // The wording the first measurements were made with (auspex, project/friction-contract.md). A change is a new prompt hash.
    public const string Instructions = """
        You are reading a record of an AI agent working on a task with tools. Find the tool calls that were FRICTION:
        cost the task did not ask for. Most calls are ordinary work and get no label. Label only what fits one of these:

          repair      fixing its own earlier work after something it ran failed: inspecting the failure, changing what
                      it wrote, running again. A first test run that fails is not repair; what follows it is.
          lost        a search or a read that led to nothing of later use: a wrong area, or the same thing searched for
                      again with new terms. A search the task needs, that finds what is then used, is not lost.
          reread      reading again something an earlier call already showed.
          dead-end    following an instruction, a document or a path that could not work as given, so the agent had to
                      find another way. Label the call where it meets the dead end.
          workaround  building a way round something instead of using it: a throwaway script, a hand-made substitute
                      for a tool or a file that should have worked.

        Do NOT label: a command that is simply missing from the machine, a timeout, or a tool refusing a call. Those
        are found another way.

        Each call starts with an id in square brackets, like [s14.1], then the tool, the command, and RESULT.
        Judge a call by what it was for, using what came before and after it.

        Answer with one JSON object and nothing else. Keys are call ids. Include ONLY the calls you label:

          {"s14.1": {"label": "repair", "quote": "error CS1002: ; expected", "why": "fixes the compile error from s13.1"}}

          label  exactly one name from the list above.
          quote  text copied exactly, character for character, from THAT call's command or result as shown. One line,
                 at most 120 characters, no ellipsis. Not the agent's own prose, not another call, and not the
                 record's placeholders such as <<HEREDOC or [...N chars...].
          why    at most 15 words.

        If nothing fits, answer {}.

        """;

    public static readonly string PromptHash = Hash(Instructions);
    static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly JsonSerializerOptions Written = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
    static readonly Regex StdoutEcho = new(@"\n\[stdout\]\n.*", RegexOptions.Singleline);
    static readonly Regex FirstObject = new(@"\{.*\}", RegexOptions.Singleline);

    /// <summary>What one attempt's reading came to. <see cref="Says"/> is the line the log prints.</summary>
    public record Reading(bool Asked, int Labelled, int Kept, List<string> Dropped, long? InputTokens, long? OutputTokens, string? Problem)
    {
        public string Says => Problem ?? $"{Kept} of {Labelled} kept{(Asked ? "" : ", reused")}";
    }

    static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];

    static string Clip(string text, int head, int tail = 0)
    {
        text = text.Trim();
        if (text.Length <= head + tail + 40) return text;
        return text[..head] + $"\n  […{text.Length - head - tail} chars…]\n" + (tail > 0 ? text[^tail..] : "");
    }

    /// <summary>
    /// One attempt as text for a reader who labels calls: the task, the agent's words, and per tool call a line id
    /// (s&lt;step&gt;.&lt;n&gt;), the command with heredoc bodies removed, and the head and tail of the result.
    /// </summary>
    public static string Render(JsonObject trajectory)
    {
        var lines = new List<string>();
        foreach (var s in (trajectory["steps"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var message = s["message"] is { } m ? Friction.Text(m) : "";
            if (Friction.String(s["source"]) != "agent")
            {
                if (Friction.String(s["source"]) == "user") lines.Add("TASK PROMPT:\n" + Clip(message, 1500) + "\n");
                continue;
            }
            var results = new Dictionary<string, string>();
            foreach (var o in ((s["observation"] as JsonObject)?["results"] as JsonArray ?? []).OfType<JsonObject>())
                results[Friction.String(o["source_call_id"]) ?? ""] = o["content"] is { } content ? Friction.Text(content) : "";
            var stepId = Friction.Text(s["step_id"]);
            if (message.Trim().Length > 0) lines.Add($"AGENT (step {stepId}): {Clip(message, 600)}");
            foreach (var (call, n) in (s["tool_calls"] as JsonArray ?? []).OfType<JsonObject>().Select((c, i) => (c, i + 1)))
            {
                var arguments = call["arguments"] as JsonObject ?? [];
                var body = Friction.String(arguments["command"]) is { } command ? Shell.StripHeredocs(command) : Shortened(arguments).ToJsonString(Compact);
                var result = StdoutEcho.Replace(results.GetValueOrDefault(Friction.String(call["tool_call_id"]) ?? "", ""), "");
                lines.Add($"[s{stepId}.{n}] {Friction.String(call["function_name"])}: {Clip(body, 1800)}\n  RESULT ({result.Length} chars): " + Clip(result, 500, 300).Replace("\n", "\n    ") + "\n");
            }
        }
        return string.Join('\n', lines);
    }

    static JsonObject Shortened(JsonObject arguments)
    {
        var shown = new JsonObject();
        foreach (var (key, value) in arguments)
            shown[key] = value is null ? null : Friction.Text(value) is { Length: >= 300 } text ? text[..300] + "…" : value.DeepClone();
        return shown;
    }

    public static string Prompt(JsonObject trajectory, string? surfaceDescription) =>
        Instructions + (string.IsNullOrEmpty(surfaceDescription) ? "" : $"\nThe thing under test in this run: {surfaceDescription}\n") + "\n===== THE RUN =====\n\n" + Render(trajectory) + "\n";

    /// <summary>The attempt's reading file for this judge, prompt and attempt.</summary>
    public static string FileOf(string attemptDir, string judge, string hash) => Path.Combine(attemptDir, $"{Layout.ReadingPrefix}.{judge}.{hash}.json");

    /// <summary>
    /// Ask the reader about one attempt, or reuse the attempt's reading, and add its checked points to <paramref name="file"/>.
    /// A reader that did not answer adds nothing and leaves no reading, so the next pass asks again.
    /// </summary>
    internal static Reading Read(string attemptDir, JsonObject trajectory, JsonObject file, List<Friction.Step> steps, string? surfaceDescription, JudgeClient client, string judge)
    {
        var def = client.Defs[judge];
        var prompt = Prompt(trajectory, surfaceDescription);
        var path = FileOf(attemptDir, judge, Hash(prompt + "\n" + def.Model));
        string reply;
        long? input = null, output = null;
        var asked = false;
        if (!client.Rejudge && File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject kept && Friction.String(kept["reply"]) is { } earlier)
        {
            reply = earlier;
        }
        else
        {
            var started = DateTime.UtcNow;
            try
            {
                var chat = client.ChatClient(def with { Endpoint = def.ResolvedEndpoint(judge) }, def.ResolvedKey(judge));
                // No temperature: the newer Claude models refuse the parameter outright ("`temperature` is deprecated for this model").
                var response = client.Send(judge, () => chat.GetResponseAsync([new ChatMessage(ChatRole.User, prompt)]));
                reply = response.Text;
                (input, output) = (response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                return new Reading(true, 0, 0, [], null, null, $"no answer: {e.Message.Split('\n')[0].Trim()}");
            }
            asked = true;
            File.WriteAllText(path, new JsonObject
            {
                ["judge"] = judge, ["model"] = def.Model, ["endpoint"] = def.Endpoint, ["prompt"] = PromptHash, ["read"] = started.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["duration_ms"] = (long)(DateTime.UtcNow - started).TotalMilliseconds, ["usage"] = new JsonObject { ["input_tokens"] = input, ["output_tokens"] = output },
                ["request"] = prompt, ["reply"] = reply,
            }.ToJsonString(Written) + "\n");
        }
        var (labelled, added, dropped, problem) = Add(file, steps, reply, def.Model ?? judge);
        return new Reading(asked, labelled, added, dropped, input, output, problem);
    }

    /// <summary>The first JSON object in a reply, tolerating a code fence or words around it.</summary>
    static JsonObject? Parse(string reply)
    {
        foreach (var candidate in new[] { reply, FirstObject.Match(reply).Value })
            try { if (candidate.Length > 0 && JsonNode.Parse(candidate) is JsonObject o) return o; }
            catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// Check a reply and add what survives to the file as points with a model source: adjacent steps with one label are
    /// one point. Returns how many calls the reader labelled, how many were kept, and each drop with its reason.
    /// </summary>
    internal static (int Labelled, int Kept, List<string> Dropped, string? Problem) Add(JsonObject file, List<Friction.Step> steps, string reply, string model)
    {
        if (Parse(reply) is not { } answers) return (0, 0, [], "the reply is not a JSON object");
        var where = steps.SelectMany((step, i) => step.Calls.Select(call => (call, i))).GroupBy(x => x.call.Id).ToDictionary(g => g.Key, g => g.Last());
        var flags = new Dictionary<(string Label, int Step), List<(JsonObject Evidence, string Call, string Why)>>();
        var dropped = new List<string>();
        var kept = 0;
        foreach (var (callId, answer) in answers)
        {
            if (!where.TryGetValue(callId, out var found)) { dropped.Add($"{callId}: no such call"); continue; }
            var label = Friction.String((answer as JsonObject)?["label"]);
            if (label is null || !Labels.Contains(label)) { dropped.Add($"{callId}: label '{label}' is not in the list"); continue; }
            var quote = Friction.String(answer!["quote"]) ?? "";
            if (!Friction.Verified(quote, found.call))
            {
                var unescaped = quote.Replace("\\\"", "\"").Replace(@"\\", @"\");          // a reader quotes JSON still escaped
                if (!Friction.Verified(unescaped, found.call)) { dropped.Add($"{callId}: the quote is not in the call: {(quote.Length > 70 ? quote[..70] : quote)}"); continue; }
                quote = unescaped;
            }
            kept++;
            var why = Friction.String(answer["why"]) ?? "";
            var evidence = new JsonObject
            {
                ["step"] = steps[found.i].Id, ["call"] = callId, ["quote"] = quote,
                ["where"] = found.call.SquashedCommand.Contains(Shell.Squash(quote), StringComparison.Ordinal) ? "command" : "result",
            };
            // The reader's one-line reason is its `because`, as a rule's sentence is (C5.1); notes keeps it for the old readers.
            if (why.Length > 0) evidence["because"] = why.Length > 160 ? why[..160] : why;
            if (!flags.TryGetValue((label, found.i), out var list)) flags[(label, found.i)] = list = [];
            list.Add((evidence, callId, why.Length > 160 ? why[..160] : why));
        }

        var points = file["points"]!.AsArray().Select(p => p!.AsObject()).Where(p => Friction.String(p["source"]!["kind"]) != "model").ToList();
        file["points"]!.AsArray().Clear();
        var order = steps.Select((s, n) => (s.Id, n)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Last().n);
        var read = new List<(int First, int Label, JsonObject Point)>();
        foreach (var (label, labelIndex) in Labels.Select((l, n) => (l, n)))
        {
            var stretch = new List<int>();
            foreach (var i in flags.Keys.Where(k => k.Label == label).Select(k => (int?)k.Step).Order().Append(null))
            {
                if (stretch.Count > 0 && (i is null || i != stretch[^1] + 1))
                {
                    var all = stretch.SelectMany(k => flags[(label, k)]).ToList();
                    var calls = all.Select(f => f.Call).Distinct().OrderBy(Friction.CallOrder, Comparer<(long, long)>.Default).ToList();
                    var costs = stretch.SelectMany(k => steps[k].Calls).Where(c => calls.Contains(c.Id)).Select(c => c.Cost).ToList();
                    read.Add((stretch[0], labelIndex, new JsonObject
                    {
                        ["label"] = AsWritten(label),
                        ["steps"] = new JsonArray(steps[stretch[0]].Id, steps[stretch[^1]].Id),
                        ["calls"] = new JsonArray(calls.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
                        ["evidence"] = new JsonArray(all.Take(6).Select(f => (JsonNode?)f.Evidence).ToArray()),
                        ["rules"] = new JsonArray(), ["detail"] = new JsonArray(),
                        ["notes"] = new JsonArray(all.Where(f => f.Why.Length > 0).Take(6).Select(f => (JsonNode?)JsonValue.Create(f.Why)).ToArray()),
                        ["cost_usd"] = costs.Any(c => c is null) ? null : Math.Round(costs.Sum(c => c!.Value), 6),
                        ["source"] = new JsonObject { ["kind"] = "model", ["model"] = model, ["prompt"] = PromptHash },
                    }));
                    stretch = [];
                }
                if (i is { } step) stretch.Add(step);
            }
        }
        // The rules' points keep their order; a reader's point follows the rules' points that start on the same step.
        var merged = points.Select((p, n) => (First: order[p["steps"]![0]!.GetValue<string>()], Kind: 0, Rank: n, Point: p))
            .Concat(read.Select(r => (r.First, Kind: 1, Rank: r.Label, r.Point)))
            .OrderBy(x => x.First).ThenBy(x => x.Kind).ThenBy(x => x.Rank).Select(x => x.Point).ToList();
        var ids = steps.Select(s => s.Id).ToList();
        var flagged = new HashSet<string>();
        var byLabel = new JsonObject();
        for (var n = 0; n < merged.Count; n++)
        {
            var p = merged[n];
            p["id"] = $"p{n + 1}";
            file["points"]!.AsArray().Add(p);
            int a = ids.IndexOf(p["steps"]![0]!.GetValue<string>()), b = ids.IndexOf(p["steps"]![1]!.GetValue<string>());
            flagged.UnionWith(ids.Skip(a).Take(b - a + 1));
            var label = p["label"]!.GetValue<string>();
            byLabel[label] = (byLabel[label]?.GetValue<int>() ?? 0) + 1;
        }
        var summary = file["summary"]!.AsObject();
        summary["points"] = merged.Count;
        summary["flagged_steps"] = flagged.Count;
        summary["by_label"] = byLabel;
        file["reader"] = new JsonObject { ["model"] = model, ["prompt"] = PromptHash, ["labels"] = Friction.LabelObjects(LabelDefs.Select(l => (l.Name, l.Describes))) };
        return (answers.Count, kept, dropped, null);
    }
}
