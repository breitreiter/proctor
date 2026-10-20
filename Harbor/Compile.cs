using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

// A suite's agent arms as Harbor tasks: one task directory per arm and task, under the experiment, and one job file
// per cell. Only the fixture's repo/ enters the image the agent runs in; its checks, the suite and the expectations go
// to tests/, which Harbor copies into the separate verifier container only.

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

/// <summary>An nb cell's own inputs to its job: the program compiled on the host for this cell and nb's config (host paths the agent uploads).</summary>
record NbCell(string Program, string? Config);

static class HarborCompile
{
    /// <summary>Where the checkout is in the image, for the agent and the verifier alike.</summary>
    public const string Workdir = "/app";
    /// <summary>What Harbor's verifier collect step writes and the verifier applies: the run's change against the fixture commit.</summary>
    const string DiffInContainer = "/logs/artifacts/diff.patch";
    /// <summary>
    /// The agents proctor brings itself, as Harbor imports them from the experiment's harbor/agents/ on PYTHONPATH: nb,
    /// and Qwen Code, whose Harbor agent reinstalls from the network on every trial (proctor's checks the image's pin
    /// instead). Claude Code and Codex are Harbor's own, which skip the install when the image has the pin.
    /// </summary>
    static readonly Dictionary<string, (string Import, string File)> OwnAgents = new()
    {
        [Arm.Nb] = ("nb_agent:NbAgent", "nb_agent.py"),
        [Arm.QwenCode] = ("qwen_code_agent:QwenCodeAgent", "qwen_code_agent.py"),
    };

    /// <summary>Whether Harbor imports this arm's agent from the experiment rather than naming one of its own.</summary>
    public static bool IsOwnAgent(Arm arm) => OwnAgents.ContainsKey(arm.Agent!);

    /// <summary>
    /// Every agent arm's tasks, under the experiment, and proctor's own Harbor agents beside them for the arms that use one. Returns the
    /// tree's hash, which the experiment records and a resume checks. The nb image is the tag of proctor's nb layer.
    /// </summary>
    public static string Tasks(string experimentDir, Suite suite, string experimentId, Func<Arm, AgentRoute> route, string? nbImage = null)
    {
        foreach (var arm in suite.Arms.Where(a => a.IsAgent))
            foreach (var c in suite.Tasks)
                Task(Layout.HarborTask(experimentDir, arm.Id!, c.Id!), suite, arm, c, experimentId, route(arm), nbImage);
        foreach (var agent in suite.Arms.Where(a => a.IsAgent && IsOwnAgent(a)).Select(a => a.Agent!).Distinct())
        {
            var agents = AgentsDir(experimentDir);
            Directory.CreateDirectory(agents);
            var file = OwnAgents[agent].File;
            using var source = typeof(HarborCompile).Assembly.GetManifestResourceStream(file)!;
            using var dest = File.Create(Path.Combine(agents, file));
            source.CopyTo(dest);
        }
        return TreeHash(experimentDir);
    }

    /// <summary>Where proctor's own Harbor agents are compiled to: the directory Harbor's process gets on PYTHONPATH.</summary>
    public static string AgentsDir(string experimentDir) => Path.Combine(experimentDir, Layout.HarborDir, "agents");

    /// <summary>The compiled tasks, plus the agents directory when there is one (an experiment of Harbor's own agents only hashes as before).</summary>
    public static string TreeHash(string experimentDir)
    {
        var tasks = Tree.Hash(Path.Combine(experimentDir, Layout.HarborDir, "tasks"), new HashSet<string> { ".git" });
        var agents = AgentsDir(experimentDir);
        return Directory.Exists(agents) ? $"{tasks}+{Tree.Hash(agents, new HashSet<string> { "__pycache__" })}" : tasks;
    }

