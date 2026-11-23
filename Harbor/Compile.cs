using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Proctor;

// An exam's agent variants as Harbor tasks: one task directory per variant and task, under the log entry of the run that compiled them, and one job file
// per attempt. Only the fixture's repo/ enters the image the agent runs in; its checks, the exam and the expectations go
// to tests/, which Harbor copies into the separate verifier container only.
// Harbor tasks are compiled once per run, per variant and task, and kept with its log entry: an attempt's verdicts are checked against the copy its run compiled.

/// <summary>
/// What compile needs from the machine: where the agent's model calls go, resolved, and the key with the variable it
/// travels in (Harbor's process environment only). The host is the one name the agent may reach; with no base URL (nb's
/// Mock) the agent reaches nothing. Env is what the agent's process needs beside the key: nb's config names its
/// endpoint as ${VAR}s, which it resolves inside the container. Config is a vendor agent's native config file on the host.
/// </summary>
record AgentRoute(string? BaseUrl, string? ApiKey, string? KeyEnv = null, Dictionary<string, string>? Env = null, string? Config = null)
{
    public string? Host => BaseUrl is null ? null : new Uri(BaseUrl).Host;
}

/// <summary>An nb attempt's own inputs to its job: the program compiled on the host for this attempt and nb's config (host paths the agent uploads).</summary>
record NbAttempt(string Program, string? Config);

/// <summary>
/// An oracle attempt's simulated user, all host paths: the task's answer sheet, the host nb that judges and its config, the
/// config entry that judges, how many answers the attempt may get, and the compiled user prompt template.
/// </summary>
record OracleAttempt(string Sheet, string Nb, string? Config, string? Judge, int Turns, string Template);

static class HarborCompile
{
    /// <summary>Where the checkout is in the image, for the agent and the verifier alike.</summary>
    public const string Workdir = "/app";
    /// <summary>What Harbor's verifier collect step writes and the verifier applies: the attempt's change against the fixture commit.</summary>
    const string DiffInContainer = "/logs/artifacts/diff.patch";
    /// <summary>
    /// The agents proctor brings itself, as Harbor imports them from the log entry's harbor/agents/ on PYTHONPATH: nb,
    /// and Qwen Code, whose Harbor agent reinstalls from the network on every trial (proctor's checks the image's pin
    /// instead). Claude Code and Codex are Harbor's own, which skip the install when the image has the pin.
    /// </summary>
    static readonly Dictionary<string, (string Import, string File)> OwnAgents = new()
    {
        [Variant.Nb] = ("nb_agent:NbAgent", "nb_agent.py"),
        [Variant.QwenCode] = ("qwen_code_agent:QwenCodeAgent", "qwen_code_agent.py"),
    };

    /// <summary>The oracle's simulated user (Harbor/oracle_user.py), compiled beside the agents for a run with an oracle task and an agent variant.</summary>
    const string OracleUserImport = "oracle_user:OracleUser", OracleUserFile = "oracle_user.py";

    /// <summary>
    /// The oracle's user prompt template. Harbor requires both variables; the sentinel line lets OracleUser take the task
    /// back out, since the bridge's instructions are for a model playing the user, and this user is a script.
    /// </summary>
    public const string OracleTemplateFile = "oracle_user.j2";
    const string OracleTemplate = "{{ instruction }}\n<<<PROCTOR-BRIDGE>>>\n{{ bridge_instructions }}\n";

    /// <summary>Whether Harbor imports this variant's agent from the log entry rather than naming one of its own.</summary>
    public static bool IsOwnAgent(Variant variant) => OwnAgents.ContainsKey(variant.Agent!);

