using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>An agent cell's run as Harbor reported it, in the manifest: the agent as it ran, the Harbor that ran it, and what it used.</summary>
record AgentRun(string Name, string? Version, string Model, Dictionary<string, string> Kwargs, Dictionary<string, string> Env, string Harbor, string? Trial,
    long? InputTokens = null, long? CachedTokens = null, long? OutputTokens = null, double? CostUsd = null);

/// <summary>
/// core.json: proctor's own count of a run, by its own definitions, from the ATIF steps, never the agent's own count.
/// A turn is one agent step (one model response); a tool call is one entry of a step's tool_calls, subagents' included;
/// kinds tally the calls by proctor's normalised tool kind.
/// </summary>
record CountedCore(string ExitReason, int Turns, int ToolCalls, int Denied, Dictionary<string, int> Kinds, long? InputTokens, long? CachedTokens, long? OutputTokens, double? CostUsd);

/// <summary>
/// A Harbor trial into a cell: the trajectory, the agent's native logs, the diff, the verifier's rewards and outputs,
/// and the counted core. Strict: a step or tool the agent's mapping does not know fails the cell as adapter_unmapped
/// rather than being dropped, so a vendor or Harbor format change shows on the first run after the bump.
/// </summary>
static class Ingest
{
    /// <summary>Claude Code's tools by proctor's kind. A tool missing here is adapter_unmapped.</summary>
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
    };

    /// <summary>Lift the trial into the cell and fill the manifest. Null when the cell completed; otherwise why it failed (infrastructure only).</summary>
    public static string? Trial(string trialDir, string cellDir, Arm arm, Manifest manifest)
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
        if (manifest.Agent.Version != arm.AgentVersion)
            return $"agent_version_mismatch: the trial ran {arm.Agent} {manifest.Agent.Version ?? "unknown"}, the arm pins {arm.AgentVersion}";
        if (!hasTrajectory)
            return "adapter_unmapped: the trial has no agent/trajectory.json";
        if (!File.Exists(diff))
            return $"the trial collected no diff (artifacts/logs/artifacts/{Layout.DiffFile}); the verifier's collect step did not run";

        File.Copy(trajectoryFile, Path.Combine(cellDir, Layout.TrajectoryFile), overwrite: true);
        var exitReason = exceptionType switch
        {
            "AgentTimeoutError" => "time_budget",
            "NonZeroAgentExitCodeError" => "agent_error",
            _ => "ok",
        };
        var (core, unmapped) = Count(JsonNode.Parse(File.ReadAllText(trajectoryFile))!.AsObject(), arm.Agent!, exitReason);
        if (unmapped is not null) return $"adapter_unmapped: {unmapped}";
        Runner.WriteJson(Path.Combine(cellDir, Layout.CoreFile), core!);
        return null;
    }

    /// <summary>The counted core of an ATIF trajectory. Under bypassPermissions nothing is refused, so denied is 0; the denial mapping comes with approval.</summary>
    public static (CountedCore? Core, string? Unmapped) Count(JsonObject trajectory, string agent, string exitReason)
    {
        var kinds = Kinds[agent];
        int turns = 0, calls = 0;
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
                    if (kinds.TryGetValue(name, out var kind)) tally[kind] = tally.GetValueOrDefault(kind) + 1;
                    else unmapped ??= $"tool '{name}' has no kind for {agent}";
                }
            }
            foreach (var sub in t["subagent_trajectories"]?.AsArray() ?? []) if (sub is JsonObject s) Walk(s);
        }
        Walk(trajectory);
        if (unmapped is not null) return (null, unmapped);

        var metrics = trajectory["final_metrics"];
        return (new CountedCore(exitReason, turns, calls, Denied: 0, tally,
            Long(metrics?["total_prompt_tokens"]), Long(metrics?["total_cached_tokens"]), Long(metrics?["total_completion_tokens"]),
            metrics?["total_cost_usd"]?.GetValue<double>()), null);
    }

    static long? Long(JsonNode? node) => node?.GetValue<long>();

    static string FirstLine(string? text) => (text ?? "").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    static void CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
