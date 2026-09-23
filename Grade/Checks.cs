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
record CellContext(string SuiteDir, string CellDir, string WorkDir, string Experiment, string Arm, TaskDef Task, int Sample)
{
    /// <summary>What the task expects, its fixture's defaults included; the task's own block when there is no fixture.</summary>
    public JsonObject? Expect { get; init; } = Task.Expect;
    public Fixture? Fixture { get; init; }
    /// <summary>The arm's resolved bundle directory, when the arm has one.</summary>
    public string? BundleDir { get; init; }
    /// <summary>The runner script in effect, relative to suites/; empty on a bare run.</summary>
    public string NbRunner { get; init; } = "";
    /// <summary>The host nb binary and its config, resolved: what a bare run starts, and what a hook mounts for a runner.</summary>
    public string NbPath { get; init; } = "";
    public string? NbConfig { get; init; }
    /// <summary>Where the runner shows nb the checkout and the bundle; null when it shows them at the host paths.</summary>
    public NbMounts? Mounts { get; init; }
    /// <summary>The judges a decide or judge check calls; set by grade, absent while running.</summary>
    public JudgeClient? Judges { get; init; }

    /// <summary>The path the model is told the checkout is at: the mount when a runner has one, else the host path.</summary>
    public string WorkMount => Mounts?.Work ?? WorkDir;
    /// <summary>The path the model is told the bundle is at, when the arm has one.</summary>
    public string? BundleMount => BundleDir is null ? null : Mounts?.Bundle ?? BundleDir;

    /// <summary>A container name for this cell, derived from its coordinates. Proctor never uses it; hooks and runners that own a container agree on it through this.</summary>
    public string Container => $"proctor-{Experiment}-{Arm}-{Task.Id}-{Sample}";

    public IDictionary<string, string> Environment() => new Dictionary<string, string>
    {
        ["PROCTOR_SUITE_DIR"] = SuiteDir,
        ["PROCTOR_FIXTURE"] = Fixture?.Dir ?? "",
        ["PROCTOR_BUNDLE"] = BundleDir ?? "",
        ["PROCTOR_EXPERIMENT"] = Experiment,
        ["PROCTOR_ARM"] = Arm,
        ["PROCTOR_TASK"] = Task.Id ?? "",
        ["PROCTOR_SAMPLE"] = Sample.ToString(),
        ["PROCTOR_CELL"] = CellDir,
        ["PROCTOR_WORK"] = WorkDir,
        ["PROCTOR_WORK_MOUNT"] = WorkMount,
        ["PROCTOR_BUNDLE_MOUNT"] = BundleMount ?? "",
        ["PROCTOR_TASK_JSON"] = JsonSerializer.Serialize(Task, Suite.JsonOptions),
        ["PROCTOR_EXPECT"] = Expect?.ToJsonString() ?? "{}",
        ["PROCTOR_TRANSCRIPT"] = Path.Combine(CellDir, Layout.TranscriptFile),
        ["PROCTOR_DIFF"] = Path.Combine(CellDir, Layout.DiffFile),
        ["PROCTOR_RUNNER"] = NbRunner,
        ["PROCTOR_NB"] = NbPath,
        ["PROCTOR_NB_CONFIG"] = NbConfig ?? "",
        ["PROCTOR_CONTAINER"] = Container,
        // The spellings before suite/task, for hooks and scripts written against them; gone after one release.
        ["PROCTOR_EVAL_DIR"] = SuiteDir,
        ["PROCTOR_CASE"] = Task.Id ?? "",
        ["PROCTOR_CASE_JSON"] = JsonSerializer.Serialize(Task, Suite.JsonOptions),
    };
}

/// <summary>The built-in vocabulary from project/plans/on-disk-layout.md, plus the script contract.</summary>
static class Checks
{
    public const string FromExpect = "@expect";
    /// <summary>The one key in a check spec that is not a check: what the check tests, for the report's reader.</summary>
    public const string Description = "description";

    static readonly string[] Known =
    [
        "exit_reason", "answer_contains", "answer_regex", "answer_equals", "answer_words",
        "tools_used", "tools_used_any", "tool_args", "tool_sequence", "denied_calls", "tool_errors",
        "loop_nudged", "files_touched", "max_tool_calls", "max_tokens", "max_duration_ms", "script",
        Judge.Decide, Judge.JudgeCheck,
    ];

    static readonly Dictionary<string, string> NotYet = new()
    {
        ["answer_json_schema"] = "needs a JSON-schema validator; not in this version",
        ["oracle_hit"] = "oracle checks wait for a suite that uses an oracle",
        ["oracle_misses"] = "oracle checks wait for a suite that uses an oracle",
        ["oracle_turns"] = "oracle checks wait for a suite that uses an oracle",
        ["max_cost"] = "cost is omitted until nb's trailer carries it",
    };

