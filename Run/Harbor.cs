namespace Proctor;

/// <summary>
/// An experiment's Harbor: the pinned release and the hash of the tasks compiled for it, which a resume must find
/// unchanged; with an nb arm, the nb layer its images copy (proctor.json harbor.nb, tagged by content); the container
/// engine every cell runs on (proctor.json harbor.engine), which holds that layer.
/// </summary>
record ResolvedHarbor(string Version, string TasksHash, string? NbImage = null, string Engine = "docker");

/// <summary>
/// One cell's trial: Harbor's job file in the cell, then `uv tool run harbor@&lt;pin&gt; run` on it with telemetry off and
/// the model key in that one process's environment, never on disk. Harbor's job directory stays in the cell as the full
/// record; ingest lifts what proctor reads out of it.
/// </summary>
static class HarborRun
{
    /// <summary>What the image build, the verifier and Harbor's own start may add to the agent's wall limit before proctor stops waiting.</summary>
    static readonly TimeSpan Overhead = TimeSpan.FromSeconds(1800 + 900 + 600);

    /// <summary>
    /// The pin covers Harbor, not what it depends on (litellm is `&gt;=1.92.0`), so uv resolves the rest fresh each time
    /// and a poisoned release reaches the next run: litellm 1.107.0 (2026-10-21) was on PyPI for two hours. Hijacked
    /// releases are pulled within hours, so nothing younger than a week is a candidate. It applies to Harbor too: a pin
    /// bumped to a release under a week old fails to resolve until it ages.
    /// </summary>
    const string Cooldown = "7 days";

    /// <summary>uv's arguments for `harbor run` on the job file in the working directory.</summary>
    public static string[] UvArgs(string version) =>
        ["tool", "run", "--exclude-newer", Cooldown, $"harbor@{version}", "run", "--config", Layout.HarborJobFile];

    /// <summary>
    /// nb's layer: the self-contained publish proctor.json harbor.nb names, as a local image holding only /opt/nb,
    /// tagged by the publish's content hash and built once in the engine's store. An nb arm's image copies it, so the
    /// binary that runs is the one hashed, and its version is asked of it here. Podman keeps a local image under
    /// localhost/, so the tag says so rather than leave a build to resolve a short name.
    /// </summary>
    public static (string Tag, string Version) NbImage(string publishDir, string engine)
    {
        var binary = Path.Combine(publishDir, "nb");
        if (!File.Exists(binary)) throw new ProctorException($"no nb binary in {publishDir} (suites/proctor.json harbor.nb): publish one with `dotnet publish nb.csproj -c Release -r linux-x64 --self-contained -o <dir>` in nb's repository");
        var asked = Subprocess.Run(binary, ["--version"], publishDir, new Dictionary<string, string> { ["NO_COLOR"] = "1" });
        if (!asked.Started || asked.ExitCode != 0) throw new ProctorException($"{binary} --version failed: {(asked.Started ? asked.FirstStderrLine : asked.Stderr)}");
        var tag = (engine == "podman" ? "localhost/" : "") + "proctor-nb:" + Tree.Hash(publishDir, new HashSet<string>()).Split(':')[1][..16];
        if (Subprocess.Run(engine, ["image", "inspect", tag], publishDir) is not { Started: true, ExitCode: 0 })
        {
            var built = Subprocess.Run(engine, ["build", "-q", "-t", tag, "-f", "-", "."], publishDir, stdin: "FROM scratch\nCOPY . /opt/nb\n");
            if (!built.Started || built.ExitCode != 0) throw new ProctorException($"could not build nb's layer {tag} from {publishDir}: {(built.Started ? built.FirstStderrLine : built.Stderr)}");
        }
        return (tag, asked.Stdout.Trim().Split('+')[0]);
    }

    /// <summary>Run the trial. Returns the trial directory, or why there is none (infrastructure: the cell fails).</summary>
    public static (string? TrialDir, string? Failure) Trial(string uv, string version, string engine, string cellDir, Arm arm, string taskDir, AgentRoute route, long wallMs, string agentsDir, NbCell? nb = null)
    {
        var harborDir = Path.Combine(cellDir, Layout.HarborDir);
        if (Directory.Exists(harborDir)) Directory.Delete(harborDir, recursive: true);   // a resumed cell starts clean
        Directory.CreateDirectory(harborDir);
        var jobsDir = Path.Combine(harborDir, "jobs");
        Runner.WriteJson(Path.Combine(harborDir, Layout.HarborJobFile), HarborCompile.Job(arm, taskDir, jobsDir, route, nb, engine));

        var env = new Dictionary<string, string> { ["HARBOR_TELEMETRY"] = "off", ["NO_COLOR"] = "1" };
        if (route is { KeyEnv: { } name, ApiKey: { } key }) env[name] = key;
        // nb's and Qwen Code's agents are proctor's own, imported from the experiment's harbor/agents/.
        if (HarborCompile.IsOwnAgent(arm)) env["PYTHONPATH"] = agentsDir;
        var timeout = TimeSpan.FromMilliseconds(wallMs) + Overhead;
        var result = Subprocess.Run(uv, UvArgs(version), harborDir, env,
            stdoutFile: Path.Combine(harborDir, Layout.HarborLogFile), stderrFile: Path.Combine(harborDir, "harbor.stderr.log"), timeout: timeout);
        if (!result.Started) return (null, $"could not start Harbor: {result.Stderr} (suites/proctor.json harbor.uv)");
        if (result.TimedOut) return (null, $"Harbor did not finish within {timeout.TotalMinutes:0} minutes; killed (its containers may need `{engine} ps` and removing)");

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
