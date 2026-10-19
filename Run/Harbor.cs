namespace Proctor;

/// <summary>
/// An experiment's Harbor: the pinned release and the hash of the tasks compiled for it, which a resume must find
/// unchanged; with an nb arm, the nb layer its images copy (proctor.json harbor.nb, tagged by content).
/// </summary>
record ResolvedHarbor(string Version, string TasksHash, string? NbImage = null);

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
    /// nb's layer: the self-contained publish proctor.json harbor.nb names, as a local image holding only /opt/nb,
    /// tagged by the publish's content hash and built once. An nb arm's image copies it, so the binary that runs is the
    /// one hashed, and its version is asked of it here.
    /// </summary>
    public static (string Tag, string Version) NbImage(string publishDir)
    {
        var binary = Path.Combine(publishDir, "nb");
        if (!File.Exists(binary)) throw new ProctorException($"no nb binary in {publishDir} (suites/proctor.json harbor.nb): publish one with `dotnet publish nb.csproj -c Release -r linux-x64 --self-contained -o <dir>` in nb's repository");
        var asked = Subprocess.Run(binary, ["--version"], publishDir, new Dictionary<string, string> { ["NO_COLOR"] = "1" });
        if (!asked.Started || asked.ExitCode != 0) throw new ProctorException($"{binary} --version failed: {(asked.Started ? asked.FirstStderrLine : asked.Stderr)}");
        var tag = "proctor-nb:" + Tree.Hash(publishDir, new HashSet<string>()).Split(':')[1][..16];
        if (Subprocess.Run(Docker, ["image", "inspect", tag], publishDir) is not { Started: true, ExitCode: 0 })
        {
            var built = Subprocess.Run(Docker, ["build", "-q", "-t", tag, "-f", "-", "."], publishDir, stdin: "FROM scratch\nCOPY . /opt/nb\n");
            if (!built.Started || built.ExitCode != 0) throw new ProctorException($"could not build nb's layer {tag} from {publishDir}: {(built.Started ? built.FirstStderrLine : built.Stderr)}");
        }
        return (tag, asked.Stdout.Trim().Split('+')[0]);
    }

    const string Docker = "docker";

    /// <summary>Run the trial. Returns the trial directory, or why there is none (infrastructure: the cell fails).</summary>
    public static (string? TrialDir, string? Failure) Trial(string uv, string version, string cellDir, Arm arm, string taskDir, AgentRoute route, long wallMs, NbCell? nb = null)
    {
        var harborDir = Path.Combine(cellDir, Layout.HarborDir);
        if (Directory.Exists(harborDir)) Directory.Delete(harborDir, recursive: true);   // a resumed cell starts clean
        Directory.CreateDirectory(harborDir);
        var jobsDir = Path.Combine(harborDir, "jobs");
        Runner.WriteJson(Path.Combine(harborDir, Layout.HarborJobFile), HarborCompile.Job(arm, taskDir, jobsDir, route, nb));

        var env = new Dictionary<string, string> { ["HARBOR_TELEMETRY"] = "off", ["NO_COLOR"] = "1" };
        if (route is { KeyEnv: { } name, ApiKey: { } key }) env[name] = key;
        // nb's agent is proctor's own, imported from the experiment's harbor/agents/.
        if (nb is not null) env["PYTHONPATH"] = nb.AgentsDir;
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
