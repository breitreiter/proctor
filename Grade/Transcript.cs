using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

// nb's JSONL read into the fixed windows the checks look at. Nothing here judges anything.

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

record ToolResult(string Id, string Output, JsonObject? Result)
{
    /// <summary>A refusal, a tool that reported an error, or a command whose exit code was not 0.</summary>
    public bool IsError =>
        Output.StartsWith("Error:", StringComparison.Ordinal)
        || (Result?["exit_code"] is JsonValue v && v.TryGetValue<int>(out var code) && code != 0)
        || (ExitCodeTrailer.Match(Output) is { Success: true } m && m.Groups[1].Value != "0");

    static readonly Regex ExitCodeTrailer = new(@"\[exit code: (\d+)\]\s*$");
}

record UserTurn(string Text, string? Source)
{
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

    /// <summary>Read a cell: transcript.jsonl plus diff.patch when the teardown hook wrote one.</summary>
    public static Transcript Read(string cellDir)
    {
        var diffFile = Path.Combine(cellDir, Layout.DiffFile);
        return Parse(
            File.ReadAllText(Path.Combine(cellDir, Layout.TranscriptFile)),
            File.Exists(diffFile) ? File.ReadAllText(diffFile) : null);
    }

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
                    t.UserTurns.Add(new UserTurn(Text(ev), Str(ev, "source")));
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

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static long? Num(JsonObject? o, string key) =>
        o?[key] is JsonValue v && v.TryGetValue<long>(out var n) ? n : null;

    private static string Text(JsonObject ev)
    {
        if (Str(ev, "text") is { } s) return s;
        if (ev["content"] is JsonArray parts)
            return string.Join("\n", parts.OfType<JsonObject>().Select(p => Str(p, "text") ?? Str(p, "note") ?? ""));
        return "";
    }
}