    /// <summary>
    /// The tasks a run's agent attempts run as, one per agent variant and task it makes attempts of (every agent variant's
    /// every task when <paramref name="pairs"/> is not given), and proctor's own Harbor agents beside them for the variants
    /// that use one. Returns the tree's hash, which the log entry records and `run` checks before it continues the run. The nb
    /// image is the tag of proctor's nb layer; on a remote engine, which cannot see a local image, nb's publish directory
    /// comes too and is copied into each nb task. The bundles are the run's resolved ones, which reach a task only through
    /// its fixture's services.
    /// </summary>
    public static string Tasks(string entryDir, Exam exam, Func<Variant, AgentRoute> route, string? nbImage = null, string? nbPublish = null,
        IReadOnlyDictionary<string, ResolvedBundle>? bundles = null, IReadOnlyCollection<(Variant Variant, TaskDef Task)>? pairs = null)
    {
        pairs ??= (from variant in exam.Variants.Where(a => a.IsAgent) from c in exam.Tasks select (variant, c)).ToList();
        if (exam.Staged)
            foreach (var variant in pairs.Select(p => p.Variant).Distinct())
                Job(Layout.HarborJobTask(entryDir, variant.Id!, exam.Id), exam, variant, route(variant), nbImage, nbPublish, bundles?.GetValueOrDefault(variant.Id!)?.Path);
        else
            foreach (var (variant, c) in pairs)
                Task(Layout.HarborTask(entryDir, variant.Id!, c.Id!), exam, variant, c, route(variant), nbImage, nbPublish, bundles?.GetValueOrDefault(variant.Id!)?.Path);
        var files = pairs.Select(p => p.Variant).Where(IsOwnAgent).Select(a => OwnAgents[a.Agent!].File).Distinct().ToList();
        var oracle = pairs.Any(p => p.Task.Oracle is not null);
        if (oracle) files.Add(OracleUserFile);
        var agents = AgentsDir(entryDir);
        foreach (var file in files)
        {
            Directory.CreateDirectory(agents);
            using var source = typeof(HarborCompile).Assembly.GetManifestResourceStream(file)!;
            using var dest = File.Create(Path.Combine(agents, file));
            source.CopyTo(dest);
        }
        if (oracle) File.WriteAllText(Path.Combine(agents, OracleTemplateFile), OracleTemplate);
        return TreeHash(entryDir);
    }

    /// <summary>Where proctor's own Harbor agents are compiled to: the directory Harbor's process gets on PYTHONPATH.</summary>
    public static string AgentsDir(string entryDir) => Path.Combine(entryDir, Layout.HarborDir, "agents");

    /// <summary>The compiled tasks, plus the agents directory when there is one (a run of Harbor's own agents only hashes as before).</summary>
    public static string TreeHash(string entryDir)
    {
        var tasks = Tree.Hash(Path.Combine(entryDir, Layout.HarborDir, "tasks"), new HashSet<string> { ".git" });
        var agents = AgentsDir(entryDir);
        return Directory.Exists(agents) ? $"{tasks}+{Tree.Hash(agents, new HashSet<string> { "__pycache__" })}" : tasks;
    }

    /// <summary>Where a variant's bundle sits beside the fixture's services in a task's environment/, for a compose file to mount as ./bundle.</summary>
    public const string ServiceBundleDir = "bundle";

    /// <summary>
    /// One Harbor task directory: instruction.md, task.toml, environment/ (the image) and tests/ (the verifier). When the
    /// fixture has services, the variant's bundle is copied beside them as environment/bundle/ (empty without one), so a
    /// service can serve it, a docs site say, while the agent's own container never holds it.
    /// </summary>
    public static void Task(string dir, Exam exam, Variant variant, TaskDef task, AgentRoute route, string? nbImage = null, string? nbPublish = null,
        string? bundleDir = null)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var fixture = exam.FixtureOf(task)!;
        Directory.CreateDirectory(Path.Combine(dir, "environment"));
        File.WriteAllText(Path.Combine(dir, "instruction.md"), task.Prompt!.Replace("\r\n", "\n").TrimEnd() + "\n");
        File.WriteAllText(Path.Combine(dir, "task.toml"), TaskToml(exam, task, route, bridged: task.Oracle is not null));
        CopyRepo(fixture, Path.Combine(dir, "environment", "repo"));
        var nbInContext = variant.Agent == Variant.Nb && nbPublish is not null;
        if (nbInContext) CopyTree(nbPublish!, Path.Combine(dir, "environment", NbContextDir), []);
        File.WriteAllText(Path.Combine(dir, "environment", "Dockerfile"), Dockerfile(fixture, variant, nbImage, nbInContext));
        if (fixture.Def.Image!.Services is { } services)
        {
            CopyTree(Path.Combine(fixture.Dir, services), Path.Combine(dir, "environment"), []);
            var bundle = Directory.CreateDirectory(Path.Combine(dir, "environment", ServiceBundleDir)).FullName;
            if (bundleDir is not null) CopyTree(bundleDir, bundle, []);
        }

