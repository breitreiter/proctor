using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

/// <summary>A check's outcome. <c>error</c> means the check could not run; it is never folded into fail.</summary>
record Verdict(string Result, string Reason)
{
    public const string Pass = "pass", Fail = "fail", NeedsJudge = "needs-judge", Error = "error";
    public static Verdict Of(bool ok, string reason) => new(ok ? Pass : Fail, reason);
    public static Verdict Err(string reason) => new(Error, reason);
}

/// <summary>Everything the runner and the grader need to know about one cell, without the transcript.</summary>
record CellContext(string EvalDir, string CellDir, string WorkDir, string Experiment, string Arm, CaseDef Case, int Sample)
{
    public IDictionary<string, string> Environment() => new Dictionary<string, string>
    {
        ["PROCTOR_EVAL_DIR"] = EvalDir,
        ["PROCTOR_EXPERIMENT"] = Experiment,
        ["PROCTOR_ARM"] = Arm,
        ["PROCTOR_CASE"] = Case.Id ?? "",
        ["PROCTOR_SAMPLE"] = Sample.ToString(),
        ["PROCTOR_CELL"] = CellDir,
        ["PROCTOR_WORK"] = WorkDir,
        ["PROCTOR_CASE_JSON"] = JsonSerializer.Serialize(Case, Eval.JsonOptions),
        ["PROCTOR_EXPECT"] = Case.Expect?.ToJsonString() ?? "{}",
        ["PROCTOR_TRANSCRIPT"] = Path.Combine(CellDir, Layout.TranscriptFile),
        ["PROCTOR_DIFF"] = Path.Combine(CellDir, Layout.DiffFile),
    };
}

/// <summary>The built-in vocabulary from project/plans/on-disk-layout.md, plus the script contract.</summary>
static class Checks
{
    public const string FromExpect = "@expect";

    static readonly string[] Known =
    [
        "exit_reason", "answer_contains", "answer_regex", "answer_equals", "answer_words",
        "tools_used", "tools_used_any", "tool_args", "tool_sequence", "denied_calls", "tool_errors",
        "loop_nudged", "files_touched", "max_tool_calls", "max_tokens", "max_duration_ms", "script",
    ];

    static readonly Dictionary<string, string> NotYet = new()
    {
        ["answer_json_schema"] = "needs a JSON-schema validator; not in this version",
        ["oracle_hit"] = "oracle checks wait for an eval that uses an oracle",
        ["oracle_misses"] = "oracle checks wait for an eval that uses an oracle",
        ["oracle_turns"] = "oracle checks wait for an eval that uses an oracle",
        ["max_cost"] = "cost is omitted until nb's trailer carries it",
    };

    /// <summary>Shape-check one field of a check spec at load time. Null when fine.</summary>
    public static string? ValidateSpec(string key, JsonNode? value, string evalDir)
    {
        var (name, negated) = Split(key);
        if (NotYet.TryGetValue(name, out var why)) return $"not yet: {why}";
        if (!Known.Contains(name)) return $"unknown check; known: {string.Join(", ", Known)}";
        if (negated && name == "script") return "a script cannot be negated; make the script return the verdict you mean";
        if (value is JsonValue v && v.TryGetValue<string>(out var s) && s == FromExpect)
            return name == "script" ? "a script path cannot come from the case" : null;
        return name switch
        {
            "script" when value is not JsonValue => "a script is a path string",
            "script" when !File.Exists(Path.Combine(evalDir, value!.GetValue<string>())) => $"script not found: {value}",
            "exit_reason" or "answer_contains" or "answer_regex" or "answer_equals" when value is not JsonValue => "expects a string",
            "answer_regex" when !IsRegex(value!.GetValue<string>()) => "not a valid regular expression",
            "tools_used" or "tools_used_any" when value is not JsonArray => "expects a list of tool names",
            "loop_nudged" when value is not JsonValue => "expects true or false",
            "max_tool_calls" or "max_tokens" or "max_duration_ms" when value is not JsonValue => "expects a number",
            "denied_calls" or "tool_errors" when value?["max"] is null => "expects {max}",
            "answer_words" when value is not JsonObject || (value["min"] is null && value["max"] is null) => "expects {min} and/or {max}",
            "tool_args" when value?["name"] is null => "expects {name, args, mode: partial|exact, ignore: [globs]}",
            "tool_args" when value["mode"] is JsonValue m && m.GetValue<string>() is not ("partial" or "exact") => "mode is partial or exact",
            "tool_sequence" when value?["names"] is not JsonArray => "expects {names, mode: in_order|exact}",
            "tool_sequence" when value["mode"] is JsonValue m && m.GetValue<string>() is not ("in_order" or "exact") => "mode is in_order or exact",
            "files_touched" when value?["paths"] is not JsonArray => "expects {paths, mode: at_least|exactly|at_most}",
            "files_touched" when value["mode"] is not JsonValue m || m.GetValue<string>() is not ("at_least" or "exactly" or "at_most") => "mode is required: at_least, exactly or at_most",
            _ => null,
        };

        static bool IsRegex(string p) { try { _ = new Regex(p); return true; } catch (ArgumentException) { return false; } }
    }

