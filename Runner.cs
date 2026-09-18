using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>runs/&lt;id&gt;/experiment.json: what is the same for every cell.</summary>
record Experiment(
    string Id, string Eval, string EvalHash, JsonObject EvalDef, List<string> Cases, int Planned,
    string Created, string CommandLine, string Host,
    Dictionary<string, string> Versions, GitInfo? Repo, ResolvedNb Nb);

record GitInfo(string Commit, bool Dirty);

record ResolvedNb(string Path, string? Config);

/// <summary>A cell's manifest.json.</summary>
record Manifest
{
    public required string RunId { get; init; }
    public required string Experiment { get; init; }
    public required string Arm { get; init; }
    public required string Case { get; init; }
    public required int Sample { get; init; }
    public required string Runner { get; init; }
    public required string Harness { get; init; }
    public required string Provider { get; init; }
    public string? Model { get; init; }
    public TranscriptRef Transcript { get; init; } = new("transcript.jsonl", "nb-jsonl");
    public string? Started { get; set; }
    public string? Ended { get; set; }
    public long? DurationMs { get; set; }
    public required string Host { get; init; }
    public required Dictionary<string, string> Versions { get; init; }
    public required string EvalHash { get; init; }
    public required string ProgramHash { get; init; }
    public string Status { get; set; } = CellStatus.Pending;
    public string? StatusReason { get; set; }
    public int Attempts { get; set; }
    public int? NbExitCode { get; set; }
}

record TranscriptRef(string File, string Format);

static class CellStatus
{
    public const string Pending = "pending", Running = "running", Completed = "completed", Failed = "failed", Skipped = "skipped";
    public static readonly string[] All = [Pending, Running, Completed, Failed, Skipped];
}

/// <summary>The matrix loop: arms, cases, samples; hooks at arm and sample level; nb as a subprocess.</summary>
sealed class Runner(string root, Experiment experiment, Eval eval, TextWriter log)
{
    readonly string experimentDir = Layout.Experiment(root, experiment.Id);
    readonly string evalsDir = Layout.Evals(root);

    /// <summary>Create a new experiment directory from an eval and run it.</summary>
    public static string Start(string root, Eval eval, ProctorConfig config, string? nbOverride, string commandLine, TextWriter log)
    {
        var nb = ResolveNb(root, config, nbOverride);
        var id = Layout.NewExperimentId(eval.Id, DateTime.UtcNow);
        var experiment = new Experiment(
            Id: id, Eval: eval.Id, EvalHash: eval.Hash,
            EvalDef: JsonNode.Parse(File.ReadAllText(Path.Combine(eval.Dir, Layout.EvalFile)))!.AsObject(),
            Cases: eval.Cases.Select(c => c.Id!).ToList(),
            Planned: eval.PlannedCells,
            Created: Now(), CommandLine: commandLine, Host: Environment.MachineName,
            Versions: Versions(nb.Path), Repo: GitInfo(root), Nb: nb);
        var dir = Layout.Experiment(root, id);
        Directory.CreateDirectory(dir);
        WriteJson(Path.Combine(dir, Layout.ExperimentFile), experiment);
        new Runner(root, experiment, eval, log).RunPending();
        return id;
    }

    /// <summary>Run whatever an existing experiment has not completed.</summary>
    public static void Resume(string root, string experimentId, string? nbOverride, TextWriter log)
    {
        var experiment = LoadExperiment(root, experimentId);
        var problems = new List<Problem>();
        var eval = Eval.Load(root, experiment.Eval, problems)
            ?? throw new ProctorException($"eval '{experiment.Eval}' no longer loads:\n" + string.Join("\n", problems));
        if (eval.Hash != experiment.EvalHash)
            throw new ProctorException($"evals/{eval.Id} has changed since experiment {experimentId} was created ({eval.Hash} vs {experiment.EvalHash}); a changed eval is a new experiment");
        if (nbOverride is not null) experiment = experiment with { Nb = experiment.Nb with { Path = Path.GetFullPath(nbOverride) } };
        new Runner(root, experiment, eval, log).RunPending();
    }