        var tests = Path.Combine(dir, "tests");
        CopyTree(exam.Dir, Path.Combine(tests, "exam"), []);
        CopyTree(fixture.Dir, Path.Combine(tests, "fixture"), [Path.GetFullPath(fixture.SourceDir ?? Path.Combine(fixture.Dir, "repo"))]);
        File.WriteAllText(Path.Combine(tests, "env.sh"), VerifierEnv(exam, variant.Id!, task));
        var test = Path.Combine(tests, "test.sh");
        File.WriteAllText(test, TestScript(exam, task));
        File.SetUnixFileMode(test, File.GetUnixFileMode(test) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// A staged exam as one Harbor task of steps (Harbor 0.24.0's `[[steps]]`, run here on 2026-11-23): the environment is
    /// built once, the agent starts a fresh conversation per step with that step's instruction.md, and Harbor keeps each
    /// step's agent logs, verifier output and collected diff under steps/&lt;id&gt;/. Harbor runs a verifier after every
    /// step and ends the trial at a step that threw and has no verifier result, so every step has one: the last stage's
    /// runs the script checks, on the workspace as the whole try left it, and the others only say they ran, so a stage
    /// that timed out does not end the try. The diff collected after each step is the try's so far.
    /// </summary>
    public static void Job(string dir, Exam exam, Variant variant, AgentRoute route, string? nbImage = null, string? nbPublish = null, string? bundleDir = null)
    {
        var last = exam.Tasks[^1];
        // The environment and the shared tests are a task's; the last stage stands for the job, since its checks are the workspace's.
        Task(dir, exam, variant, last, route, nbImage, nbPublish, bundleDir);
        File.Delete(Path.Combine(dir, "instruction.md"));
        File.WriteAllText(Path.Combine(dir, "task.toml"), TaskToml(exam, last, route, bridged: false, name: exam.Id, description: exam.Def.Description ?? exam.Id)
            + string.Concat(exam.Tasks.Select(s => $"\n[[steps]]\nname = {Toml(s.Id!)}\n")));
        var shared = Path.Combine(dir, "tests", "test.sh");
        var lastTests = Directory.CreateDirectory(Path.Combine(dir, "steps", last.Id!, "tests")).FullName;
        File.Move(shared, Path.Combine(lastTests, "test.sh"));
        File.WriteAllText(shared, "#!/bin/bash\n# Compiled by proctor: a stage before the last has no workspace check; this says its verifier ran.\necho '{\"ran\": 1}' > /logs/verifier/reward.json\n");
        File.SetUnixFileMode(shared, File.GetUnixFileMode(Path.Combine(lastTests, "test.sh")));
        foreach (var stage in exam.Tasks)
        {
            Directory.CreateDirectory(Path.Combine(dir, "steps", stage.Id!));
            File.WriteAllText(Path.Combine(dir, "steps", stage.Id!, "instruction.md"), stage.Prompt!.Replace("\r\n", "\n").TrimEnd() + "\n");
        }
    }

    /// <summary>Where a compiled task keeps the verifier's script: a staged exam's is its last stage's step, a task's the task's own.</summary>
    static string TestScriptFile(string taskDir, Exam exam, TaskDef task) =>
        exam.Staged ? Path.Combine(taskDir, "steps", task.Id!, "tests", "test.sh") : Path.Combine(taskDir, "tests", "test.sh");

    /// <summary>
    /// How long a Modal sandbox may sit with nothing running in it. Harbor's Modal environment otherwise keeps a sandbox
    /// for a day with no idle limit, so a trial Harbor never closed would bill for a day.
    /// </summary>
    public static readonly TimeSpan ModalIdle = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Harbor's job for one attempt: one trial of one task by one agent. Every setting Harbor would otherwise default is
    /// stated: the container engine, the pinned version, the permission mode, each model alias (Harbor maps them all to one model on a custom
    /// base URL; proctor says so rather than inherits it), no retries (a retry is proctor's attempt), one trial at a time.
    /// The key is not here: it goes in the environment of the one Harbor process. proctor's own agents (nb, Qwen
    /// Code) are named by import path; nb's kwargs carry the attempt's host-compiled program and nb's config. On Modal each
    /// sandbox lives no longer than the attempt may take (<paramref name="lifetime"/>) and ends after <see cref="ModalIdle"/> idle.
    /// </summary>
    public static JsonObject Job(Variant variant, string taskDir, string jobsDir, AgentRoute route, NbAttempt? nb = null, string engine = "docker", OracleAttempt? oracle = null, TimeSpan? lifetime = null)
    {
        var agent = new JsonObject();
        if (OwnAgents.TryGetValue(variant.Agent!, out var own)) agent["import_path"] = own.Import;
        else agent["name"] = variant.Agent;
        agent["model_name"] = variant.Model;
        agent["kwargs"] = JsonSerializer.SerializeToNode(AgentKwargs(variant, route, nb));
        agent["env"] = JsonSerializer.SerializeToNode(AgentEnv(variant, route));
        var job = new JsonObject
        {
            ["job_name"] = "job",
            ["jobs_dir"] = jobsDir,
            ["n_attempts"] = 1,
            ["n_concurrent_trials"] = 1,
            ["quiet"] = true,
            ["retry"] = new JsonObject { ["max_retries"] = 0 },
            ["environment"] = engine == HarborConfig.Modal
                ? new JsonObject
                {
                    ["type"] = engine,
                    ["kwargs"] = new JsonObject
                    {
                        ["sandbox_timeout_secs"] = (int)(lifetime ?? TimeSpan.FromHours(1)).TotalSeconds,
                        ["sandbox_idle_timeout_secs"] = (int)ModalIdle.TotalSeconds,
                    },
                }
                : new JsonObject { ["type"] = engine },
            ["agents"] = new JsonArray(agent),
            ["tasks"] = new JsonArray(new JsonObject { ["path"] = taskDir }),
        };
        if (oracle is not null) job["user_agent"] = UserAgent(oracle);
        return job;
    }

    /// <summary>
    /// An oracle attempt's simulated user: OracleUser on the host, bridged to the agent over ACP, with the sheet, the judge
    /// and the budget as kwargs. Harbor hands it the task through the compiled template.
    /// </summary>
    public static JsonObject UserAgent(OracleAttempt oracle)
    {
        var kwargs = new JsonObject { ["sheet"] = oracle.Sheet, ["nb"] = oracle.Nb, ["turns"] = oracle.Turns };
        if (oracle.Config is not null) kwargs["config"] = oracle.Config;
        if (oracle.Judge is not null) kwargs["judge"] = oracle.Judge;
        return new JsonObject
        {
            ["import_path"] = OracleUserImport,
            ["user_prompt_template_path"] = oracle.Template,
            ["bridge"] = new JsonObject { ["kind"] = "acp" },
            ["kwargs"] = kwargs,
        };
    }

    /// <summary>
    /// The compiled program without its run: over the bridge the simulated user sends the prompt, and nb --acp refuses a
    /// program that holds one. Validation guarantees an oracle task's template has exactly one run, last.
    /// </summary>
    public static string WithoutRun(string jsonl)
    {
        var lines = jsonl.Split('\n').ToList();
        var run = lines.FindLastIndex(l => l.Length > 0 && JsonNode.Parse(l)?["type"]?.GetValue<string>() == "run");
        if (run < 0) throw new ProctorException("the compiled program has no run to hand to the simulated user");
        lines.RemoveAt(run);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// The agent's settings as Harbor takes them; the attempt manifest records the same. A vendor agent's: its pin, and
    /// Claude Code's permission mode (Codex and Qwen Code have no setting for it: Harbor runs them with approvals and
    /// sandbox bypassed, always). nb's: the program and config on the host, the work directory, and the key's
    /// variable by name.
    /// </summary>
    public static Dictionary<string, string> AgentKwargs(Variant variant, AgentRoute route, NbAttempt? nb = null)
    {
        switch (variant.Agent)
        {
            case Variant.ClaudeCode: return new() { ["version"] = variant.AgentVersion!, ["permission_mode"] = "bypassPermissions" };
            case Variant.Codex when route.Config is not null: return new() { ["version"] = variant.AgentVersion!, ["config"] = route.Config };
            case Variant.Codex or Variant.QwenCode: return new() { ["version"] = variant.AgentVersion! };
        }
        var kwargs = new Dictionary<string, string> { ["program"] = nb!.Program, ["workdir"] = Workdir };
        if (nb.Config is not null) kwargs["config"] = nb.Config;
        if (route.KeyEnv is not null) kwargs["secret"] = route.KeyEnv;
        return kwargs;
    }

    /// <summary>The variable a vendor agent's key travels in, in Harbor's process environment.</summary>
    public static string KeyEnv(string agent) => agent == Variant.ClaudeCode ? "ANTHROPIC_API_KEY" : "OPENAI_API_KEY";

    /// <summary>
    /// What the agent's process sees beyond Harbor's own. Claude Code: the base URL and every model alias set to the
    /// variant's model, explicitly. Codex and Qwen Code: the base URL (Harbor writes Codex's into its config.toml; Qwen
    /// Code takes the model from model_name). nb: the variables its config's provider entry names for its endpoint.
    /// </summary>
    public static Dictionary<string, string> AgentEnv(Variant variant, AgentRoute route) => variant.Agent switch
    {
        Variant.ClaudeCode => new()
        {
            ["ANTHROPIC_BASE_URL"] = route.BaseUrl!,
            ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = variant.Model!,
            ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = variant.Model!,
            ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = variant.Model!,
            ["CLAUDE_CODE_SUBAGENT_MODEL"] = variant.Model!,
        },
        Variant.Codex or Variant.QwenCode => new() { ["OPENAI_BASE_URL"] = route.BaseUrl! },
        _ => new(route.Env ?? []),
    };

    /// <summary>
    /// The agent's phase reaches only the model endpoint's host, exactly as the base URL spells it (Harbor's allowlist
    /// matches names exactly), or nothing when there is no endpoint; the image builds with network; the verifier has
    /// none, so the image restores packages. A bridged task's diff leaves out the bridge's acpx config, which Harbor
    /// writes into the work directory (its exec's default cwd, the image's WORKDIR).
    /// </summary>
    static string TaskToml(Exam exam, TaskDef c, AgentRoute route, bool bridged, string? name = null, string? description = null) => $"""
        version = "1.0"

        [task]
        name = {Toml($"proctor/{name ?? c.Id}")}
        description = {Toml(description ?? Exam.Describe(c))}

        [agent]
        timeout_sec = {exam.Def.Limits!.WallMs!.Value / 1000.0:0.0##}
        {(route.Host is { } host ? $"network_mode = \"allowlist\"\nallowed_hosts = [{Toml(host)}]" : "network_mode = \"no-network\"")}

        [environment]
        build_timeout_sec = 1800.0
        network_mode = "public"

        [verifier]
        timeout_sec = 900.0
        environment_mode = "separate"
        network_mode = "no-network"

        [[verifier.collect]]
        command = {Toml($"cd {Workdir} && git add -A{(bridged ? " -- . ':(exclude).acpxrc.json'" : "")} && git diff --cached --binary HEAD > {DiffInContainer}")}

        """;

    /// <summary>
    /// The fixture as a commit in the work directory, so the diff after the agent is the change; the fixture's own
    /// setup after it; then the variant's agent, pinned, so a trial installs nothing and needs no network to start.
    /// </summary>
    /// <summary>Where nb's publish sits in an nb task's build context on a remote engine.</summary>
    const string NbContextDir = "proctor-nb";

    static string Dockerfile(Fixture fixture, Variant variant, string? nbImage, bool nbInContext = false)
    {
        var image = fixture.Def.Image!;
        var sb = new StringBuilder();
        sb.Append($"# Compiled by proctor from fixture {fixture.Id} ({fixture.Hash}) for agent {variant.Agent} {(variant.IsVendor ? variant.AgentVersion : nbImage)}.\n");
        sb.Append($"FROM {image.From}\n");
        sb.Append("ENV NO_COLOR=1");
        foreach (var (k, v) in image.Env ?? []) sb.Append($" {k}={JsonSerializer.Serialize(v)}");
        sb.Append('\n');
        sb.Append($"COPY repo {Workdir}\n");
        sb.Append($"WORKDIR {Workdir}\n");
        sb.Append($"RUN git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm {Shell($"fixture {fixture.Id} {fixture.Hash}")}\n");
        foreach (var command in image.Run ?? []) sb.Append($"RUN {command}\n");
        sb.Append(AgentInstall(variant, nbInContext ? NbContextDir : $"--from={nbImage} /opt/nb"));
        return sb.ToString();
    }

    /// <summary>
    /// The agent at its pin. nb comes from proctor's nb layer (the publish proctor.json names, tagged by its hash), so the
    /// image holds that binary and nothing it was built from: <paramref name="nbSource"/> is `--from=&lt;tag&gt; /opt/nb` on a
    /// local engine, the publish's directory in the build context on a remote one.
    /// </summary>
    static string AgentInstall(Variant variant, string nbSource) => variant.Agent switch
    {
        Variant.Nb => $"""
            COPY {nbSource} /opt/nb
            ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
            RUN /opt/nb/nb --version

            """,
        Variant.ClaudeCode => $"""
            RUN apt-get update -q && apt-get install -y -q --no-install-recommends procps >/dev/null && rm -rf /var/lib/apt/lists/* \
             && curl -fsSL https://downloads.claude.ai/claude-code-releases/bootstrap.sh | bash -s -- {variant.AgentVersion} \
             && /root/.local/bin/claude --version

            """,
        // Node from nvm under ~/.nvm, where Harbor's agents look for it, so their version checks find the pin.
        Variant.Codex => $"""
            RUN apt-get update -q && apt-get install -y -q --no-install-recommends ripgrep procps >/dev/null && rm -rf /var/lib/apt/lists/* \
             && {NodeInstall} \
             && npm install -g @openai/codex@{variant.AgentVersion} >/dev/null && codex --version \
             && ln -sf "$(command -v node)" /usr/local/bin/node && ln -sf "$(command -v codex)" /usr/local/bin/codex

            """,
        Variant.QwenCode => $"""
            RUN {NodeInstall} \
             && npm install -g @qwen-code/qwen-code@{variant.AgentVersion} >/dev/null && qwen --version

            """,
        _ => throw new InvalidOperationException($"no install for agent {variant.Agent}"),
    };

    /// <summary>
    /// The script check contract inside the verifier: the paths the scripts see are the container's, and what has no
    /// meaning there (the attempt's directory and number, nb, a runner) is empty. There is no transcript in the verifier: a check that
    /// reads one cannot run there yet.
    /// </summary>
    static string VerifierEnv(Exam exam, string variant, TaskDef task)
    {
        var values = new Dictionary<string, string>
        {
            ["PROCTOR_EXAM_DIR"] = "/tests/exam",
            ["PROCTOR_FIXTURE"] = "/tests/fixture",
            ["PROCTOR_BUNDLE"] = "",
            ["PROCTOR_EXAM"] = exam.Id,
            ["PROCTOR_VARIANT"] = variant,
            ["PROCTOR_TASK"] = task.Id!,
            ["PROCTOR_ATTEMPT"] = "",
            ["PROCTOR_ATTEMPT_DIR"] = "",
            ["PROCTOR_WORK"] = Workdir,
            ["PROCTOR_WORK_MOUNT"] = Workdir,
            ["PROCTOR_BUNDLE_MOUNT"] = "",
            ["PROCTOR_TASK_JSON"] = JsonSerializer.Serialize(task, Exam.JsonOptions),
            ["PROCTOR_EXPECT"] = exam.ExpectFor(task)?.ToJsonString() ?? "{}",
            ["PROCTOR_TRANSCRIPT"] = "",
            ["PROCTOR_DIFF"] = DiffInContainer,
            ["PROCTOR_RUNNER"] = "",
            ["PROCTOR_NB"] = "",
            ["PROCTOR_NB_CONFIG"] = "",
            ["PROCTOR_CONTAINER"] = "",
        };
        return "# The script check contract, as the verifier sees it. Compiled by proctor.\n"
            + string.Concat(values.Select(kv => $"export {kv.Key}={Shell(kv.Value)}\n"));
    }

    /// <summary>
    /// The verifier: the attempt's diff on the clean fixture commit, then each script check the task carries, in declared
    /// order. One reward key per check: 1 pass, 0 fail, 2 needs-judge; any other exit leaves the key out, which is
    /// error. Each check's output is kept as verifier/&lt;check&gt;.out and its exit code as &lt;check&gt;.exit.
    /// </summary>
    static string TestScript(Exam exam, TaskDef task)
    {
        var sb = new StringBuilder("""
            #!/bin/bash
            # Compiled by proctor: the run's diff on the fixture commit, then one reward key per script check.
            set -uo pipefail
            out=/logs/verifier
            . /tests/env.sh
            cd /app
            if [ -s "$PROCTOR_DIFF" ] && ! git apply --whitespace=nowarn "$PROCTOR_DIFF" > "$out/apply.out" 2>&1; then
              echo '{}' > "$out/reward.json"; exit 0
            fi
            rewards=()
            check() {
              local name=$1 script=$2 rc
              chmod +x "$script" 2>/dev/null
              (cd "$out" && "$script") > "$out/$name.out" 2>&1; rc=$?
              echo "$rc" > "$out/$name.exit"
              case $rc in 0) rewards+=("\"$name\": 1") ;; 1) rewards+=("\"$name\": 0") ;; 2) rewards+=("\"$name\": 2") ;; esac
            }

            """);
        foreach (var (name, check) in exam.ChecksFor(task))
        {
            if (check.Spec["script"] is not JsonValue v || !v.TryGetValue<string>(out var script)) continue;
            var root = check.Level == "fixture" ? "/tests/fixture" : "/tests/exam";
            sb.Append($"check {Shell(name)} {Shell($"{root}/{script}")}\n");
        }
        sb.Append("""
            (IFS=,; echo "{${rewards[*]}}") > "$out/reward.json"

            """);
        return sb.ToString();
    }

    /// <summary>
    /// For each script check the task carries now, why the verdict an attempt made from this compiled task holds for it may not
    /// be the check's as it stands, keyed by check; a check missing from the result is current. The verifier ran what the run
    /// compiled into <paramref name="taskDir"/>/tests/, so that copy is compared with what compile would write today: the
    /// check's line in test.sh (which script, from which level), every file the scripts could read (the exam directory and
    /// the fixture beside its source, less the definition files proctor reads itself and hands over through the environment),
    /// and the expected values env.sh exported. Any file counts, since a script reads what it likes (acceptance tests, a
    /// service), except another check's script. With no compiled copy, every script check is reported, since nothing shows
    /// its verdict is current.
    /// </summary>
    public static Dictionary<string, string> Outdated(string? taskDir, Exam exam, TaskDef task, string variant)
    {
        var names = exam.ChecksFor(task).Where(k => k.Value.Spec.ContainsKey("script")).Select(k => k.Key).ToList();
        var tests = taskDir is null ? null : Path.Combine(taskDir, "tests");
        if (names.Count == 0) return [];
        var testScript = taskDir is null ? null : TestScriptFile(taskDir, exam, task);
        if (tests is null || !File.Exists(testScript) || !File.Exists(Path.Combine(tests, "env.sh")))
            return names.ToDictionary(n => n, _ => "its run kept no compiled copy of its script checks, so nothing shows this verdict is the check's as it is now; make the attempt again");

        var root = Path.GetDirectoryName(Path.GetDirectoryName(exam.Dir))!;
        var fixture = exam.FixtureOf(task)!;
        var changed = ChangedFiles(Path.Combine(tests, "exam"), exam.Dir, root, ExamDefinition)
            .Concat(ChangedFiles(Path.Combine(tests, "fixture"), fixture.Dir, root, FixtureDefinition(fixture))).ToList();
        string? Line(IEnumerable<string> lines, string prefix) => lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        var expectThen = Line(File.ReadLines(Path.Combine(tests, "env.sh")), "export PROCTOR_EXPECT=");
        var expectNow = Line(VerifierEnv(exam, variant, task).Split('\n'), "export PROCTOR_EXPECT=");
        var then = File.ReadAllLines(testScript!);
        var now = TestScript(exam, task).Split('\n');

        // Another check's script is that check's business: editing one script outdates its own check, not every check beside it.
        string FromVerifier(string path) => Path.GetRelativePath(root, path.StartsWith("/tests/fixture/", StringComparison.Ordinal)
            ? Path.Combine(fixture.Dir, path["/tests/fixture/".Length..]) : Path.Combine(exam.Dir, path["/tests/exam/".Length..])).Replace('\\', '/');
        var scripts = then.Concat(now).Select(l => Regex.Match(l, @"^check '[^']*' '(/tests/(?:exam|fixture)/[^']*)'$")).Where(m => m.Success)
            .Select(m => FromVerifier(m.Groups[1].Value)).ToHashSet();

        var outdated = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var prefix = $"check {Shell(name)} ";
            var (lineThen, lineNow) = (Line(then, prefix), Line(now, prefix));
            var script = Regex.Match(lineNow!, @"'(/tests/(?:exam|fixture)/[^']*)'$") is { Success: true } m ? FromVerifier(m.Groups[1].Value) : "";
            var relevant = changed.Where(f => f == script || !scripts.Contains(f)).OrderBy(f => f != script).ToList();
            var why = lineThen is null ? "this check did not exist when the attempt was made, so the verifier never ran it"
                : lineThen != lineNow ? "this check ran another script when the attempt was made"
                : relevant.Count > 0 ? $"changed since the attempt was made: {Listed(relevant)}, so the verdict on file may not be this check's as it is now"
                : expectThen != expectNow ? "the task's expected values changed since the attempt was made, and the verifier ran with the old ones"
                : null;
            if (why is not null) outdated[name] = $"{why}; make the attempt again";
        }
        return outdated;

        static string Listed(List<string> files) => files.Count <= 3 ? string.Join(", ", files) : $"{string.Join(", ", files.Take(3))} and {files.Count - 3} more";
    }

    /// <summary>What proctor reads from an exam directory itself; a script sees its checks and expected values through the environment, and the rest is in the task's hash.</summary>
    static bool ExamDefinition(string relative) =>
        relative is Layout.ExamFile or Layout.LiveFile
        || (relative.StartsWith(Layout.TasksDir + "/", StringComparison.Ordinal) || relative.StartsWith(Layout.VariantsDir + "/", StringComparison.Ordinal)) && relative.EndsWith(".json", StringComparison.Ordinal);

    /// <summary>What proctor reads from a fixture directory itself, and its source, which reaches the verifier as the image's commit and not as a file under tests/.</summary>
    static Func<string, bool> FixtureDefinition(Fixture fixture)
    {
        var source = Path.GetRelativePath(fixture.Dir, fixture.SourceDir ?? Path.Combine(fixture.Dir, "repo")).Replace('\\', '/') + "/";
        return relative => relative == Layout.FixtureFile || relative.StartsWith(source, StringComparison.Ordinal);
    }

    /// <summary>The files that differ between a compiled copy and the directory it was copied from, by content, added or removed, as paths from the repository root.</summary>
    static IEnumerable<string> ChangedFiles(string copy, string dir, string root, Func<string, bool> skip)
    {
        var excluded = new HashSet<string> { ".git" };
        var then = Directory.Exists(copy) ? Tree.Files(copy, excluded).Where(f => !skip(f.Relative)).ToDictionary(f => f.Relative, f => f.Full) : [];
        var now = Tree.Files(dir, excluded).Where(f => !skip(f.Relative)).ToDictionary(f => f.Relative, f => f.Full);
        return then.Keys.Union(now.Keys).Order(StringComparer.Ordinal)
            .Where(f => !then.TryGetValue(f, out var a) || !now.TryGetValue(f, out var b) || !File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)))
            .Select(f => Path.GetRelativePath(root, Path.Combine(dir, f)).Replace('\\', '/'));
    }

    /// <summary>The fixture's source as a plain tree: no .git, since the image commits it itself, so an unchanged fixture is a cached layer.</summary>
    static void CopyRepo(Fixture fixture, string dest)
    {
        if (fixture.Source.Git is not null)
        {
            if (Checkout.Materialise(fixture, dest) is { } error) throw new ProctorException($"fixture {fixture.Id}: {error}");
            Directory.Delete(Path.Combine(dest, ".git"), recursive: true);
            return;
        }
        foreach (var (relative, full) in fixture.SourceFiles())
        {
            var to = Path.Combine(dest, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(full, to);
        }
    }

    static void CopyTree(string from, string to, HashSet<string> skip)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            File.SetUnixFileMode(dest, File.GetUnixFileMode(file));
        }
        foreach (var dir in Directory.GetDirectories(from).Where(d => !skip.Contains(Path.GetFullPath(d)) && Path.GetFileName(d) != ".git"))
            CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)), skip);
    }

    /// <summary>Node 22 by nvm, as Harbor's own installs do it. NVM_DIR is explicit: under RUN's /bin/sh, nvm.sh cannot find its own directory and installs Node elsewhere.</summary>
    const string NodeInstall = """export NVM_DIR="$HOME/.nvm" && curl -fsSL https://raw.githubusercontent.com/nvm-sh/nvm/v0.40.2/install.sh | env -u NODE_VERSION bash >/dev/null && . "$NVM_DIR/nvm.sh" && nvm install 22 >/dev/null && nvm alias default 22 >/dev/null""";

    static readonly JsonSerializerOptions TomlString = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A TOML basic string: JSON's escapes are TOML's.</summary>
    static string Toml(string value) => JsonSerializer.Serialize(value, TomlString);

    /// <summary>A single-quoted shell word.</summary>
    static string Shell(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
