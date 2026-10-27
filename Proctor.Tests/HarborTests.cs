using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Agent arms: validation, compiling a task for Harbor, ingesting a trial, and the runner with a stand-in for Harbor.</summary>
public class HarborTests
{
    const string BaseUrl = "http://library:8086/x/anthropic";

    static JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    /// <summary>harbor-unit, with proctor.json naming the agent's endpoint, a key from a variable only this test sets, and uv as given.</summary>
    static (TestRepo Repo, string KeyVar) AgentRepo(string? uv = null)
    {
        var repo = new TestRepo();
        repo.CopySuite("harbor-unit");
        var keyVar = "PROCTOR_TEST_KEY_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Environment.SetEnvironmentVariable(keyVar, "sk-test-" + keyVar);
        File.WriteAllText(Path.Combine(repo.Root, "suites", "proctor.json"), JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            agents = new Dictionary<string, object> { ["claude-code"] = new { base_url = BaseUrl, api_key = "${" + keyVar + "}" } },
            harbor = new { uv = uv ?? "uv" },
        }));
        return (repo, keyVar);
    }

    [Fact]
    public void AgentSmoke_Loads_AsOneAgentArm()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        var suite = repo.LoadSuite("harbor-unit");
        var arm = Assert.Single(suite.Arms);
        Assert.True(arm.IsAgent);
        Assert.True(suite.HasAgentArms);
        Assert.Equal("claude-code", arm.Agent);
        Assert.Equal("2.1.301", arm.AgentVersion);
        Assert.Equal("0.24.0", suite.Def.Harbor!.Version);
        Assert.Equal(900_000, suite.Def.Limits!.WallMs);
        Assert.Equal(["builds", "diff-in-scope", "tests-pass"], suite.ChecksFor(suite.Tasks[0]).Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("harness", "\"claude-code\"", "arms[0].harness", "an nb field")]
    [InlineData("provider", "\"anthropic\"", "arms[0].provider", "an nb field")]
    [InlineData("model", "\"gpt-5.4\"", "arms[0].model", "not an Anthropic model")]
    [InlineData("model", "\"anthropic/claude-haiku-5-5\"", "arms[0].model", "provider prefix")]
    [InlineData("agent_version", "null", "arms[0].agent_version", "pinned")]
    [InlineData("agent_version", "\"latest\"", "arms[0].agent_version", "not a version number")]
    [InlineData("agent", "\"cursor\"", "arms[0].agent", "unknown agent")]
    public void AgentArm_IsValidated(string field, string value, string problemField, string message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditJson("harbor-unit/suite.json", e => e["arms"]![0]![field] = JsonNode.Parse(value));
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == problemField && p.Message.Contains(message));
    }

    [Fact]
    public void AgentArm_NeedsAPinnedHarbor_AWallLimit_AndAnImageOnEveryFixture()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditJson("harbor-unit/suite.json", e => { e.Remove("harbor"); e["limits"] = JsonNode.Parse("{\"tool_calls\": 60}"); });
        repo.EditJson("../fixtures/ledger/fixture.json", f => f.Remove("image"));
        var problems = repo.Problems("harbor-unit");
        Assert.Contains(problems, p => p.Field == "harbor.version" && p.Message.Contains("required with an agent arm"));
        Assert.Contains(problems, p => p.Field == "limits.wall_ms" && p.Message.Contains("required"));
        Assert.Contains(problems, p => p.Field == "limits.tool_calls" && p.Message.Contains("not yet"));
        Assert.Contains(problems, p => p.File.EndsWith("fixture.json") && p.Field == "image");
    }

    [Fact]
    public void AgentArm_RefusesSampleHooks_AndANbSuiteRefusesTheHarborBlocks()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.WriteScript("harbor-unit/hooks/setup.sh", "#!/bin/sh\nexit 0\n");
        repo.EditJson("harbor-unit/suite.json", e => e["hooks"] = JsonNode.Parse("{\"sample\": {\"setup\": \"hooks/setup.sh\"}}"));
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == "hooks.sample" && p.Message.Contains("fixture's image"));

        repo.CopySuite("smoke");
        repo.EditJson("smoke/suite.json", e => { e["harbor"] = JsonNode.Parse("{\"version\": \"0.24.0\"}"); e["limits"] = JsonNode.Parse("{\"wall_ms\": 60000}"); });
        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.Field == "harbor" && p.Message.Contains("nothing runs through Harbor"));
        Assert.Contains(problems, p => p.Field == "limits" && p.Message.Contains("budget directives"));
    }

    [Fact]
    public void Podman_NeedsDockerComposeAndAShortNameRegistry()
    {
        Assert.Empty(HarborRun.PodmanProblems("Docker Compose version 2.40.3+ds1-0ubuntu1", """{"search":["docker.io"]}"""));

        var pc = Assert.Single(HarborRun.PodmanProblems("podman-compose version 1.6.0\npodman version 5.7.0", """{"search":["docker.io"]}"""));
        Assert.Contains("podman-compose", pc);
        Assert.Contains("PODMAN_COMPOSE_PROVIDER", pc);
        Assert.Contains("podman compose", Assert.Single(HarborRun.PodmanProblems(null, """{"search":["docker.io"]}""")));

        var shortName = Assert.Single(HarborRun.PodmanProblems("Docker Compose version v2.40.3", "{}"));
        Assert.Contains("unqualified-search-registries = [\"docker.io\"]", shortName);
        Assert.Single(HarborRun.PodmanProblems("Docker Compose version v2.40.3", """{"search":[]}"""));
        Assert.Equal(2, HarborRun.PodmanProblems(null, null).Count);

        // the banner podman prints on stderr before every compose call reaches Harbor's exec output
        var banner = Assert.Single(HarborRun.PodmanProblems("Docker Compose version 2.40.3", """{"search":["docker.io"]}""",
            "\u001b[4m>>>> Executing external compose provider \"/usr/libexec/docker/cli-plugins/docker-compose\". Please see podman-compose(1) for how to disable this message. <<<<\n"));
        Assert.Contains("compose_warning_logs = false", banner);
        Assert.Empty(HarborRun.PodmanProblems("Docker Compose version 2.40.3", """{"search":["docker.io"]}""", ""));
    }

    [Fact]
    public void Compile_WritesTheTask_WithTheRepoInTheImage_AndTheChecksOnlyInTheVerifier()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        var suite = repo.LoadSuite("harbor-unit");
        var expDir = Path.Combine(repo.Root, "runs", "exp");
        var hash = HarborCompile.Tasks(expDir, suite, "exp", _ => new AgentRoute(BaseUrl, "sk-secret"));
        Assert.Equal(hash, HarborCompile.TreeHash(expDir));
        var task = Layout.HarborTask(expDir, "cc-haiku", "fix-null-deref");

        Assert.Equal(suite.Tasks[0].Prompt!.TrimEnd() + "\n", File.ReadAllText(Path.Combine(task, "instruction.md")));

        var toml = File.ReadAllText(Path.Combine(task, "task.toml"));
        Assert.Contains("name = \"proctor/fix-null-deref\"", toml);
        Assert.Contains("timeout_sec = 900.0\nnetwork_mode = \"allowlist\"\nallowed_hosts = [\"library\"]", toml);
        Assert.Contains("environment_mode = \"separate\"\nnetwork_mode = \"no-network\"", toml);
        Assert.Contains("git diff --cached --binary HEAD > /logs/artifacts/diff.patch", toml);

        var dockerfile = File.ReadAllText(Path.Combine(task, "environment", "Dockerfile"));
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0\n", dockerfile);
        Assert.Contains("DOTNET_NOLOGO=\"1\"", dockerfile);
        Assert.Contains("COPY repo /app\nWORKDIR /app\nRUN git init -q", dockerfile);
        Assert.Contains("RUN find . -name '*.csproj' -exec dotnet restore -v q {} \\;\n", dockerfile);
        Assert.Contains("bootstrap.sh | bash -s -- 2.1.301", dockerfile);

        // Only repo/ enters the image's build context: no checks, no .git, nothing excluded.
        var context = Path.Combine(task, "environment", "repo");
        Assert.True(File.Exists(Path.Combine(context, "Ledger", "Statement.cs")));
        Assert.False(Directory.Exists(Path.Combine(context, ".git")));
        Assert.False(Directory.Exists(Path.Combine(context, "checks")));
        Assert.Empty(Directory.GetDirectories(context, "obj", SearchOption.AllDirectories));

        // The checks, the suite and the expectations are the verifier's.
        var tests = Path.Combine(task, "tests");
        Assert.True(File.Exists(Path.Combine(tests, "fixture", "checks", "builds.sh")));
        Assert.False(Directory.Exists(Path.Combine(tests, "fixture", "repo")));
        Assert.True(File.Exists(Path.Combine(tests, "suite", "tasks", "fix-null-deref.json")));
        var test = File.ReadAllText(Path.Combine(tests, "test.sh"));
        Assert.Contains("check 'builds' '/tests/fixture/checks/builds.sh'\n", test);
        Assert.Contains("check 'tests-pass' '/tests/fixture/checks/tests-pass.sh'\n", test);
        Assert.DoesNotContain("diff-in-scope", test);
        Assert.True(File.GetUnixFileMode(Path.Combine(tests, "test.sh")).HasFlag(UnixFileMode.UserExecute));
        var env = File.ReadAllText(Path.Combine(tests, "env.sh"));
        Assert.Contains("export PROCTOR_WORK='/app'\n", env);
        Assert.Contains("export PROCTOR_TASK='fix-null-deref'\n", env);
        Assert.Contains("\"files_touched\":{\"paths\":[\"Ledger/**\"]", env);

        // Nothing compiled holds the key.
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(expDir, "harbor"), "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("sk-secret"));
    }

    [Fact]
    public void Job_StatesEverySettingHarborWouldDefault_AndCarriesNoKey()
    {
        var arm = new Arm("cc-haiku", null, null, null, "claude-haiku-5-5", 1, null, Agent: "claude-code", AgentVersion: "2.1.301");
        var job = HarborCompile.Job(arm, "/t", "/j", new AgentRoute(BaseUrl, "sk-secret"));
        Assert.Equal(1, job["n_attempts"]!.GetValue<int>());
        Assert.Equal(0, job["retry"]!["max_retries"]!.GetValue<int>());
        Assert.Equal("docker", job["environment"]!["type"]!.GetValue<string>());
        Assert.Equal("podman", HarborCompile.Job(arm, "/t", "/j", new AgentRoute(BaseUrl, "sk-secret"), engine: "podman")["environment"]!["type"]!.GetValue<string>());
        var agent = job["agents"]![0]!;
        Assert.Equal("claude-code", agent["name"]!.GetValue<string>());
        Assert.Equal("claude-haiku-5-5", agent["model_name"]!.GetValue<string>());
        Assert.Equal("2.1.301", agent["kwargs"]!["version"]!.GetValue<string>());
        Assert.Equal("bypassPermissions", agent["kwargs"]!["permission_mode"]!.GetValue<string>());
        Assert.Equal(BaseUrl, agent["env"]!["ANTHROPIC_BASE_URL"]!.GetValue<string>());
        foreach (var alias in new[] { "ANTHROPIC_DEFAULT_OPUS_MODEL", "ANTHROPIC_DEFAULT_SONNET_MODEL", "ANTHROPIC_DEFAULT_HAIKU_MODEL", "CLAUDE_CODE_SUBAGENT_MODEL" })
            Assert.Equal("claude-haiku-5-5", agent["env"]![alias]!.GetValue<string>());
        Assert.DoesNotContain("sk-secret", job.ToJsonString());
    }

    // A trial as Harbor 0.24.0 writes one: result.json, the agent's logs with the ATIF trajectory, the artifacts, the verifier.

    static readonly Arm CcArm = new("cc-haiku", null, null, null, "claude-haiku-5-5", 1, null, Agent: "claude-code", AgentVersion: "2.1.301");

    static string Trajectory(params string[] tools) => new JsonObject
    {
        ["schema_version"] = "ATIF-v1.7",
        ["agent"] = new JsonObject { ["name"] = "claude-code", ["version"] = "2.1.301" },
        ["steps"] = new JsonArray(
            new JsonObject { ["step_id"] = 1, ["source"] = "user", ["message"] = "fix it" },
            new JsonObject
            {
                ["step_id"] = 2, ["source"] = "agent", ["message"] = "",
                ["tool_calls"] = new JsonArray(tools.Select((t, i) => (JsonNode)new JsonObject { ["tool_call_id"] = $"c{i}", ["function_name"] = t, ["arguments"] = new JsonObject() }).ToArray()),
            },
            new JsonObject { ["step_id"] = 3, ["source"] = "agent", ["message"] = "Fixed; 6 of 6 tests pass." }),
        ["final_metrics"] = new JsonObject { ["total_prompt_tokens"] = 1200, ["total_completion_tokens"] = 300, ["total_cached_tokens"] = 800, ["total_cost_usd"] = 0.004 },
    }.ToJsonString();

    static string WriteTrial(string dir, string? exception = null, string version = "2.1.301", string? trajectory = null)
    {
        Directory.CreateDirectory(Path.Combine(dir, "agent"));
        Directory.CreateDirectory(Path.Combine(dir, "artifacts", "logs", "artifacts"));
        Directory.CreateDirectory(Path.Combine(dir, "verifier"));
        File.WriteAllText(Path.Combine(dir, "result.json"), new JsonObject
        {
            ["task_name"] = "proctor/fix-null-deref",
            ["agent_info"] = new JsonObject { ["name"] = "claude-code", ["version"] = version },
            ["agent_result"] = new JsonObject { ["n_input_tokens"] = 1200, ["n_cache_tokens"] = 800, ["n_output_tokens"] = 300, ["cost_usd"] = 0.004 },
            ["agent_execution"] = new JsonObject { ["started_at"] = "2026-10-16T21:59:40.250000Z", ["finished_at"] = "2026-10-16T22:00:21.750000Z" },
            ["exception_info"] = exception is null ? null : new JsonObject { ["exception_type"] = exception, ["exception_message"] = "it went wrong\nat line 1" },
        }.ToJsonString());
        if (trajectory is not "") File.WriteAllText(Path.Combine(dir, "agent", "trajectory.json"), trajectory ?? Trajectory("Read", "Edit", "Bash"));
        File.WriteAllText(Path.Combine(dir, "agent", "claude-code.txt"), "{\"type\":\"result\"}\n");
        File.WriteAllText(Path.Combine(dir, "artifacts", "logs", "artifacts", "diff.patch"), "diff --git a/Ledger/Statement.cs b/Ledger/Statement.cs\n");
        File.WriteAllText(Path.Combine(dir, "verifier", "reward.json"), "{\"builds\": 1, \"tests-pass\": 1}\n");
        File.WriteAllText(Path.Combine(dir, "verifier", "builds.out"), "dotnet build: 0 errors\n");
        return dir;
    }

    static Manifest NewManifest() => new()
    {
        RunId = "r", Experiment = "e", Arm = "cc-haiku", Task = "fix-null-deref", Sample = 1, Host = "h", Versions = [], SuiteHash = "s", ProgramHash = "p",
        Agent = new AgentRun("claude-code", null, "claude-haiku-5-5", [], [], "0.24.0", null),
    };

    [Fact]
    public void Ingest_LiftsTheTrialIntoTheCell_AndCountsByProctorsDefinitions()
    {
        using var repo = new TestRepo();
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"));
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        var manifest = NewManifest();

        Assert.Null(Ingest.Trial(trial, cell, CcArm, manifest, CcArm.AgentVersion!));
        Assert.Equal(["agent", "core.json", "diff.patch", "reward.json", "trajectory.json", "verifier"],
            Directory.GetFileSystemEntries(cell).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(cell, "agent", "claude-code.txt")));
        Assert.True(File.Exists(Path.Combine(cell, "verifier", "builds.out")));
        Assert.Equal("2.1.301", manifest.Agent!.Version);
        Assert.Equal(1200, manifest.Agent.InputTokens);
        Assert.Equal(0.004, manifest.Agent.CostUsd);
        Assert.Equal(Path.Combine("..", "trial"), manifest.Agent.Trial);

        var core = ReadJson(Path.Combine(cell, "core.json"));
        Assert.Equal("ok", core["exit_reason"]!.GetValue<string>());
        Assert.Equal(2, core["turns"]!.GetValue<int>());
        Assert.Equal(3, core["tool_calls"]!.GetValue<int>());
        Assert.Equal(0, core["denied"]!.GetValue<int>());
        Assert.Equal(1, core["kinds"]!["read"]!.GetValue<int>());
        Assert.Equal(1, core["kinds"]!["edit"]!.GetValue<int>());
        Assert.Equal(1, core["kinds"]!["shell"]!.GetValue<int>());
        Assert.Equal(1200, core["input_tokens"]!.GetValue<long>());
        Assert.Equal(41_500, core["duration_ms"]!.GetValue<long>());   // the agent's phase, not the trial's
    }

    [Fact]
    public void Ingest_AVendorRunWithNoTurn_IsAnOutage_NotARun()
    {
        using var repo = new TestRepo();
        var noTurn = new JsonObject { ["schema_version"] = "ATIF-v1.7", ["steps"] = new JsonArray(new JsonObject { ["step_id"] = 1, ["source"] = "user", ["message"] = "fix it" }) };
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"), "AgentTimeoutError", trajectory: noTurn.ToJsonString());
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        Assert.StartsWith("the agent took no turn (time_budget)", Ingest.Trial(trial, cell, CcArm, NewManifest(), CcArm.AgentVersion!));
    }

    [Fact]
    public void Ingest_CountsSubagentCalls()
    {
        var trajectory = JsonNode.Parse(Trajectory("Task"))!.AsObject();
        trajectory["subagent_trajectories"] = new JsonArray(JsonNode.Parse(Trajectory("Grep", "Read")));
        var (core, unmapped) = Ingest.Count(trajectory, "claude-code", "ok");
        Assert.Null(unmapped);
        Assert.Equal(3, core!.ToolCalls);
        Assert.Equal(4, core.Turns);
        Assert.Equal(1, core.Kinds["agent"]);
        Assert.Equal(1, core.Kinds["search"]);
    }

    [Theory]
    [InlineData("AgentTimeoutError", null, "time_budget")]
    [InlineData("NonZeroAgentExitCodeError", null, "agent_error")]
    [InlineData("EnvironmentStartTimeoutError", "harbor: EnvironmentStartTimeoutError: it went wrong", null)]
    public void Ingest_FailedIsInfrastructure_ATimeoutIsACompletedRun(string exception, string? failure, string? exitReason)
    {
        using var repo = new TestRepo();
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"), exception);
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        Assert.Equal(failure, Ingest.Trial(trial, cell, CcArm, NewManifest(), CcArm.AgentVersion!));
        if (exitReason is not null) Assert.Equal(exitReason, ReadJson(Path.Combine(cell, "core.json"))["exit_reason"]!.GetValue<string>());
    }

    [Fact]
    public void Ingest_IsStrict_AboutTheVersion_AndTheTools()
    {
        using var repo = new TestRepo();
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        Assert.Equal("agent_version_mismatch: the trial ran claude-code 2.1.302, the arm pins 2.1.301",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t1"), version: "2.1.302"), cell, CcArm, NewManifest(), CcArm.AgentVersion!));
        Assert.Equal("adapter_unmapped: tool 'mcp__fs__read' has no kind for claude-code",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t2"), trajectory: Trajectory("Read", "mcp__fs__read")), cell, CcArm, NewManifest(), CcArm.AgentVersion!));
        Assert.Equal("adapter_unmapped: the trial has no agent/trajectory.json",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t3"), trajectory: ""), cell, CcArm, NewManifest(), CcArm.AgentVersion!));
        var noDiff = WriteTrial(Path.Combine(repo.Root, "t5"));
        File.Delete(Path.Combine(noDiff, "artifacts", "logs", "artifacts", "diff.patch"));
        Assert.StartsWith("the trial collected no diff", Ingest.Trial(noDiff, cell, CcArm, NewManifest(), CcArm.AgentVersion!));
        Assert.Equal("agent exited without a trajectory: NonZeroAgentExitCodeError: it went wrong",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t4"), "NonZeroAgentExitCodeError", trajectory: ""), cell, CcArm, NewManifest(), CcArm.AgentVersion!));
    }

    /// <summary>
    /// A stand-in for `uv tool run harbor@… run --config job.json`: it records its argv and the two variables proctor
    /// sets, then writes one trial where the job file says. The trial is the one the ingest tests use.
    /// </summary>
    static string FakeUv(TestRepo repo, string trialScript) => repo.WriteScript("fake-uv.sh", $$"""
        #!/usr/bin/env bash
        set -euo pipefail
        echo "$*" > "{{repo.Root}}/uv.argv"
        echo "telemetry=${HARBOR_TELEMETRY:-} key=${ANTHROPIC_API_KEY:-}" > "{{repo.Root}}/uv.env"
        jobs=$(jq -r .jobs_dir job.json)
        t="$jobs/job/fix-null-deref__abc123"
        mkdir -p "$t/agent" "$t/artifacts/logs/artifacts" "$t/verifier"
        {{trialScript}}
        """);

    const string GoodTrial = """
        echo '{"agent_info": {"name": "claude-code", "version": "2.1.301"}, "agent_result": {"n_input_tokens": 10, "n_output_tokens": 5, "cost_usd": 0.001}, "exception_info": null}' > "$t/result.json"
        echo '{"steps": [{"step_id": 1, "source": "agent", "message": "done", "tool_calls": [{"tool_call_id": "a", "function_name": "Edit", "arguments": {}}]}]}' > "$t/agent/trajectory.json"
        echo 'diff --git a/x b/x' > "$t/artifacts/logs/artifacts/diff.patch"
        echo '{"builds": 1, "tests-pass": 0}' > "$t/verifier/reward.json"
        echo 'dotnet build: 0 errors' > "$t/verifier/builds.out"; echo 0 > "$t/verifier/builds.exit"
        echo '1 of 6 tests failed: Parse_LineWithoutMemo_HasNullMemo' > "$t/verifier/tests-pass.out"; echo 1 > "$t/verifier/tests-pass.exit"
        """;

    [Fact]
    public void Run_AnAgentArm_CompilesOnce_RunsHarborPerCell_AndIngests()
    {
        using var scratch = new TestRepo();
        var (repo, keyVar) = AgentRepo(FakeUv(scratch, GoodTrial));
        using var _r = repo;
        var suite = repo.LoadSuite("harbor-unit");
        var id = Runner.Start(repo.Root, suite, Suite.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null);
        var expDir = Layout.Experiment(repo.Root, id);

        var experiment = ReadJson(Path.Combine(expDir, "experiment.json"));
        Assert.Equal("0.24.0", experiment["versions"]!["harbor"]!.GetValue<string>());
        Assert.Equal("0.24.0", experiment["harbor"]!["version"]!.GetValue<string>());
        Assert.Equal(HarborCompile.TreeHash(expDir), experiment["harbor"]!["tasks_hash"]!.GetValue<string>());

        var cell = Layout.Cell(expDir, "cc-haiku", "fix-null-deref", 1);
        Assert.Equal("completed", File.ReadAllText(Path.Combine(cell, "status")).Trim());
        var manifest = ReadJson(Path.Combine(cell, "manifest.json"));
        Assert.Equal("atif", manifest["transcript"]!["format"]!.GetValue<string>());
        Assert.Equal("trajectory.json", manifest["transcript"]!["file"]!.GetValue<string>());
        Assert.Null(manifest["harness"]);
        Assert.Null(manifest["provider"]);
        Assert.Equal("claude-code", manifest["agent"]!["name"]!.GetValue<string>());
        Assert.Equal("2.1.301", manifest["agent"]!["version"]!.GetValue<string>());
        Assert.Equal("0.24.0", manifest["agent"]!["harbor"]!.GetValue<string>());
        Assert.Equal("bypassPermissions", manifest["agent"]!["kwargs"]!["permission_mode"]!.GetValue<string>());
        Assert.Equal("0.24.0", manifest["versions"]!["harbor"]!.GetValue<string>());
        Assert.Equal(1, ReadJson(Path.Combine(cell, "core.json"))["tool_calls"]!.GetValue<int>());
        Assert.Equal("{\"builds\": 1, \"tests-pass\": 0}", File.ReadAllText(Path.Combine(cell, "reward.json")).Trim());

        Assert.Equal("tool run --exclude-newer 7 days harbor@0.24.0 run --config job.json",File.ReadAllText(Path.Combine(scratch.Root, "uv.argv")).Trim());
        Assert.Equal($"telemetry=off key=sk-test-{keyVar}", File.ReadAllText(Path.Combine(scratch.Root, "uv.env")).Trim());
        var job = ReadJson(Path.Combine(cell, "harbor", "job.json"));
        Assert.Equal(Layout.HarborTask(expDir, "cc-haiku", "fix-null-deref"), job["tasks"]![0]!["path"]!.GetValue<string>());
        // The key reached Harbor's process and no file.
        Assert.DoesNotContain(Directory.GetFiles(expDir, "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains($"sk-test-{keyVar}"));

        // grade reads the script verdicts the verifier left, and runs the built-ins over the trajectory and the diff.
        var grade = Assert.Single(Grade.Experiment(repo.Root, Runner.LoadExperiment(repo.Root, id), suite, TextWriter.Null));
        Assert.False(grade.Pass);
        Assert.Equal(new Verdict(Verdict.Pass, "dotnet build: 0 errors"), grade.Checks!["builds"]);
        Assert.Equal(new Verdict(Verdict.Fail, "1 of 6 tests failed: Parse_LineWithoutMemo_HasNullMemo"), grade.Checks["tests-pass"]);
        Assert.Equal(new Verdict(Verdict.Fail, "touched 1 outside expected: x"), grade.Checks["diff-in-scope"]);
        var row = Assert.Single(Results.Collect(repo.Root, Runner.LoadExperiment(repo.Root, id), suite));
        Assert.Equal("ok", row.ExitReason);
        Assert.Equal(1, row.ToolCalls);
        Assert.Equal(15, row.Usage!.Total);
    }

    /// <summary>An agent cell as ingest leaves it, graded on its own: script verdicts from the verifier, the rest from the trajectory and the diff.</summary>
    static Dictionary<string, Verdict> GradeAgentCell(Action<string> edit, Action<JsonObject>? editSuite = null)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        if (editSuite is not null) repo.EditJson("harbor-unit/suite.json", editSuite);
        var suite = repo.LoadSuite("harbor-unit");
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"));
        File.WriteAllText(Path.Combine(trial, "verifier", "tests-pass.out"), "6 of 6 tests pass\n");
        Assert.Null(Ingest.Trial(trial, cell, CcArm, NewManifest(), CcArm.AgentVersion!));
        edit(cell);
        var context = new CellContext(suite.Dir, cell, Path.Combine(repo.Root, "work"), "e", "cc-haiku", suite.Tasks[0], 1)
            { Expect = suite.ExpectFor(suite.Tasks[0]), Fixture = suite.FixtureOf(suite.Tasks[0]), ScriptsInVerifier = true, Vendor = true };
        return Grade.Cell(context, suite, Transcript.Read(cell));
    }

    [Fact]
    public void Grade_AnAgentCell_ReadsTheVerifier_AndTheTrajectory()
    {
        var checks = GradeAgentCell(_ => { });
        Assert.Equal(new Verdict(Verdict.Pass, "dotnet build: 0 errors"), checks["builds"]);
        Assert.Equal(new Verdict(Verdict.Pass, "6 of 6 tests pass"), checks["tests-pass"]);
        Assert.Equal(new Verdict(Verdict.Pass, "touched 1, all expected"), checks["diff-in-scope"]);
    }

    [Fact]
    public void Grade_AMissingRewardKey_IsAnError_NeverAFail()
    {
        var checks = GradeAgentCell(cell =>
        {
            File.WriteAllText(Path.Combine(cell, "reward.json"), "{\"builds\": 1}");
            File.WriteAllText(Path.Combine(cell, "verifier", "tests-pass.out"), "dotnet: command not found\n");
            File.WriteAllText(Path.Combine(cell, "verifier", "tests-pass.exit"), "127\n");
        });
        Assert.Equal(new Verdict(Verdict.Error, "dotnet: command not found"), checks["tests-pass"]);

        checks = GradeAgentCell(cell =>
        {
            File.WriteAllText(Path.Combine(cell, "reward.json"), "{}");
            File.WriteAllText(Path.Combine(cell, "verifier", "apply.out"), "error: patch failed: Ledger/Statement.cs:31\n");
        });
        Assert.Equal(new Verdict(Verdict.Error, "the run's diff did not apply in the verifier: error: patch failed: Ledger/Statement.cs:31"), checks["builds"]);
    }

    [Fact]
    public void Grade_BuiltIns_ReadTheCountedCore_AndTheNudgeIsNbs()
    {
        var checks = GradeAgentCell(_ => { }, e =>
        {
            e["grading"]!["checks"]!["few-calls"] = JsonNode.Parse("{\"max_tool_calls\": 2}");
            e["grading"]!["checks"]!["exit-ok"] = JsonNode.Parse("{\"exit_reason\": \"ok\"}");
            e["grading"]!["checks"]!["not-nudged"] = JsonNode.Parse("{\"loop_nudged\": false}");
            e["grading"]!["checks"]!["answered"] = JsonNode.Parse("{\"answer_contains\": \"6 of 6\"}");
        });
        Assert.Equal(new Verdict(Verdict.Fail, "3 tool calls (max 2)"), checks["few-calls"]);
        Assert.Equal(Verdict.Pass, checks["exit-ok"].Result);
        Assert.Equal(Verdict.Pass, checks["answered"].Result);
        Assert.Equal(new Verdict(Verdict.Error, "loop_nudged is nb's nudge; a vendor agent has none to fire"), checks["not-nudged"]);
    }

    [Fact]
    public void Run_HarborWithoutATrial_FailsTheCell_AndResumeRunsItAgain()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "echo 'docker: Cannot connect to the Docker daemon' >&2; rm -rf \"$jobs\"; exit 1"));
        using var _r = repo;
        var id = Runner.Start(repo.Root, repo.LoadSuite("harbor-unit"), Suite.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null);
        var cell = Layout.Cell(Layout.Experiment(repo.Root, id), "cc-haiku", "fix-null-deref", 1);
        Assert.Equal("failed", File.ReadAllText(Path.Combine(cell, "status")).Trim());
        Assert.Equal("Harbor exited 1 without a trial: docker: Cannot connect to the Docker daemon", ReadJson(Path.Combine(cell, "manifest.json"))["status_reason"]!.GetValue<string>());

        File.WriteAllText(Path.Combine(scratch.Root, "suites", "fake-uv.sh"), File.ReadAllText(Path.Combine(scratch.Root, "suites", "fake-uv.sh")).Replace("echo 'docker: Cannot connect to the Docker daemon' >&2; rm -rf \"$jobs\"; exit 1", GoodTrial));
        Runner.Resume(repo.Root, id, null, null, TextWriter.Null);
        Assert.Equal("completed", File.ReadAllText(Path.Combine(cell, "status")).Trim());
        Assert.Equal(2, ReadJson(Path.Combine(cell, "manifest.json"))["attempts"]!.GetValue<int>());
    }

    [Fact]
    public void Resume_RefusesChangedCompiledTasks()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "exit 1"));
        using var _r = repo;
        var id = Runner.Start(repo.Root, repo.LoadSuite("harbor-unit"), Suite.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null);
        File.AppendAllText(Path.Combine(Layout.HarborTask(Layout.Experiment(repo.Root, id), "cc-haiku", "fix-null-deref"), "instruction.md"), "and also\n");
        var e = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, null, TextWriter.Null));
        Assert.Contains("have changed on disk", e.Message);
    }

    [Fact]
    public void Run_OnPodman_RecordsTheEngine_HandsItToHarbor_AndResumeRefusesAnother()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "exit 1"));
        using var _r = repo;
        var config = Path.Combine(repo.Root, "suites", "proctor.json");
        void Engine(string engine) { var c = ReadJson(config); c["harbor"]!["engine"] = engine; File.WriteAllText(config, c.ToJsonString()); }
        Engine("podman");
        var id = Runner.Start(repo.Root, repo.LoadSuite("harbor-unit"), Suite.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null);
        var expDir = Layout.Experiment(repo.Root, id);
        Assert.Equal("podman", ReadJson(Path.Combine(expDir, "experiment.json"))["harbor"]!["engine"]!.GetValue<string>());
        var job = Path.Combine(Layout.Cell(expDir, "cc-haiku", "fix-null-deref", 1), Layout.HarborDir, Layout.HarborJobFile);
        Assert.Equal("podman", ReadJson(job)["environment"]!["type"]!.GetValue<string>());

        Engine("docker");
        var e = Assert.Throws<ProctorException>(() => Runner.Resume(repo.Root, id, null, null, TextWriter.Null));
        Assert.Contains("the container engine (suites/proctor.json harbor.engine) has changed", e.Message);
    }

    [Fact]
    public void Run_WithoutTheAgentsEndpoint_FailsBeforeAnythingIsMade()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.WriteProctorConfig();
        var e = Assert.Throws<ProctorException>(() => Runner.Start(repo.Root, repo.LoadSuite("harbor-unit"), Suite.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null));
        Assert.Contains("no agents.claude-code", e.Message);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "runs")));
    }

    // Codex and Qwen Code: Harbor's agents on an OpenAI-dialect route, Qwen Code's install replaced by proctor's pin check.

    const string OpenAiUrl = "http://library:8086/x/openai/v1";
    static readonly Arm CodexArm = new("codex-mini", null, null, null, "gpt-5-mini", 1, null, Agent: "codex", AgentVersion: "0.163.0");
    static readonly Arm QwenArm = new("qwen", null, null, null, "qwen3-coder-next", 1, null, Agent: "qwen-code", AgentVersion: "0.25.2");

    [Theory]
    [InlineData("codex", "gpt-5-mini", null)]
    [InlineData("codex", "o4-mini", null)]
    [InlineData("codex", "claude-haiku-5-5", "not an OpenAI model")]
    [InlineData("qwen-code", "qwen3-coder-next", null)]
    [InlineData("qwen-code", "gpt-5-mini", "not a Qwen model")]
    [InlineData("claude-code", "qwen3-coder-next", "not an Anthropic model")]
    public void VendorArm_TakesItsVendorsModelsOnly(string agent, string model, string? message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditJson("harbor-unit/suite.json", e => { e["arms"]![0]!["agent"] = agent; e["arms"]![0]!["model"] = model; });
        var problems = repo.Problems("harbor-unit").Where(p => p.Field.StartsWith("arms[0]")).ToList();
        if (message is null) Assert.Empty(problems);
        else Assert.Contains(problems, p => p.Field == "arms[0].model" && p.Message.Contains(message));
    }

    [Fact]
    public void Config_KnowsEveryVendorAgent()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        File.WriteAllText(Path.Combine(repo.Root, "suites", "proctor.json"), """{"agents": {"codex": {"base_url": "http://x/v1"}, "qwen-code": {"base_url": "http://x/v1"}, "cursor": {"base_url": "http://x"}}}""");
        var problems = new List<Problem>();
        Suite.LoadConfig(repo.Root, problems);
        Assert.Equal(["agents.cursor"], problems.Select(p => p.Field));
    }

    [Theory]
    [InlineData("docker", null)]
    [InlineData("podman", null)]
    [InlineData("nerdctl", "unknown engine 'nerdctl'; known: docker, podman")]
    public void Config_KnowsDockerAndPodman(string engine, string? message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        File.WriteAllText(Path.Combine(repo.Root, "suites", "proctor.json"), $$$"""{"harbor": {"engine": "{{{engine}}}"}}""");
        var problems = new List<Problem>();
        Suite.LoadConfig(repo.Root, problems);
        Assert.Equal(message is null ? [] : [("harbor.engine", message)], problems.Select(p => (p.Field, p.Message)));
    }

    [Fact]
    public void Job_Codex_IsHarborsAgent_AtItsPin_OnTheBaseUrl()
    {
        var job = HarborCompile.Job(CodexArm, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("codex")));
        var agent = job["agents"]![0]!;
        Assert.Equal("codex", agent["name"]!.GetValue<string>());
        Assert.Null(agent["import_path"]);
        Assert.Equal("gpt-5-mini", agent["model_name"]!.GetValue<string>());
        Assert.Equal("""{"version":"0.163.0"}""", agent["kwargs"]!.ToJsonString());
        Assert.Equal($$"""{"OPENAI_BASE_URL":"{{OpenAiUrl}}"}""", agent["env"]!.ToJsonString());
        Assert.Equal("OPENAI_API_KEY", HarborCompile.KeyEnv("codex"));
        Assert.DoesNotContain("sk-secret", job.ToJsonString());
    }

    [Fact]
    public void Job_Codex_PassesTheRoutesNativeConfig()
    {
        var job = HarborCompile.Job(CodexArm, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("codex"), Config: "/suites/codex.toml"));
        Assert.Equal("""{"version":"0.163.0","config":"/suites/codex.toml"}""", job["agents"]![0]!["kwargs"]!.ToJsonString());
    }

    [Fact]
    public void Compile_CopiesTheFixturesServices_BesideTheImage()
    {
        using var repo = new TestRepo();
        repo.CopySuite("conformance");
        var expDir = Path.Combine(repo.Root, "runs", "exp");
        HarborCompile.Tasks(expDir, repo.LoadSuite("conformance"), "exp", _ => new AgentRoute("http://localhost:8765/v1", "stub"));
        var environment = Path.Combine(Layout.HarborTask(expDir, "codex", "ok"), "environment");

        Assert.Equal(File.ReadAllText(Path.Combine(TestRepo.SourceRoot, "fixtures", "stub", "services", "docker-compose.yaml")), File.ReadAllText(Path.Combine(environment, "docker-compose.yaml")));
        Assert.True(File.Exists(Path.Combine(environment, "stub.py")));
        Assert.True(File.Exists(Path.Combine(environment, "Dockerfile")));
        Assert.Contains("allowed_hosts = [\"localhost\"]", File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "codex", "ok"), "task.toml")));
    }

    [Theory]
    [InlineData("nowhere", "no docker-compose.yaml in nowhere")]
    [InlineData("clash", "Dockerfile and repo are the image's own names in environment/")]
    public void FixtureServices_AreValidated(string services, string message)
    {
        using var repo = new TestRepo();
        repo.CopySuite("conformance");
        Directory.CreateDirectory(Path.Combine(repo.Root, "fixtures", "stub", "clash"));
        File.WriteAllText(Path.Combine(repo.Root, "fixtures", "stub", "clash", "docker-compose.yaml"), "services: {}\n");
        File.WriteAllText(Path.Combine(repo.Root, "fixtures", "stub", "clash", "Dockerfile"), "FROM scratch\n");
        repo.EditJson("../fixtures/stub/fixture.json", f => f["image"]!["services"] = services);

        Assert.Contains(repo.Problems("conformance"), p => p.Field == "image.services" && p.Message == message);
    }

    [Fact]
    public void Job_QwenCode_ImportsProctorsAgent()
    {
        var agent = HarborCompile.Job(QwenArm, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("qwen-code")))["agents"]![0]!;
        Assert.Null(agent["name"]);
        Assert.Equal("qwen_code_agent:QwenCodeAgent", agent["import_path"]!.GetValue<string>());
        Assert.Equal("""{"version":"0.25.2"}""", agent["kwargs"]!.ToJsonString());
        Assert.Equal($$"""{"OPENAI_BASE_URL":"{{OpenAiUrl}}"}""", agent["env"]!.ToJsonString());
    }

    [Fact]
    public void Compile_CodexAndQwenCode_InstallThePinInTheImage_AndOnlyQwenCodeBringsAnAgent()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditJson("harbor-unit/suite.json", e => e["arms"] = JsonNode.Parse("""
            [{"id": "codex-mini", "agent": "codex", "agent_version": "0.163.0", "model": "gpt-5-mini"},
             {"id": "qwen", "agent": "qwen-code", "agent_version": "0.25.2", "model": "qwen3-coder-next"}]
            """));
        var suite = repo.LoadSuite("harbor-unit");
        var expDir = Path.Combine(repo.Root, "runs", "exp");
        HarborCompile.Tasks(expDir, suite, "exp", _ => new AgentRoute(OpenAiUrl, "sk-secret"));

        var codex = File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "codex-mini", "fix-null-deref"), "environment", "Dockerfile"));
        Assert.Contains("npm install -g @openai/codex@0.163.0", codex);
        Assert.Contains("ln -sf \"$(command -v codex)\" /usr/local/bin/codex", codex);
        var qwen = File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "qwen", "fix-null-deref"), "environment", "Dockerfile"));
        // nvm under RUN's /bin/sh finds its own directory only when told, and Harbor's agents look for Node in ~/.nvm.
        Assert.Contains("export NVM_DIR=\"$HOME/.nvm\" && ", qwen);
        Assert.Contains("nvm install 22", qwen);
        Assert.Contains("npm install -g @qwen-code/qwen-code@0.25.2", qwen);
        Assert.Contains("allowed_hosts = [\"library\"]", File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "qwen", "fix-null-deref"), "task.toml")));

        Assert.Equal(["qwen_code_agent.py"], Directory.GetFiles(HarborCompile.AgentsDir(expDir)).Select(Path.GetFileName));
        Assert.Contains("class QwenCodeAgent(QwenCode)", File.ReadAllText(Path.Combine(HarborCompile.AgentsDir(expDir), "qwen_code_agent.py")));
    }

    [Fact]
    public void Compile_ClaudeCodeOnly_BringsNoAgent()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        var expDir = Path.Combine(repo.Root, "runs", "exp");
        var hash = HarborCompile.Tasks(expDir, repo.LoadSuite("harbor-unit"), "exp", _ => new AgentRoute(BaseUrl, "sk-secret"));
        Assert.False(Directory.Exists(HarborCompile.AgentsDir(expDir)));
        Assert.DoesNotContain("+", hash);
    }

    [Theory]
    [InlineData("codex", new[] { "exec_command", "write_stdin", "apply_patch", "update_plan", "web_search_call" }, "shell:2 edit:1 plan:1 web:1")]
    [InlineData("qwen-code", new[] { "read_file", "glob", "grep_search", "edit", "write_file", "run_shell_command", "todo_write" }, "read:1 search:2 edit:1 write:1 shell:1 plan:1")]
    public void Count_MapsEachVendorsToolsToKinds(string agent, string[] tools, string expected)
    {
        var (core, unmapped) = Ingest.Count(JsonNode.Parse(Trajectory(tools))!.AsObject(), agent, "ok");
        Assert.Null(unmapped);
        Assert.Equal(expected.Split(' ').Order(StringComparer.Ordinal), core!.Kinds.Select(k => $"{k.Key}:{k.Value}").Order(StringComparer.Ordinal));
        Assert.Equal($"tool 'Bash' has no kind for {agent}", Ingest.Count(JsonNode.Parse(Trajectory("Bash"))!.AsObject(), agent, "ok").Unmapped);
    }

    // nb as a Harbor agent: the gate suite's harbor arm.

    static readonly Arm NbArm = new("nb-harbor", null, "nb", "Mock", "mock", 2, null, Agent: "nb");

    [Fact]
    public void NbAgentArm_IsAHarborArm_ButNotAVendors()
    {
        using var repo = new TestRepo();
        repo.CopySuite("gate");
        var suite = repo.LoadSuite("gate");
        var (bare, harbor) = (suite.Arms[0], suite.Arms[1]);
        Assert.False(bare.IsAgent);
        Assert.True(harbor.IsAgent);
        Assert.False(harbor.IsVendor);
        Assert.True(suite.HasAgentArms);
    }

    [Theory]
    [InlineData("runner", "\"nb\"", "arms[1].runner", "bare path")]
    [InlineData("agent_version", "\"0.9.0\"", "arms[1].agent_version", "the binary's")]
    [InlineData("provider", "null", "arms[1].provider", "required for agent nb")]
    [InlineData("bundle", "{\"path\": \"bundles/smoke\"}", "arms[1].bundle", "not yet")]
    [InlineData("id", "\"harbor\"", "arms[1].id", "compiled for Harbor")]
    public void NbAgentArm_IsValidated(string field, string value, string problemField, string message)
    {
        using var repo = new TestRepo();
        repo.CopySuite("gate");
        repo.EditJson("gate/suite.json", e => e["arms"]![1]![field] = JsonNode.Parse(value));
        Assert.Contains(repo.Problems("gate"), p => p.Field == problemField && p.Message.Contains(message));
    }

    [Fact]
    public void Compile_NbArm_CopiesNbsLayer_ReachesNothingOnMock_AndCompilesTheAgent()
    {
        using var repo = new TestRepo();
        repo.CopySuite("gate");
        var suite = repo.LoadSuite("gate");
        var expDir = Path.Combine(repo.Root, "runs", "exp");
        var hash = HarborCompile.Tasks(expDir, suite, "exp", _ => new AgentRoute(null, null), "proctor-nb:0123456789abcdef");
        var task = Layout.HarborTask(expDir, "nb-harbor", "writes");
        Assert.False(Directory.Exists(Layout.HarborTask(expDir, "nb-bare", "writes")));

        var toml = File.ReadAllText(Path.Combine(task, "task.toml"));
        Assert.Contains("timeout_sec = 120.0\nnetwork_mode = \"no-network\"\n\n[environment]", toml);
        var dockerfile = File.ReadAllText(Path.Combine(task, "environment", "Dockerfile"));
        Assert.Contains("COPY --from=proctor-nb:0123456789abcdef /opt/nb /opt/nb\nENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1\nRUN /opt/nb/nb --version\n", dockerfile);
        Assert.DoesNotContain("claude", dockerfile);
        Assert.Contains("check 'new-file' '/tests/suite/checks/new-file.sh'\n", File.ReadAllText(Path.Combine(task, "tests", "test.sh")));

        // The agent is compiled beside the tasks, and a changed agent is a changed tree.
        var agent = Path.Combine(HarborCompile.AgentsDir(expDir), "nb_agent.py");
        Assert.Contains("class NbAgent(BaseAgent)", File.ReadAllText(agent));
        Assert.Contains("+", hash);
        File.AppendAllText(agent, "# edited\n");
        Assert.NotEqual(hash, HarborCompile.TreeHash(expDir));
    }

    [Fact]
    public void Job_NbArm_ImportsProctorsAgent_WithTheCellsProgram_AndTheKeyByNameOnly()
    {
        var route = new AgentRoute("http://library:8086/x/qcoder/v1", "sk-secret", "LLM_GATEWAY_KEY", new() { ["LLM_GATEWAY"] = "http://library:8086/x" });
        var job = HarborCompile.Job(NbArm, "/t", "/j", route, new NbCell("/cell/program.jsonl", "/suites/nb.json"));
        var agent = job["agents"]![0]!.AsObject();
        Assert.Null(agent["name"]);
        Assert.Equal("nb_agent:NbAgent", agent["import_path"]!.GetValue<string>());
        Assert.Equal("""{"program":"/cell/program.jsonl","workdir":"/app","config":"/suites/nb.json","secret":"LLM_GATEWAY_KEY"}""", agent["kwargs"]!.ToJsonString());
        Assert.Equal("""{"LLM_GATEWAY":"http://library:8086/x"}""", agent["env"]!.ToJsonString());
        Assert.DoesNotContain("sk-secret", job.ToJsonString());
    }

    static string WriteNbTrial(string dir, string jsonl, string version = "0.9.0", string? exception = null, string exit = "0")
    {
        Directory.CreateDirectory(Path.Combine(dir, "agent"));
        Directory.CreateDirectory(Path.Combine(dir, "artifacts", "logs", "artifacts"));
        Directory.CreateDirectory(Path.Combine(dir, "verifier"));
        File.WriteAllText(Path.Combine(dir, "result.json"), new JsonObject
        {
            ["agent_info"] = new JsonObject { ["name"] = "nb", ["version"] = version },
            ["agent_execution"] = new JsonObject { ["started_at"] = "2026-10-18T10:00:00Z", ["finished_at"] = "2026-10-18T10:00:01.5Z" },
            ["exception_info"] = exception is null ? null : new JsonObject { ["exception_type"] = exception, ["exception_message"] = "timed out" },
        }.ToJsonString());
        if (jsonl.Length > 0) File.WriteAllText(Path.Combine(dir, "agent", "nb.jsonl"), jsonl);
        File.WriteAllText(Path.Combine(dir, "agent", "nb.exit"), exit + "\n");
        File.WriteAllText(Path.Combine(dir, "agent", "nb.stderr.log"), "\nUnhandled exception: provider 'Mock' not configured\n");
        File.WriteAllText(Path.Combine(dir, "artifacts", "logs", "artifacts", "diff.patch"), "");
        File.WriteAllText(Path.Combine(dir, "verifier", "reward.json"), "{\"new-file\": 0}\n");
        return dir;
    }

    static string Captured(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".jsonl"));

    [Fact]
    public void Ingest_NbTrial_ConvertsNbsLogToATrajectory_AndCountsIt()
    {
        using var repo = new TestRepo();
        var trial = WriteNbTrial(Path.Combine(repo.Root, "trial"), Captured("loop-nudged"));
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        var manifest = NewManifest() with { Agent = new AgentRun("nb", null, "mock", [], [], "0.24.0", null) };

        Assert.Null(Ingest.Trial(trial, cell, NbArm, manifest, "0.9.0"));
        Assert.True(File.Exists(Path.Combine(cell, "agent", "nb.jsonl")));
        Assert.Equal(0, manifest.NbExitCode);
        Assert.Equal("nb", ReadJson(Path.Combine(cell, "trajectory.json"))["agent"]!["name"]!.GetValue<string>());
        var core = ReadJson(Path.Combine(cell, "core.json"));
        Assert.Equal("max_tool_calls", core["exit_reason"]!.GetValue<string>());   // nb's own ending
        Assert.Equal(3, core["tool_calls"]!.GetValue<int>());
        Assert.Equal(3, core["kinds"]!["shell"]!.GetValue<int>());
        Assert.Equal(1500, core["duration_ms"]!.GetValue<long>());

        var t = Transcript.Read(cell);
        Assert.True(t.LoopNudged);
        Assert.Equal("Mock", t.Trailer!.Provider);
    }

    [Fact]
    public void Ingest_NbTrial_FailsAsTheBarePathDoes()
    {
        using var repo = new TestRepo();
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        AgentRun nbRun = new("nb", null, "mock", [], [], "0.24.0", null);
        string? Ingested(string trial) => Ingest.Trial(trial, cell, NbArm, NewManifest() with { Agent = nbRun }, "0.9.0");

        Assert.Equal("agent_version_mismatch: the image ran nb 0.8.1, the host nb that compiled the program is 0.9.0",
            Ingested(WriteNbTrial(Path.Combine(repo.Root, "t1"), Captured("plain"), version: "0.8.1")));
        Assert.Equal("nb exited 1 without a result trailer: Unhandled exception: provider 'Mock' not configured",
            Ingested(WriteNbTrial(Path.Combine(repo.Root, "t2"), "{\"type\":\"user\",\"turn\":0,\"text\":\"hi\"}\n", exit: "1")));
        Assert.Equal("nb did not run in the trial (no agent/nb.jsonl)", Ingested(WriteNbTrial(Path.Combine(repo.Root, "t3"), "")));

        // Out of time is a completed run, whatever nb got written.
        Assert.Null(Ingested(WriteNbTrial(Path.Combine(repo.Root, "t4"), "{\"type\":\"user\",\"turn\":0,\"text\":\"hi\"}\n", exception: "AgentTimeoutError", exit: "")));
        Assert.Equal("time_budget", ReadJson(Path.Combine(cell, "core.json"))["exit_reason"]!.GetValue<string>());
    }
}
