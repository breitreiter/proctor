using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

// A cell's run read into the fixed windows the checks look at: nb's JSONL, or an agent cell's ATIF trajectory with
// proctor's counted core. Nothing here judges anything.

record Trailer(
    string ExitReason,
    long? Input, long? Output, long? Total, bool Estimated,
    int? Turns, int? ToolCalls, long? DurationMs,
    string? Provider, string? Harness, int Denied, int OracleTurns);

record ToolCall(string Id, string Name, JsonObject? Arguments, string? Approved, string? ApprovalReason)
{
    public bool Denied => Approved == "deny";
    /// <summary>The rung name without nb's explanatory suffix, e.g. "default-deny" from "default-deny (default=deny; …)".</summary>
    public string? Rung => ApprovalReason?.Split(' ', 2)[0];
}

record ToolResult(string Id, string Output, JsonObject? Result, bool? Failed = null)
{
    /// <summary>What the agent said of the result, when it says (an ATIF result's is_error); else a refusal, a tool that reported an error, or a command whose exit code was not 0.</summary>
    public bool IsError => Failed ?? (
        Output.StartsWith("Error:", StringComparison.Ordinal)
        || (Result?["exit_code"] is JsonValue v && v.TryGetValue<int>(out var code) && code != 0)
        || (ExitCodeTrailer.Match(Output) is { Success: true } m && m.Groups[1].Value != "0"));

    static readonly Regex ExitCodeTrailer = new(@"\[exit code: (\d+)\]\s*$");
}

/// <summary>A user turn after the prompt. Source is who wrote it when the program did not (nb's "oracle"); Keys are the sheet entries an oracle turn answered with.</summary>
record UserTurn(string Text, string? Source, IReadOnlyList<string>? Keys = null)
{
    public bool IsOracle => Source == "oracle";

    // By value, keys included: a record compares a list by reference, and two reads of one run must be equal.
    public virtual bool Equals(UserTurn? other) =>
        other is not null && Text == other.Text && Source == other.Source && (Keys ?? []).SequenceEqual(other.Keys ?? []);

    public override int GetHashCode() => HashCode.Combine(Text, Source, Keys?.Count ?? 0);

    public bool IsLoopNudge => Text.StartsWith("<system_reminder>", StringComparison.Ordinal) && Text.Contains("repetitive loop");
}

sealed class Transcript
{
    public Trailer? Trailer { get; private set; }
    public string? Answer { get; private set; }
    public JsonNode? AnswerJson { get; private set; }
    public List<ToolCall> ToolCalls { get; } = [];
    public List<ToolResult> ToolResults { get; } = [];
    public List<UserTurn> UserTurns { get; } = [];
    public string? Diff { get; init; }
    public bool LoopNudged => UserTurns.Any(u => u.IsLoopNudge);

    /// <summary>Whether a cell holds a run to read: nb's transcript, or an agent cell's trajectory.</summary>
    public static bool Exists(string cellDir) =>
        File.Exists(Path.Combine(cellDir, Layout.TranscriptFile)) || File.Exists(Path.Combine(cellDir, Layout.TrajectoryFile));

    /// <summary>Read a cell: transcript.jsonl, else an agent cell's trajectory.json and core.json; plus diff.patch when there is one.</summary>
    public static Transcript Read(string cellDir)
    {
        var diffFile = Path.Combine(cellDir, Layout.DiffFile);
        var diff = File.Exists(diffFile) ? File.ReadAllText(diffFile) : null;
        var trajectory = Path.Combine(cellDir, Layout.TrajectoryFile);
        if (!File.Exists(Path.Combine(cellDir, Layout.TranscriptFile)) && File.Exists(trajectory))
        {
            var core = Path.Combine(cellDir, Layout.CoreFile);
            return FromAtif(JsonNode.Parse(File.ReadAllText(trajectory))!.AsObject(),
                File.Exists(core) ? Runner.ReadJson<CountedCore>(core) : null, diff);
        }
        return Parse(File.ReadAllText(Path.Combine(cellDir, Layout.TranscriptFile)), diff);
    }