    public static Experiment LoadExperiment(string root, string experimentId)
    {
        var file = Path.Combine(Layout.Experiment(root, experimentId), Layout.ExperimentFile);
        if (!File.Exists(file)) throw new ProctorException($"no experiment {experimentId} under {Layout.Experiment(root, "")}");
        return JsonSerializer.Deserialize<Experiment>(File.ReadAllText(file), Eval.JsonOptions)!;
    }

    /// <summary>Every cell's coordinates, in declared order.</summary>
    public static IEnumerable<(Arm Arm, CaseDef Case, int Sample)> Cells(Eval eval) =>
        from arm in eval.Arms from c in eval.Cases from s in Enumerable.Range(1, arm.SamplesOrOne) select (arm, c, s);

    public static string ReadStatus(string cellDir)
    {
        var file = Path.Combine(cellDir, Layout.StatusFile);
        return File.Exists(file) ? File.ReadAllText(file).Trim() : CellStatus.Pending;
    }

    void RunPending()
    {
        WriteStatusCounts();
        foreach (var arm in eval.Arms)
        {
            var pending = eval.Cases.SelectMany(c => Enumerable.Range(1, arm.SamplesOrOne).Select(s => (c, s)))
                .Where(x => ReadStatus(Layout.Cell(experimentDir, arm.Id!, x.c.Id!, x.s)) != CellStatus.Completed).ToList();
            if (pending.Count == 0) continue;

            var armDir = Layout.Arm(experimentDir, arm.Id!);
            Directory.CreateDirectory(Path.Combine(armDir, Layout.HooksDir));
            var armEnv = new Dictionary<string, string> { ["PROCTOR_EXPERIMENT"] = experiment.Id, ["PROCTOR_ARM"] = arm.Id!, ["PROCTOR_EVAL_DIR"] = eval.Dir };
            var setup = RunHook(eval.Def.Hooks?.Arm?.Setup, armEnv, Path.Combine(armDir, Layout.HooksDir, "arm.setup.log"));
            if (setup is not null)
            {
                foreach (var (c, s) in pending) MarkFailed(arm, c, s, $"arm setup hook failed: {setup}");
                continue;
            }

            foreach (var (c, s) in pending) RunCell(arm, c, s);

            var teardown = RunHook(eval.Def.Hooks?.Arm?.Teardown, armEnv, Path.Combine(armDir, Layout.HooksDir, "arm.teardown.log"));
            if (teardown is not null) log.WriteLine($"  {arm.Id}: arm teardown hook failed: {teardown}");
        }
        WriteStatusCounts();
    }

