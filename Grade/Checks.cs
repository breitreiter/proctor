using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

/// <summary>
/// A check's outcome. <c>error</c> means the check could not run; it is never folded into fail. <c>outdated</c> means the
/// verdict on file was not produced by the check as it is now (a script check on an agent attempt, changed or added since
/// the attempt was made), so it decides nothing until the attempt is made again.
/// </summary>
record Verdict(string Result, string Reason)
{
    /// <summary>What the check does, as it was when it graded this attempt (<see cref="Checks.Describe"/>), so a viewer copies none of proctor's logic and an exam edited later doesn't re-explain an old verdict.</summary>
    public string? Describes { get; init; }
    /// <summary>Why the check exists, as it was when it graded: its author's sentence, or proctor's default for a guardrail (<see cref="Checks.WhyOf"/>).</summary>
    public string? Why { get; init; }
    /// <summary>A script check's script, relative to proctor's root, so a person can open what decided.</summary>
    public string? Script { get; init; }
    /// <summary>The check's role when it graded (<see cref="Exam.RoleOf"/>): correctness, integrity, quality or observation. What the answer was used for.</summary>
    public string? Role { get; init; }
    /// <summary>Where in the attempt the check found what it looks for (check-evidence.md, C3): steps of the trajectory, or files of the diff. Absent when it found nothing to point at.</summary>
    public List<Evidence>? Evidence { get; init; }

    // A verdict is its result and reason; what it carries to explain the check doesn't make it another verdict.
    public virtual bool Equals(Verdict? other) => other is not null && Result == other.Result && Reason == other.Reason;
    public override int GetHashCode() => HashCode.Combine(Result, Reason);

    public const string Pass = "pass", Fail = "fail", NeedsJudge = "needs-judge", Error = "error", Outdated = "outdated";
    /// <summary>The check does not apply in this try's configuration (its `when`). Neither pass, fail nor undecided: it counts toward nothing.</summary>
    public const string NotApplicable = "not-applicable";
    public static Verdict Of(bool ok, string reason) => new(ok ? Pass : Fail, reason);
    public static Verdict Err(string reason) => new(Error, reason);
}

/// <summary>One thing a verdict points at: an ATIF step by its id, or a path in the attempt's diff.</summary>
record Evidence(string? Step = null, string? File = null);

