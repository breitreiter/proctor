using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>runs/&lt;id&gt;/experiment.json: what is the same for every cell.</summary>
record Experiment(
    string Id, string Suite, string SuiteHash, JsonObject SuiteDef, List<string> Tasks, int Planned,
    string Created, string CommandLine, string Host,
    Dictionary<string, string> Versions, GitInfo? Repo, ResolvedNb Nb,
    Dictionary<string, ResolvedBundle>? Bundles = null)
{
    public ResolvedBundle? BundleOf(string arm) => Bundles?.GetValueOrDefault(arm);
}

/// <summary>An arm's bundle as resolved for this experiment: where it was read from, the directory handed to the run, and its identity.</summary>
record ResolvedBundle(string Source, string Path, string Hash);

record GitInfo(string Commit, bool Dirty);

/// <summary>How nb is run: the host binary, its config, the runner script that stands in for the binary at the run step, if any, and where that runner shows nb the checkout and the bundle. Mounts are only in effect with a runner: a bare run resolves the host paths.</summary>
record ResolvedNb(string Path, string? Config, ResolvedRunner? Runner = null, NbMounts? Mounts = null);

/// <summary>The runner script as configured (relative to suites/), where it is, and the hash of its text.</summary>
record ResolvedRunner(string Script, string Path, string Hash);

/// <summary>A cell's manifest.json.</summary>
record Manifest
{
    public required string RunId { get; init; }
    public required string Experiment { get; init; }
    public required string Arm { get; init; }
    public required string Task { get; init; }
    public required int Sample { get; init; }
    public required string Runner { get; init; }
    public required string Harness { get; init; }
    public required string Provider { get; init; }
    public string? Model { get; init; }
    public FixtureRef? Fixture { get; init; }
    public ResolvedBundle? Bundle { get; init; }
    /// <summary>How nb was run for this cell: the host binary, its config and the runner script, if any.</summary>
    public ResolvedNb? Nb { get; init; }
    public TranscriptRef Transcript { get; init; } = new("transcript.jsonl", "nb-jsonl");
    public string? Started { get; set; }
    public string? Ended { get; set; }
    public long? DurationMs { get; set; }
    public required string Host { get; init; }
    public required Dictionary<string, string> Versions { get; init; }
    public required string SuiteHash { get; init; }
    public required string ProgramHash { get; init; }
    public string Status { get; set; } = CellStatus.Pending;
    public string? StatusReason { get; set; }
    public int Attempts { get; set; }
    public int? NbExitCode { get; set; }
}

record TranscriptRef(string File, string Format);

/// <summary>What a cell was run against: the fixture and the identity of its source tree.</summary>
record FixtureRef(string Id, string Hash);

static class CellStatus
{
    public const string Pending = "pending", Running = "running", Completed = "completed", Failed = "failed", Skipped = "skipped";
    public static readonly string[] All = [Pending, Running, Completed, Failed, Skipped];
}

/// <summary>The matrix loop: arms, tasks, samples; hooks at arm and sample level; nb as a subprocess.</summary>
sealed class Runner(string root, Experiment experiment, Suite suite, TextWriter log)
{
    readonly string experimentDir = Layout.Experiment(root, experiment.Id);
    readonly string suitesDir = Layout.Suites(root);

    /// <summary>Create a new experiment directory from a suite and run it.</summary>
    public static string Start(string root, Suite suite, ProctorConfig config, string? nbOverride, string? runnerOverride, string commandLine, TextWriter log)
    {
        var nb = ResolveNb(root, suite, config, nbOverride, runnerOverride);
        var id = Layout.NewExperimentId(suite.Id, DateTime.UtcNow);
        var experiment = new Experiment(
            Id: id, Suite: suite.Id, SuiteHash: suite.Hash,
            SuiteDef: JsonNode.Parse(File.ReadAllText(Layout.SuiteFileIn(suite.Dir)))!.AsObject(),
            Tasks: suite.Tasks.Select(c => c.Id!).ToList(),
            Planned: suite.PlannedCells,
            Created: Now(), CommandLine: commandLine, Host: Environment.MachineName,
            Versions: Versions(nb.Path), Repo: GitInfo(root), Nb: nb,
            Bundles: ResolveBundles(root, suite));
        var dir = Layout.Experiment(root, id);
        Directory.CreateDirectory(dir);
        WriteJson(Path.Combine(dir, Layout.ExperimentFile), experiment);
        new Runner(root, experiment, suite, log).RunPending();
        return id;
    }

