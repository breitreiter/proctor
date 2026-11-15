using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

/// <summary>runs/&lt;id&gt;/experiment.json: what is the same for every cell.</summary>
record Experiment(
    string Id, string Exam, string ExamHash, JsonObject ExamDef, List<string> Tasks, int Planned,
    string Created, string CommandLine, string Host,
    Dictionary<string, string> Versions, GitInfo? Repo, ResolvedNb Nb,
    Dictionary<string, ResolvedBundle>? Bundles = null, ResolvedHarbor? Harbor = null)
{
    public ResolvedBundle? BundleOf(string variant) => Bundles?.GetValueOrDefault(variant);
}

/// <summary>A variant's bundle as resolved for this experiment: where it was read from, the directory handed to the run, and its identity.</summary>
record ResolvedBundle(string Source, string Path, string Hash);

record GitInfo(string Commit, bool Dirty);

/// <summary>How nb is run: the host binary, its config, the runner script that stands in for the binary at the run step, if any, and where that runner shows nb the checkout and the bundle. Mounts are only in effect with a runner: a bare run resolves the host paths.</summary>
record ResolvedNb(string Path, string? Config, ResolvedRunner? Runner = null, NbMounts? Mounts = null);

/// <summary>The runner script as configured (relative to exams/), where it is, and the hash of its text.</summary>
record ResolvedRunner(string Script, string Path, string Hash);

/// <summary>A cell's manifest.json.</summary>
record Manifest
{
    public required string RunId { get; init; }
    public required string Experiment { get; init; }
    public required string Variant { get; init; }
    public required string Task { get; init; }
    public required int Sample { get; init; }
    /// <summary>How an nb variant ran; absent on an agent variant, whose run is <see cref="Agent"/>.</summary>
    public string? Runner { get; init; }
    public string? Harness { get; init; }
    public string? Provider { get; init; }
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
    public required string ExamHash { get; init; }
    public required string ProgramHash { get; init; }
    public string Status { get; set; } = CellStatus.Pending;
    public string? StatusReason { get; set; }
    public int Tries { get; set; }
    public int? NbExitCode { get; set; }
    /// <summary>An agent variant's run: the agent as the trial reported it, its settings, the Harbor that ran it, and what it used.</summary>
    public AgentRun? Agent { get; set; }
    /// <summary>An oracle cell's simulated user on the Harbor path (<see cref="Ingest"/>); absent on the bare path, where nb's oracle is in the run.</summary>
    public OracleRun? Oracle { get; set; }
}

record TranscriptRef(string File, string Format);

/// <summary>What a cell was run against: the fixture and the identity of its source tree.</summary>
record FixtureRef(string Id, string Hash);

static class CellStatus
{
    public const string Pending = "pending", Running = "running", Completed = "completed", Failed = "failed", Skipped = "skipped";
    public static readonly string[] All = [Pending, Running, Completed, Failed, Skipped];
}

/// <summary>The matrix loop: variants, tasks, samples; hooks at variant and sample level; nb as a subprocess.</summary>
sealed class Runner(string root, Experiment experiment, Exam exam, ProctorConfig config, TextWriter log)
{
    readonly string experimentDir = Layout.Experiment(root, experiment.Id);
    readonly string examsDir = Layout.Exams(root);