    /// <summary>Evaluate a declared check over one cell. A spec with several fields is their conjunction.</summary>
    public static Verdict Evaluate(JsonObject spec, CellContext cell, Transcript t)
    {
        var verdicts = spec.Select(field => EvaluateField(field.Key, field.Value, cell, t)).ToList();
        if (verdicts.Count == 1) return verdicts[0];
        var worst = verdicts.MaxBy(v => v.Result switch { Verdict.Error => 3, Verdict.NeedsJudge => 2, Verdict.Fail => 1, _ => 0 })!;
        return worst with { Reason = string.Join("; ", verdicts.Select(v => v.Reason)) };
    }

    private static Verdict EvaluateField(string key, JsonNode? value, CellContext cell, Transcript t)
    {
        var (name, negated) = Split(key);
        if (value is JsonValue jv && jv.TryGetValue<string>(out var s) && s == FromExpect)
        {
            value = cell.Case.Expect?[name];
            if (value is null) return Verdict.Err($"{name}: case has no expect.{name}");
        }
        var verdict = name == "script" ? RunScript(value!.GetValue<string>(), cell) : BuiltIn(name, value!, t);
        if (!negated || verdict.Result is Verdict.Error or Verdict.NeedsJudge) return verdict;
        return new Verdict(verdict.Result == Verdict.Pass ? Verdict.Fail : Verdict.Pass, "not: " + verdict.Reason);
    }