    /// <summary>
    /// An ATIF trajectory: the main agent's conversation as the windows (a subagent's calls are counted in the core, not
    /// listed), and the core as the trailer, so a check reads proctor's count and never the agent's. The answer is the
    /// last agent step with text; a tool result is an error when the agent marked it so (Claude Code's is_error). What
    /// nb's conversion keeps under extra (a turn's source, a call's approval, a result's structured result, the parsed
    /// answer, nb's own trailer) is read back, so an nb run reads the same through Harbor as bare.
    /// </summary>
    public static Transcript FromAtif(JsonObject trajectory, CountedCore? core, string? diff = null)
    {
        var t = new Transcript { Diff = diff };
        var nb = Str(trajectory["agent"] as JsonObject ?? [], "name") == Arm.Nb;
        foreach (var step in (trajectory["steps"]?.AsArray() ?? []).OfType<JsonObject>())
        {
            var text = AtifText(step["message"]);
            var extra = step["extra"] as JsonObject;
            switch (Str(step, "source"))
            {
                case "user":
                    t.UserTurns.Add(new UserTurn(text, extra is null ? null : Str(extra, "source"), Keys(extra?["keys"])));
                    break;
                case "agent":
                    if (text.Trim().Length > 0)
                    {
                        t.Answer = text;
                        t.AnswerJson = extra?["assistant_json"]?.DeepClone() ?? LastJsonFence(text);
                    }
                    foreach (var call in (step["tool_calls"]?.AsArray() ?? []).OfType<JsonObject>())
                    {
                        var approval = call["extra"] as JsonObject ?? [];
                        t.ToolCalls.Add(new ToolCall(Str(call, "tool_call_id") ?? "", Str(call, "function_name") ?? "", call["arguments"]?.DeepClone() as JsonObject, Str(approval, "approved"), Str(approval, "approval_reason")));
                    }
                    foreach (var result in (step["observation"]?["results"]?.AsArray() ?? []).OfType<JsonObject>())
                    {
                        var isError = result["extra"]?["tool_result_metadata"]?["raw_tool_result"]?["is_error"] is JsonValue e && e.TryGetValue<bool>(out var b) ? b : (bool?)null;
                        // nb marks no result an error; its output says so, as on the bare path.
                        t.ToolResults.Add(new ToolResult(Str(result, "source_call_id") ?? "", AtifText(result["content"]), result["extra"]?["result"]?.DeepClone() as JsonObject, isError ?? (nb ? null : false)));
                    }
                    break;
            }
        }
        if (core is not null)
        {
            var own = trajectory["extra"]?["result"] as JsonObject;
            var usage = own?["usage"] as JsonObject;
            t.Trailer = new Trailer(core.ExitReason, core.InputTokens, core.OutputTokens,
                Num(usage, "total") ?? (core.InputTokens is { } i && core.OutputTokens is { } o ? i + o : null),
                Estimated: usage?["estimated"] is JsonValue est && est.TryGetValue<bool>(out var estimated) && estimated,
                core.Turns, core.ToolCalls, core.DurationMs,
                Provider: own is null ? null : Str(own, "provider"),
                Harness: nb ? (own is null ? null : Str(own, "harness")) ?? Arm.Nb : Str(trajectory["agent"] as JsonObject ?? [], "name"),
                core.Denied, OracleTurns: (int?)Num(own, "oracle_turns") ?? 0);
        }
        return t;
    }