    /// <summary>Create a new experiment directory from an exam and run it.</summary>
    public static string Start(string root, Exam exam, ProctorConfig config, string? nbOverride, string? runnerOverride, string commandLine, TextWriter log)
    {
        var nb = ResolveNb(root, exam, config, nbOverride, runnerOverride);
        foreach (var variant in exam.Variants.Where(a => a.IsAgent)) Route(root, config, nb, variant);   // a missing endpoint fails before anything is made
        var id = Layout.NewExperimentId(exam.Id, DateTime.UtcNow);
        var experiment = new Experiment(
            Id: id, Exam: exam.Id, ExamHash: exam.Hash,
            ExamDef: JsonNode.Parse(File.ReadAllText(Layout.ExamFileIn(exam.Dir)))!.AsObject(),
            Tasks: exam.Tasks.Select(c => c.Id!).ToList(),
            Planned: exam.PlannedCells,
            Created: Now(), CommandLine: commandLine, Host: Environment.MachineName,
            Versions: Versions(nb.Path), Repo: GitInfo(root), Nb: nb,
            Bundles: ResolveBundles(root, exam));
        if (exam.HasAgentVariants)
        {
            HarborRun.Preflight(config.HarborOrDefault);
            if (config.HarborOrDefault.Remote) RequireReachable(exam, config, variant => Route(root, config, nb, variant));
        }
        var dir = Layout.Experiment(root, id);
        Directory.CreateDirectory(dir);
        if (exam.HasAgentVariants)
        {
            var version = exam.Def.Harbor!.Version!;
            var engine = config.HarborOrDefault.Engine;
            var nbImage = exam.Variants.Any(a => a.IsAgent && !a.IsVendor) ? NbImage(root, config, experiment.Versions["nb"]) : null;
            experiment = experiment with
            {
                Versions = new(experiment.Versions) { ["harbor"] = version },
                Harbor = new ResolvedHarbor(version, HarborCompile.Tasks(dir, exam, id, variant => Route(root, config, nb, variant), nbImage, nbImage is not null && config.HarborOrDefault.Remote ? NbPublish(root, config) : null, experiment.Bundles), nbImage, engine),
            };
        }
        WriteJson(Path.Combine(dir, Layout.ExperimentFile), experiment);
        new Runner(root, experiment, exam, config, log).RunPending();
        return id;
    }

    /// <summary>Run whatever an existing experiment has not completed.</summary>
    public static void Resume(string root, string experimentId, string? nbOverride, string? runnerOverride, TextWriter log, string? configFile = null)
    {
        var experiment = LoadExperiment(root, experimentId);
        var problems = new List<Problem>();
        var config = Exam.LoadConfig(root, problems, configFile);
        var exam = Exam.Load(root, experiment.Exam, problems)
            ?? throw new ProctorException($"exam '{experiment.Exam}' no longer loads:\n" + string.Join("\n", problems));
        if (exam.Hash != experiment.ExamHash)
            throw new ProctorException($"exams/{exam.Id} has changed since experiment {experimentId} was created ({exam.Hash} vs {experiment.ExamHash}); a changed exam is a new experiment");
        foreach (var (variant, bundle) in ResolveBundles(root, exam))
            if (experiment.BundleOf(variant)?.Hash != bundle.Hash)
                throw new ProctorException($"the bundle of variant '{variant}' ({bundle.Source}) has changed since experiment {experimentId} was created ({bundle.Hash} vs {experiment.BundleOf(variant)?.Hash ?? "none"}); a changed bundle is a new experiment");
        var runner = ResolveRunner(root, exam, runnerOverride);
        if ((runner?.Script, runner?.Hash) != (experiment.Nb.Runner?.Script, experiment.Nb.Runner?.Hash))
            throw new ProctorException($"the runner has changed since experiment {experimentId} was created ({Describe(runner)} vs {Describe(experiment.Nb.Runner)}); a changed runner is a new experiment");
        if (experiment.Harbor is { } harbor && HarborCompile.TreeHash(Layout.Experiment(root, experimentId)) != harbor.TasksHash)
            throw new ProctorException($"the Harbor tasks compiled for experiment {experimentId} have changed on disk ({Layout.HarborDir}/tasks); a changed task is a new experiment");
        if (experiment.Harbor is { } recorded && config.HarborOrDefault.Engine is var engine && engine != recorded.Engine)
            throw new ProctorException($"the engine ({config.Source} harbor.engine) is {engine}, and experiment {experimentId} was created on {recorded.Engine}: resume with the --config it was run with; a changed engine is a new experiment");
        // An experiment from before the layer left the local store recorded podman's tag with its localhost/ prefix; the hash is what identifies the publish.
        if (experiment.Harbor?.NbImage is { } image && NbImage(root, config, experiment.Versions["nb"]) is var now && now.Replace("localhost/", "") != image.Replace("localhost/", ""))
            throw new ProctorException($"nb's publish ({config.Source} harbor.nb) has changed since experiment {experimentId} was created ({now} vs {image}); a changed nb is a new experiment");
        if (experiment.Harbor is not null) HarborRun.Preflight(config.HarborOrDefault);
        if (nbOverride is not null) experiment = experiment with { Nb = experiment.Nb with { Path = Path.GetFullPath(nbOverride) } };
        new Runner(root, experiment, exam, config, log).RunPending();

        static string Describe(ResolvedRunner? r) => r is null ? "none" : $"{r.Script} {r.Hash}";
    }