    /// <summary>Run whatever an existing experiment has not completed.</summary>
    public static void Resume(string root, string experimentId, string? nbOverride, string? runnerOverride, TextWriter log)
    {
        var experiment = LoadExperiment(root, experimentId);
        var problems = new List<Problem>();
        var config = Suite.LoadConfig(root, problems);
        var suite = Suite.Load(root, experiment.Suite, problems)
            ?? throw new ProctorException($"suite '{experiment.Suite}' no longer loads:\n" + string.Join("\n", problems));
        if (suite.Hash != experiment.SuiteHash)
            throw new ProctorException($"suites/{suite.Id} has changed since experiment {experimentId} was created ({suite.Hash} vs {experiment.SuiteHash}); a changed suite is a new experiment");
        foreach (var (arm, bundle) in ResolveBundles(root, suite))
            if (experiment.BundleOf(arm)?.Hash != bundle.Hash)
                throw new ProctorException($"the bundle of arm '{arm}' ({bundle.Source}) has changed since experiment {experimentId} was created ({bundle.Hash} vs {experiment.BundleOf(arm)?.Hash ?? "none"}); a changed bundle is a new experiment");
        var runner = ResolveRunner(root, suite, runnerOverride);
        if ((runner?.Script, runner?.Hash) != (experiment.Nb.Runner?.Script, experiment.Nb.Runner?.Hash))
            throw new ProctorException($"the runner has changed since experiment {experimentId} was created ({Describe(runner)} vs {Describe(experiment.Nb.Runner)}); a changed runner is a new experiment");
        if (nbOverride is not null) experiment = experiment with { Nb = experiment.Nb with { Path = Path.GetFullPath(nbOverride) } };
        new Runner(root, experiment, suite, log).RunPending();

        static string Describe(ResolvedRunner? r) => r is null ? "none" : $"{r.Script} {r.Hash}";
    }

    public static Experiment LoadExperiment(string root, string experimentId)
    {
        var file = Path.Combine(Layout.Experiment(root, experimentId), Layout.ExperimentFile);
        if (!File.Exists(file)) throw new ProctorException($"no experiment {experimentId} under {Layout.Experiment(root, "")}");
        return ReadJson<Experiment>(file, ("eval", "suite"), ("eval_hash", "suite_hash"), ("eval_def", "suite_def"), ("cases", "tasks"))!;
    }

    /// <summary>Every cell's coordinates, in declared order.</summary>
    public static IEnumerable<(Arm Arm, TaskDef Task, int Sample)> Cells(Suite suite) =>
        from arm in suite.Arms from c in suite.Tasks from s in Enumerable.Range(1, arm.SamplesOrOne) select (arm, c, s);

    /// <summary>Everything a hook or check needs to know about a cell, for the runner and the grader alike.</summary>
    public static CellContext Context(string root, Experiment experiment, Suite suite, Arm arm, TaskDef c, int sample) =>
        new(suite.Dir, Layout.Cell(Layout.Experiment(root, experiment.Id), arm.Id!, c.Id!, sample), Layout.Work(root, experiment.Id, arm.Id!, c.Id!, sample), experiment.Id, arm.Id!, c, sample)
        { Expect = suite.ExpectFor(c), Fixture = suite.FixtureOf(c), BundleDir = experiment.BundleOf(arm.Id!)?.Path, NbRunner = experiment.Nb.Runner?.Script ?? "", NbPath = experiment.Nb.Path, NbConfig = experiment.Nb.Config, Mounts = experiment.Nb.Mounts };