    /// <summary>A check that calls a model: decide or judge, negated or not.</summary>
    public static bool AsksAModel(JsonObject spec) => spec.Any(f => Split(f.Key).Name is Judge.Decide or Judge.JudgeCheck);

    /// <summary>The model checks of a spec, for validating their judges against proctor.json.</summary>
    public static IEnumerable<(string Name, JsonObject Spec)> ModelChecks(JsonObject spec) =>
        spec.Where(f => Split(f.Key).Name is Judge.Decide or Judge.JudgeCheck && f.Value is JsonObject).Select(f => (Split(f.Key).Name, (JsonObject)f.Value!));

    /// <summary>
    /// Validate a whole check at load time: every field's shape, and that a script check says what it tests. A built-in
    /// check describes itself (<see cref="Describe"/>); a script is a black box from outside, so its author must.
    /// Yields (field, problem); an empty field is the check itself.
    /// </summary>
    public static IEnumerable<(string Field, string Problem)> ValidateCheck(JsonObject? spec, string suiteDir)
    {
        var fields = spec?.Where(f => f.Key != Description).ToList() ?? [];
        if (fields.Count == 0) { yield return ("", "a check is an object with at least one check field"); yield break; }
        var description = spec![Description];
        if (description is not null && (description is not JsonValue dv || !dv.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)))
            yield return (Description, "a description is a sentence, not an empty string");
        if (description is null && spec.ContainsKey("script"))
            yield return (Description, "required for a script check: say in a sentence what the script tests, for the report's reader");
        foreach (var (key, value) in fields)
            if (ValidateSpec(key, value, suiteDir) is { } problem) yield return (key, problem);
    }

    /// <summary>What a check tests, for a reader: its description, else a sentence derived from its built-in fields.</summary>
    public static string Describe(JsonObject spec)
    {
        if (spec[Description] is JsonValue d && d.TryGetValue<string>(out var text)) return text;
        return string.Join(" and ", spec.Where(f => f.Key != Description).Select(f => DescribeField(f.Key, f.Value)));
    }

    private static string DescribeField(string key, JsonNode? v)
    {
        var (name, negated) = Split(key);
        var fromCase = v is JsonValue jv && jv.TryGetValue<string>(out var s) && s == FromExpect;
        string List(JsonNode? n) => string.Join(", ", n!.AsArray().Select(x => x!.GetValue<string>()));
        string Max(string what) => v!["max"]!.GetValue<long>() == 0 ? $"no {what}" : $"at most {v["max"]} {what}";
        var phrase = name switch
        {
            _ when fromCase && name == "files_touched" => "the files changed are the ones the task expects",
            _ when fromCase => $"{name.Replace('_', ' ')} is what the task expects",
            "exit_reason" => $"nb exits with '{v}'",
            "max_tool_calls" => $"at most {v} tool calls",
            "max_tokens" => $"at most {v!.GetValue<long>():N0} tokens",
            "max_duration_ms" => $"finishes within {DescribeMs(v!.GetValue<long>())}",
            "denied_calls" => Max("denied tool calls"),
            "tool_errors" => Max("tool errors"),
            "loop_nudged" => v!.GetValue<bool>() ? "the loop nudge fires" : "the loop nudge does not fire",
            "answer_contains" => $"the answer contains '{v}'",
            "answer_equals" => $"the answer is exactly '{v}'",
            "answer_regex" => $"the answer matches /{v}/",
            "answer_words" => (v!["min"], v["max"]) switch
            {
                ({ } min, { } max) => $"the answer is {min} to {max} words",
                ({ } min, null) => $"the answer is at least {min} words",
                (_, var max) => $"the answer is at most {max} words",
            },
            "tools_used" => $"uses {List(v)}",
            "tools_used_any" => $"uses at least one of {List(v)}",
            "tool_args" => $"calls {v!["name"]} with the expected arguments",
            "tool_sequence" => $"calls {List(v!["names"])} {(v["mode"]?.GetValue<string>() == "exact" ? "and nothing else" : "in that order")}",
            "files_touched" => v!["mode"]!.GetValue<string>() switch
            {
                "at_least" => $"changes touch all of {List(v["paths"])}",
                "at_most" => $"changes stay within {List(v["paths"])}",
                _ => $"changes touch exactly {List(v["paths"])}",
            },
            "script" => $"passes {v}",
            Judge.Decide or Judge.JudgeCheck => Judge.Describe(name, v),
            _ => name,
        };
        return negated ? $"not: {phrase}" : phrase;
    }

    private static string DescribeMs(long ms) => ms switch
    {
        >= 3_600_000 when ms % 3_600_000 == 0 => $"{ms / 3_600_000} h",
        >= 60_000 when ms % 60_000 == 0 => $"{ms / 60_000} min",
        >= 1_000 when ms % 1_000 == 0 => $"{ms / 1_000} s",
        _ => $"{ms} ms",
    };

    /// <summary>Shape-check one field of a check spec at load time. Null when fine.</summary>
    public static string? ValidateSpec(string key, JsonNode? value, string suiteDir)
    {
        var (name, negated) = Split(key);
        if (NotYet.TryGetValue(name, out var why)) return $"not yet: {why}";
        if (!Known.Contains(name)) return $"unknown check; known: {string.Join(", ", Known)}";
        if (negated && name == "script") return "a script cannot be negated; make the script return the verdict you mean";
        if (name is Judge.Decide or Judge.JudgeCheck) return negated ? "a model check cannot be negated; say what you expect with expect" : Judge.ValidateSpec(name, value);
        if (value is JsonValue v && v.TryGetValue<string>(out var s) && s == FromExpect)
            return name == "script" ? "a script path cannot come from the task" : null;
        return name switch
        {
            "script" when value is not JsonValue => "a script is a path string",
            "script" when !File.Exists(Path.Combine(suiteDir, value!.GetValue<string>())) => $"script not found: {value}",
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

    /// <summary>Evaluate a declared check over one cell. A spec with several fields is their conjunction. The name is what a model check's verdict file and @expect are keyed by.</summary>
    public static Verdict Evaluate(CheckDef check, CellContext cell, Transcript t, string name = "check")
    {
        var verdicts = check.Spec.Where(f => f.Key != Description).Select(field => EvaluateField(field.Key, field.Value, check.Dir, name, cell, t)).ToList();
        if (verdicts.Count == 1) return verdicts[0];
        var worst = verdicts.MaxBy(v => v.Result switch { Verdict.Error => 3, Verdict.NeedsJudge => 2, Verdict.Fail => 1, _ => 0 })!;
        return worst with { Reason = string.Join("; ", verdicts.Select(v => v.Reason)) };
    }

    /// <summary>A suite's check: its script path is relative to the suite directory.</summary>
    public static Verdict Evaluate(JsonObject spec, CellContext cell, Transcript t, string name = "check") => Evaluate(new CheckDef(spec, cell.SuiteDir, "suite"), cell, t, name);

    private static Verdict EvaluateField(string key, JsonNode? value, string scriptDir, string check, CellContext cell, Transcript t)
    {
        var (name, negated) = Split(key);
        if (name is Judge.Decide or Judge.JudgeCheck) return Judge.Evaluate(name, (JsonObject)value!, check, cell, t);
        if (value is JsonValue jv && jv.TryGetValue<string>(out var s) && s == FromExpect)
        {
            // Keyed by the check's name first, so two checks over one field can differ per task; by the field for the common single task.
            value = (cell.Expect?[check] as JsonObject)?[name] ?? cell.Expect?[name];
            if (value is null) return Verdict.Err($"{name}: task has no expect.{check}.{name} or expect.{name}");
        }
        var verdict = name == "script" ? RunScript(value!.GetValue<string>(), scriptDir, cell) : BuiltIn(name, value!, cell, t);
        if (!negated || verdict.Result is Verdict.Error or Verdict.NeedsJudge) return verdict;
        return new Verdict(verdict.Result == Verdict.Pass ? Verdict.Fail : Verdict.Pass, "not: " + verdict.Reason);
    }

    private static Verdict BuiltIn(string name, JsonNode v, CellContext cell, Transcript t)
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
            {
                // nb's trailer carries no duration (2026-09); the cell's wall time from the manifest is the fallback, as in the report.
                var (ms, source) = trailer?.DurationMs is { } d ? (d, "trailer") : (WallTime(cell), "wall time");
                if (ms is null) return Verdict.Err("no duration on the trailer or in the manifest");
                return Verdict.Of(ms <= v.GetValue<long>(), $"{ms} ms {source} (max {v})");
            }
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

    private static long? WallTime(CellContext cell)
    {
        var file = Path.Combine(cell.CellDir, Layout.ManifestFile);
        if (!File.Exists(file)) return null;
        return JsonNode.Parse(File.ReadAllText(file))?["duration_ms"]?.GetValue<long>();
    }

    /// <summary>The script contract: runs in the cell, exit 0/1/2 = pass/fail/needs-judge, first stdout line is the reason.</summary>
    private static Verdict RunScript(string script, string scriptDir, CellContext cell)
    {
        var path = Path.GetFullPath(Path.Combine(scriptDir, script));
        var result = Subprocess.Run(path, [], cell.CellDir, cell.Environment());
        if (!result.Started) return Verdict.Err($"could not run {script}: {result.Stderr}");
        var firstLine = result.Stdout.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return result.ExitCode switch
        {
            0 => new Verdict(Verdict.Pass, firstLine ?? "pass"),
            1 => new Verdict(Verdict.Fail, firstLine ?? "fail"),
            2 => new Verdict(Verdict.NeedsJudge, firstLine ?? "needs judge"),
            var code => Verdict.Err(firstLine ?? $"{script} exited {code}: {result.FirstStderrLine}"),
        };
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
