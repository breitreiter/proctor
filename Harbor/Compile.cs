using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Proctor;

// A suite's agent arms as Harbor tasks: one task directory per arm and task, under the experiment, and one job file
// per cell. Only the fixture's repo/ enters the image the agent runs in; its checks, the suite and the expectations go
// to tests/, which Harbor copies into the separate verifier container only.

/// <summary>What compile needs from the machine: where the agent's model calls go, resolved. The host is the one name the agent may reach.</summary>
record AgentRoute(string BaseUrl, string? ApiKey)
{
    public string Host => new Uri(BaseUrl).Host;
}

static class HarborCompile
{
    /// <summary>Where the checkout is in the image, for the agent and the verifier alike.</summary>
    public const string Workdir = "/app";
    /// <summary>What Harbor's verifier collect step writes and the verifier applies: the run's change against the fixture commit.</summary>
    const string DiffInContainer = "/logs/artifacts/diff.patch";

    /// <summary>Every agent arm's tasks, under the experiment. Returns the tree's hash, which the experiment records and a resume checks.</summary>
    public static string Tasks(string experimentDir, Suite suite, string experimentId, Func<Arm, AgentRoute> route)
    {
        foreach (var arm in suite.Arms.Where(a => a.IsAgent))
            foreach (var c in suite.Tasks)
                Task(Layout.HarborTask(experimentDir, arm.Id!, c.Id!), suite, arm, c, experimentId, route(arm));
        return TreeHash(experimentDir);
    }

    public static string TreeHash(string experimentDir) => Tree.Hash(Path.Combine(experimentDir, Layout.HarborDir, "tasks"), new HashSet<string> { ".git" });

    /// <summary>One Harbor task directory: instruction.md, task.toml, environment/ (the image) and tests/ (the verifier).</summary>
    public static void Task(string dir, Suite suite, Arm arm, TaskDef c, string experimentId, AgentRoute route)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        var fixture = suite.FixtureOf(c)!;
        Directory.CreateDirectory(Path.Combine(dir, "environment"));
        File.WriteAllText(Path.Combine(dir, "instruction.md"), c.Prompt!.Replace("\r\n", "\n").TrimEnd() + "\n");
        File.WriteAllText(Path.Combine(dir, "task.toml"), TaskToml(suite, c, route));
        CopyRepo(fixture, Path.Combine(dir, "environment", "repo"));
        File.WriteAllText(Path.Combine(dir, "environment", "Dockerfile"), Dockerfile(fixture, arm));

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
    /// The key is not here: it goes in the environment of the one Harbor process.
    /// </summary>
    public static JsonObject Job(Arm arm, string taskDir, string jobsDir, AgentRoute route) => new()
    {
        ["job_name"] = "job",
        ["jobs_dir"] = jobsDir,
        ["n_attempts"] = 1,
        ["n_concurrent_trials"] = 1,
        ["quiet"] = true,
        ["retry"] = new JsonObject { ["max_retries"] = 0 },
        ["agents"] = new JsonArray(new JsonObject
        {
            ["name"] = arm.Agent,
            ["model_name"] = arm.Model,
            ["kwargs"] = JsonSerializer.SerializeToNode(AgentKwargs(arm)),
            ["env"] = JsonSerializer.SerializeToNode(AgentEnv(arm, route)),
        }),
        ["tasks"] = new JsonArray(new JsonObject { ["path"] = taskDir }),
    };

    /// <summary>The agent's settings as Harbor takes them; the cell manifest records the same.</summary>
    public static Dictionary<string, string> AgentKwargs(Arm arm) => new()
    {
        ["version"] = arm.AgentVersion!,
        ["permission_mode"] = "bypassPermissions",
    };

    /// <summary>What the agent's process sees beyond Harbor's own: the base URL and every model alias set to the arm's model, explicitly.</summary>
    public static Dictionary<string, string> AgentEnv(Arm arm, AgentRoute route) => new()
    {
        ["ANTHROPIC_BASE_URL"] = route.BaseUrl,
        ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = arm.Model!,
        ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = arm.Model!,
        ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = arm.Model!,
        ["CLAUDE_CODE_SUBAGENT_MODEL"] = arm.Model!,
    };

    /// <summary>
    /// The agent's phase reaches only the model endpoint's host, exactly as the base URL spells it (Harbor's allowlist
    /// matches names exactly); the image builds with network; the verifier has none, so the image restores packages.
    /// </summary>
    static string TaskToml(Suite suite, TaskDef c, AgentRoute route) => $"""
        version = "1.0"

        [task]
        name = {Toml($"proctor/{c.Id}")}
        description = {Toml(Suite.Describe(c))}

        [agent]
        timeout_sec = {suite.Def.Limits!.WallMs!.Value / 1000.0:0.0##}
        network_mode = "allowlist"
        allowed_hosts = [{Toml(route.Host)}]

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
    static string Dockerfile(Fixture fixture, Arm arm)
    {
        var image = fixture.Def.Image!;
        var sb = new StringBuilder();
        sb.Append($"# Compiled by proctor from fixture {fixture.Id} ({fixture.Hash}) for agent {arm.Agent} {arm.AgentVersion}.\n");
        sb.Append($"FROM {image.From}\n");
        sb.Append("ENV NO_COLOR=1");
        foreach (var (k, v) in image.Env ?? []) sb.Append($" {k}={JsonSerializer.Serialize(v)}");
        sb.Append('\n');
        sb.Append($"COPY repo {Workdir}\n");
        sb.Append($"WORKDIR {Workdir}\n");
        sb.Append($"RUN git init -q && git add -A && git -c user.name=proctor -c user.email=proctor@localhost commit -qm {Shell($"fixture {fixture.Id} {fixture.Hash}")}\n");
        foreach (var command in image.Run ?? []) sb.Append($"RUN {command}\n");
        sb.Append(AgentInstall(arm));
        return sb.ToString();
    }

    static string AgentInstall(Arm arm) => arm.Agent switch
    {
        Arm.ClaudeCode => $"""
            RUN apt-get update -q && apt-get install -y -q --no-install-recommends procps >/dev/null && rm -rf /var/lib/apt/lists/* \
             && curl -fsSL https://downloads.claude.ai/claude-code-releases/bootstrap.sh | bash -s -- {arm.AgentVersion} \
             && /root/.local/bin/claude --version

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

    static readonly JsonSerializerOptions TomlString = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>A TOML basic string: JSON's escapes are TOML's.</summary>
    static string Toml(string value) => JsonSerializer.Serialize(value, TomlString);

    /// <summary>A single-quoted shell word.</summary>
    static string Shell(string value) => "'" + value.Replace("'", "'\\''") + "'";
}