    /// <summary>Each arm's bundle, once per experiment: a path is hashed in place; a git revision is cloned under .proctor/bundles/ and identified by its revision.</summary>
    public static Dictionary<string, ResolvedBundle> ResolveBundles(string root, Suite suite)
    {
        var bundles = new Dictionary<string, ResolvedBundle>();
        foreach (var arm in suite.Arms.Where(a => a.Bundle is not null))
        {
            var source = arm.Bundle!;
            if (source.Git is { } url)
            {
                var dir = Layout.GitBundle(root, source.Rev!);
                if (!Directory.Exists(Path.Combine(dir, ".git")))
                {
                    if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
                    Directory.CreateDirectory(dir);
                    foreach (var args in new[] { new[] { "clone", "-q", url, "." }, new[] { "checkout", "-q", source.Rev! } })
                    {
                        var result = Subprocess.Run("git", args, dir);
                        if (!result.Started || result.ExitCode != 0)
                            throw new ProctorException($"bundle of arm '{arm.Id}': git {args[0]} {url} failed: {(result.Started ? result.FirstStderrLine : result.Stderr)}");
                    }
                }
                bundles[arm.Id!] = new ResolvedBundle(source.Describe(), dir, $"git:{source.Rev}");
            }
            else
            {
                var dir = Path.GetFullPath(Path.Combine(root, source.Path!));
                bundles[arm.Id!] = new ResolvedBundle(source.Describe(), dir, Tree.Hash(dir, new HashSet<string> { ".git" }));
            }
        }
        return bundles;
    }

    public static string ReadStatus(string cellDir)
    {
        var file = Path.Combine(cellDir, Layout.StatusFile);
        return File.Exists(file) ? File.ReadAllText(file).Trim() : CellStatus.Pending;
    }

    void RunPending()
    {
        WriteStatusCounts();
        foreach (var arm in suite.Arms)
        {
            var pending = suite.Tasks.SelectMany(c => Enumerable.Range(1, arm.SamplesOrOne).Select(s => (c, s)))
                .Where(x => ReadStatus(Layout.Cell(experimentDir, arm.Id!, x.c.Id!, x.s)) != CellStatus.Completed).ToList();
            if (pending.Count == 0) continue;

            var armDir = Layout.Arm(experimentDir, arm.Id!);
            Directory.CreateDirectory(Path.Combine(armDir, Layout.HooksDir));
            var armEnv = new Dictionary<string, string> { ["PROCTOR_EXPERIMENT"] = experiment.Id, ["PROCTOR_ARM"] = arm.Id!, ["PROCTOR_SUITE_DIR"] = suite.Dir, ["PROCTOR_EVAL_DIR"] = suite.Dir, ["PROCTOR_BUNDLE"] = experiment.BundleOf(arm.Id!)?.Path ?? "", ["PROCTOR_RUNNER"] = experiment.Nb.Runner?.Script ?? "" };
            var setup = RunHook(suite.Def.Hooks?.Arm?.Setup, armEnv, Path.Combine(armDir, Layout.HooksDir, "arm.setup.log"));
            if (setup is not null)
            {
                foreach (var (c, s) in pending) MarkFailed(arm, c, s, $"arm setup hook failed: {setup}");
                continue;
            }

            foreach (var (c, s) in pending) RunCell(arm, c, s);

            var teardown = RunHook(suite.Def.Hooks?.Arm?.Teardown, armEnv, Path.Combine(armDir, Layout.HooksDir, "arm.teardown.log"));
            if (teardown is not null) log.WriteLine($"  {arm.Id}: arm teardown hook failed: {teardown}");
        }
        WriteStatusCounts();
    }