    void RunCell(Arm arm, CaseDef c, int sample)
    {
        var cellDir = Layout.Cell(experimentDir, arm.Id!, c.Id!, sample);
        var workDir = Layout.Work(root, experiment.Id, arm.Id!, c.Id!, sample);
        Directory.CreateDirectory(Path.Combine(cellDir, Layout.HooksDir));
        Directory.CreateDirectory(workDir);
        var cell = new CellContext(eval.Dir, cellDir, workDir, experiment.Id, arm.Id!, c, sample);

        var program = ResolveProgram(eval.ProgramTemplate, arm, c, sample, workDir);
        var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestFile), Eval.JsonOptions)!
            : new Manifest
            {
                RunId = Layout.NewRunId(), Experiment = experiment.Id, Arm = arm.Id!, Case = c.Id!, Sample = sample,
                Runner = arm.Runner!, Harness = arm.Harness!, Provider = arm.Provider!, Model = arm.Model,
                Host = experiment.Host, Versions = experiment.Versions, EvalHash = eval.Hash, ProgramHash = Sha256(program),
            };
        manifest.Attempts++;
        manifest.Started = Now();
        manifest.Ended = null;
        manifest.DurationMs = null;
        manifest.StatusReason = null;
        SetStatus(cellDir, manifest, CellStatus.Running);
        File.WriteAllText(Path.Combine(cellDir, Layout.ProgramFile), program);

        var stopwatch = Stopwatch.StartNew();
        var env = cell.Environment();
        var failure = RunHook(eval.Def.Hooks?.Sample?.Setup, env, Path.Combine(cellDir, Layout.HooksDir, "sample.setup.log")) is { } setupError
            ? $"sample setup hook failed: {setupError}"
            : RunNb(cellDir, workDir, manifest);
        // Teardown runs whenever setup ran, so a failed run still collects what it can.
        if (failure is null || !failure.StartsWith("sample setup"))
            if (RunHook(eval.Def.Hooks?.Sample?.Teardown, env, Path.Combine(cellDir, Layout.HooksDir, "sample.teardown.log")) is { } teardownError)
                failure ??= $"sample teardown hook failed: {teardownError}";
        stopwatch.Stop();

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(cellDir, manifest, failure is null ? CellStatus.Completed : CellStatus.Failed);
        WriteStatusCounts();
        log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  {manifest.Status}  {(failure ?? ExitReasonOf(cellDir))}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>Spawn nb on the resolved program. Null on success; otherwise why the cell is `failed` (infrastructure only).</summary>
    string? RunNb(string cellDir, string workDir, Manifest manifest)
    {
        var args = new List<string> { "--output", "jsonl" };
        if (experiment.Nb.Config is not null) args.AddRange(["--config", experiment.Nb.Config]);
        args.Add(Path.Combine(cellDir, Layout.ProgramFile));
        var result = Subprocess.Run(experiment.Nb.Path, args, workDir,
            env: new Dictionary<string, string> { ["NO_COLOR"] = "1" },
            stdoutFile: Path.Combine(cellDir, Layout.TranscriptFile),
            stderrFile: Path.Combine(cellDir, Layout.StderrFile));
        if (!result.Started) return result.Stderr;
        manifest.NbExitCode = result.ExitCode;
        var trailer = Transcript.Read(cellDir).Trailer;
        if (trailer is null)
        {
            var stderr = File.ReadAllLines(Path.Combine(cellDir, Layout.StderrFile)).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return $"nb exited {result.ExitCode} without a result trailer: {stderr}";
        }
        return null;
    }

    /// <summary>Run a hook script if one is declared. Null on success, else a one-line reason. Output goes to the log file.</summary>
    string? RunHook(string? script, IDictionary<string, string> env, string logFile)
    {
        if (script is null) return null;
        var result = Subprocess.Run(Path.GetFullPath(Path.Combine(eval.Dir, script)), [], eval.Dir, env);
        File.WriteAllText(logFile, result.Stdout + (result.Stderr.Length > 0 ? "\n--- stderr ---\n" + result.Stderr : ""));
        if (!result.Started) return result.Stderr;
        return result.ExitCode == 0 ? null : $"{script} exited {result.ExitCode}: {result.FirstStderrLine}";
    }

    void MarkFailed(Arm arm, CaseDef c, int sample, string reason)
    {
        var cellDir = Layout.Cell(experimentDir, arm.Id!, c.Id!, sample);
        Directory.CreateDirectory(cellDir);
        var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestFile), Eval.JsonOptions)!
            : new Manifest
            {
                RunId = Layout.NewRunId(), Experiment = experiment.Id, Arm = arm.Id!, Case = c.Id!, Sample = sample,
                Runner = arm.Runner!, Harness = arm.Harness!, Provider = arm.Provider!, Model = arm.Model,
                Host = experiment.Host, Versions = experiment.Versions, EvalHash = eval.Hash, ProgramHash = "",
            };
        manifest.Attempts++;
        manifest.StatusReason = reason;
        SetStatus(cellDir, manifest, CellStatus.Failed);
        log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  failed  {reason}");
    }

    static void SetStatus(string cellDir, Manifest manifest, string status)
    {
        manifest.Status = status;
        File.WriteAllText(Path.Combine(cellDir, Layout.StatusFile), status + "\n");
        WriteJson(Path.Combine(cellDir, Layout.ManifestFile), manifest);
    }

    void WriteStatusCounts()
    {
        var counts = CellStatus.All.ToDictionary(s => s, _ => 0);
        foreach (var (arm, c, s) in Cells(eval)) counts[ReadStatus(Layout.Cell(experimentDir, arm.Id!, c.Id!, s))]++;
        WriteJson(Path.Combine(experimentDir, Layout.StatusCountsFile), new { planned = experiment.Planned, counts, updated = Now() });
    }

    static string? ExitReasonOf(string cellDir)
    {
        var file = Path.Combine(cellDir, Layout.TranscriptFile);
        return File.Exists(file) ? Transcript.Read(cellDir).Trailer?.ExitReason : null;
    }

    /// <summary>Fill the template. A prompt's newlines become nb continuation lines so the program stays one directive.</summary>
    public static string ResolveProgram(string template, Arm arm, CaseDef c, int sample, string workDir)
    {
        var values = new Dictionary<string, string>
        {
            ["prompt"] = c.Prompt!.Replace("\r\n", "\n").Replace("\n", " \\\n"),
            ["case"] = c.Id!,
            ["work"] = workDir,
            ["provider"] = arm.Provider ?? "",
            ["model"] = arm.Model ?? "",
            ["harness"] = arm.Harness ?? "",
            ["arm"] = arm.Id!,
            ["sample"] = sample.ToString(),
        };
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([^}]*?)\s*\}\}", m => values[m.Groups[1].Value]);
    }

    static ResolvedNb ResolveNb(string root, ProctorConfig config, string? nbOverride)
    {
        var nb = config.NbOrDefault;
        var evalsDir = Layout.Evals(root);
        var path = nbOverride ?? nb.Path;
        if (path.Contains(Path.DirectorySeparatorChar)) path = Path.GetFullPath(Path.Combine(nbOverride is null ? evalsDir : Directory.GetCurrentDirectory(), path));
        else if (FindOnPath(path) is { } found) path = found;
        else throw new ProctorException($"nb not found: '{path}' is not on PATH; set nb.path in evals/proctor.json or pass --nb");
        if (!File.Exists(path)) throw new ProctorException($"nb not found at {path}");
        var configPath = nb.Config is null ? null : Path.GetFullPath(Path.Combine(evalsDir, nb.Config));
        if (configPath is not null && !File.Exists(configPath)) throw new ProctorException($"nb config not found at {configPath} (evals/proctor.json nb.config)");
        return new ResolvedNb(path, configPath);
    }

    static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    static Dictionary<string, string> Versions(string nbPath)
    {
        var proctor = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";
        var dll = Path.ChangeExtension(nbPath, ".dll");
        var info = File.Exists(dll) ? FileVersionInfo.GetVersionInfo(dll) : null;
        var nb = new[] { info?.ProductVersion?.Split('+')[0], info?.FileVersion }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        return new() { ["proctor"] = proctor, ["nb"] = nb ?? "unknown" };
    }

    static GitInfo? GitInfo(string root)
    {
        var head = Subprocess.Run("git", ["rev-parse", "HEAD"], root);
        if (!head.Started || head.ExitCode != 0) return null;
        var status = Subprocess.Run("git", ["status", "--porcelain"], root);
        return new GitInfo(head.Stdout.Trim(), status.Stdout.Trim().Length > 0);
    }

    static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    static string Sha256(string text) =>
        "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    public static void WriteJson(string file, object value) =>
        File.WriteAllText(file, JsonSerializer.Serialize(value, value.GetType(), Eval.JsonOptions) + "\n");
}