    /// <summary>An ATIF message or result content: a string, or content parts whose text parts are joined.</summary>
    static string AtifText(JsonNode? node) => node switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonArray parts => string.Join("\n", parts.OfType<JsonObject>().Select(p => Str(p, "text") ?? "").Where(x => x.Length > 0)),
        _ => "",
    };

    public static Transcript Parse(string jsonl, string? diff = null)
    {
        var t = new Transcript { Diff = diff };
        foreach (var line in jsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? ev;
            try { ev = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { continue; }   // a torn last line from a killed run is not evidence
            if (ev is null) continue;
            switch (Str(ev, "type"))
            {
                case "user":
                    t.UserTurns.Add(new UserTurn(Text(ev), Str(ev, "source"), Keys(ev["keys"])));
                    break;
                case "assistant_text":
                    t.Answer = Text(ev);
                    t.AnswerJson = LastJsonFence(t.Answer);   // nb emits no assistant_json event yet; the fence is the contract
                    break;
                case "assistant_json":
                    t.AnswerJson = ev["value"]?.DeepClone();
                    break;
                case "tool_call":
                    t.ToolCalls.Add(new ToolCall(Str(ev, "id") ?? "", Str(ev, "name") ?? "",
                        ev["arguments"]?.DeepClone() as JsonObject, Str(ev, "approved"), Str(ev, "approval_reason")));
                    break;
                case "tool_result":
                    t.ToolResults.Add(new ToolResult(Str(ev, "id") ?? "", Str(ev, "output") ?? "", ev["result"]?.DeepClone() as JsonObject));
                    break;
                case "result":
                    var usage = ev["usage"] as JsonObject;
                    t.Trailer = new Trailer(
                        Str(ev, "exit_reason") ?? "ok",
                        Num(usage, "input"), Num(usage, "output"), Num(usage, "total"),
                        usage?["estimated"] is JsonValue e && e.TryGetValue<bool>(out var est) && est,
                        (int?)Num(ev, "turns"), (int?)Num(ev, "tool_calls"), Num(ev, "duration_ms"),
                        Str(ev, "provider"), Str(ev, "harness"),
                        (int?)Num(ev, "denied") ?? 0, (int?)Num(ev, "oracle_turns") ?? 0);
                    break;
            }
        }
        return t;
    }

    /// <summary>Paths a unified diff touches, from its `diff --git a/x b/x` headers (or `+++ b/x` when a diff lacks them).</summary>
    public IReadOnlyList<string> TouchedPaths()
    {
        if (Diff is null) return [];
        var paths = new List<string>();
        foreach (var line in Diff.Split('\n'))
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                var parts = line.Split(' ');
                if (parts.Length >= 4) paths.Add(StripPrefix(parts[3]));
            }
            else if (line.StartsWith("+++ ", StringComparison.Ordinal) && !line.StartsWith("+++ /dev/null", StringComparison.Ordinal))
            {
                var p = StripPrefix(line[4..].Split('\t')[0]);
                if (!paths.Contains(p)) paths.Add(p);
            }
        }
        return paths;

        static string StripPrefix(string p) => p.StartsWith("a/") || p.StartsWith("b/") ? p[2..] : p;
    }

    static readonly Regex JsonFence = new("```json[ \\t]*\\n(.*?)\\n[ \\t]*```", RegexOptions.Singleline);

    /// <summary>The last ```json fence in the text, parsed; null when there is none or it does not parse.</summary>
    public static JsonNode? LastJsonFence(string text)
    {
        var last = JsonFence.Matches(text).LastOrDefault();
        if (last is null) return null;
        try { return JsonNode.Parse(last.Groups[1].Value); }
        catch (JsonException) { return null; }
    }

    private static IReadOnlyList<string>? Keys(JsonNode? keys) =>
        keys is JsonArray a ? a.Select(k => k?.GetValue<string>()).OfType<string>().ToList() : null;

    static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long? Num(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;

    /// <summary>An nb event's text: its text field, or its content parts joined.</summary>
    public static string Text(JsonObject ev)
    {
        if (Str(ev, "text") is { } s) return s;
        if (ev["content"] is JsonArray parts)
            return string.Join("\n", parts.OfType<JsonObject>().Select(p => Str(p, "text") ?? Str(p, "note") ?? ""));
        return "";
    }
}
