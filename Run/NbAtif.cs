using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>
/// nb's JSONL as an ATIF v1.7 trajectory, so an nb arm in Harbor is read like any agent. One agent step per nb turn
/// (its text, thinking, tool calls and their results); a user or system message is its own step. What ATIF has no
/// field for goes under `extra`, in nb's own words: a user turn's source and keys, a call's approval and rung, a
/// result's structured result, the parsed answer JSON, and the result trailer. Strict like every mapping at ingest:
/// an event type this does not know is unmapped, never dropped.
/// </summary>
static class NbAtif
{
    /// <summary>nb's directive events: configuration, not conversation, kept under the trajectory's extra.</summary>
    static readonly HashSet<string> Directives = ["run", "provider", "model", "harness", "mcp", "tools", "approval", "loop", "budget", "oracle"];

    public static (JsonObject? Trajectory, string? Unmapped) Convert(string jsonl, string? version)
    {
        var steps = new JsonArray();
        var directives = new JsonArray();
        JsonObject? trailer = null;
        JsonObject? agentStep = null;
        long? agentTurn = null;

        JsonObject AgentStep(JsonObject ev)
        {
            var turn = ev["turn"] is JsonValue v && v.TryGetValue<long>(out var n) ? n : (long?)null;
            if (agentStep is null || agentTurn != turn)
            {
                agentStep = new JsonObject { ["step_id"] = steps.Count + 1, ["source"] = "agent", ["message"] = "" };
                agentTurn = turn;
                steps.Add(agentStep);
            }
            return agentStep;
        }

        void Add(JsonObject step)
        {
            step["step_id"] = steps.Count + 1;
            steps.Add(step);
            agentStep = null;
        }

        foreach (var line in jsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? ev;
            try { ev = JsonNode.Parse(line) as JsonObject; }
            catch (JsonException) { continue; }   // a torn last line from a killed run is not evidence
            if (ev is null) continue;
            var type = Str(ev, "type");
            switch (type)
            {
                case "system":
                    Add(new JsonObject { ["source"] = "system", ["message"] = Transcript.Text(ev) });
                    break;
                case "user":
                    var user = new JsonObject { ["source"] = "user", ["message"] = Transcript.Text(ev) };
                    var userExtra = new JsonObject();
                    if (ev["source"] is { } source) userExtra["source"] = source.DeepClone();
                    if (ev["keys"] is { } keys) userExtra["keys"] = keys.DeepClone();
                    if (userExtra.Count > 0) user["extra"] = userExtra;
                    Add(user);
                    break;
                case "assistant_text":
                    var step = AgentStep(ev);
                    var text = Transcript.Text(ev);
                    step["message"] = step["message"]!.GetValue<string>() is { Length: > 0 } before ? before + "\n" + text : text;
                    break;
                case "thinking":
                    AgentStep(ev)["reasoning_content"] = Transcript.Text(ev);
                    break;
                case "assistant_json":
                    Extra(AgentStep(ev))["assistant_json"] = ev["value"]?.DeepClone();
                    break;
                case "tool_call":
                    var call = new JsonObject
                    {
                        ["tool_call_id"] = Str(ev, "id") ?? "",
                        ["function_name"] = Str(ev, "name") ?? "",
                        ["arguments"] = ev["arguments"]?.DeepClone() ?? new JsonObject(),
                    };
                    var callExtra = new JsonObject();
                    if (Str(ev, "approved") is { } approved) callExtra["approved"] = approved;
                    if (Str(ev, "approval_reason") is { } reason) callExtra["approval_reason"] = reason;
                    if (callExtra.Count > 0) call["extra"] = callExtra;
                    var calls = AgentStep(ev)["tool_calls"] ??= new JsonArray();
                    calls.AsArray().Add(call);
                    break;
                case "tool_result":
                    var result = new JsonObject { ["source_call_id"] = Str(ev, "id") ?? "", ["content"] = Str(ev, "output") ?? "" };
                    if (ev["result"] is JsonObject structured) result["extra"] = new JsonObject { ["result"] = structured.DeepClone() };
                    var observation = AgentStep(ev)["observation"] ??= new JsonObject { ["results"] = new JsonArray() };
                    observation["results"]!.AsArray().Add(result);
                    break;
                case "result":
                    trailer = (JsonObject)ev.DeepClone();
                    break;
                case { } directive when Directives.Contains(directive):
                    directives.Add(ev.DeepClone());
                    break;
                default:
                    return (null, $"nb event type '{type}' has no ATIF mapping");
            }
        }

        var trajectory = new JsonObject
        {
            ["schema_version"] = "ATIF-v1.7",
            ["agent"] = new JsonObject { ["name"] = Arm.Nb, ["version"] = version, ["model_name"] = trailer is null ? null : Str(trailer, "model") },
            ["steps"] = steps,
        };
        if (trailer?["usage"] is JsonObject usage)
            trajectory["final_metrics"] = new JsonObject
            {
                ["total_prompt_tokens"] = usage["input"]?.DeepClone(),
                ["total_completion_tokens"] = usage["output"]?.DeepClone(),
                ["total_steps"] = steps.Count,
            };
        var extra = new JsonObject();
        if (trailer is not null) extra["result"] = trailer;
        if (directives.Count > 0) extra["directives"] = directives;
        if (extra.Count > 0) trajectory["extra"] = extra;
        return (trajectory, null);
    }

    static JsonObject Extra(JsonObject step) => (step["extra"] ??= new JsonObject()).AsObject();

    static string? Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
