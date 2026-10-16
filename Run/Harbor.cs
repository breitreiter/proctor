namespace Proctor;

/// <summary>An experiment's Harbor: the pinned release and the hash of the tasks compiled for it, which a resume must find unchanged.</summary>
record ResolvedHarbor(string Version, string TasksHash);

/// <summary>
/// One cell's trial: Harbor's job file in the cell, then `uv tool run harbor@&lt;pin&gt; run` on it with telemetry off and
/// the model key in that one process's environment, never on disk. Harbor's job directory stays in the cell as the full
/// record; ingest lifts what proctor reads out of it.
/// </summary>
static class HarborRun
{
    /// <summary>What the image build, the verifier and Harbor's own start may add to the agent's wall limit before proctor stops waiting.</summary>
    static readonly TimeSpan Overhead = TimeSpan.FromSeconds(1800 + 900 + 600);

    /// <summary>Run the trial. Returns the trial directory, or why there is none (infrastructure: the cell fails).</summary>
    public static (string? TrialDir, string? Failure) Trial(string uv, string version, string cellDir, Arm arm, string taskDir, AgentRoute route, long wallMs)
    {
        var harborDir = Path.Combine(cellDir, Layout.HarborDir);
        if (Directory.Exists(harborDir)) Directory.Delete(harborDir, recursive: true);   // a resumed cell starts clean
        Directory.CreateDirectory(harborDir);
        var jobsDir = Path.Combine(harborDir, "jobs");
        Runner.WriteJson(Path.Combine(harborDir, Layout.HarborJobFile), HarborCompile.Job(arm, taskDir, jobsDir, route));

        var env = new Dictionary<string, string> { ["HARBOR_TELEMETRY"] = "off", ["NO_COLOR"] = "1" };
        if (route.ApiKey is { } key) env["ANTHROPIC_API_KEY"] = key;
        var timeout = TimeSpan.FromMilliseconds(wallMs) + Overhead;
        var result = Subprocess.Run(uv, ["tool", "run", $"harbor@{version}", "run", "--config", Layout.HarborJobFile], harborDir, env,
            stdoutFile: Path.Combine(harborDir, Layout.HarborLogFile), stderrFile: Path.Combine(harborDir, "harbor.stderr.log"), timeout: timeout);
        if (!result.Started) return (null, $"could not start Harbor: {result.Stderr} (suites/proctor.json harbor.uv)");
        if (result.TimedOut) return (null, $"Harbor did not finish within {timeout.TotalMinutes:0} minutes; killed (its containers may need `docker ps` and removing)");

        var trial = Directory.Exists(Path.Combine(jobsDir, "job"))
            ? Directory.GetDirectories(Path.Combine(jobsDir, "job")).FirstOrDefault(d => File.Exists(Path.Combine(d, "result.json")))
            : null;
        if (trial is null)
        {
            var stderr = File.ReadAllLines(Path.Combine(harborDir, "harbor.stderr.log")).Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
            return (null, $"Harbor exited {result.ExitCode} without a trial: {stderr}");
        }
        return (trial, null);
    }
}
