using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>An agent attempt as Harbor reported it, in the manifest: the agent as it ran, the Harbor that ran it, and what it used.</summary>
record AgentTrial(string Name, string? Version, string Model, Dictionary<string, string> Kwargs, Dictionary<string, string> Env, string Harbor, string? Trial,
    long? InputTokens = null, long? CachedTokens = null, long? OutputTokens = null, double? CostUsd = null);

/// <summary>
/// An oracle attempt's simulated user on the Harbor path, in the manifest: its ending (oracle-user.json's, null when the
/// trial was killed mid-loop), the answers it sent, and the judge's tokens. The judge is a separate process, so they are
/// not in the counted core; on the bare path nb charges them to the attempt.
/// </summary>
record OracleOutcome(string? Ending, int Answers, long? JudgeInputTokens, long? JudgeOutputTokens);

/// <summary>
/// core.json: proctor's own count of an attempt, by its own definitions, from the ATIF steps, never the agent's own count.
/// A turn is one agent step (one model response); a tool call is one entry of a step's tool_calls, subagents' included;
/// kinds tally the calls by proctor's normalised tool kind; the duration is the agent's phase alone.
/// </summary>
record CountedCore(string ExitReason, int Turns, int ToolCalls, int Denied, Dictionary<string, int> Kinds, long? InputTokens, long? CachedTokens, long? OutputTokens, double? CostUsd, long? DurationMs = null);

/// <summary>
/// A Harbor trial into an attempt: the trajectory, the agent's native logs, the diff, the verifier's rewards and outputs,
/// and the counted core. Strict: a step or tool the agent's mapping does not know fails the attempt as adapter_unmapped
/// rather than being dropped, so a vendor or Harbor format change shows on the first attempt after the bump. The one
/// exception is a tool name the model made up and the agent refused with an error result, counted as kind "none".
/// </summary>
static class Ingest
{
    /// <summary>Each agent's tools by proctor's kind, as the trajectory names them. A tool missing here is adapter_unmapped.</summary>
    internal static readonly Dictionary<string, Dictionary<string, string>> Kinds = new()
    {
        [Variant.ClaudeCode] = new()
        {
            ["Bash"] = "shell", ["BashOutput"] = "shell", ["KillShell"] = "shell", ["KillBash"] = "shell", ["TaskStop"] = "shell",
            ["EnterWorktree"] = "shell", ["ExitWorktree"] = "shell",
            ["Read"] = "read", ["LS"] = "read", ["NotebookRead"] = "read",
            ["Write"] = "write",
            ["Edit"] = "edit", ["MultiEdit"] = "edit", ["NotebookEdit"] = "edit",
            ["Glob"] = "search", ["Grep"] = "search",
            ["Task"] = "agent", ["Agent"] = "agent", ["ListAgents"] = "agent", ["SendMessage"] = "agent", ["Workflow"] = "agent",
            ["TodoWrite"] = "plan", ["ExitPlanMode"] = "plan",
            ["WebFetch"] = "web", ["WebSearch"] = "web",
            ["Skill"] = "skill", ["SlashCommand"] = "skill",
            ["CronCreate"] = "schedule", ["CronDelete"] = "schedule", ["CronList"] = "schedule", ["ScheduleWakeup"] = "schedule",
            ["ReportFindings"] = "ask",
        },
        // Codex's names are its rollout's (Harbor keeps them): unified exec's two calls, the older shells, apply_patch for every file change.
        [Variant.Codex] = new()
        {
            ["exec_command"] = "shell", ["write_stdin"] = "shell", ["shell"] = "shell", ["local_shell"] = "shell", ["shell_command"] = "shell",
            ["view_image"] = "read",
            ["apply_patch"] = "edit",
            ["update_plan"] = "plan", ["get_goal"] = "plan", ["create_goal"] = "plan", ["update_goal"] = "plan",
            ["request_user_input"] = "ask",
            ["web_search_call"] = "web", ["web_search"] = "web",
            ["multi_agent_v1"] = "agent",
        },
        [Variant.QwenCode] = new()
        {
            ["run_shell_command"] = "shell",
            ["read_file"] = "read", ["read_many_files"] = "read", ["list_directory"] = "read",
            ["write_file"] = "write",
            ["edit"] = "edit", ["replace"] = "edit", ["notebook_edit"] = "edit",
            ["glob"] = "search", ["grep_search"] = "search", ["search_file_content"] = "search",
            ["task"] = "agent",
            ["todo_write"] = "plan", ["exit_plan_mode"] = "plan",
            ["web_fetch"] = "web", ["web_search"] = "web",
            ["skill"] = "skill",
            ["save_memory"] = "memory",
            ["tool_search"] = "meta", ["tool_call"] = "meta",
        },
        // Mirrored by nb's nb.Tests/ProctorContractTests.cs, which fails when nb advertises a tool not listed here.
        [Variant.Nb] = new()
        {
            ["bash"] = "shell",
            ["read_file"] = "read", ["list_dir"] = "read",
            ["write_file"] = "write",
            ["edit_file"] = "edit", ["apply_patch"] = "edit",
            ["find_files"] = "search", ["grep"] = "search",
            ["fetch_url"] = "web", ["search_web"] = "web",
            // `todo` is nb's directive; on the wire it is two tools.
            ["todo_write"] = "plan", ["todo_read"] = "plan",
        },
    };