    public static Experiment LoadExperiment(string root, string experimentId)
    {
        var file = Path.Combine(Layout.Experiment(root, experimentId), Layout.ExperimentFile);
        if (!File.Exists(file)) throw new ProctorException($"no experiment {experimentId} under {Layout.Experiment(root, "")}");
        return ReadJson<Experiment>(file)!;
    }

    /// <summary>Every cell's coordinates, in declared order.</summary>
    public static IEnumerable<(Variant Variant, TaskDef Task, int Sample)> Cells(Exam exam) =>
        from variant in exam.Variants from c in exam.Tasks from s in Enumerable.Range(1, variant.SamplesOrOne) select (variant, c, s);

    /// <summary>Everything a hook or check needs to know about a cell, for the runner and the grader alike.</summary>
    public static CellContext Context(string root, Experiment experiment, Exam exam, Variant variant, TaskDef c, int sample) =>
        new(exam.Dir, Layout.Cell(Layout.Experiment(root, experiment.Id), variant.Id!, c.Id!, sample), Layout.Work(root, experiment.Id, variant.Id!, c.Id!, sample), experiment.Id, variant.Id!, c, sample)
        { Expect = exam.ExpectFor(c), Fixture = exam.FixtureOf(c), BundleDir = experiment.BundleOf(variant.Id!)?.Path, NbRunner = experiment.Nb.Runner?.Script ?? "", NbPath = experiment.Nb.Path, NbConfig = experiment.Nb.Config, Mounts = experiment.Nb.Mounts, ScriptsInVerifier = variant.IsAgent, Vendor = variant.IsVendor };