    /// <summary>One Harbor task directory: instruction.md, task.toml, environment/ (the image) and tests/ (the verifier).</summary>
    public static void Task(string dir, Suite suite, Arm arm, TaskDef c, string experimentId, AgentRoute route, string? nbImage = null)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var fixture = suite.FixtureOf(c)!;
        Directory.CreateDirectory(Path.Combine(dir, "environment"));
        File.WriteAllText(Path.Combine(dir, "instruction.md"), c.Prompt!.Replace("\r\n", "\n").TrimEnd() + "\n");
        File.WriteAllText(Path.Combine(dir, "task.toml"), TaskToml(suite, c, route));
        CopyRepo(fixture, Path.Combine(dir, "environment", "repo"));
        File.WriteAllText(Path.Combine(dir, "environment", "Dockerfile"), Dockerfile(fixture, arm, nbImage));

        var tests = Path.Combine(dir, "tests");
        CopyTree(suite.Dir, Path.Combine(tests, "suite"), []);
        CopyTree(fixture.Dir, Path.Combine(tests, "fixture"), [Path.GetFullPath(fixture.SourceDir ?? Path.Combine(fixture.Dir, "repo"))]);
        File.WriteAllText(Path.Combine(tests, "env.sh"), VerifierEnv(suite, arm, c, experimentId));
        var test = Path.Combine(tests, "test.sh");
        File.WriteAllText(test, TestScript(suite, c));
        File.SetUnixFileMode(test, File.GetUnixFileMode(test) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Harbor's job for one cell: one trial of one task by one agent. Every setting Harbor would otherwise default is
    /// stated: the pinned version, the permission mode, each model alias (Harbor maps them all to one model on a custom
    /// base URL; proctor says so rather than inherits it), no retries (a retry is proctor's attempt), one trial at a time.
    /// The key is not here: it goes in the environment of the one Harbor process. proctor's own agents (nb, Qwen
    /// Code) are named by import path; nb's kwargs carry the cell's host-compiled program and nb's config.
    /// </summary>
    public static JsonObject Job(Arm arm, string taskDir, string jobsDir, AgentRoute route, NbCell? nb = null)
    {
        var agent = new JsonObject();
        if (OwnAgents.TryGetValue(arm.Agent!, out var own)) agent["import_path"] = own.Import;
        else agent["name"] = arm.Agent;
        agent["model_name"] = arm.Model;
        agent["kwargs"] = JsonSerializer.SerializeToNode(AgentKwargs(arm, route, nb));
        agent["env"] = JsonSerializer.SerializeToNode(AgentEnv(arm, route));
        return new()
        {
            ["job_name"] = "job",
            ["jobs_dir"] = jobsDir,
            ["n_attempts"] = 1,
            ["n_concurrent_trials"] = 1,
            ["quiet"] = true,
            ["retry"] = new JsonObject { ["max_retries"] = 0 },
            ["agents"] = new JsonArray(agent),
            ["tasks"] = new JsonArray(new JsonObject { ["path"] = taskDir }),
        };
    }

    /// <summary>
    /// The agent's settings as Harbor takes them; the cell manifest records the same. A vendor agent's: its pin, and
    /// Claude Code's permission mode (Codex and Qwen Code have no setting for it: Harbor runs them with approvals and
    /// sandbox bypassed, always). nb's: the program and config on the host, the work directory, and the key's
    /// variable by name.
    /// </summary>
    public static Dictionary<string, string> AgentKwargs(Arm arm, AgentRoute route, NbCell? nb = null)
    {
        switch (arm.Agent)
        {
            case Arm.ClaudeCode: return new() { ["version"] = arm.AgentVersion!, ["permission_mode"] = "bypassPermissions" };
            case Arm.Codex when route.Config is not null: return new() { ["version"] = arm.AgentVersion!, ["config"] = route.Config };
            case Arm.Codex or Arm.QwenCode: return new() { ["version"] = arm.AgentVersion! };
        }
        var kwargs = new Dictionary<string, string> { ["program"] = nb!.Program, ["workdir"] = Workdir };
        if (nb.Config is not null) kwargs["config"] = nb.Config;
        if (route.KeyEnv is not null) kwargs["secret"] = route.KeyEnv;
        return kwargs;
    }

    /// <summary>The variable a vendor agent's key travels in, in Harbor's process environment.</summary>
    public static string KeyEnv(string agent) => agent == Arm.ClaudeCode ? "ANTHROPIC_API_KEY" : "OPENAI_API_KEY";

    /// <summary>
    /// What the agent's process sees beyond Harbor's own. Claude Code: the base URL and every model alias set to the
    /// arm's model, explicitly. Codex and Qwen Code: the base URL (Harbor writes Codex's into its config.toml; Qwen
    /// Code takes the model from model_name). nb: the variables its config's provider entry names for its endpoint.
    /// </summary>
    public static Dictionary<string, string> AgentEnv(Arm arm, AgentRoute route) => arm.Agent switch
    {
        Arm.ClaudeCode => new()
        {
            ["ANTHROPIC_BASE_URL"] = route.BaseUrl!,
            ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = arm.Model!,
            ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = arm.Model!,
            ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = arm.Model!,
            ["CLAUDE_CODE_SUBAGENT_MODEL"] = arm.Model!,
        },
        Arm.Codex or Arm.QwenCode => new() { ["OPENAI_BASE_URL"] = route.BaseUrl! },
        _ => new(route.Env ?? []),
    };

    /// <summary>
    /// The agent's phase reaches only the model endpoint's host, exactly as the base URL spells it (Harbor's allowlist
    /// matches names exactly), or nothing when there is no endpoint; the image builds with network; the verifier has
    /// none, so the image restores packages.
    /// </summary>
    static string TaskToml(Suite suite, TaskDef c, AgentRoute route) => $"""
        version = "1.0"

        [task]
        name = {Toml($"proctor/{c.Id}")}
        description = {Toml(Suite.Describe(c))}

        [agent]
        timeout_sec = {suite.Def.Limits!.WallMs!.Value / 1000.0:0.0##}
        {(route.Host is { } host ? $"network_mode = \"allowlist\"\nallowed_hosts = [{Toml(host)}]" : "network_mode = \"no-network\"")}

        [environment]
        build_timeout_sec = 1800.0
        network_mode = "public"

        [verifier]
        timeout_sec = 900.0
        environment_mode = "separate"
        network_mode = "no-network"

        [[verifier.collect]]
        command = {Toml($"cd {Workdir} && git add -A && git diff --cached --binary HEAD > {DiffInContainer}")}

        """;

    /// <summary>
    /// The fixture as a commit in the work directory, so the diff after the agent is the change; the fixture's own
    /// setup after it; then the arm's agent, pinned, so a trial installs nothing and needs no network to start.
    /// </summary>
    static string Dockerfile(Fixture fixture, Arm arm, string? nbImage)
    {
        var image = fixture.Def.Image!;
        var sb = new StringBuilder();
        sb.Append($"# Compiled by proctor from fixture {fixture.Id} ({fixture.Hash}) for agent {arm.Agent} {(arm.IsVendor ? arm.AgentVersion : nbImage)}.\n");
        sb.Append($"FROM {image.From}\n");
        sb.Append("ENV NO_COLOR=1");
        foreach (var (k, v) in image.Env ?? []) sb.Append($" {k}={JsonSerializer.Serialize(v)}");
        sb.Append('\n');
        sb.Append($"COPY repo {Workdir}\n");
        sb.Append($"WORKDIR {Workdir}\n");
        sb.Append($"RUN git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm {Shell($"fixture {fixture.Id} {fixture.Hash}")}\n");
        foreach (var command in image.Run ?? []) sb.Append($"RUN {command}\n");
        sb.Append(AgentInstall(arm, nbImage));
        return sb.ToString();
    }

    /// <summary>The agent at its pin. nb comes from proctor's nb layer (the publish proctor.json names, tagged by its hash), so the image holds that binary and nothing it was built from.</summary>
    static string AgentInstall(Arm arm, string? nbImage) => arm.Agent switch
    {
        Arm.Nb => $"""
            COPY --from={nbImage} /opt/nb /opt/nb
            ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
            RUN /opt/nb/nb --version

            """,
        Arm.ClaudeCode => $"""
            RUN apt-get update -q && apt-get install -y -q --no-install-recommends procps >/dev/null && rm -rf /var/lib/apt/lists/* \
             && curl -fsSL https://downloads.claude.ai/claude-code-releases/bootstrap.sh | bash -s -- {arm.AgentVersion} \
             && /root/.local/bin/claude --version

            """,
        // Node from nvm under ~/.nvm, where Harbor's agents look for it, so their version checks find the pin.
        Arm.Codex => $"""
            RUN apt-get update -q && apt-get install -y -q --no-install-recommends ripgrep procps >/dev/null && rm -rf /var/lib/apt/lists/* \
             && {NodeInstall} \
             && npm install -g @openai/codex@{arm.AgentVersion} >/dev/null && codex --version \
             && ln -sf "$(command -v node)" /usr/local/bin/node && ln -sf "$(command -v codex)" /usr/local/bin/codex

            """,
        Arm.QwenCode => $"""
            RUN {NodeInstall} \
             && npm install -g @qwen-code/qwen-code@{arm.AgentVersion} >/dev/null && qwen --version

            """,
        _ => throw new InvalidOperationException($"no install for agent {arm.Agent}"),
    };

    /// <summary>
    /// The script check contract inside the verifier: the paths the scripts see are the container's, and what has no
    /// meaning there (the cell, the sample, nb, a runner) is empty. There is no transcript in the verifier: a check that
    /// reads one cannot run there yet.
    /// </summary>
    static string VerifierEnv(Suite suite, Arm arm, TaskDef c, string experimentId)
    {
        var values = new Dictionary<string, string>
        {
            ["PROCTOR_SUITE_DIR"] = "/tests/suite",
            ["PROCTOR_FIXTURE"] = "/tests/fixture",
            ["PROCTOR_BUNDLE"] = "",
            ["PROCTOR_EXPERIMENT"] = experimentId,
            ["PROCTOR_ARM"] = arm.Id!,
            ["PROCTOR_TASK"] = c.Id!,
            ["PROCTOR_SAMPLE"] = "",
            ["PROCTOR_CELL"] = "",
            ["PROCTOR_WORK"] = Workdir,
            ["PROCTOR_WORK_MOUNT"] = Workdir,
            ["PROCTOR_BUNDLE_MOUNT"] = "",
            ["PROCTOR_TASK_JSON"] = JsonSerializer.Serialize(c, Suite.JsonOptions),
            ["PROCTOR_EXPECT"] = suite.ExpectFor(c)?.ToJsonString() ?? "{}",
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
    /// The verifier: the run's diff on the clean fixture commit, then each script check the task carries, in declared
    /// order. One reward key per check: 1 pass, 0 fail, 2 needs-judge; any other exit leaves the key out, which is
    /// error. Each check's output is kept as verifier/&lt;check&gt;.out and its exit code as &lt;check&gt;.exit.
    /// </summary>
    static string TestScript(Suite suite, TaskDef c)
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
        foreach (var (name, check) in suite.ChecksFor(c))
        {
            if (check.Spec["script"] is not JsonValue v || !v.TryGetValue<string>(out var script)) continue;
            var root = check.Level == "fixture" ? "/tests/fixture" : "/tests/suite";
            sb.Append($"check {Shell(name)} {Shell($"{root}/{script}")}\n");
        }
        sb.Append("""
            (IFS=,; echo "{${rewards[*]}}") > "$out/reward.json"

            """);
        return sb.ToString();
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