    /// <summary>
    /// A multi-step trial's steps, each made to read as a trial of its own: Harbor keeps a step's agent logs, verifier
    /// output and artifacts under steps/&lt;name&gt;/ in a trial's own shape, and its result inside the trial's
    /// result.json, which is written out beside them here. Returns each step's directory by name, for the steps Harbor
    /// ran; and why the trial as a whole failed, when it did (the environment never built, say).
    /// </summary>
    public static (Dictionary<string, string> Steps, string? Failure) Steps(string trialDir)
    {
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(trialDir, "result.json")))!.AsObject();
        var steps = new Dictionary<string, string>();
        foreach (var step in (result["step_results"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var name = step["step_name"]!.GetValue<string>();
            var dir = Path.Combine(trialDir, "steps", name);
            Directory.CreateDirectory(dir);
            Runner.WriteJson(Path.Combine(dir, "result.json"), new JsonObject
            {
                ["agent_info"] = result["agent_info"]?.DeepClone(), ["agent_result"] = step["agent_result"]?.DeepClone(),
                ["exception_info"] = step["exception_info"]?.DeepClone(), ["agent_execution"] = step["agent_execution"]?.DeepClone(),
            });
            steps[name] = dir;
        }
        var exception = result["exception_info"];
        return (steps, exception is null ? null : $"harbor: {exception["exception_type"]?.GetValue<string>()}: {Said(exception["exception_message"]?.GetValue<string>())}");
    }

    /// <summary>
    /// Lift the trial into the attempt and fill the manifest. Null when the attempt completed; otherwise why it failed
    /// (infrastructure only). The version is the variant's pin for a vendor agent, the host nb's for nb. A bridged trial is an
    /// oracle attempt's: nb served over ACP, driven by the simulated user (<see cref="Oracle"/>).
    /// </summary>
    public static string? Trial(string trialDir, string attemptDir, Variant variant, Manifest manifest, string expectedVersion, bool bridged = false)
    {
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(trialDir, "result.json")))!.AsObject();
        CopyTree(Path.Combine(trialDir, "agent"), Path.Combine(attemptDir, Layout.AgentLogsDir));
        CopyTree(Path.Combine(trialDir, Layout.UserAgentLogsDir), Path.Combine(attemptDir, Layout.UserAgentLogsDir));
        CopyTree(Path.Combine(trialDir, "verifier"), Path.Combine(attemptDir, Layout.VerifierDir));
        // Harbor mirrors a collected container path under artifacts/: the verifier's collect step wrote /logs/artifacts/diff.patch.
        var diff = Path.Combine(trialDir, "artifacts", "logs", "artifacts", Layout.DiffFile);
        if (File.Exists(diff)) File.Copy(diff, Path.Combine(attemptDir, Layout.DiffFile), overwrite: true);
        var reward = Path.Combine(trialDir, "verifier", Layout.RewardFile);
        if (File.Exists(reward)) File.Copy(reward, Path.Combine(attemptDir, Layout.RewardFile), overwrite: true);

        var agentInfo = result["agent_info"];
        var agentResult = result["agent_result"];
        manifest.Agent = manifest.Agent! with
        {
            Version = agentInfo?["version"]?.GetValue<string>(),
            Trial = Path.GetRelativePath(attemptDir, trialDir),
            InputTokens = Long(agentResult?["n_input_tokens"]),
            CachedTokens = Long(agentResult?["n_cache_tokens"]),
            OutputTokens = Long(agentResult?["n_output_tokens"]),
            CostUsd = agentResult?["cost_usd"]?.GetValue<double>(),
        };

        var exception = result["exception_info"];
        var exceptionType = exception?["exception_type"]?.GetValue<string>();
        var exceptionLine = $"{exceptionType}: {Said(exception?["exception_message"]?.GetValue<string>())}";
        var trajectoryFile = Path.Combine(trialDir, "agent", Layout.TrajectoryFile);
        var hasTrajectory = File.Exists(trajectoryFile);

        if (exception is not null && exceptionType is not ("AgentTimeoutError" or "NonZeroAgentExitCodeError"))
            return $"harbor: {exceptionLine}";
        if (exceptionType == "NonZeroAgentExitCodeError" && !hasTrajectory)
            return $"agent exited without a trajectory: {exceptionLine}";
        if (manifest.Agent.Version != expectedVersion)
            return variant.IsVendor
                ? $"agent_version_mismatch: the trial ran {variant.Agent} {manifest.Agent.Version ?? "unknown"}, the variant pins {variant.AgentVersion}"
                : $"agent_version_mismatch: the image ran nb {manifest.Agent.Version ?? "unknown"}, the host nb that compiled the program is {expectedVersion}";
        if (!variant.IsVendor)
        {
            if (Nb(trialDir, attemptDir, exceptionType, manifest, bridged) is { } nbFailure) return nbFailure;
            hasTrajectory = true;
            trajectoryFile = Path.Combine(attemptDir, Layout.TrajectoryFile);
        }
        if (!hasTrajectory)
            return "adapter_unmapped: the trial has no agent/trajectory.json";
        if (!File.Exists(diff))
            return $"the trial collected no diff (artifacts/logs/artifacts/{Layout.DiffFile}); the verifier's collect step did not run";

        if (variant.IsVendor) File.Copy(trajectoryFile, Path.Combine(attemptDir, Layout.TrajectoryFile), overwrite: true);
        var trajectory = JsonNode.Parse(File.ReadAllText(trajectoryFile))!.AsObject();
        string? ending = null;
        if (bridged)
        {
            if (Oracle(trialDir, trajectory, manifest, out ending) is { } oracleFailure) return oracleFailure;
            Runner.WriteJson(trajectoryFile, trajectory);
        }
        var exitReason = exceptionType switch
        {
            "AgentTimeoutError" => "time_budget",
            "NonZeroAgentExitCodeError" => "agent_error",
            // The simulated user's ending, which nb's prompt never saw, as nb names it on the bare path.
            _ when ending is "oracle_miss" or "oracle_budget" => ending,
            // nb names its own ending, as on the bare path.
            _ => trajectory["extra"]?["result"]?["exit_reason"]?.GetValue<string>() ?? "ok",
        };
        var (core, unmapped) = Count(trajectory, variant.Agent!, exitReason);
        if (unmapped is not null) return $"adapter_unmapped: {unmapped}";
        // A vendor agent that never took a turn never heard from its model (Codex retries an unreachable endpoint until
        // the timeout): an outage, not an attempt. nb names its own ending, as on the bare path.
        if (variant.IsVendor && core!.Turns == 0)
            return $"the agent took no turn ({exitReason}): its model never answered; see agent/ for why";
        // Tokens are the trajectory's when it carries them, else Harbor's account of the agent's run.
        Runner.WriteJson(Path.Combine(attemptDir, Layout.CoreFile), core! with
        {
            InputTokens = core.InputTokens ?? manifest.Agent.InputTokens,
            CachedTokens = core.CachedTokens ?? manifest.Agent.CachedTokens,
            OutputTokens = core.OutputTokens ?? manifest.Agent.OutputTokens,
            CostUsd = core.CostUsd ?? manifest.Agent.CostUsd,
            DurationMs = Elapsed(result["agent_execution"]),
        });
        return null;
    }

    /// <summary>
    /// nb's own trajectory (agent/trajectory.json, which nb wrote with --trajectory beside its JSONL, stderr and exit
    /// code) as the attempt's. Failed as on the bare path: nb that ends without one before its time is up did not run.
    /// Out of time is a completed attempt with nothing written: nb writes both files when its run ends. Bridged, nb is the
    /// ACP server Harbor started, with no JSONL, stderr or exit code of its own, and rewrites the trajectory after each
    /// prompt.
    /// </summary>
    static string? Nb(string trialDir, string attemptDir, string? exceptionType, Manifest manifest, bool bridged)
    {
        var logs = Path.Combine(trialDir, "agent");
        if (int.TryParse(ReadOr(Path.Combine(logs, "nb.exit")).Trim(), out var exit)) manifest.NbExitCode = exit;
        var stderr = FirstLine(ReadOr(Path.Combine(logs, "nb.stderr.log")));
        if (!bridged && !File.Exists(Path.Combine(logs, Layout.NbLogFile))) return $"nb did not run in the trial (no agent/{Layout.NbLogFile}){(exceptionType is null ? "" : $": {exceptionType}")}";
        var trajectory = ReadOr(Path.Combine(logs, Layout.TrajectoryFile)) is { Length: > 0 } text ? JsonNode.Parse(text)!.AsObject() : null;
        if (trajectory is null && exceptionType != "AgentTimeoutError")
            return bridged
                ? $"nb answered no prompt over the bridge (no agent/{Layout.TrajectoryFile}){(exceptionType is null ? "" : $": {exceptionType}")}"
                : $"nb exited {manifest.NbExitCode?.ToString() ?? "?"} without a trajectory: {stderr}";
        trajectory ??= new JsonObject
        {
            ["schema_version"] = "ATIF-v1.7",
            ["agent"] = new JsonObject { ["name"] = Variant.Nb, ["version"] = manifest.Agent!.Version },
            ["steps"] = new JsonArray(),
        };
        Runner.WriteJson(Path.Combine(attemptDir, Layout.TrajectoryFile), trajectory);
        return null;
    }

    /// <summary>
    /// An oracle attempt's simulated user (user-agent/oracle-user.json, from Harbor/oracle_user.py) against nb's trajectory.
    /// The trajectory's last user steps must be the prompts it sent, in order, compared with trailing whitespace trimmed
    /// (acpx trims a prompt's); the answers among them are marked as the bare path's are, extra {source: oracle, keys}.
    /// Strict: any other shape is adapter_unmapped. A judge that failed fails the attempt, since the attempt then measures the
    /// judge, not the agent. Null when it lines up, with the ending out and the manifest's <see cref="OracleOutcome"/> filled.
    /// </summary>
    static string? Oracle(string trialDir, JsonObject trajectory, Manifest manifest, out string? ending)
    {
        ending = null;
        var file = Path.Combine(trialDir, Layout.UserAgentLogsDir, Layout.OracleUserFile);
        if (!File.Exists(file)) return $"the simulated user left no {Layout.UserAgentLogsDir}/{Layout.OracleUserFile}";
        var record = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        ending = record["ending"]?.GetValue<string>();
        var prompts = (record["prompts"]?.AsArray() ?? []).OfType<JsonObject>().ToList();
        var usage = (record["judgements"]?.AsArray() ?? []).Select(j => j?["usage"]).OfType<JsonObject>().ToList();
        long? Judge(string field) => usage.Count == 0 ? null : usage.Sum(u => Long(u[field]) ?? 0);
        manifest.Oracle = new OracleOutcome(ending, prompts.Count(p => p["keys"] is not null), Judge("input"), Judge("output"));
        if (ending == "judge_error") return $"judge_error: {record["error"]?.GetValue<string>()}";

        var users = (trajectory["steps"]?.AsArray() ?? []).OfType<JsonObject>().Where(s => s["source"]?.GetValue<string>() == "user").ToList();
        bool Sent(int n) => users.Count >= n && Enumerable.Range(0, n).All(i => Trimmed(users[users.Count - n + i]["message"]) == Trimmed(prompts[i]["text"]));
        var sent = prompts.Count;
        if (!Sent(sent))
        {
            // A prompt still in flight when the trial was killed has no stop_reason, and may not have reached the trajectory.
            if (ending is null && sent > 0 && !prompts[^1].ContainsKey("stop_reason") && Sent(sent - 1)) sent--;
            else return $"adapter_unmapped: the trajectory's last user steps are not the {sent} prompts the simulated user sent ({Layout.UserAgentLogsDir}/{Layout.OracleUserFile})";
        }
        for (var i = 1; i < sent; i++)
            users[users.Count - sent + i]["extra"] = new JsonObject { ["source"] = "oracle", ["keys"] = prompts[i]["keys"]?.DeepClone() };
        return null;
    }

    /// <summary>The kind of a call to a tool the agent does not have, which the agent refused with an error result.</summary>
    public const string NoSuchTool = "none";

    /// <summary>The ids of a step's calls whose results the agent marked as errors (Claude Code's is_error).</summary>
    static HashSet<string> Refused(JsonNode step) =>
        (step["observation"]?["results"]?.AsArray() ?? []).OfType<JsonObject>()
            .Where(r => r["extra"]?["tool_result_metadata"]?["is_error"] is JsonValue v && v.TryGetValue<bool>(out var e) && e)
            .Select(r => r["source_call_id"]?.GetValue<string>() ?? "").Where(id => id.Length > 0).ToHashSet();

    static string Trimmed(JsonNode? text) => (text?.GetValue<string>() ?? "").TrimEnd();

    static string ReadOr(string file) => File.Exists(file) ? File.ReadAllText(file) : "";

    /// <summary>The counted core of an ATIF trajectory. Denied is the calls the agent refused: nb marks them; the vendor agents run with approvals bypassed (Claude Code's bypassPermissions, Codex's and Qwen Code's only mode in Harbor), so refuse none, and their denial mapping comes with approval.</summary>
    public static (CountedCore? Core, string? Unmapped) Count(JsonObject trajectory, string agent, string exitReason)
    {
        var kinds = Kinds[agent];
        int turns = 0, calls = 0, denied = 0;
        var tally = new Dictionary<string, int>();
        string? unmapped = null;

        void Walk(JsonObject t)
        {
            foreach (var step in t["steps"]?.AsArray() ?? [])
            {
                var source = step?["source"]?.GetValue<string>();
                if (source is not ("system" or "user" or "agent")) { unmapped ??= $"step {step?["step_id"]} has source '{source}'"; continue; }
                if (source != "agent") continue;
                turns++;
                var refused = Refused(step!);
                foreach (var call in (step!["tool_calls"]?.AsArray() ?? []).OfType<JsonObject>())
                {
                    var (name, _) = Called(call);
                    calls++;
                    if (call["extra"]?["approved"]?.GetValue<string>() == "deny") denied++;
                    if (kinds.TryGetValue(name, out var kind)) tally[kind] = tally.GetValueOrDefault(kind) + 1;
                    // A name the model made up, which the agent answered with an error: the model's slip, not a format change.
                    else if (refused.Contains(call["tool_call_id"]?.GetValue<string>() ?? "")) tally[NoSuchTool] = tally.GetValueOrDefault(NoSuchTool) + 1;
                    else unmapped ??= $"tool '{name}' has no kind for {agent}";
                }
            }
            foreach (var sub in t["subagent_trajectories"]?.AsArray() ?? []) if (sub is JsonObject s) Walk(s);
        }
        Walk(trajectory);
        if (unmapped is not null) return (null, unmapped);

        var metrics = trajectory["final_metrics"];
        return (new CountedCore(exitReason, turns, calls, denied, tally,
            Long(metrics?["total_prompt_tokens"]), Long(metrics?["total_cached_tokens"]), Long(metrics?["total_completion_tokens"]),
            metrics?["total_cost_usd"]?.GetValue<double>()), null);
    }

    /// <summary>
    /// The tool an ATIF call ran and its arguments. Qwen Code calls a tool it loads on demand (web_fetch among them) through
    /// its `tool_call` wrapper, `{name, arguments}`, so the wrapper is unwrapped to the tool inside; the trajectory keeps it.
    /// A wrapper without that shape is itself, a meta call.
    /// </summary>
    internal static (string Name, JsonObject? Arguments) Called(JsonObject call)
    {
        var name = call["function_name"]?.GetValue<string>() ?? "";
        var args = call["arguments"] as JsonObject;
        if (name == "tool_call" && args?["name"] is JsonValue inner && inner.TryGetValue<string>(out var innerName) && innerName.Length > 0)
            return (innerName, args["arguments"] switch
            {
                JsonObject o => o,
                JsonValue s when s.TryGetValue<string>(out var text) && TryObject(text) is { } parsed => parsed,
                _ => null,
            });
        return (name, args);

        static JsonObject? TryObject(string text) { try { return JsonNode.Parse(text) as JsonObject; } catch (System.Text.Json.JsonException) { return null; } }
    }

    static long? Long(JsonNode? node) => node?.GetValue<long>();

    /// <summary>A Harbor phase's wall time: the agent's run alone, without the image build, the setup or the verifier.</summary>
    static long? Elapsed(JsonNode? timing) =>
        DateTimeOffset.TryParse(timing?["started_at"]?.GetValue<string>(), out var start) && DateTimeOffset.TryParse(timing?["finished_at"]?.GetValue<string>(), out var end)
            ? (long)(end - start).TotalMilliseconds : null;

    static string FirstLine(string? text) => (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    /// <summary>
    /// What Harbor's exception says, in a line. When an image could not be built its first line is the compose command and
    /// the build's own error is far below it, so that error is the line: a configuration whose image cannot be built
    /// should say why (plans/axes-build.md, slice 4).
    /// </summary>
    static string Said(string? message)
    {
        var lines = (message ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var build = lines.LastOrDefault(l => l.StartsWith("failed to solve:", StringComparison.Ordinal) || l.StartsWith("Error: building at STEP", StringComparison.Ordinal));
        return build is null ? lines.FirstOrDefault() ?? "" : "the image could not be built: " + (build.Length > 400 ? build[..400] + "…" : build);
    }

    static void CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
