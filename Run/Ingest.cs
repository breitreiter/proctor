using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>An agent cell's run as Harbor reported it, in the manifest: the agent as it ran, the Harbor that ran it, and what it used.</summary>
record AgentRun(string Name, string? Version, string Model, Dictionary<string, string> Kwargs, Dictionary<string, string> Env, string Harbor, string? Trial,
    long? InputTokens = null, long? CachedTokens = null, long? OutputTokens = null, double? CostUsd = null);

/// <summary>
/// core.json: proctor's own count of a run, by its own definitions, from the ATIF steps, never the agent's own count.
/// A turn is one agent step (one model response); a tool call is one entry of a step's tool_calls, subagents' included;
/// kinds tally the calls by proctor's normalised tool kind; the duration is the agent's phase alone.
/// </summary>
record CountedCore(string ExitReason, int Turns, int ToolCalls, int Denied, Dictionary<string, int> Kinds, long? InputTokens, long? CachedTokens, long? OutputTokens, double? CostUsd, long? DurationMs = null);

/// <summary>
/// A Harbor trial into a cell: the trajectory, the agent's native logs, the diff, the verifier's rewards and outputs,
/// and the counted core. Strict: a step or tool the agent's mapping does not know fails the cell as adapter_unmapped
/// rather than being dropped, so a vendor or Harbor format change shows on the first run after the bump.
/// </summary>
static class Ingest
{
    /// <summary>Each agent's tools by proctor's kind, as the trajectory names them. A tool missing here is adapter_unmapped.</summary>
    static readonly Dictionary<string, Dictionary<string, string>> Kinds = new()
    {
        [Arm.ClaudeCode] = new()
        {
            ["Bash"] = "shell", ["BashOutput"] = "shell", ["KillShell"] = "shell", ["KillBash"] = "shell",
            ["Read"] = "read", ["LS"] = "read", ["NotebookRead"] = "read",
            ["Write"] = "write",
            ["Edit"] = "edit", ["MultiEdit"] = "edit", ["NotebookEdit"] = "edit",
            ["Glob"] = "search", ["Grep"] = "search",
            ["Task"] = "agent", ["Agent"] = "agent",
            ["TodoWrite"] = "plan", ["ExitPlanMode"] = "plan",
            ["WebFetch"] = "web", ["WebSearch"] = "web",
            ["Skill"] = "skill", ["SlashCommand"] = "skill",
        },
        // Codex's names are its rollout's (Harbor keeps them): unified exec's two calls, the older shells, apply_patch for every file change.
        [Arm.Codex] = new()
        {
            ["exec_command"] = "shell", ["write_stdin"] = "shell", ["shell"] = "shell", ["local_shell"] = "shell", ["shell_command"] = "shell",
            ["view_image"] = "read",
            ["apply_patch"] = "edit",
            ["update_plan"] = "plan", ["get_goal"] = "plan", ["create_goal"] = "plan", ["update_goal"] = "plan",
            ["request_user_input"] = "ask",
            ["web_search_call"] = "web",
        },
        [Arm.QwenCode] = new()
        {
            ["run_shell_command"] = "shell",
            ["read_file"] = "read", ["read_many_files"] = "read", ["list_directory"] = "read",
            ["write_file"] = "write",
            ["edit"] = "edit", ["replace"] = "edit",
            ["glob"] = "search", ["grep_search"] = "search", ["search_file_content"] = "search",
            ["task"] = "agent",
            ["todo_write"] = "plan", ["exit_plan_mode"] = "plan",
            ["web_fetch"] = "web", ["web_search"] = "web",
            ["skill"] = "skill",
            ["save_memory"] = "memory",
        },
        [Arm.Nb] = new()
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
    /// Lift the trial into the cell and fill the manifest. Null when the cell completed; otherwise why it failed
    /// (infrastructure only). The version is the arm's pin for a vendor agent, the host nb's for nb.
    /// </summary>
    public static string? Trial(string trialDir, string cellDir, Arm arm, Manifest manifest, string expectedVersion)
    {
        var result = JsonNode.Parse(File.ReadAllText(Path.Combine(trialDir, "result.json")))!.AsObject();
        CopyTree(Path.Combine(trialDir, "agent"), Path.Combine(cellDir, Layout.AgentLogsDir));
        CopyTree(Path.Combine(trialDir, "verifier"), Path.Combine(cellDir, Layout.VerifierDir));
        // Harbor mirrors a collected container path under artifacts/: the verifier's collect step wrote /logs/artifacts/diff.patch.
        var diff = Path.Combine(trialDir, "artifacts", "logs", "artifacts", Layout.DiffFile);
        if (File.Exists(diff)) File.Copy(diff, Path.Combine(cellDir, Layout.DiffFile), overwrite: true);
        var reward = Path.Combine(trialDir, "verifier", Layout.RewardFile);
        if (File.Exists(reward)) File.Copy(reward, Path.Combine(cellDir, Layout.RewardFile), overwrite: true);

        var agentInfo = result["agent_info"];
        var agentResult = result["agent_result"];
        manifest.Agent = manifest.Agent! with
        {
            Version = agentInfo?["version"]?.GetValue<string>(),
            Trial = Path.GetRelativePath(cellDir, trialDir),
            InputTokens = Long(agentResult?["n_input_tokens"]),
            CachedTokens = Long(agentResult?["n_cache_tokens"]),
            OutputTokens = Long(agentResult?["n_output_tokens"]),
            CostUsd = agentResult?["cost_usd"]?.GetValue<double>(),
        };

        var exception = result["exception_info"];
        var exceptionType = exception?["exception_type"]?.GetValue<string>();
        var exceptionLine = $"{exceptionType}: {FirstLine(exception?["exception_message"]?.GetValue<string>())}";
        var trajectoryFile = Path.Combine(trialDir, "agent", Layout.TrajectoryFile);
        var hasTrajectory = File.Exists(trajectoryFile);

        if (exception is not null && exceptionType is not ("AgentTimeoutError" or "NonZeroAgentExitCodeError"))
            return $"harbor: {exceptionLine}";
        if (exceptionType == "NonZeroAgentExitCodeError" && !hasTrajectory)
            return $"agent exited without a trajectory: {exceptionLine}";
        if (manifest.Agent.Version != expectedVersion)
            return arm.IsVendor
                ? $"agent_version_mismatch: the trial ran {arm.Agent} {manifest.Agent.Version ?? "unknown"}, the arm pins {arm.AgentVersion}"
                : $"agent_version_mismatch: the image ran nb {manifest.Agent.Version ?? "unknown"}, the host nb that compiled the program is {expectedVersion}";
        if (!arm.IsVendor)
        {
            if (Nb(trialDir, cellDir, exceptionType, manifest) is { } nbFailure) return nbFailure;
            hasTrajectory = true;
            trajectoryFile = Path.Combine(cellDir, Layout.TrajectoryFile);
        }
        if (!hasTrajectory)
            return "adapter_unmapped: the trial has no agent/trajectory.json";
        if (!File.Exists(diff))
            return $"the trial collected no diff (artifacts/logs/artifacts/{Layout.DiffFile}); the verifier's collect step did not run";

        if (arm.IsVendor) File.Copy(trajectoryFile, Path.Combine(cellDir, Layout.TrajectoryFile), overwrite: true);
        var trajectory = JsonNode.Parse(File.ReadAllText(trajectoryFile))!.AsObject();
        var exitReason = exceptionType switch
        {
            "AgentTimeoutError" => "time_budget",
            "NonZeroAgentExitCodeError" => "agent_error",
            // nb names its own ending, as on the bare path.
            _ => trajectory["extra"]?["result"]?["exit_reason"]?.GetValue<string>() ?? "ok",
        };
        var (core, unmapped) = Count(trajectory, arm.Agent!, exitReason);
        if (unmapped is not null) return $"adapter_unmapped: {unmapped}";
        // A vendor agent that never took a turn never heard from its model (Codex retries an unreachable endpoint until
        // the timeout): an outage, not a run. nb names its own ending, as on the bare path.
        if (arm.IsVendor && core!.Turns == 0)
            return $"the agent took no turn ({exitReason}): its model never answered; see agent/ for why";
        // Tokens are the trajectory's when it carries them, else Harbor's account of the agent's run.
        Runner.WriteJson(Path.Combine(cellDir, Layout.CoreFile), core! with
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
    /// nb's native log (agent/nb.jsonl, its stderr and exit code, which the agent wrote) as the cell's trajectory.
    /// Failed as on the bare path: nb that ends without a result trailer before its time is up did not run.
    /// </summary>
    static string? Nb(string trialDir, string cellDir, string? exceptionType, Manifest manifest)
    {
        var logs = Path.Combine(trialDir, "agent");
        var jsonl = Path.Combine(logs, Layout.NbLogFile);
        if (int.TryParse(ReadOr(Path.Combine(logs, "nb.exit")).Trim(), out var exit)) manifest.NbExitCode = exit;
        var stderr = FirstLine(ReadOr(Path.Combine(logs, "nb.stderr.log")));
        if (!File.Exists(jsonl)) return $"nb did not run in the trial (no agent/{Layout.NbLogFile}){(exceptionType is null ? "" : $": {exceptionType}")}";
        var native = File.ReadAllText(jsonl);
        var (trajectory, unmapped) = NbAtif.Convert(native, manifest.Agent!.Version);
        if (unmapped is not null) return $"adapter_unmapped: {unmapped}";
        if (trajectory!["extra"]?["result"] is null && exceptionType != "AgentTimeoutError")
            return $"nb exited {manifest.NbExitCode?.ToString() ?? "?"} without a result trailer: {stderr}";
        Runner.WriteJson(Path.Combine(cellDir, Layout.TrajectoryFile), trajectory);
        return null;
    }

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
                foreach (var call in step!["tool_calls"]?.AsArray() ?? [])
                {
                    var name = call?["function_name"]?.GetValue<string>() ?? "";
                    calls++;
                    if (call?["extra"]?["approved"]?.GetValue<string>() == "deny") denied++;
                    if (kinds.TryGetValue(name, out var kind)) tally[kind] = tally.GetValueOrDefault(kind) + 1;
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

    static long? Long(JsonNode? node) => node?.GetValue<long>();

    /// <summary>A Harbor phase's wall time: the agent's run alone, without the image build, the setup or the verifier.</summary>
    static long? Elapsed(JsonNode? timing) =>
        DateTimeOffset.TryParse(timing?["started_at"]?.GetValue<string>(), out var start) && DateTimeOffset.TryParse(timing?["finished_at"]?.GetValue<string>(), out var end)
            ? (long)(end - start).TotalMilliseconds : null;

    static string FirstLine(string? text) => (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    static void CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