    /// <summary>Each variant's bundle, once per experiment: a path is hashed in place; a git revision is cloned under .proctor/bundles/ and identified by its revision.</summary>
    public static Dictionary<string, ResolvedBundle> ResolveBundles(string root, Exam exam)
    {
        var bundles = new Dictionary<string, ResolvedBundle>();
        foreach (var variant in exam.Variants.Where(a => a.Bundle is not null))
        {
            var source = variant.Bundle!;
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
                            throw new ProctorException($"bundle of variant '{variant.Id}': git {args[0]} {url} failed: {(result.Started ? result.FirstStderrLine : result.Stderr)}");
                    }
                }
                bundles[variant.Id!] = new ResolvedBundle(source.Describe(), dir, $"git:{source.Rev}");
            }
            else
            {
                var dir = Path.GetFullPath(Path.Combine(root, source.Path!));
                bundles[variant.Id!] = new ResolvedBundle(source.Describe(), dir, Tree.Hash(dir, new HashSet<string> { ".git" }));
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
        foreach (var variant in exam.Variants)
        {
            var pending = exam.Tasks.SelectMany(c => Enumerable.Range(1, variant.SamplesOrOne).Select(s => (c, s)))
                .Where(x => ReadStatus(Layout.Cell(experimentDir, variant.Id!, x.c.Id!, x.s)) != CellStatus.Completed).ToList();
            if (pending.Count == 0) continue;

            var variantDir = Layout.Variant(experimentDir, variant.Id!);
            Directory.CreateDirectory(Path.Combine(variantDir, Layout.HooksDir));
            var variantEnv = new Dictionary<string, string> { ["PROCTOR_EXPERIMENT"] = experiment.Id, ["PROCTOR_VARIANT"] = variant.Id!, ["PROCTOR_EXAM_DIR"] = exam.Dir, ["PROCTOR_BUNDLE"] = experiment.BundleOf(variant.Id!)?.Path ?? "", ["PROCTOR_RUNNER"] = experiment.Nb.Runner?.Script ?? "" };
            var setup = RunHook(exam.Def.Hooks?.Variant?.Setup, variantEnv, Path.Combine(variantDir, Layout.HooksDir, "variant.setup.log"));
            if (setup is not null)
            {
                foreach (var (c, s) in pending) MarkFailed(variant, c, s, $"variant setup hook failed: {setup}");
                continue;
            }

            foreach (var (c, s) in pending)
                if (variant.IsAgent) RunAgentCell(variant, c, s);
                else RunCell(variant, c, s);

            var teardown = RunHook(exam.Def.Hooks?.Variant?.Teardown, variantEnv, Path.Combine(variantDir, Layout.HooksDir, "variant.teardown.log"));
            if (teardown is not null) log.WriteLine($"  {variant.Id}: variant teardown hook failed: {teardown}");
        }
        WriteStatusCounts();
    }

    void RunCell(Variant variant, TaskDef c, int sample)
    {
        var cell = Context(root, experiment, exam, variant, c, sample);
        var (cellDir, workDir) = (cell.CellDir, cell.WorkDir);
        Directory.CreateDirectory(Path.Combine(cellDir, Layout.HooksDir));
        Directory.CreateDirectory(workDir);

        var program = OraclePrefix(exam, c) + ResolveProgram(exam.ProgramTemplate, variant, c, sample, cell.WorkMount, cell.BundleMount);
        var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                RunId = Layout.NewRunId(), Experiment = experiment.Id, Variant = variant.Id!, Task = c.Id!, Sample = sample,
                Runner = variant.Runner, Harness = variant.Harness, Provider = variant.Provider, Model = variant.Model,
                Fixture = cell.Fixture is null ? null : new FixtureRef(cell.Fixture.Id, cell.Fixture.Hash),
                Bundle = experiment.BundleOf(variant.Id!), Nb = experiment.Nb,
                Host = experiment.Host, Versions = experiment.Versions, ExamHash = exam.Hash, ProgramHash = Sha256(program),
            };
        manifest.Tries++;
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
            failure = RunHook(exam.Def.Hooks?.Sample?.Setup, env, Path.Combine(cellDir, Layout.HooksDir, "sample.setup.log")) is { } setupError
                ? $"sample setup hook failed: {setupError}"
                : RunNb(cellDir, workDir, compiled!, env, manifest);
        // Teardown and the diff run whenever setup ran, even when setup failed: a container it half-made must not be left for the resumed cell.
        if (setupRan)
        {
            if (RunHook(exam.Def.Hooks?.Sample?.Teardown, env, Path.Combine(cellDir, Layout.HooksDir, "sample.teardown.log")) is { } teardownError)
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
        log.WriteLine($"  {variant.Id}/{c.Id}/{sample}  {manifest.Status}  {(failure ?? ExitReasonOf(cellDir))}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// An agent variant's cell: one Harbor trial of the task compiled for the variant, then ingest. The checkout, the setup and
    /// the diff are the image's and Harbor's; variant hooks still ran on the host around the variant's cells. The variant's bundle is
    /// the host's too: recorded here and handed to those hooks, never put in the container.
    /// </summary>
    void RunAgentCell(Variant variant, TaskDef c, int sample)
    {
        var cellDir = Layout.Cell(experimentDir, variant.Id!, c.Id!, sample);
        Directory.CreateDirectory(cellDir);
        var taskDir = Layout.HarborTask(experimentDir, variant.Id!, c.Id!);
        var route = Route(root, config, experiment.Nb, variant);
        var fixture = exam.FixtureOf(c)!;
        // nb's program is compiled on the host as on the bare path, with the checkout where the image has it.
        var program = variant.IsVendor ? null : ResolveProgram(exam.ProgramTemplate, variant, c, sample, HarborCompile.Workdir);
        var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                RunId = Layout.NewRunId(), Experiment = experiment.Id, Variant = variant.Id!, Task = c.Id!, Sample = sample, Model = variant.Model,
                Harness = variant.Harness, Provider = variant.Provider,
                Fixture = new FixtureRef(fixture.Id, fixture.Hash), Bundle = experiment.BundleOf(variant.Id!), Transcript = new TranscriptRef(Layout.TrajectoryFile, "atif"),
                Host = experiment.Host, Versions = experiment.Versions, ExamHash = exam.Hash,
                ProgramHash = Sha256(program ?? File.ReadAllText(Path.Combine(taskDir, "instruction.md"))),
            };
        var nb = program is null ? null : new NbCell(Path.Combine(cellDir, Layout.CompiledProgramFile), experiment.Nb.Config);
        var oracle = OracleCellOf(variant, c);
        manifest.Agent = new AgentRun(variant.Agent!, null, variant.Model ?? "", HarborCompile.AgentKwargs(variant, route, nb), HarborCompile.AgentEnv(variant, route), experiment.Harbor!.Version, null);
        manifest.Oracle = null;
        manifest.Tries++;
        manifest.Started = Now();
        manifest.Ended = null;
        manifest.DurationMs = null;
        manifest.StatusReason = null;
        SetStatus(cellDir, manifest, CellStatus.Running);

        var stopwatch = Stopwatch.StartNew();
        string? trialDir = null, failure = null;
        if (program is not null)
        {
            File.WriteAllText(Path.Combine(cellDir, Layout.ProgramFile), program);
            var (compiled, compileError) = Compile(cellDir, program);
            if (compileError is not null) failure = $"program compile failed: {compileError}";
            else if (oracle is not null) File.WriteAllText(nb!.Program, HarborCompile.WithoutRun(compiled!));
        }
        if (failure is null)
            (trialDir, failure) = HarborRun.Trial(config.Harbor?.Uv ?? new HarborConfig().Uv, experiment.Harbor.Version, experiment.Harbor.Engine, cellDir, variant, taskDir, route, exam.Def.Limits!.WallMs!.Value, HarborCompile.AgentsDir(experimentDir), nb, oracle);
        if (trialDir is not null) failure = Ingest.Trial(trialDir, cellDir, variant, manifest, variant.IsVendor ? variant.AgentVersion! : experiment.Versions["nb"], bridged: oracle is not null);
        stopwatch.Stop();

        manifest.Ended = Now();
        manifest.DurationMs = stopwatch.ElapsedMilliseconds;
        manifest.StatusReason = failure;
        SetStatus(cellDir, manifest, failure is null ? CellStatus.Completed : CellStatus.Failed);
        WriteStatusCounts();
        var exitReason = failure is null ? ReadJson<CountedCore>(Path.Combine(cellDir, Layout.CoreFile))?.ExitReason : null;
        log.WriteLine($"  {variant.Id}/{c.Id}/{sample}  {manifest.Status}  {failure ?? exitReason}  {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    /// <summary>
    /// An oracle task's simulated user on this variant: the host nb judges, with its config, as the variant's own provider unless
    /// the exam names a judge, which is what nb's in-process oracle does on the bare path.
    /// </summary>
    OracleCell? OracleCellOf(Variant variant, TaskDef c) => exam.OracleOf(c) is { } sheet
        ? new OracleCell(sheet, experiment.Nb.Path, experiment.Nb.Config, exam.Def.Oracle?.Judge ?? variant.Provider, exam.OracleTurns,
            Path.Combine(HarborCompile.AgentsDir(experimentDir), HarborCompile.OracleTemplateFile))
        : null;

    /// <summary>
    /// Where an agent variant's model calls go, ${VAR} resolved now: the key never reaches a file. A vendor agent's is
    /// proctor.json's agents block; nb's is its own config's provider entry, whose endpoint variables go to the agent's
    /// process by value and whose key goes by name, as on the bare path, where nb resolves them itself. A provider
    /// with no endpoint (Mock) reaches nothing.
    /// </summary>
    static AgentRoute Route(string root, ProctorConfig config, ResolvedNb nb, Variant variant)
    {
        if (!variant.IsVendor) return NbRoute(nb, variant);
        var endpoint = config.Agents?.GetValueOrDefault(variant.Agent!)
            ?? throw new ProctorException($"{config.Source} has no agents.{variant.Agent} (variant '{variant.Id}'): add {{\"base_url\", \"api_key\"}} for it, as in proctor.example.json");
        var baseUrl = ResolveEnv(endpoint.BaseUrl!, $"{config.Source} agents.{variant.Agent}.base_url").TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new ProctorException($"{config.Source} agents.{variant.Agent}.base_url: '{baseUrl}' is not a URL");
        var agentConfig = endpoint.Config is null ? null : Path.GetFullPath(Path.Combine(Layout.Exams(root), endpoint.Config));
        if (agentConfig is not null && !File.Exists(agentConfig)) throw new ProctorException($"{config.Source} agents.{variant.Agent}.config: {agentConfig} not found");
        return new AgentRoute(baseUrl, endpoint.ApiKey is null ? null : ResolveEnv(endpoint.ApiKey, $"{config.Source} agents.{variant.Agent}.api_key"), HarborCompile.KeyEnv(variant.Agent!), Config: agentConfig);
    }

    /// <summary>
    /// On a remote engine, every agent variant must reach its model from the internet, and no task may have a service beside
    /// it: Harbor's Modal environment runs a compose task as docker-in-docker with no allowlist, and refuses it rather
    /// than run it unconfined. Everything wrong is said at once, before anything is made.
    /// </summary>
    static void RequireReachable(Exam exam, ProctorConfig config, Func<Variant, AgentRoute> route)
    {
        var engine = config.HarborOrDefault.Engine;
        var problems = new List<string>();
        foreach (var variant in exam.Variants.Where(a => a.IsAgent))
            if (route(variant).Host is { } host && HarborRun.Unreachable(host) is { } why)
                problems.Add($"variant '{variant.Id}': its model is at {host}, {why}, which a sandbox on {engine} cannot reach; run it on a local engine, or point {(variant.IsVendor ? $"{config.Source} agents.{variant.Agent}" : $"nb's config {variant.Provider}")} at a public endpoint");
        foreach (var fixture in exam.Tasks.Select(exam.FixtureOf).OfType<Fixture>().DistinctBy(f => f.Id))
            if (fixture.Def.Image?.Services is not null)
                problems.Add($"fixture '{fixture.Id}' runs services beside the agent (image.services), and {engine} cannot confine the network of a task with services; its tasks run on docker or podman only");
        if (problems.Count > 0) throw new ProctorException(string.Join("\n", problems));
    }

    static readonly System.Text.RegularExpressions.Regex EnvRef = new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}");

    static AgentRoute NbRoute(ResolvedNb nb, Variant variant)
    {
        var where = nb.Config ?? "nb's config";
        var providers = nb.Config is null ? null : JsonNode.Parse(File.ReadAllText(nb.Config), documentOptions: new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["ChatProviders"] as JsonArray;
        var entry = providers?.OfType<JsonObject>().FirstOrDefault(p => p["Name"]?.GetValue<string>() == variant.Provider)
            ?? throw new ProctorException($"variant '{variant.Id}': provider '{variant.Provider}' is not in {where} ChatProviders; an nb variant in Harbor reads its endpoint from there");
        var endpoint = entry["Endpoint"]?.GetValue<string>();
        if (endpoint is null) return new AgentRoute(null, null);
        var env = EnvRef.Matches(endpoint).Select(m => m.Groups[1].Value).Distinct().ToDictionary(v => v, v => ResolveEnv($"${{{v}}}", $"{where} {variant.Provider}.Endpoint"));
        var baseUrl = EnvRef.Replace(endpoint, m => env[m.Groups[1].Value]);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _)) throw new ProctorException($"{where} {variant.Provider}.Endpoint: '{baseUrl}' is not a URL");
        var key = entry["ApiKey"]?.GetValue<string>() is { } k && EnvRef.Match(k) is { Success: true } m ? m.Groups[1].Value : null;
        return new AgentRoute(baseUrl, key is null ? null : ResolveEnv($"${{{key}}}", $"{where} {variant.Provider}.ApiKey"), key, env);
    }

    /// <summary>nb's layer from proctor.json harbor.nb, which must be the same nb as the host binary that compiles the programs and runs the bare variants.</summary>
    static string NbImage(string root, ProctorConfig config, string hostVersion)
    {
        var publish = config.Harbor?.Nb ?? throw new ProctorException($"an nb variant runs through Harbor on nb's self-contained linux-x64 publish: set harbor.nb in {config.Source} to its directory");
        var (tag, version) = HarborRun.NbImage(NbPublish(root, config), config.HarborOrDefault);
        if (version != hostVersion)
            throw new ProctorException($"nb's publish ({config.Source} harbor.nb) is {version}, the host nb is {hostVersion}: publish the same nb, so the program compiled on the host is the program the image runs");
        return tag;
    }

    /// <summary>The directory of nb's publish, proctor.json harbor.nb resolved against exams/; compile copies it into each nb task's image on a remote engine.</summary>
    static string NbPublish(string root, ProctorConfig config) => Path.GetFullPath(Path.Combine(Layout.Exams(root), config.Harbor!.Nb!));

    static string ResolveEnv(string value, string field) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", m => Environment.GetEnvironmentVariable(m.Groups[1].Value)
            ?? throw new ProctorException($"{m.Groups[1].Value} is not set in the environment or {EnvFile.DefaultPath()} ({field})"));

    /// <summary>
    /// `nb --compile` on the host binary, in the exam directory so `@file` includes resolve against the exam: the
    /// JSONL that goes down stdin, with every include inlined, so nothing nb runs on names a file the runner would
    /// have to carry. Written beside the source as program.jsonl. The program hash stays the source's. A program
    /// nb refuses fails here, on the host, before a checkout or a container exists for it.
    /// </summary>
    (string? Compiled, string? Error) Compile(string cellDir, string program)
    {
        var nb = experiment.Nb;
        var args = new List<string> { "--compile" };
        if (nb.Config is not null) args.AddRange(["--config", nb.Config]);
        var result = Subprocess.Run(nb.Path, args, exam.Dir, new Dictionary<string, string> { ["NO_COLOR"] = "1" }, stdin: program);
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
        // nb's own ATIF beside its JSONL, by environment so a runner script passes it on untouched. Grading reads the
        // JSONL on this path; the Harbor gate compares the trajectory.
        env["NB_TRAJECTORY"] = Path.GetFullPath(Path.Combine(cellDir, Layout.TrajectoryFile));
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
        var result = Subprocess.Run(Path.GetFullPath(Path.Combine(exam.Dir, script)), [], exam.Dir, env);
        File.WriteAllText(logFile, result.Stdout + (result.Stderr.Length > 0 ? "\n--- stderr ---\n" + result.Stderr : ""));
        if (!result.Started) return result.Stderr;
        return result.ExitCode == 0 ? null : $"{script} exited {result.ExitCode}: {result.FirstStderrLine}";
    }

    void MarkFailed(Variant variant, TaskDef c, int sample, string reason)
    {
        var cellDir = Layout.Cell(experimentDir, variant.Id!, c.Id!, sample);
        Directory.CreateDirectory(cellDir);
        var manifestFile = Path.Combine(cellDir, Layout.ManifestFile);
        var manifest = File.Exists(manifestFile)
            ? ReadManifest(manifestFile)
            : new Manifest
            {
                RunId = Layout.NewRunId(), Experiment = experiment.Id, Variant = variant.Id!, Task = c.Id!, Sample = sample,
                Runner = variant.Runner, Harness = variant.Harness, Provider = variant.Provider, Model = variant.Model,
                Host = experiment.Host, Versions = experiment.Versions, ExamHash = exam.Hash, ProgramHash = "",
            };
        manifest.Tries++;
        manifest.StatusReason = reason;
        SetStatus(cellDir, manifest, CellStatus.Failed);
        log.WriteLine($"  {variant.Id}/{c.Id}/{sample}  failed  {reason}");
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
        foreach (var (variant, c, s) in Cells(exam)) counts[ReadStatus(Layout.Cell(experimentDir, variant.Id!, c.Id!, s))]++;
        WriteJson(Path.Combine(experimentDir, Layout.StatusCountsFile), new { planned = experiment.Planned, counts, updated = Now() });
    }

    static string? ExitReasonOf(string cellDir)
    {
        var file = Path.Combine(cellDir, Layout.TranscriptFile);
        return File.Exists(file) ? Transcript.Read(cellDir).Trailer?.ExitReason : null;
    }

    /// <summary>
    /// A bare cell's oracle, as nb's own directives ahead of the template: they apply to every run after them, so the
    /// template needs no placeholder. The sheet is the host path, which --compile inlines, so a runner's container never
    /// needs the file. Empty for a task without a sheet.
    /// </summary>
    public static string OraclePrefix(Exam exam, TaskDef c)
    {
        if (exam.OracleOf(c) is not { } sheet) return "";
        var prefix = $"oracle @{sheet}\n";
        if (exam.Def.Oracle?.Judge is { } judge) prefix += $"oracle provider {judge}\n";
        return prefix + $"budget oracle_turns {exam.OracleTurns}\n";
    }

    /// <summary>Fill the template. {{work}} and {{bundle}} are the paths the model will see, which a runner may mount elsewhere. A prompt's newlines become nb continuation lines so the program stays one directive.</summary>
    public static string ResolveProgram(string template, Variant variant, TaskDef c, int sample, string workDir, string? bundleDir = null)
    {
        var values = new Dictionary<string, string>
        {
            ["prompt"] = c.Prompt!.Replace("\r\n", "\n").Replace("\n", " \\\n"),
            ["task"] = c.Id!,
            ["work"] = workDir,
            ["bundle"] = bundleDir ?? "",
            ["provider"] = variant.Provider ?? "",
            ["model"] = variant.Model ?? "",
            ["harness"] = variant.Harness ?? "",
            ["variant"] = variant.Id!,
            ["sample"] = sample.ToString(),
        };
        return System.Text.RegularExpressions.Regex.Replace(template, @"\{\{\s*([^}]*?)\s*\}\}", m => values[m.Groups[1].Value]);
    }

    static ResolvedNb ResolveNb(string root, Exam exam, ProctorConfig config, string? nbOverride, string? runnerOverride)
    {
        var nb = config.NbOrDefault;
        var examsDir = Layout.Exams(root);
        if (!File.Exists(Path.Combine(root, config.Source)) && File.Exists(Path.Combine(examsDir, "proctor.example.json")))
            throw new ProctorException("no exams/proctor.json: run `proctor setup` at a terminal, which writes it (skip every job for a config the playground runs on); or copy exams/proctor.example.json and nb.example.json beside themselves and point them at your gateway");
        var path = nbOverride ?? nb.Path;
        if (path.Contains(Path.DirectorySeparatorChar)) path = Path.GetFullPath(Path.Combine(nbOverride is null ? examsDir : Directory.GetCurrentDirectory(), path));
        else if (FindOnPath(path) is { } found) path = found;
        else throw new ProctorException($"nb not found: '{path}' is not on PATH; set nb.path in {config.Source} or pass --nb");
        if (!File.Exists(path)) throw new ProctorException($"nb not found at {path}");
        var configPath = nb.Config is null ? null : Path.GetFullPath(Path.Combine(examsDir, nb.Config));
        if (configPath is not null && !File.Exists(configPath)) throw new ProctorException($"nb config not found at {configPath} ({config.Source} nb.config){(File.Exists(Path.ChangeExtension(configPath, ".example.json")) ? $"; copy {Path.GetFileName(Path.ChangeExtension(configPath, ".example.json"))} beside it" : "")}");
        var runner = ResolveRunner(root, exam, runnerOverride);
        return new ResolvedNb(path, configPath, runner, runner is null ? null : exam.Def.Nb?.Mounts);
    }

    /// <summary>The runner in effect: --runner (from the current directory) over the exam's nb.runner (from the exam directory); `--runner none` is a bare run. Recorded relative to exams/ when it lives there, else absolute, so a resume can compare it.</summary>
    static ResolvedRunner? ResolveRunner(string root, Exam exam, string? runnerOverride)
    {
        var examsDir = Layout.Exams(root);
        var script = runnerOverride ?? exam.Def.Nb?.Runner;
        if (script is null || runnerOverride == "none") return null;
        var path = Path.GetFullPath(Path.Combine(runnerOverride is null ? exam.Dir : Directory.GetCurrentDirectory(), script));
        if (!File.Exists(path)) throw new ProctorException($"runner not found at {path} ({(runnerOverride is null ? $"exams/{exam.Id}/exam.json nb.runner" : "--runner")})");
        var relative = Path.GetRelativePath(examsDir, path);
        return new ResolvedRunner(relative.StartsWith("..") ? path : relative, path, Sha256(File.ReadAllText(path)));
    }

    /// <summary>The first <paramref name="name"/> on PATH; on Windows also with each of PATHEXT's extensions (<c>uv.exe</c> for <c>uv</c>).</summary>
    internal static string? FindOnPath(string name)
    {
        var names = OperatingSystem.IsWindows() && !Path.HasExtension(name)
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => name + e).Prepend(name)
            : [name];
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(d => names.Select(n => Path.Combine(d, n))).FirstOrDefault(File.Exists);
    }

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
        File.WriteAllText(file, JsonSerializer.Serialize(value, value.GetType(), Exam.JsonOptions) + "\n");

    public static Manifest ReadManifest(string file) => ReadJson<Manifest>(file)!;

    public static T? ReadJson<T>(string file) where T : class =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(file), Exam.JsonOptions);
}