    private static Verdict BuiltIn(string name, JsonNode v, Transcript t)
    {
        var trailer = t.Trailer;
        switch (name)
        {
            case "exit_reason":
                if (trailer is null) return Verdict.Err("no result trailer");
                return Verdict.Of(trailer.ExitReason == v.GetValue<string>(), $"exit_reason={trailer.ExitReason}");
            case "max_tool_calls":
                if (trailer is null) return Verdict.Err("no result trailer");
                return Verdict.Of(trailer.ToolCalls <= v.GetValue<long>(), $"{trailer.ToolCalls} tool calls (max {v})");
            case "max_tokens":
                if (trailer?.Total is null) return Verdict.Err("no token usage on the trailer");
                return Verdict.Of(trailer.Total <= v.GetValue<long>(), $"{trailer.Total} tokens{(trailer.Estimated ? " (estimated)" : "")} (max {v})");
            case "max_duration_ms":
                if (trailer?.DurationMs is null) return Verdict.Err("no duration on the trailer");
                return Verdict.Of(trailer.DurationMs <= v.GetValue<long>(), $"{trailer.DurationMs} ms (max {v})");
            case "denied_calls":
            {
                var denied = t.ToolCalls.Where(c => c.Denied).ToList();
                var max = v["max"]!.GetValue<int>();
                var detail = denied.Count == 0 ? "0 denied" : $"{denied.Count} denied call{(denied.Count == 1 ? "" : "s")}: {string.Join(", ", denied.Select(c => $"{c.Name} ({c.Rung})"))}";
                return Verdict.Of(denied.Count <= max, detail);
            }
            case "tool_errors":
            {
                var errors = t.ToolResults.Count(r => r.IsError);
                return Verdict.Of(errors <= v["max"]!.GetValue<int>(), $"{errors} tool error{(errors == 1 ? "" : "s")} (max {v["max"]})");
            }
            case "loop_nudged":
                return Verdict.Of(t.LoopNudged == v.GetValue<bool>(), t.LoopNudged ? "loop nudge fired" : "no loop nudge");
            case "answer_contains":
                if (t.Answer is null) return Verdict.Err("no assistant answer");
                return Verdict.Of(t.Answer.Contains(v.GetValue<string>(), StringComparison.Ordinal), $"answer {(t.Answer.Contains(v.GetValue<string>()) ? "contains" : "lacks")} '{v}'");
            case "answer_equals":
                if (t.Answer is null) return Verdict.Err("no assistant answer");
                return Verdict.Of(t.Answer.Trim() == v.GetValue<string>().Trim(), $"answer {(t.Answer.Trim() == v.GetValue<string>().Trim() ? "equals" : "differs from")} '{v}'");
            case "answer_regex":
                if (t.Answer is null) return Verdict.Err("no assistant answer");
                var hit = Regex.IsMatch(t.Answer, v.GetValue<string>());
                return Verdict.Of(hit, $"answer {(hit ? "matches" : "does not match")} /{v}/");
            case "answer_words":
            {
                if (t.Answer is null) return Verdict.Err("no assistant answer");
                var words = t.Answer.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
                var min = (int?)v["min"]?.GetValue<int>();
                var max = (int?)v["max"]?.GetValue<int>();
                return Verdict.Of((min is null || words >= min) && (max is null || words <= max), $"{words} words (min {min?.ToString() ?? "-"}, max {max?.ToString() ?? "-"})");
            }
            case "tools_used":
            {
                var wanted = v.AsArray().Select(n => n!.GetValue<string>()).ToList();
                var used = t.ToolCalls.Select(c => c.Name).ToHashSet();
                var missing = wanted.Where(w => !used.Contains(w)).ToList();
                return Verdict.Of(missing.Count == 0, missing.Count == 0 ? $"used {string.Join(", ", wanted)}" : $"never used {string.Join(", ", missing)}");
            }
            case "tools_used_any":
            {
                var wanted = v.AsArray().Select(n => n!.GetValue<string>()).ToList();
                var used = t.ToolCalls.Select(c => c.Name).Where(wanted.Contains).Distinct().ToList();
                return Verdict.Of(used.Count > 0, used.Count > 0 ? $"used {string.Join(", ", used)}" : $"used none of {string.Join(", ", wanted)}");
            }
            case "tool_args":
            {
                var toolName = v["name"]!.GetValue<string>();
                var expected = v["args"] as JsonObject ?? new JsonObject();
                var exact = v["mode"]?.GetValue<string>() == "exact";
                var ignore = (v["ignore"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList() ?? [];
                var calls = t.ToolCalls.Where(c => c.Name == toolName).ToList();
                if (calls.Count == 0) return new Verdict(Verdict.Fail, $"no call to {toolName}");
                var matched = calls.Any(c => ArgsMatch(expected, c.Arguments ?? new JsonObject(), exact, ignore));
                return Verdict.Of(matched, matched ? $"{toolName} called with the expected args" : $"{calls.Count} call{(calls.Count == 1 ? "" : "s")} to {toolName}, none with the expected args");
            }
            case "tool_sequence":
            {
                var names = v["names"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
                var actual = t.ToolCalls.Select(c => c.Name).ToList();
                var ok = v["mode"]?.GetValue<string>() == "exact" ? actual.SequenceEqual(names) : IsSubsequence(names, actual);
                return Verdict.Of(ok, $"calls: {(actual.Count == 0 ? "(none)" : string.Join(" > ", actual))}");
            }
            case "files_touched":
            {
                if (t.Diff is null) return Verdict.Err("no diff.patch in the cell");
                var patterns = v["paths"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
                var touched = t.TouchedPaths();
                var unmatched = patterns.Where(p => !touched.Any(f => Glob.IsMatch(p, f))).ToList();
                var outside = touched.Where(f => !patterns.Any(p => Glob.IsMatch(p, f))).ToList();
                var (ok, reason) = v["mode"]!.GetValue<string>() switch
                {
                    "at_least" => (unmatched.Count == 0, unmatched.Count == 0 ? $"touched all {patterns.Count} expected" : $"untouched: {string.Join(", ", unmatched)}"),
                    "at_most" => (outside.Count == 0, outside.Count == 0 ? $"touched {touched.Count}, all expected" : $"touched {outside.Count} outside expected: {string.Join(", ", outside)}"),
                    _ => (unmatched.Count == 0 && outside.Count == 0,
                          unmatched.Count == 0 && outside.Count == 0 ? $"touched exactly {patterns.Count}" :
                          string.Join("; ", new[] { unmatched.Count > 0 ? $"untouched: {string.Join(", ", unmatched)}" : null, outside.Count > 0 ? $"outside: {string.Join(", ", outside)}" : null }.Where(s => s is not null))),
                };
                return Verdict.Of(ok, reason);
            }
            default:
                return Verdict.Err($"unknown check {name}");
        }
    }

    private static bool ArgsMatch(JsonObject expected, JsonObject actual, bool exact, List<string> ignore)
    {
        bool Ignored(string k) => ignore.Any(g => Glob.IsMatch(g, k));
        foreach (var (k, ev) in expected)
        {
            if (Ignored(k)) continue;
            if (!actual.TryGetPropertyValue(k, out var av) || !JsonNode.DeepEquals(ev, av)) return false;
        }
        return !exact || actual.All(a => Ignored(a.Key) || expected.ContainsKey(a.Key));
    }

    private static bool IsSubsequence(List<string> wanted, List<string> actual)
    {
        var i = 0;
        foreach (var a in actual) if (i < wanted.Count && a == wanted[i]) i++;
        return i == wanted.Count;
    }

    /// <summary>The script contract: runs in the cell, exit 0/1/2 = pass/fail/needs-judge, first stdout line is the reason.</summary>
    private static Verdict RunScript(string script, CellContext cell)
    {
        var path = Path.GetFullPath(Path.Combine(cell.EvalDir, script));
        var psi = new ProcessStartInfo(path)
        {
            WorkingDirectory = cell.CellDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var (k, val) in cell.Environment()) psi.Environment[k] = val;
        Process p;
        try { p = Process.Start(psi) ?? throw new InvalidOperationException("no process"); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return Verdict.Err($"could not run {script}: {e.Message}");
        }
        using (p)
        {
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            var firstLine = stdout.Result.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return p.ExitCode switch
            {
                0 => new Verdict(Verdict.Pass, firstLine ?? "pass"),
                1 => new Verdict(Verdict.Fail, firstLine ?? "fail"),
                2 => new Verdict(Verdict.NeedsJudge, firstLine ?? "needs judge"),
                var code => Verdict.Err(firstLine ?? $"{script} exited {code}: {stderr.Result.Split('\n').FirstOrDefault(l => l.Length > 0)}"),
            };
        }
    }

    private static (string Name, bool Negated) Split(string key) =>
        key.StartsWith("not_", StringComparison.Ordinal) ? (key[4..], true) : (key, false);
}

/// <summary>Path globs: `*` within a segment, `**` across segments. Used by files_touched and tool_args.ignore.</summary>
static class Glob
{
    public static bool IsMatch(string pattern, string path)
    {
        if (!pattern.Contains('*')) return pattern == path;
        var re = "^" + Regex.Escape(pattern).Replace(@"\*\*/", "(.*/)?").Replace(@"\*\*", ".*").Replace(@"\*", "[^/]*") + "$";
        return Regex.IsMatch(path, re);
    }
}
