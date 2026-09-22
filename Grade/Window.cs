using System.Text;
using System.Text.Json;

namespace Proctor;

/// <summary>
/// What a model check sees: named, deterministic extractions from the transcript, joined with `+`. The whole transcript
/// is not a window and cannot be spelled. An empty part or an oversized total is an error before any call, never a
/// truncation, because a decider answers an empty or cut state confidently and wrongly.
/// </summary>
static class Window
{
    /// <summary>Each window and the one-sentence description a judge is given for it.</summary>
    public static readonly Dictionary<string, string> Known = new()
    {
        ["answer"] = "The complete text of the assistant's last message, sent after its final tool call. This is the whole message, not an excerpt.",
        ["answer_json"] = "The last JSON fence in the assistant's last message, pretty-printed.",
        ["prompt"] = "The complete task the assistant was given, verbatim.",
        ["tool_calls"] = "Every tool call the assistant made, in order: an ordinal, the tool name, its arguments on one line, and whether it was denied. Tool outputs are not included.",
        ["tool_results"] = "The output of every tool call, in order, each cut to its first lines.",
        ["diff"] = "The complete git diff of the repository after the assistant finished, against the state it started from.",
        ["user_turns"] = "Every user turn after the task prompt, in order: scripted turns and the loop nudge.",
    };

    public const int ResultLines = 40;
    const int ArgumentChars = 400;

    public static string? Validate(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return $"required: one or more of {string.Join(", ", Known.Keys)}, joined with +";
        var unknown = Parts(spec).Where(p => !Known.ContainsKey(p)).ToList();
        return unknown.Count == 0 ? null : $"unknown window {string.Join(", ", unknown)}; known: {string.Join(", ", Known.Keys)}";
    }

    public static List<string> Parts(string spec) => spec.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>The window's parts as text, in the order named. The error names the part that was empty, or says the total was too large.</summary>
    public static (Dictionary<string, string>? Parts, string? Error) Extract(string spec, CellContext cell, Transcript t, int maxChars)
    {
        var parts = new Dictionary<string, string>();
        foreach (var name in Parts(spec))
        {
            var text = Text(name, cell, t);
            if (string.IsNullOrWhiteSpace(text)) return (null, $"window '{name}' is empty");
            parts[name] = text;
        }
        var total = parts.Values.Sum(v => v.Length);
        return total > maxChars ? (null, $"window too large: {total:N0} characters (max {maxChars:N0})") : (parts, null);
    }

    /// <summary>The parts fenced and labelled, so a judge can quote from them and a reader can see what it saw.</summary>
    public static string Compile(Dictionary<string, string> parts)
    {
        var sb = new StringBuilder();
        foreach (var (name, text) in parts)
            sb.Append($"### {name}\n{Known[name]}\n\n<{name}>\n{text}\n</{name}>\n\n");
        return sb.ToString().TrimEnd() + "\n";
    }

    static string? Text(string name, CellContext cell, Transcript t) => name switch
    {
        "answer" => t.Answer,
        "answer_json" => t.AnswerJson?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
        "prompt" => cell.Case.Prompt,
        "tool_calls" => string.Join("\n", t.ToolCalls.Select((c, i) =>
            $"{i + 1}. {c.Name} {Cut(c.Arguments?.ToJsonString() ?? "{}", ArgumentChars)}{(c.Denied ? " [denied]" : "")}")),
        "tool_results" => string.Join("\n\n", t.ToolResults.Select((r, i) =>
            $"{i + 1}. {FirstLines(r.Output, ResultLines)}")),
        "diff" => t.Diff,
        "user_turns" => string.Join("\n\n", t.UserTurns.Skip(1).Select((u, i) => $"{i + 1}. {u.Text}")),
        _ => null,
    };

    static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    static string FirstLines(string s, int n)
    {
        var lines = s.Split('\n');
        return lines.Length <= n ? s : string.Join("\n", lines.Take(n)) + $"\n… ({lines.Length - n} more lines)";
    }
}