/// <summary>Everything the runner and the grader need to know about one attempt, without the transcript.</summary>
record AttemptContext(string ExamDir, string AttemptDir, string WorkDir, string ExamId, string Configuration, TaskDef Task, int Attempt)
{
    /// <summary>What the task expects, its fixture's defaults included; the task's own block when there is no fixture.</summary>
    public JsonObject? Expect { get; init; } = Task.Expect;
    public Fixture? Fixture { get; init; }
    /// <summary>The runner script in effect, relative to exams/; empty on a bare run.</summary>
    public string NbRunner { get; init; } = "";
    /// <summary>The host nb binary and its config, resolved: what a bare run starts, and what a hook mounts for a runner.</summary>
    public string NbPath { get; init; } = "";
    public string? NbConfig { get; init; }
    /// <summary>Where the runner shows nb the checkout; null when it shows it at the host path.</summary>
    public NbMounts? Mounts { get; init; }
    /// <summary>The judges a decide or judge check calls; set by grade, absent while running.</summary>
    public JudgeClient? Judges { get; init; }
    /// <summary>An agent attempt: its script checks already ran in Harbor's verifier, and grade reads their verdicts from the attempt.</summary>
    public bool ScriptsInVerifier { get; init; }
    /// <summary>The Harbor task an agent attempt ran as: its tests/ hold the script checks as the verifier ran them, which grade compares with the checks as they are now.</summary>
    public string? VerifierTask { get; init; }
    /// <summary>A vendor agent's attempt, not nb's: nb's own behaviour (the loop nudge) has nothing to read there.</summary>
    public bool Vendor { get; init; }
    /// <summary>On an exam with axes: the value on each axis, every parameter of the base as this configuration has it, and the files its values lay over the checkout.</summary>
    public IReadOnlyDictionary<string, string> Axis { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Params { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<(string Dir, string? To)> Overlays { get; init; } = [];

    /// <summary>The path the model is told the checkout is at: the mount when a runner has one, else the host path.</summary>
    public string WorkMount => Mounts?.Work ?? WorkDir;

    /// <summary>The id the try is kept under in the bank.</summary>
    public string? Try { get; init; }

    /// <summary>
    /// A container name for the try, the same for every stage of it, since the hooks that would make a container run once
    /// around the try. Proctor never uses it; hooks and runners that own a container agree on it through this. A try's
    /// id is letters, digits and hyphens, which every engine takes as a name.
    /// </summary>
    public string Container => $"proctor-{ExamId}-{Try ?? Attempt.ToString()}";

    public IDictionary<string, string> Environment()
    {
        var env = Coordinates();
        // Declared axes and parameters only: what a hook or a check reads here is in the configuration's identity.
        foreach (var (axis, value) in Axis) env["PROCTOR_AXIS_" + axis.ToUpperInvariant().Replace('-', '_')] = value;
        foreach (var (name, value) in Params) env["PROCTOR_PARAM_" + name] = value;
        return env;
    }

    Dictionary<string, string> Coordinates() => new()
    {
        ["PROCTOR_EXAM_DIR"] = ExamDir,
        ["PROCTOR_FIXTURE"] = Fixture?.Dir ?? "",
        ["PROCTOR_EXAM"] = ExamId,
        ["PROCTOR_CONFIGURATION"] = Configuration,
        ["PROCTOR_TASK"] = Task.Id ?? "",
        ["PROCTOR_ATTEMPT"] = Attempt.ToString(),
        ["PROCTOR_ATTEMPT_DIR"] = AttemptDir,
        ["PROCTOR_WORK"] = WorkDir,
        ["PROCTOR_WORK_MOUNT"] = WorkMount,
        ["PROCTOR_TASK_JSON"] = JsonSerializer.Serialize(Task, Exam.JsonOptions),
        ["PROCTOR_EXPECT"] = Expect?.ToJsonString() ?? "{}",
        ["PROCTOR_TRANSCRIPT"] = Path.Combine(AttemptDir, Layout.TranscriptFile),
        ["PROCTOR_DIFF"] = Path.Combine(AttemptDir, Layout.DiffFile),
        ["PROCTOR_RUNNER"] = NbRunner,
        ["PROCTOR_NB"] = NbPath,
        ["PROCTOR_NB_CONFIG"] = NbConfig ?? "",
        ["PROCTOR_CONTAINER"] = Container,
    };
}

/// <summary>The built-in vocabulary from project/plans/on-disk-layout.md, plus the script contract.</summary>
static class Checks
{
    public const string FromExpect = "@expect";
    /// <summary>A key in a check spec that is not a check: what the check tests, for the report's reader.</summary>
    public const string Description = "description";
    /// <summary>The other key that is not a check: one sentence on why the author thinks this is worth checking (check-evidence.md, C1).</summary>
    public const string Why = "why";

    /// <summary>A key that says something about the check rather than being one: <see cref="Description"/> or <see cref="Why"/>.</summary>
    /// <summary>The third: the configurations a check applies in, `axis = value` or `axis != value` joined by `and`, on an exam with axes.</summary>
    public const string When = "when";
    public static bool IsNote(string key) => key is Description or Why or When;

    static readonly string[] Known =
    [
        "exit_reason", "answer_contains", "answer_regex", "answer_equals", "answer_words",
        "tools_used", "tools_used_any", "tool_args", "tool_sequence", "denied_calls", "tool_errors", "result_matches",
        "loop_nudged", "oracle_hit", "oracle_misses", "oracle_turns", "files_touched", "max_tool_calls", "max_tokens", "max_duration_ms", "script",
        Judge.Decide, Judge.JudgeCheck,
    ];

    static readonly Dictionary<string, string> NotYet = new()
    {
        ["answer_json_schema"] = "needs a JSON-schema validator; not in this version",
        ["max_cost"] = "cost is omitted until nb's trailer carries it",
    };

    /// <summary>A field that reads the oracle's turns or ending, negated or not.</summary>
    public static bool IsOracleCheck(string key) => Split(key).Name is "oracle_hit" or "oracle_misses" or "oracle_turns";

    /// <summary>A check that calls a model: decide or judge, negated or not.</summary>
    /// <summary>Whether the check reads the workspace the agent left (the diff, or a script run in the checkout) rather than what it said and did.</summary>
    public static bool OnWorkspace(JsonObject spec) => spec.Any(f => Split(f.Key).Name is "files_touched" or "script");

    public static bool AsksAModel(JsonObject spec) => spec.Any(f => Split(f.Key).Name is Judge.Decide or Judge.JudgeCheck);

    /// <summary>The model checks of a spec, for validating their judges against proctor.json.</summary>
    public static IEnumerable<(string Name, JsonObject Spec)> ModelChecks(JsonObject? spec) =>
        (spec ?? []).Where(f => Split(f.Key).Name is Judge.Decide or Judge.JudgeCheck && f.Value is JsonObject).Select(f => (Split(f.Key).Name, (JsonObject)f.Value!));

    /// <summary>
    /// Validate a whole check at load time: every field's shape, and that a script check says what it tests. A built-in
    /// check describes itself (<see cref="Describe"/>); a script is a black box from outside, so its author must.
    /// Yields (field, problem); an empty field is the check itself.
    /// </summary>
    public static IEnumerable<(string Field, string Problem)> ValidateCheck(JsonObject? spec, string examDir)
    {
        var fields = spec?.Where(f => !IsNote(f.Key)).ToList() ?? [];
        if (fields.Count == 0) { yield return ("", "a check is an object with at least one check field"); yield break; }
        var description = spec![Description];
        if (description is not null && (description is not JsonValue dv || !dv.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text)))
            yield return (Description, "a description is a sentence, not an empty string");
        var why = spec[Why];
        if (why is not null && (why is not JsonValue wv || !wv.TryGetValue<string>(out var whyText) || string.IsNullOrWhiteSpace(whyText)))
            yield return (Why, "a why is a sentence, not an empty string");
        if (spec[When] is { } when && (when is not JsonValue cv || !cv.TryGetValue<string>(out var condition) || Proctor.When.Parse(condition, out _) is null))
            yield return (When, when is JsonValue pv && pv.TryGetValue<string>(out var given) && Proctor.When.Parse(given, out var problem) is null ? problem! : "a condition: `axis = value` or `axis != value`, joined by `and`");
        if (description is null && spec.ContainsKey("script"))
            yield return (Description, "required for a script check: say in a sentence what the script tests, for the report's reader");
        foreach (var (key, value) in fields)
            if (ValidateSpec(key, value, examDir) is { } problem) yield return (key, problem);
    }

    /// <summary>What a check tests, for a reader: its description, else a sentence derived from its built-in fields.</summary>
    public static string Describe(JsonObject spec)
    {
        if (spec[Description] is JsonValue d && d.TryGetValue<string>(out var text)) return text;
        return string.Join(" and ", spec.Where(f => !IsNote(f.Key)).Select(f => DescribeField(f.Key, f.Value)));
    }

    /// <summary>Why a check exists: its author's sentence, else proctor's default for a guardrail, else null (no stated reason).</summary>
    public static string? WhyOf(JsonObject spec) =>
        spec[Why] is JsonValue w && w.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : DefaultWhy(spec);

    /// <summary>
    /// The reason for a guardrail nearly every exam carries, written once here (check-evidence.md, C1). Recognised by what
    /// the check does, not its name: an exit reason of ok, no denied calls, no loop nudge, or a budget of calls, tokens or
    /// time alone. A budget's reason doesn't depend on its numbers, so an exam that sets its own still gets it. A check
    /// that adds anything else is the author's, and needs the author's reason.
    /// </summary>
    public static string? DefaultWhy(JsonObject spec)
    {
        var fields = spec.Where(f => !IsNote(f.Key)).ToList();
        if (fields.Count == 0) return null;
        static bool Is(JsonNode? v, string s) => v is JsonValue jv && jv.TryGetValue<string>(out var t) && t == s;
        static bool IsFalse(JsonNode? v) => v is JsonValue jv && jv.TryGetValue<bool>(out var b) && !b;
        if (fields is [("exit_reason", var exit)] && Is(exit, "ok"))
            return "An attempt that ended on a limit or an error, not by finishing, says nothing yet about the task.";
        if (fields is [("denied_calls", _)])
            return "A denied call means the agent tried something the setup forbids, which a user would see as a stuck session.";
        if (fields is [("loop_nudged", var nudged)] && IsFalse(nudged))
            return "A nudge means the agent stopped making progress and the harness had to push it on.";
        if (fields.All(f => f.Key is "max_tool_calls" or "max_tokens" or "max_duration_ms"))
            return "An attempt over its budget costs a user more time or money than this task should.";
        return null;
    }

    private static string DescribeField(string key, JsonNode? v)
    {
        var (name, negated) = Split(key);
        var fromTask = v is JsonValue jv && jv.TryGetValue<string>(out var s) && s == FromExpect;
        string List(JsonNode? n) => string.Join(", ", n!.AsArray().Select(x => x!.GetValue<string>()));
        string Max(string what) => v!["max"]!.GetValue<long>() == 0 ? $"no {what}" : $"at most {v["max"]} {what}";
        var phrase = name switch
        {
            _ when fromTask && name == "files_touched" => "the files changed are the ones the task expects",
            _ when fromTask => name switch
            {
                "answer_contains" => "the answer contains what the task expects",
                "answer_equals" => "the answer is exactly what the task expects",
                "answer_regex" => "the answer matches the pattern the task expects",
                "exit_reason" => "the attempt ends the way the task expects",
                _ => $"{name.Replace('_', ' ')} is what the task expects",
            },
            "exit_reason" => $"the attempt ends with '{v}'",
            "max_tool_calls" => $"at most {v} tool calls",
            "max_tokens" => $"at most {v!.GetValue<long>():N0} tokens",
            "max_duration_ms" => $"finishes within {DescribeMs(v!.GetValue<long>())}",
            "denied_calls" => Max("denied tool calls"),
            "tool_errors" => Max("tool errors"),
            "loop_nudged" => v!.GetValue<bool>() ? "the loop nudge fires" : "the loop nudge does not fire",
            "oracle_hit" => $"the oracle answers with {List(v)}",
            "oracle_misses" => v!["max"]!.GetValue<long>() == 0 ? "the agent asks nothing the answer sheet lacks" : $"at most {v["max"]} oracle misses",
            "oracle_turns" => Max("oracle answers"),
            "answer_contains" => $"the answer contains '{v}'",
            "answer_equals" => $"the answer is exactly '{v}'",
            "answer_regex" => $"the answer matches /{v}/",
            "result_matches" => $"a tool result matches /{v}/",
            "answer_words" => (v!["min"], v["max"]) switch
            {
                ({ } min, { } max) => $"the answer is {min} to {max} words",
                ({ } min, null) => $"the answer is at least {min} words",
                (_, var max) => $"the answer is at most {max} words",
            },
            "tools_used" => $"uses {List(v)}",
            "tools_used_any" => $"uses at least one of {List(v)}",
            "tool_args" => $"calls {v!["name"]} with {(v["match"]?.GetValue<string>() == "regex" ? "arguments matching the expected patterns" : "the expected arguments")}",
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
    public static string? ValidateSpec(string key, JsonNode? value, string examDir)
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
            "script" when !File.Exists(Path.Combine(examDir, value!.GetValue<string>())) => $"script not found: {value}",
            "exit_reason" or "answer_contains" or "answer_regex" or "answer_equals" or "result_matches" when value is not JsonValue => "expects a string",
            "answer_regex" or "result_matches" when !IsRegex(value!.GetValue<string>()) => "not a valid regular expression",
            "tools_used" or "tools_used_any" when value is not JsonArray => "expects a list of tool names",
            "loop_nudged" when value is not JsonValue => "expects true or false",
            "max_tool_calls" or "max_tokens" or "max_duration_ms" when value is not JsonValue => "expects a number",
            "denied_calls" or "tool_errors" or "oracle_misses" or "oracle_turns" when value is not JsonObject o || o["max"] is null => "expects {max}",
            "oracle_hit" when value is not JsonArray a || a.Count == 0 => "expects a non-empty list of answer sheet entry ids",
            "answer_words" when value is not JsonObject || (value["min"] is null && value["max"] is null) => "expects {min} and/or {max}",
            "tool_args" when value?["name"] is null => "expects {name, args, mode: partial|exact, match: equals|regex, ignore: [globs]}",
            "tool_args" when value["mode"] is JsonValue m && m.GetValue<string>() is not ("partial" or "exact") => "mode is partial or exact",
            "tool_args" when value["match"] is JsonValue m && m.GetValue<string>() is not ("equals" or "regex") => "match is equals or regex",
            "tool_args" when value["match"]?.GetValue<string>() == "regex" && value["args"] is JsonObject a
                && a.FirstOrDefault(kv => kv.Value is JsonValue r && r.TryGetValue<string>(out var p) && !IsRegex(p)) is { Key: not null } bad
                => $"args.{bad.Key} is not a valid regular expression",
            "tool_sequence" when value?["names"] is not JsonArray => "expects {names, mode: in_order|exact}",
            "tool_sequence" when value["mode"] is JsonValue m && m.GetValue<string>() is not ("in_order" or "exact") => "mode is in_order or exact",
            "files_touched" when value?["paths"] is not JsonArray => "expects {paths, mode: at_least|exactly|at_most}",
            "files_touched" when value["mode"] is not JsonValue m || m.GetValue<string>() is not ("at_least" or "exactly" or "at_most") => "mode is required: at_least, exactly or at_most",
            _ => null,
        };

        static bool IsRegex(string p) { try { _ = new Regex(p); return true; } catch (ArgumentException) { return false; } }
    }

    /// <summary>A check that reads only nb's own behaviour (the loop nudge): a vendor agent's attempt does not carry it, so its rate is over nb's attempts.
    /// Combined with anything else it stays, and the nudge part is an error on a vendor attempt.</summary>
    public static bool NbOnly(JsonObject spec) => spec.Where(f => !IsNote(f.Key)).Select(f => f.Key).SequenceEqual(["loop_nudged"]);

    /// <summary>Evaluate a declared check over one attempt. A spec with several fields is their conjunction. The name is what a model check's verdict file and @expect are keyed by.</summary>
    public static Verdict Evaluate(CheckDef check, AttemptContext attempt, Transcript t, string name = "check")
    {
        var verdicts = check.Spec.Where(f => !IsNote(f.Key)).Select(field => EvaluateField(field.Key, field.Value, check.Dir, name, attempt, t)).ToList();
        if (verdicts.Count == 1) return verdicts[0];
        var worst = verdicts.MaxBy(v => v.Result switch { Verdict.Error => 3, Verdict.NeedsJudge => 2, Verdict.Fail => 1, _ => 0 })!;
        // The parts that decided the verdict lead, so a skimmed reason opens with why it failed. Evidence is every part's.
        var evidence = verdicts.SelectMany(v => v.Evidence ?? []).Distinct().ToList();
        return worst with { Reason = string.Join("; ", verdicts.OrderBy(v => v.Result != worst.Result).Select(v => v.Reason)), Evidence = evidence.Count > 0 ? evidence : null };
    }

    /// <summary>An exam's check: its script path is relative to the exam directory.</summary>
    public static Verdict Evaluate(JsonObject spec, AttemptContext attempt, Transcript t, string name = "check") => Evaluate(new CheckDef(spec, attempt.ExamDir, "exam"), attempt, t, name);

    private static Verdict EvaluateField(string key, JsonNode? value, string scriptDir, string check, AttemptContext attempt, Transcript t)
    {
        var (name, negated) = Split(key);
        if (name is Judge.Decide or Judge.JudgeCheck) return Judge.Evaluate(name, (JsonObject)value!, check, attempt, t);
        if (value is JsonValue jv && jv.TryGetValue<string>(out var s) && s == FromExpect)
        {
            // Keyed by the check's name first, so two checks over one field can differ per task; by the field for the common single task.
            value = (attempt.Expect?[check] as JsonObject)?[name] ?? attempt.Expect?[name];
            if (value is null) return Verdict.Err($"{name}: task has no expect.{check}.{name} or expect.{name}");
        }
        var verdict = name == "script"
            ? attempt.ScriptsInVerifier ? VerifierScript(check, attempt) : RunScript(value!.GetValue<string>(), scriptDir, attempt)
            : BuiltIn(name, value!, attempt, t);
        if (verdict.Result is not Verdict.Error && name != "script" && Found(name, value!, t) is { Count: > 0 } found) verdict = verdict with { Evidence = found };
        if (!negated || verdict.Result is Verdict.Error or Verdict.NeedsJudge) return verdict;
        return verdict with { Result = verdict.Result == Verdict.Pass ? Verdict.Fail : Verdict.Pass, Reason = "not: " + verdict.Reason };
    }

    private static Verdict BuiltIn(string name, JsonNode v, AttemptContext attempt, Transcript t)
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
                // The trailer's duration_ms (nb 0.9 on; an agent attempt's core.json) comes first; the attempt's wall time from the manifest is the fallback, as in the report.
                var (ms, source) = trailer?.DurationMs is { } d ? (d, "trailer") : (WallTime(attempt), "wall time");
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
            case "loop_nudged" when attempt.Vendor:
                return Verdict.Err("loop_nudged is nb's nudge; a vendor agent has none to fire");
            case "loop_nudged":
                return Verdict.Of(t.LoopNudged == v.GetValue<bool>(), t.LoopNudged ? "loop nudge fired" : "no loop nudge");
            case "oracle_hit":
            {
                if (attempt.Task.Oracle is null) return Verdict.Err($"task {attempt.Task.Id} names no answer sheet, so the oracle could not answer");
                var keys = t.UserTurns.Where(u => u.IsOracle).SelectMany(u => u.Keys ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var missing = v.AsArray().Select(n => n!.GetValue<string>()).Where(k => !keys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList();
                var answered = keys.Count == 0 ? "the oracle never answered" : $"the oracle answered with {string.Join(", ", keys)}";
                return Verdict.Of(missing.Count == 0, missing.Count == 0 ? answered : $"{answered}; never {string.Join(", ", missing)}");
            }
            // One attempt ends on at most one miss: nb ends it there, as oracle_miss.
            case "oracle_misses":
            {
                if (trailer is null) return Verdict.Err("no result trailer");
                var misses = trailer.ExitReason == "oracle_miss" ? 1 : 0;
                return Verdict.Of(misses <= v["max"]!.GetValue<int>(), misses == 0 ? "no oracle miss" : "the attempt ended on an oracle miss: the agent asked for something the sheet does not cover");
            }
            case "oracle_turns":
            {
                var turns = t.UserTurns.Count(u => u.IsOracle);
                return Verdict.Of(turns <= v["max"]!.GetValue<int>(), $"{turns} oracle answer{(turns == 1 ? "" : "s")} (max {v["max"]})");
            }
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
            case "result_matches":
            {
                // What an attempt was shown, whatever it did next: a docs line that cannot be followed, an error it stepped past.
                var pattern = new Regex(v.GetValue<string>());
                foreach (var result in t.ToolResults)
                    if (pattern.Match(result.Output) is { Success: true } m)
                    {
                        var shown = string.Join(' ', m.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                        return new Verdict(Verdict.Pass, $"the result of call {result.Id} matches: \"{(shown.Length > 80 ? shown[..80] + "…" : shown)}\"");
                    }
                return new Verdict(Verdict.Fail, $"none of {t.ToolResults.Count} tool result{(t.ToolResults.Count == 1 ? "" : "s")} matches /{v}/");
            }
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
                var regex = v["match"]?.GetValue<string>() == "regex";
                var ignore = (v["ignore"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToList() ?? [];
                var calls = t.ToolCalls.Where(c => c.Name == toolName).ToList();
                if (calls.Count == 0) return new Verdict(Verdict.Fail, $"no call to {toolName}");
                bool matched;
                try { matched = calls.Any(c => ArgsMatch(expected, c.Arguments ?? new JsonObject(), exact, regex, ignore)); }
                catch (ArgumentException e) { return Verdict.Err($"tool_args: {e.Message}"); } // a pattern from the task's expect is unchecked until now
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
                if (t.Diff is null) return Verdict.Err("no diff.patch in the attempt");
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

    /// <summary>
    /// Where a built-in field found what it looks for, whatever the verdict (check-evidence.md, C3): the same steps serve a
    /// pass and a negated fail, and a field that found nothing points at nothing (absent evidence was dropped). Steps are
    /// ATIF step ids, so they exist only on a transcript read from a trajectory. A check on the run as a whole (exit
    /// reason, budgets, the nudge, the oracle) has none; nor has a call count over its limit, since which call went over
    /// is arbitrary.
    /// </summary>
    static List<Evidence> Found(string name, JsonNode v, Transcript t)
    {
        List<Evidence> Steps(IEnumerable<string?> steps) => [.. steps.OfType<string>().Distinct().Select(s => new Evidence(Step: s))];
        List<string> Names(JsonNode? n) => n is JsonArray a ? [.. a.Select(x => x!.GetValue<string>())] : [];
        switch (name)
        {
            case "tools_used" or "tools_used_any":
            {
                var wanted = Names(v);
                return Steps(t.ToolCalls.Where(c => wanted.Contains(c.Name)).Select(c => c.Step));
            }
            case "tool_args":
                return Steps(t.ToolCalls.Where(c => c.Name == v["name"]?.GetValue<string>()).Select(c => c.Step));
            case "tool_sequence":
            {
                var names = Names(v["names"]);
                return Steps(t.ToolCalls.Where(c => names.Contains(c.Name)).Select(c => c.Step));
            }
            case "answer_contains" or "answer_equals" or "answer_regex" or "answer_words":
                return Steps([t.AnswerStep]);
            case "result_matches":
            {
                Regex pattern;
                try { pattern = new Regex(v.GetValue<string>()); } catch (ArgumentException) { return []; }
                return Steps(t.ToolResults.Where(r => pattern.IsMatch(r.Output)).Select(r => r.Step));
            }
            case "denied_calls":
                return Steps(t.ToolCalls.Where(c => c.Denied).Select(c => c.Step));
            case "tool_errors":
                return Steps(t.ToolResults.Where(r => r.IsError).Select(r => r.Step));
            case "files_touched":
            {
                if (t.Diff is null) return [];
                var patterns = Names(v["paths"]);
                var touched = t.TouchedPaths();
                var files = v["mode"]?.GetValue<string>() == "at_most" ? touched.Where(f => !patterns.Any(p => Glob.IsMatch(p, f))) : touched.Where(f => patterns.Any(p => Glob.IsMatch(p, f)));
                return [.. files.Distinct().Select(f => new Evidence(File: f))];
            }
            default:
                return [];
        }
    }

    /// <summary>In regex mode a string expectation is a pattern over a string argument; anything else compares as JSON.</summary>
    private static bool ArgsMatch(JsonObject expected, JsonObject actual, bool exact, bool regex, List<string> ignore)
    {
        bool Ignored(string k) => ignore.Any(g => Glob.IsMatch(g, k));
        foreach (var (k, ev) in expected)
        {
            if (Ignored(k)) continue;
            if (!actual.TryGetPropertyValue(k, out var av)) return false;
            var ok = regex && ev is JsonValue pv && pv.TryGetValue<string>(out var pattern)
                ? av is JsonValue a && a.TryGetValue<string>(out var text) && Regex.IsMatch(text, pattern)
                : JsonNode.DeepEquals(ev, av);
            if (!ok) return false;
        }
        return !exact || actual.All(a => Ignored(a.Key) || expected.ContainsKey(a.Key));
    }

    private static bool IsSubsequence(List<string> wanted, List<string> actual)
    {
        var i = 0;
        foreach (var a in actual) if (i < wanted.Count && a == wanted[i]) i++;
        return i == wanted.Count;
    }

    private static long? WallTime(AttemptContext attempt)
    {
        var file = Path.Combine(attempt.AttemptDir, Layout.ManifestFile);
        if (!File.Exists(file)) return null;
        return JsonNode.Parse(File.ReadAllText(file))?["duration_ms"]?.GetValue<long>();
    }

    /// <summary>The script contract: runs in the attempt, exit 0/1/2 = pass/fail/needs-judge, first stdout line is the reason.</summary>
    private static Verdict RunScript(string script, string scriptDir, AttemptContext attempt)
    {
        var path = Path.GetFullPath(Path.Combine(scriptDir, script));
        var result = Subprocess.Run(path, [], attempt.AttemptDir, attempt.Environment());
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

    /// <summary>
    /// A script check that ran in Harbor's verifier: reward.json's key is the verdict (1 pass, 0 fail, 2 needs-judge),
    /// the first line of verifier/&lt;check&gt;.out the reason. A missing key is error, never fail: the script exited
    /// otherwise, or the verifier never reached it.
    /// </summary>
    private static Verdict VerifierScript(string check, AttemptContext attempt)
    {
        var verifier = Path.Combine(attempt.AttemptDir, Layout.VerifierDir);
        string? FirstLine(string file) => File.Exists(Path.Combine(verifier, file))
            ? File.ReadAllLines(Path.Combine(verifier, file)).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) : null;
        var rewardFile = Path.Combine(attempt.AttemptDir, Layout.RewardFile);
        if (!File.Exists(rewardFile)) return Verdict.Err("the verifier wrote no reward.json");
        var reward = JsonNode.Parse(File.ReadAllText(rewardFile))?[check];
        var reason = FirstLine($"{check}.out");
        return reward is JsonValue v && v.TryGetValue<double>(out var r) ? r switch
        {
            1 => new Verdict(Verdict.Pass, reason ?? "pass"),
            0 => new Verdict(Verdict.Fail, reason ?? "fail"),
            2 => new Verdict(Verdict.NeedsJudge, reason ?? "needs judge"),
            _ => Verdict.Err($"reward {r} is not 0, 1 or 2"),
        }
        : FirstLine("apply.out") is { } applyError ? Verdict.Err($"the attempt's diff did not apply in the verifier: {applyError}")
        : FirstLine($"{check}.exit") is { } code ? Verdict.Err(reason ?? $"exited {code}")
        : Verdict.Err("the verifier did not run this check");
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