    void RunCell(Arm arm, TaskDef c, int sample)
    {
        var cell = Context(root, experiment, suite, arm, c, sample);
        var (cellDir, workDir) = (cell.CellDir, cell.WorkDir);
        Directory.CreateDirectory(Path.Combine(cellDir, Layout.HooksDir));
        Directory.CreateDirectory(workDir);

        var program = ResolveProgram(suite.ProgramTemplate, arm, c, sample, cell.WorkMount, cell.BundleMount);
        var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                RunId = Layout.NewRunId(), Experiment = experiment.Id, Arm = arm.Id!, Task = c.Id!, Sample = sample,
                Runner = arm.Runner!, Harness = arm.Harness!, Provider = arm.Provider!, Model = arm.Model,
                Fixture = cell.Fixture is null ? null : new FixtureRef(cell.Fixture.Id, cell.Fixture.Hash),
                Bundle = experiment.BundleOf(arm.Id!), Nb = experiment.Nb,
                Host = experiment.Host, Versions = experiment.Versions, SuiteHash = suite.Hash, ProgramHash = Sha256(program),
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
        // The program is compiled on the host first, then the fixture is checked out, then the setup hook, nb, the teardown hook, and the diff is collected.
        var (compiled, compileError) = Compile(cellDir, program);
        var failure = compileError is not null ? $"program compile failed: {compileError}"
            : cell.Fixture is not null && Checkout.Materialise(cell.Fixture, workDir) is { } checkoutError ? $"fixture checkout failed: {checkoutError}"
            : null;
        var setupRan = failure is null;
        if (setupRan)
            failure = RunHook(suite.Def.Hooks?.Sample?.Setup, env, Path.Combine(cellDir, Layout.HooksDir, "sample.setup.log")) is { } setupError
                ? $"sample setup hook failed: {setupError}"
                : RunNb(cellDir, workDir, compiled!, env, manifest);
        // Teardown and the diff run whenever setup ran, even when setup failed: a container it half-made must not be left for the resumed cell.
        if (setupRan)
        {
            if (RunHook(suite.Def.Hooks?.Sample?.Teardown, env, Path.Combine(cellDir, Layout.HooksDir, "sample.teardown.log")) is { } teardownError)
                failure ??= $"sample teardown hook failed: {teardownError}";
            if (cell.Fixture is not null && Checkout.CollectDiff(workDir, cellDir) is { } diffError)
                failure ??= $"diff collection failed: {diffError}";
        }
        stopwatch.Stop();

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(cellDir, manifest, failure is null ? CellStatus.Completed : CellStatus.Failed);
        WriteStatusCounts();
        log.WriteLine($"  {arm.Id}/{c.Id}/{sample}  {manifest.Status}  {(failure ?? ExitReasonOf(cellDir))}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// `nb --compile` on the host binary, in the suite directory so `@file` includes resolve against the suite: the
    /// JSONL that goes down stdin, with every include inlined, so nothing nb runs on names a file the runner would
    /// have to carry. Written beside the source as program.jsonl. The program hash stays the source's. A program
    /// nb refuses fails here, on the host, before a checkout or a container exists for it.
    /// </summary>
    (string? Compiled, string? Error) Compile(string cellDir, string program)
    {
        var nb = experiment.Nb;
        var args = new List<string> { "--compile" };
        if (nb.Config is not null) args.AddRange(["--config", nb.Config]);
        var result = Subprocess.Run(nb.Path, args, suite.Dir, new Dictionary<string, string> { ["NO_COLOR"] = "1" }, stdin: program);
        if (!result.Started) return (null, result.Stderr);
        if (result.ExitCode != 0) return (null, $"nb --compile exited {result.ExitCode}: {result.FirstStderrLine}");
        File.WriteAllText(Path.Combine(cellDir, Layout.CompiledProgramFile), result.Stdout);
        return (result.Stdout, null);
    }

    /// <summary>
    /// Run nb on the compiled program, which travels on stdin. Bare, that is nb itself with the argv the runner
    /// contract names; with a runner, it is the script with nothing on argv and the cell environment, which must
    /// start nb the same way wherever it runs. Null on success; otherwise why the cell is `failed` (infrastructure only).
    /// </summary>
    string? RunNb(string cellDir, string workDir, string program, IDictionary<string, string> cellEnv, Manifest manifest)
    {
        var nb = experiment.Nb;
        var args = new List<string> { "--output", "jsonl" };
        if (nb.Config is not null) args.AddRange(["--config", nb.Config]);
        args.Add("-");
        var env = nb.Runner is null
            ? new Dictionary<string, string> { ["NO_COLOR"] = "1" }
            : new Dictionary<string, string>(cellEnv) { ["NO_COLOR"] = "1" };
        var result = Subprocess.Run(nb.Runner?.Path ?? nb.Path, nb.Runner is null ? args : [], workDir, env,
            stdoutFile: Path.Combine(cellDir, Layout.TranscriptFile),
            stderrFile: Path.Combine(cellDir, Layout.StderrFile),
            stdin: program);
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
        var result = Subprocess.Run(Path.GetFullPath(Path.Combine(suite.Dir, script)), [], suite.Dir, env);
        File.WriteAllText(logFile, result.Stdout + (result.Stderr.Length > 0 ? "\n--- stderr ---\n" + result.Stderr : ""));
        if (!result.Started) return result.Stderr;
        return result.ExitCode == 0 ? null : $"{script} exited {result.ExitCode}: {result.FirstStderrLine}";
    }

    void MarkFailed(Arm arm, TaskDef c, int sample, string reason)
    {
        var cellDir = Layout.Cell(experimentDir, arm.Id!, c.Id!, sample);
        Directory.CreateDirectory(cellDir);
        var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                RunId = Layout.NewRunId(), Experiment = experiment.Id, Arm = arm.Id!, Task = c.Id!, Sample = sample,
                Runner = arm.Runner!, Harness = arm.Harness!, Provider = arm.Provider!, Model = arm.Model,
                Host = experiment.Host, Versions = experiment.Versions, SuiteHash = suite.Hash, ProgramHash = "",
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
        foreach (var (arm, c, s) in Cells(suite)) counts[ReadStatus(Layout.Cell(experimentDir, arm.Id!, c.Id!, s))]++;
        WriteJson(Path.Combine(experimentDir, Layout.StatusCountsFile), new { planned = experiment.Planned, counts, updated = Now() });
    }

    static string? ExitReasonOf(string cellDir)
    {
        var file = Path.Combine(cellDir, Layout.TranscriptFile);
        return File.Exists(file) ? Transcript.Read(cellDir).Trailer?.ExitReason : null;
    }

    /// <summary>Fill the template. {{work}} and {{bundle}} are the paths the model will see, which a runner may mount elsewhere. A prompt's newlines become nb continuation lines so the program stays one directive.</summary>
    public static string ResolveProgram(string template, Arm arm, TaskDef c, int sample, string workDir, string? bundleDir = null)
    {
        var values = new Dictionary<string, string>
        {
            ["prompt"] = c.Prompt!.Replace("\r\n", "\n").Replace("\n", " \\\n"),
            ["task"] = c.Id!,
            ["case"] = c.Id!,
            ["work"] = workDir,
            ["bundle"] = bundleDir ?? "",
            ["provider"] = arm.Provider ?? "",
            ["model"] = arm.Model ?? "",
            ["harness"] = arm.Harness ?? "",
            ["arm"] = arm.Id!,
            ["sample"] = sample.ToString(),
        };
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([^}]*?)\s*\}\}", m => values[m.Groups[1].Value]);
    }

    static ResolvedNb ResolveNb(string root, Suite suite, ProctorConfig config, string? nbOverride, string? runnerOverride)
    {
        var nb = config.NbOrDefault;
        var suitesDir = Layout.Suites(root);
        var path = nbOverride ?? nb.Path;
        if (path.Contains(Path.DirectorySeparatorChar)) path = Path.GetFullPath(Path.Combine(nbOverride is null ? suitesDir : Directory.GetCurrentDirectory(), path));
        else if (FindOnPath(path) is { } found) path = found;
        else throw new ProctorException($"nb not found: '{path}' is not on PATH; set nb.path in suites/proctor.json or pass --nb");
        if (!File.Exists(path)) throw new ProctorException($"nb not found at {path}");
        var configPath = nb.Config is null ? null : Path.GetFullPath(Path.Combine(suitesDir, nb.Config));
        if (configPath is not null && !File.Exists(configPath)) throw new ProctorException($"nb config not found at {configPath} (suites/proctor.json nb.config)");
        var runner = ResolveRunner(root, suite, runnerOverride);
        return new ResolvedNb(path, configPath, runner, runner is null ? null : suite.Def.Nb?.Mounts);
    }

    /// <summary>The runner in effect: --runner (from the current directory) over the suite's nb.runner (from the suite directory); `--runner none` is a bare run. Recorded relative to suites/ when it lives there, else absolute, so a resume can compare it.</summary>
    static ResolvedRunner? ResolveRunner(string root, Suite suite, string? runnerOverride)
    {
        var suitesDir = Layout.Suites(root);
        var script = runnerOverride ?? suite.Def.Nb?.Runner;
        if (script is null || runnerOverride == "none") return null;
        var path = Path.GetFullPath(Path.Combine(runnerOverride is null ? suite.Dir : Directory.GetCurrentDirectory(), script));
        if (!File.Exists(path)) throw new ProctorException($"runner not found at {path} ({(runnerOverride is null ? $"suites/{suite.Id}/suite.json nb.runner" : "--runner")})");
        var relative = Path.GetRelativePath(suitesDir, path);
        return new ResolvedRunner(relative.StartsWith("..") ? path : relative, path, Sha256(File.ReadAllText(path)));
    }

    static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    /// <summary>Proctor's own version and nb's, asked of the host binary so a wrapper or a self-contained publish reports the same as the DLL would.</summary>
    /// <summary>Proctor's own version, from the csproj, without the +commit suffix; what --version prints and every experiment records.</summary>
    public static string ProctorVersion =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0";

    static Dictionary<string, string> Versions(string nbPath)
    {
        var proctor = ProctorVersion;
        var result = Subprocess.Run(nbPath, ["--version"], Path.GetDirectoryName(nbPath)!, new Dictionary<string, string> { ["NO_COLOR"] = "1" });
        var nb = result.Started && result.ExitCode == 0 ? result.Stdout.Trim().Split('+')[0] : "";
        return new() { ["proctor"] = proctor, ["nb"] = nb.Length > 0 ? nb : "unknown" };
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
        File.WriteAllText(file, JsonSerializer.Serialize(value, value.GetType(), Suite.JsonOptions) + "\n");

    /// <summary>A cell's manifest, with the keys a cell written before suite/task used.</summary>
    public static Manifest ReadManifest(string file) => ReadJson<Manifest>(file, ("case", "task"), ("eval_hash", "suite_hash"))!;

    /// <summary>
    /// Read a JSON file into a record, renaming top-level keys written under an older spelling first. Files under runs/
    /// and a pinned baseline outlive a rename, so every reader of one accepts both spellings for a release.
    /// </summary>
    public static T? ReadJson<T>(string file, params (string Old, string New)[] legacy) where T : class
    {
        var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
        if (node is null) return null;
        foreach (var (old, @new) in legacy)
            if (node.ContainsKey(old) && !node.ContainsKey(@new)) { node[@new] = node[old]!.DeepClone(); node.Remove(old); }
        return node.Deserialize<T>(Suite.JsonOptions);
    }
}
