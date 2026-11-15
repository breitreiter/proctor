using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>Agent variants: validation, compiling a task for Harbor, ingesting a trial, and the runner with a stand-in for Harbor.</summary>
public class HarborTests
{
    const string BaseUrl = "http://library:8086/x/anthropic";

    static JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    /// <summary>harbor-unit, with proctor.json naming the agent's endpoint, a key from a variable only this test sets, and uv as given.</summary>
    static (TestRepo Repo, string KeyVar) AgentRepo(string? uv = null)
    {
        var repo = new TestRepo();
        repo.CopyExam("harbor-unit");
        var keyVar = "PROCTOR_TEST_KEY_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        Environment.SetEnvironmentVariable(keyVar, "sk-test-" + keyVar);
        File.WriteAllText(Path.Combine(repo.Root, "exams", "proctor.json"), JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            agents = new Dictionary<string, object> { ["claude-code"] = new { base_url = BaseUrl, api_key = "${" + keyVar + "}" } },
            harbor = new { uv = uv ?? "uv" },
        }));
        return (repo, keyVar);
    }

    [Fact]
    public void AgentSmoke_Loads_AsOneAgentVariant()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        var exam = repo.LoadExam("harbor-unit");
        var variant = Assert.Single(exam.Variants);
        Assert.True(variant.IsAgent);
        Assert.True(exam.HasAgentVariants);
        Assert.Equal("claude-code", variant.Agent);
        Assert.Equal("2.1.301", variant.AgentVersion);
        Assert.Equal("0.24.0", exam.Def.Harbor!.Version);
        Assert.Equal(900_000, exam.Def.Limits!.WallMs);
        Assert.Equal(["builds", "diff-in-scope", "tests-pass"], exam.ChecksFor(exam.Tasks[0]).Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("harness", "\"claude-code\"", "harness", "an nb field")]
    [InlineData("provider", "\"anthropic\"", "provider", "an nb field")]
    [InlineData("model", "\"gpt-5.4\"", "model", "not an Anthropic model")]
    [InlineData("model", "\"anthropic/claude-haiku-5-5\"", "model", "provider prefix")]
    [InlineData("agent_version", "null", "agent_version", "pinned")]
    [InlineData("agent_version", "\"latest\"", "agent_version", "not a version number")]
    [InlineData("agent", "\"cursor\"", "agent", "unknown agent")]
    public void AgentVariant_IsValidated(string field, string value, string problemField, string message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditVariant("harbor-unit", "cc-haiku", v => v[field] = JsonNode.Parse(value));
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == problemField && p.Message.Contains(message));
    }

    [Fact]
    public void AgentVariant_NeedsAPinnedHarbor_AWallLimit_AndAnImageOnEveryFixture()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditJson("harbor-unit/exam.json", e => { e.Remove("harbor"); e["limits"] = JsonNode.Parse("{\"tool_calls\": 60}"); });
        repo.EditJson("../fixtures/ledger/fixture.json", f => f.Remove("image"));
        var problems = repo.Problems("harbor-unit");
        Assert.Contains(problems, p => p.Field == "harbor.version" && p.Message.Contains("required with an agent variant"));
        Assert.Contains(problems, p => p.Field == "limits.wall_ms" && p.Message.Contains("required"));
        Assert.Contains(problems, p => p.Field == "limits.tool_calls" && p.Message.Contains("not yet"));
        Assert.Contains(problems, p => p.File.EndsWith("fixture.json") && p.Field == "image");
    }

    [Fact]
    public void AgentVariant_RefusesAttemptHooks_AndANbExamRefusesTheHarborBlocks()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.WriteScript("harbor-unit/hooks/setup.sh", "#!/bin/sh\nexit 0\n");
        repo.EditJson("harbor-unit/exam.json", e => e["hooks"] = JsonNode.Parse("{\"attempt\": {\"setup\": \"hooks/setup.sh\"}}"));
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == "hooks.attempt" && p.Message.Contains("fixture's image"));

        repo.CopyExam("smoke");
        repo.EditJson("smoke/exam.json", e => { e["harbor"] = JsonNode.Parse("{\"version\": \"0.24.0\"}"); e["limits"] = JsonNode.Parse("{\"wall_ms\": 60000}"); });
        var problems = repo.Problems("smoke");
        Assert.Contains(problems, p => p.Field == "harbor" && p.Message.Contains("nothing runs through Harbor"));
        Assert.Contains(problems, p => p.Field == "limits" && p.Message.Contains("budget directives"));
    }

    [Fact]
    public void AKilledTrial_IsTornDownByItsComposeProjects()
    {
        // docker-compose lower-cases the project name; the trial directory keeps Harbor's mixed-case id.
        Assert.Equal(["misses__ol6ktta__env", "misses__ol6ktta__verifier__trial"], HarborRun.TrialProjects("misses__oL6KTTA"));

        using var repo = new TestRepo();
        var jobs = Path.Combine(repo.Root, "jobs");
        Assert.Equal("No trial had started, so nothing was left in the engine.", HarborRun.TearDown("false", jobs));
        Directory.CreateDirectory(Path.Combine(jobs, "job", "misses__oL6KTTA"));
        File.WriteAllText(Path.Combine(jobs, "job", "config.json"), "{}");
        // An engine that lists nothing: the summary still names the trial and counts zero of each.
        Assert.Equal("Removed what trial misses__oL6KTTA left in true: 0 containers, 0 networks, 0 images.", HarborRun.TearDown("true", jobs));
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
        var exam = repo.LoadExam("harbor-unit");
        var expDir = Path.Combine(repo.Root, "attempts", "exp");
        var hash = HarborCompile.Tasks(expDir, exam, "exp", _ => new AgentRoute(BaseUrl, "sk-secret"));
        Assert.Equal(hash, HarborCompile.TreeHash(expDir));
        var task = Layout.HarborTask(expDir, "cc-haiku", "fix-null-deref");

        Assert.Equal(exam.Tasks[0].Prompt!.TrimEnd() + "\n", File.ReadAllText(Path.Combine(task, "instruction.md")));

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

        // The checks, the exam and the expectations are the verifier's.
        var tests = Path.Combine(task, "tests");
        Assert.True(File.Exists(Path.Combine(tests, "fixture", "checks", "builds.sh")));
        Assert.False(Directory.Exists(Path.Combine(tests, "fixture", "repo")));
        Assert.True(File.Exists(Path.Combine(tests, "exam", "tasks", "fix-null-deref.json")));
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
        var variant = new Variant("cc-haiku", null, null, null, "claude-haiku-5-5", null, Agent: "claude-code", AgentVersion: "2.1.301");
        var job = HarborCompile.Job(variant, "/t", "/j", new AgentRoute(BaseUrl, "sk-secret"));
        Assert.Equal(1, job["n_attempts"]!.GetValue<int>());
        Assert.Equal(0, job["retry"]!["max_retries"]!.GetValue<int>());
        Assert.Equal("docker", job["environment"]!["type"]!.GetValue<string>());
        Assert.Equal("podman", HarborCompile.Job(variant, "/t", "/j", new AgentRoute(BaseUrl, "sk-secret"), engine: "podman")["environment"]!["type"]!.GetValue<string>());
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

    static readonly Variant CcVariant = new("cc-haiku", null, null, null, "claude-haiku-5-5", null, Agent: "claude-code", AgentVersion: "2.1.301");

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
        Id = "r", Batch = "e", Exam = "x", Variant = new("cc-haiku", "sha256:v"), Task = new("fix-null-deref", "sha256:t"), Attempt = 1, Host = "h", Versions = [], ProgramHash = "p",
        Agent = new AgentRun("claude-code", null, "claude-haiku-5-5", [], [], "0.24.0", null),
    };

    [Fact]
    public void Ingest_LiftsTheTrialIntoTheAttempt_AndCountsByProctorsDefinitions()
    {
        using var repo = new TestRepo();
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"));
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        var manifest = NewManifest();

        Assert.Null(Ingest.Trial(trial, attempt, CcVariant, manifest, CcVariant.AgentVersion!));
        Assert.Equal(["agent", "core.json", "diff.patch", "reward.json", "trajectory.json", "verifier"],
            Directory.GetFileSystemEntries(attempt).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(attempt, "agent", "claude-code.txt")));
        Assert.True(File.Exists(Path.Combine(attempt, "verifier", "builds.out")));
        Assert.Equal("2.1.301", manifest.Agent!.Version);
        Assert.Equal(1200, manifest.Agent.InputTokens);
        Assert.Equal(0.004, manifest.Agent.CostUsd);
        Assert.Equal(Path.Combine("..", "trial"), manifest.Agent.Trial);

        var core = ReadJson(Path.Combine(attempt, "core.json"));
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
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        Assert.StartsWith("the agent took no turn (time_budget)", Ingest.Trial(trial, attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
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
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        Assert.Equal(failure, Ingest.Trial(trial, attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
        if (exitReason is not null) Assert.Equal(exitReason, ReadJson(Path.Combine(attempt, "core.json"))["exit_reason"]!.GetValue<string>());
    }

    [Fact]
    public void Ingest_IsStrict_AboutTheVersion_AndTheTools()
    {
        using var repo = new TestRepo();
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        Assert.Equal("agent_version_mismatch: the trial ran claude-code 2.1.302, the variant pins 2.1.301",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t1"), version: "2.1.302"), attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
        Assert.Equal("adapter_unmapped: tool 'mcp__fs__read' has no kind for claude-code",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t2"), trajectory: Trajectory("Read", "mcp__fs__read")), attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
        Assert.Equal("adapter_unmapped: the trial has no agent/trajectory.json",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t3"), trajectory: ""), attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
        var noDiff = WriteTrial(Path.Combine(repo.Root, "t5"));
        File.Delete(Path.Combine(noDiff, "artifacts", "logs", "artifacts", "diff.patch"));
        Assert.StartsWith("the trial collected no diff", Ingest.Trial(noDiff, attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
        Assert.Equal("agent exited without a trajectory: NonZeroAgentExitCodeError: it went wrong",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t4"), "NonZeroAgentExitCodeError", trajectory: ""), attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
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
    public void Run_AnAgentVariant_CompilesOnce_RunsHarborPerAttempt_AndIngests()
    {
        using var scratch = new TestRepo();
        var (repo, keyVar) = AgentRepo(FakeUv(scratch, GoodTrial));
        using var _r = repo;
        var exam = repo.LoadExam("harbor-unit");
        var id = Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null)!;
        var expDir = Layout.Batch(repo.Root, id);

        var batch = ReadJson(Path.Combine(expDir, "batch.json"));
        Assert.Equal("0.24.0", batch["versions"]!["harbor"]!.GetValue<string>());
        Assert.Equal("0.24.0", batch["harbor"]!["version"]!.GetValue<string>());
        Assert.Equal(HarborCompile.TreeHash(expDir), batch["harbor"]!["tasks_hash"]!.GetValue<string>());

        var attempt = Layout.Attempt(expDir, "cc-haiku", "fix-null-deref", 1);
        Assert.Equal("completed", File.ReadAllText(Path.Combine(attempt, "status")).Trim());
        var manifest = ReadJson(Path.Combine(attempt, "manifest.json"));
        Assert.Equal("atif", manifest["transcript"]!["format"]!.GetValue<string>());
        Assert.Equal("trajectory.json", manifest["transcript"]!["file"]!.GetValue<string>());
        Assert.Null(manifest["harness"]);
        Assert.Null(manifest["provider"]);
        Assert.Equal("claude-code", manifest["agent"]!["name"]!.GetValue<string>());
        Assert.Equal("2.1.301", manifest["agent"]!["version"]!.GetValue<string>());
        Assert.Equal("0.24.0", manifest["agent"]!["harbor"]!.GetValue<string>());
        Assert.Equal("bypassPermissions", manifest["agent"]!["kwargs"]!["permission_mode"]!.GetValue<string>());
        Assert.Equal("0.24.0", manifest["versions"]!["harbor"]!.GetValue<string>());
        Assert.Equal(1, ReadJson(Path.Combine(attempt, "core.json"))["tool_calls"]!.GetValue<int>());
        Assert.Equal("{\"builds\": 1, \"tests-pass\": 0}", File.ReadAllText(Path.Combine(attempt, "reward.json")).Trim());

        Assert.Equal("tool run --exclude-newer 7 days harbor@0.24.0 run --config job.json",File.ReadAllText(Path.Combine(scratch.Root, "uv.argv")).Trim());
        Assert.Equal($"telemetry=off key=sk-test-{keyVar}", File.ReadAllText(Path.Combine(scratch.Root, "uv.env")).Trim());
        var job = ReadJson(Path.Combine(attempt, "harbor", "job.json"));
        Assert.Equal(Layout.HarborTask(expDir, "cc-haiku", "fix-null-deref"), job["tasks"]![0]!["path"]!.GetValue<string>());
        // The key reached Harbor's process and no file.
        Assert.DoesNotContain(Directory.GetFiles(expDir, "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains($"sk-test-{keyVar}"));

        // grade reads the script verdicts the verifier left, and runs the built-ins over the trajectory and the diff.
        var grade = Assert.Single(Grade.Attempts(repo.Root, exam, Pool.Select(repo.Root, exam).Current, TextWriter.Null));
        Assert.False(grade.Pass);
        Assert.Equal(new Verdict(Verdict.Pass, "dotnet build: 0 errors"), grade.Checks!["builds"]);
        Assert.Equal(new Verdict(Verdict.Fail, "1 of 6 tests failed: Parse_LineWithoutMemo_HasNullMemo"), grade.Checks["tests-pass"]);
        Assert.Equal(new Verdict(Verdict.Fail, "touched 1 outside expected: x"), grade.Checks["diff-in-scope"]);
        var row = Assert.Single(Results.Collect(repo.Root, exam, Pool.Select(repo.Root, exam).Current));
        Assert.Equal("ok", row.ExitReason);
        Assert.Equal(1, row.ToolCalls);
        Assert.Equal(15, row.Usage!.Total);
        Assert.Equal(0.001, row.Usage.CostUsd);
        // The failed headline check leads the reasons, ahead of the guardrail the exam declares first.
        Assert.Equal(["tests-pass", "diff-in-scope"], row.Reasons!.Keys);
    }

    /// <summary>An agent attempt as ingest leaves it, graded on its own: script verdicts from the verifier, the rest from the trajectory and the diff.</summary>
    static Dictionary<string, Verdict> GradeAgentAttempt(Action<string> edit, Action<JsonObject>? editExam = null)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        if (editExam is not null) repo.EditJson("harbor-unit/exam.json", editExam);
        var exam = repo.LoadExam("harbor-unit");
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"));
        File.WriteAllText(Path.Combine(trial, "verifier", "tests-pass.out"), "6 of 6 tests pass\n");
        Assert.Null(Ingest.Trial(trial, attempt, CcVariant, NewManifest(), CcVariant.AgentVersion!));
        edit(attempt);
        var context = new AttemptContext(exam.Dir, attempt, Path.Combine(repo.Root, "work"), "e", "cc-haiku", exam.Tasks[0], 1)
            { Expect = exam.ExpectFor(exam.Tasks[0]), Fixture = exam.FixtureOf(exam.Tasks[0]), ScriptsInVerifier = true, Vendor = true };
        return Grade.Attempt(context, exam, Transcript.Read(attempt));
    }

    [Fact]
    public void Grade_AnAgentAttempt_ReadsTheVerifier_AndTheTrajectory()
    {
        var checks = GradeAgentAttempt(_ => { });
        Assert.Equal(new Verdict(Verdict.Pass, "dotnet build: 0 errors"), checks["builds"]);
        Assert.Equal(new Verdict(Verdict.Pass, "6 of 6 tests pass"), checks["tests-pass"]);
        Assert.Equal(new Verdict(Verdict.Pass, "touched 1, all expected"), checks["diff-in-scope"]);
    }

    [Fact]
    public void Grade_AMissingRewardKey_IsAnError_NeverAFail()
    {
        var checks = GradeAgentAttempt(attempt =>
        {
            File.WriteAllText(Path.Combine(attempt, "reward.json"), "{\"builds\": 1}");
            File.WriteAllText(Path.Combine(attempt, "verifier", "tests-pass.out"), "dotnet: command not found\n");
            File.WriteAllText(Path.Combine(attempt, "verifier", "tests-pass.exit"), "127\n");
        });
        Assert.Equal(new Verdict(Verdict.Error, "dotnet: command not found"), checks["tests-pass"]);

        checks = GradeAgentAttempt(attempt =>
        {
            File.WriteAllText(Path.Combine(attempt, "reward.json"), "{}");
            File.WriteAllText(Path.Combine(attempt, "verifier", "apply.out"), "error: patch failed: Ledger/Statement.cs:31\n");
        });
        Assert.Equal(new Verdict(Verdict.Error, "the attempt's diff did not apply in the verifier: error: patch failed: Ledger/Statement.cs:31"), checks["builds"]);
    }

    [Fact]
    public void Grade_BuiltIns_ReadTheCountedCore_AndTheNudgeIsNbs()
    {
        var checks = GradeAgentAttempt(_ => { }, e =>
        {
            e["grading"]!["checks"]!["few-calls"] = JsonNode.Parse("{\"max_tool_calls\": 2}");
            e["grading"]!["checks"]!["exit-ok"] = JsonNode.Parse("{\"exit_reason\": \"ok\"}");
            e["grading"]!["checks"]!["not-nudged"] = JsonNode.Parse("{\"loop_nudged\": false, \"description\": \"no nudge\"}");
            e["grading"]!["checks"]!["quiet"] = JsonNode.Parse("{\"loop_nudged\": false, \"tool_errors\": {\"max\": 5}}");
            e["grading"]!["checks"]!["answered"] = JsonNode.Parse("{\"answer_contains\": \"6 of 6\"}");
        });
        Assert.Equal(new Verdict(Verdict.Fail, "3 tool calls (max 2)"), checks["few-calls"]);
        Assert.Equal(Verdict.Pass, checks["exit-ok"].Result);
        Assert.Equal(Verdict.Pass, checks["answered"].Result);
        Assert.False(checks.ContainsKey("not-nudged"));
        Assert.Equal(Verdict.Error, checks["quiet"].Result);
        Assert.StartsWith("loop_nudged is nb's nudge; a vendor agent has none to fire", checks["quiet"].Reason);
    }

    [Theory]
    [InlineData("pass")]
    [InlineData("validity")]
    public void Validate_ANudgeOnlyCheckInTheHeadline_BesideAVendorVariant_IsAProblem(string list)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditJson("harbor-unit/exam.json", e =>
        {
            e["grading"]!["checks"]!["not-nudged"] = JsonNode.Parse("{\"loop_nudged\": false}");
            e["grading"]![list] = new JsonArray([.. (e["grading"]![list]?.AsArray().Select(n => n!.DeepClone()) ?? []), "not-nudged"]);
        });
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == $"grading.{list}" && p.Message.Contains("loop nudge"));
    }

    [Fact]
    public void Run_HarborWithoutATrial_FailsTheAttempt_AndTheNextRunContinuesTheBatch()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "echo 'docker: Cannot connect to the Docker daemon' >&2; rm -rf \"$jobs\"; exit 1"));
        using var _r = repo;
        var id = Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null)!;
        var attempt = Layout.Attempt(Layout.Batch(repo.Root, id), "cc-haiku", "fix-null-deref", 1);
        Assert.Equal("failed", File.ReadAllText(Path.Combine(attempt, "status")).Trim());
        Assert.Equal("Harbor exited 1 without a trial: docker: Cannot connect to the Docker daemon", ReadJson(Path.Combine(attempt, "manifest.json"))["status_reason"]!.GetValue<string>());

        File.WriteAllText(Path.Combine(scratch.Root, "exams", "fake-uv.sh"), File.ReadAllText(Path.Combine(scratch.Root, "exams", "fake-uv.sh")).Replace("echo 'docker: Cannot connect to the Docker daemon' >&2; rm -rf \"$jobs\"; exit 1", GoodTrial));
        Assert.Equal(id, Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null));
        Assert.Equal("completed", File.ReadAllText(Path.Combine(attempt, "status")).Trim());
        Assert.Equal(2, ReadJson(Path.Combine(attempt, "manifest.json"))["tries"]!.GetValue<int>());
    }

    [Fact]
    public void Run_DoesNotContinueABatchWhoseCompiledTasksChanged()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "exit 1"));
        using var _r = repo;
        var id = Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null)!;
        File.AppendAllText(Path.Combine(Layout.HarborTask(Layout.Batch(repo.Root, id), "cc-haiku", "fix-null-deref"), "instruction.md"), "and also\n");
        var log = new StringWriter();
        var again = Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", log);
        Assert.Contains($"batch {id} was interrupted: 0 of 1 attempt finished, 1 failed\n  not continuing it: the Harbor tasks it compiled have changed on disk (harbor/tasks)\n", log.ToString());
        Assert.NotEqual(id, again);
    }

    /// <summary>Issue #11: an agent variant's bundle is the host's, as on the bare path: hashed, recorded per attempt, handed to variant hooks, part of the variant; never in the task.</summary>
    [Fact]
    public void Run_AnAgentVariantsBundle_IsRecorded_HandedToVariantHooks_KeptOutOfTheTask_AndPartOfTheVariant()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "exit 1"));
        using var _r = repo;
        var bundleDir = Directory.CreateDirectory(Path.Combine(repo.Root, "bundles", "corpus")).FullName;
        File.WriteAllText(Path.Combine(bundleDir, "page.html"), "<h1>corpus page</h1>\n");
        repo.WriteScript("harbor-unit/hooks/serve.sh", $"#!/usr/bin/env bash\necho \"$PROCTOR_BUNDLE\" > \"{scratch.Root}/hook.bundle\"\n");
        repo.EditVariant("harbor-unit", "cc-haiku", v => v["bundle"] = JsonNode.Parse("""{"path": "bundles/corpus"}"""));
        repo.EditJson("harbor-unit/exam.json", e => e["hooks"] = JsonNode.Parse("""{"variant": {"setup": "hooks/serve.sh"}}"""));

        var id = Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null)!;
        var expDir = Layout.Batch(repo.Root, id);
        var hash = ReadJson(Path.Combine(expDir, "batch.json"))["bundles"]!["cc-haiku"]!["hash"]!.GetValue<string>();
        Assert.StartsWith("sha256:", hash);
        var manifest = ReadJson(Path.Combine(Layout.Attempt(expDir, "cc-haiku", "fix-null-deref", 1), "manifest.json"));
        Assert.Equal(hash, manifest["bundle"]!["hash"]!.GetValue<string>());
        Assert.Equal(bundleDir, File.ReadAllText(Path.Combine(scratch.Root, "hook.bundle")).Trim());
        Assert.DoesNotContain(Directory.GetFiles(Layout.HarborTask(expDir, "cc-haiku", "fix-null-deref"), "*", SearchOption.AllDirectories),
            f => File.ReadAllText(f).Contains("corpus page"));

        File.AppendAllText(Path.Combine(bundleDir, "page.html"), "rebuilt\n");
        var e = Assert.Throws<ProctorException>(() => Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null));
        Assert.StartsWith("exams/harbor-unit/variants/cc-haiku.json: this is not the cc-haiku that made 1 attempt\n  (bundle ", e.Message);
    }

    [Fact]
    public void Run_OnPodman_RecordsTheEngine_HandsItToHarbor_AndDoesNotContinueTheBatchOnAnother()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "exit 1"));
        using var _r = repo;
        var config = Path.Combine(repo.Root, "exams", "proctor.json");
        void Engine(string engine) { var c = ReadJson(config); c["harbor"]!["engine"] = engine; File.WriteAllText(config, c.ToJsonString()); }
        Engine("podman");
        var id = Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null)!;
        var expDir = Layout.Batch(repo.Root, id);
        Assert.Equal("podman", ReadJson(Path.Combine(expDir, "batch.json"))["harbor"]!["engine"]!.GetValue<string>());
        var job = Path.Combine(Layout.Attempt(expDir, "cc-haiku", "fix-null-deref", 1), Layout.HarborDir, Layout.HarborJobFile);
        Assert.Equal("podman", ReadJson(job)["environment"]!["type"]!.GetValue<string>());

        Engine("docker");
        var log = new StringWriter();
        var again = Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", log);
        Assert.Contains("  not continuing it: the engine (exams/proctor.json harbor.engine) is docker now, podman then; run with the --config it was made with to continue it\n", log.ToString());
        Assert.Equal("docker", ReadJson(Path.Combine(Layout.Batch(repo.Root, again!), "batch.json"))["harbor"]!["engine"]!.GetValue<string>());
    }

    [Fact]
    public void Run_WithoutTheAgentsEndpoint_FailsBeforeAnythingIsMade()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.WriteProctorConfig();
        var e = Assert.Throws<ProctorException>(() => Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", TextWriter.Null));
        Assert.Contains("no agents.claude-code", e.Message);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "attempts")));
    }

    // Modal: the same Harbor tasks on Modal's sandboxes, from a second machine file.

    const string PublicUrl = "https://api.anthropic.com";

    [Theory]
    [InlineData("api.anthropic.com", null)]
    [InlineData("openrouter.ai", null)]
    [InlineData("203.0.113.7", null)]
    [InlineData("2606:4700::6810:85e5", null)]
    [InlineData("localhost", "loopback")]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("::1", "loopback")]
    [InlineData("10.1.2.3", "a private address")]
    [InlineData("172.20.0.5", "a private address")]
    [InlineData("192.168.1.40", "a private address")]
    [InlineData("100.101.102.103", "a private address")]
    [InlineData("fd12:3456::1", "a private address")]
    [InlineData("169.254.169.254", "a link-local address")]
    [InlineData("library", "a name without a domain, which only this network resolves")]
    [InlineData("imp.local", "a .local name, which only this network resolves")]
    [InlineData("gpu.lan", "a .lan name, which only this network resolves")]
    public void Unreachable_IsWhatOnlyThisMachineOrNetworkCanReach(string host, string? why) =>
        Assert.Equal(why, HarborRun.Unreachable(host));

    [Fact]
    public void Modal_NeedsALogin_FromTheEnvironmentOrTheSdksFile()
    {
        var none = new Dictionary<string, string>();
        Assert.Contains("run `uvx modal token new`", HarborRun.ModalProblem(v => none.GetValueOrDefault(v), _ => false));
        Assert.Null(HarborRun.ModalProblem(v => none.GetValueOrDefault(v), f => f.EndsWith(".modal.toml")));
        var tokens = new Dictionary<string, string> { ["MODAL_TOKEN_ID"] = "ak-1", ["MODAL_TOKEN_SECRET"] = "as-1" };
        Assert.Null(HarborRun.ModalProblem(v => tokens.GetValueOrDefault(v), _ => false));
        var named = new Dictionary<string, string> { ["MODAL_CONFIG_PATH"] = "/etc/modal.toml" };
        Assert.Null(HarborRun.ModalProblem(v => named.GetValueOrDefault(v), f => f == "/etc/modal.toml"));
        Assert.Contains("/etc/modal.toml", HarborRun.ModalProblem(v => named.GetValueOrDefault(v), _ => false));
    }

    [Fact]
    public void Job_OnModal_GivesEachSandboxTheAttemptsLifetime_AndAnIdleLimit_AndHarborItsModalExtra()
    {
        var job = HarborCompile.Job(CcVariant, "/t", "/j", new AgentRoute(PublicUrl, "sk"), engine: "modal", lifetime: TimeSpan.FromMinutes(70));
        Assert.Equal("""{"type":"modal","kwargs":{"sandbox_timeout_secs":4200,"sandbox_idle_timeout_secs":600}}""", job["environment"]!.ToJsonString());
        Assert.Equal("""{"type":"podman"}""", HarborCompile.Job(CcVariant, "/t", "/j", new AgentRoute(PublicUrl, "sk"), engine: "podman")["environment"]!.ToJsonString());

        Assert.Equal("tool run --exclude-newer 7 days harbor[modal]@0.24.0 run --config job.json", string.Join(" ", HarborRun.UvArgs("0.24.0", "modal")));
        Assert.Equal("tool run --exclude-newer 7 days harbor@0.24.0 run --config job.json", string.Join(" ", HarborRun.UvArgs("0.24.0", "docker")));
        // Nothing to remove on this machine: the sandbox ends itself.
        Assert.StartsWith("A sandbox it left on Modal ends itself within 10 minutes", HarborRun.TearDown("modal", "/nowhere"));
    }

    [Fact]
    public void Compile_NbVariant_OnARemoteEngine_CarriesNbsPublishInTheBuildContext()
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        var publish = Directory.CreateDirectory(Path.Combine(repo.Root, "publish")).FullName;
        File.WriteAllText(Path.Combine(publish, "nb"), "#!/bin/sh\necho nb\n");
        File.SetUnixFileMode(Path.Combine(publish, "nb"), (UnixFileMode)0b111_101_101);
        var expDir = Path.Combine(repo.Root, "attempts", "exp");
        HarborCompile.Tasks(expDir, repo.LoadExam("gate"), "exp", _ => new AgentRoute(null, null), "proctor-nb:0123456789abcdef", publish);
        var environment = Path.Combine(Layout.HarborTask(expDir, "nb-harbor", "writes"), "environment");

        Assert.Contains("COPY proctor-nb /opt/nb\nENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1\nRUN /opt/nb/nb --version\n", File.ReadAllText(Path.Combine(environment, "Dockerfile")));
        Assert.DoesNotContain("--from=", File.ReadAllText(Path.Combine(environment, "Dockerfile")));
        var nb = Path.Combine(environment, "proctor-nb", "nb");
        Assert.Equal("#!/bin/sh\necho nb\n", File.ReadAllText(nb));
        Assert.True(File.GetUnixFileMode(nb).HasFlag(UnixFileMode.UserExecute));
    }

    /// <summary>harbor-unit on Modal, from a machine file of its own beside exams/proctor.json, with a login in the environment.</summary>
    static (TestRepo Repo, string ConfigFile) ModalRepo(string baseUrl, string? uv = null)
    {
        var (repo, keyVar) = AgentRepo(uv);
        Environment.SetEnvironmentVariable("MODAL_TOKEN_ID", "ak-test");
        Environment.SetEnvironmentVariable("MODAL_TOKEN_SECRET", "as-test");
        var file = Path.Combine(repo.Root, "exams", "proctor.modal.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            agents = new Dictionary<string, object> { ["claude-code"] = new { base_url = baseUrl, api_key = "${" + keyVar + "}" } },
            harbor = new { uv = uv ?? "uv", engine = "modal" },
        }));
        return (repo, file);
    }

    [Fact]
    public void Config_FromAnotherFile_IsTheMachine_AndMessagesNameIt()
    {
        var (repo, file) = ModalRepo(PublicUrl);
        using var _r = repo;
        var config = Exam.LoadConfig(repo.Root, [], file);
        Assert.Equal("modal", config.HarborOrDefault.Engine);
        Assert.True(config.HarborOrDefault.Remote);
        Assert.Equal(Path.Combine("exams", "proctor.modal.json"), config.Source);
        Assert.Equal("docker", Exam.LoadConfig(repo.Root, []).HarborOrDefault.Engine);
        Assert.Equal(Path.Combine("exams", "proctor.json"), Exam.LoadConfig(repo.Root, []).Source);
        Assert.Contains("--config nowhere.json: no such file", Assert.Throws<ProctorException>(() => Exam.LoadConfig(repo.Root, [], "nowhere.json")).Message);
    }

    [Fact]
    public void Run_OnModal_HandsHarborTheSandboxsLifetime_AndContinuesTheBatchOnlyWithTheSameConfig()
    {
        using var scratch = new TestRepo();
        var (repo, file) = ModalRepo(PublicUrl, FakeUv(scratch, GoodTrial));
        using var _r = repo;
        var id = Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, [], file), null, null, "test", TextWriter.Null)!;
        var expDir = Layout.Batch(repo.Root, id);
        Assert.Equal("modal", ReadJson(Path.Combine(expDir, "batch.json"))["harbor"]!["engine"]!.GetValue<string>());
        var attempt = Layout.Attempt(expDir, "cc-haiku", "fix-null-deref", 1);
        Assert.Equal("completed", File.ReadAllText(Path.Combine(attempt, "status")).Trim());
        var job = ReadJson(Path.Combine(attempt, "harbor", "job.json"));
        Assert.Equal("modal", job["environment"]!["type"]!.GetValue<string>());
        // The exam's 900 s wall limit and the build, verifier and start overhead.
        Assert.Equal(900 + 3300, job["environment"]!["kwargs"]!["sandbox_timeout_secs"]!.GetValue<int>());
        Assert.Equal("tool run --exclude-newer 7 days harbor[modal]@0.24.0 run --config job.json", File.ReadAllText(Path.Combine(scratch.Root, "uv.argv")).Trim());
        Assert.Contains("allowed_hosts = [\"api.anthropic.com\"]", File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "cc-haiku", "fix-null-deref"), "task.toml")));

        // Interrupted: continued with the Modal config, and not without it.
        File.WriteAllText(Path.Combine(attempt, "status"), "failed\n");
        Assert.Equal(id, Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, [], file), null, null, "test", TextWriter.Null));
        File.WriteAllText(Path.Combine(attempt, "status"), "failed\n");
        var log = new StringWriter();
        Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, []), null, null, "test", log);
        Assert.Contains("not continuing it: the engine (exams/proctor.json harbor.engine) is docker now, modal then", log.ToString());
    }

    [Fact]
    public void Run_OnModal_RefusesAModelOnThisNetwork_AndAFixtureWithServices_BeforeAnythingIsMade()
    {
        var (repo, file) = ModalRepo(BaseUrl);
        using var _r = repo;
        var services = Directory.CreateDirectory(Path.Combine(repo.Root, "fixtures", "ledger", "services")).FullName;
        File.WriteAllText(Path.Combine(services, "docker-compose.yaml"), "services: {}\n");
        repo.EditJson("../fixtures/ledger/fixture.json", f => f["image"]!["services"] = "services");

        var e = Assert.Throws<ProctorException>(() => Runner.Run(repo.Root, repo.LoadExam("harbor-unit"), Exam.LoadConfig(repo.Root, [], file), null, null, "test", TextWriter.Null));
        Assert.Equal([
            "variant 'cc-haiku': its model is at library, a name without a domain, which only this network resolves, which a sandbox on modal cannot reach; run it on a local engine, or point exams/proctor.modal.json agents.claude-code at a public endpoint",
            "fixture 'ledger' runs services beside the agent (image.services), and modal cannot confine the network of a task with services; its tasks run on docker or podman only",
        ], e.Message.Split('\n'));
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "attempts")));
    }

    // Codex and Qwen Code: Harbor's agents on an OpenAI-dialect route, Qwen Code's install replaced by proctor's pin check.

    const string OpenAiUrl = "http://library:8086/x/openai/v1";
    static readonly Variant CodexVariant = new("codex-mini", null, null, null, "gpt-5-mini", null, Agent: "codex", AgentVersion: "0.163.0");
    static readonly Variant QwenVariant = new("qwen", null, null, null, "qwen3-coder-next", null, Agent: "qwen-code", AgentVersion: "0.25.2");

    [Theory]
    [InlineData("codex", "gpt-5-mini", null)]
    [InlineData("codex", "o4-mini", null)]
    [InlineData("codex", "claude-haiku-5-5", "not an OpenAI model")]
    [InlineData("qwen-code", "qwen3-coder-next", null)]
    [InlineData("qwen-code", "gpt-5-mini", "not a Qwen model")]
    [InlineData("claude-code", "qwen3-coder-next", "not an Anthropic model")]
    public void VendorVariant_TakesItsVendorsModelsOnly(string agent, string model, string? message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditVariant("harbor-unit", "cc-haiku", v => { v["agent"] = agent; v["model"] = model; });
        var problems = repo.Problems("harbor-unit").Where(p => p.File.EndsWith("variants/cc-haiku.json")).ToList();
        if (message is null) Assert.Empty(problems);
        else Assert.Contains(problems, p => p.Field == "model" && p.Message.Contains(message));
    }

    [Fact]
    public void Config_KnowsEveryVendorAgent()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        File.WriteAllText(Path.Combine(repo.Root, "exams", "proctor.json"), """{"agents": {"codex": {"base_url": "http://x/v1"}, "qwen-code": {"base_url": "http://x/v1"}, "cursor": {"base_url": "http://x"}}}""");
        var problems = new List<Problem>();
        Exam.LoadConfig(repo.Root, problems);
        Assert.Equal(["agents.cursor"], problems.Select(p => p.Field));
    }

    [Theory]
    [InlineData("docker", null)]
    [InlineData("podman", null)]
    [InlineData("modal", null)]
    [InlineData("nerdctl", "unknown engine 'nerdctl'; known: docker, podman, modal")]
    public void Config_KnowsDockerPodmanAndModal(string engine, string? message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        File.WriteAllText(Path.Combine(repo.Root, "exams", "proctor.json"), $$$"""{"harbor": {"engine": "{{{engine}}}"}}""");
        var problems = new List<Problem>();
        Exam.LoadConfig(repo.Root, problems);
        Assert.Equal(message is null ? [] : [("harbor.engine", message)], problems.Select(p => (p.Field, p.Message)));
    }

    [Fact]
    public void Job_Codex_IsHarborsAgent_AtItsPin_OnTheBaseUrl()
    {
        var job = HarborCompile.Job(CodexVariant, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("codex")));
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
        var job = HarborCompile.Job(CodexVariant, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("codex"), Config: "/exams/codex.toml"));
        Assert.Equal("""{"version":"0.163.0","config":"/exams/codex.toml"}""", job["agents"]![0]!["kwargs"]!.ToJsonString());
    }

    [Fact]
    public void Compile_CopiesTheFixturesServices_BesideTheImage()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var expDir = Path.Combine(repo.Root, "attempts", "exp");
        HarborCompile.Tasks(expDir, repo.LoadExam("conformance"), "exp", _ => new AgentRoute("http://localhost:8765/v1", "stub"));
        var environment = Path.Combine(Layout.HarborTask(expDir, "codex", "ok"), "environment");

        Assert.Equal(File.ReadAllText(Path.Combine(TestRepo.SourceRoot, "fixtures", "stub", "services", "docker-compose.yaml")), File.ReadAllText(Path.Combine(environment, "docker-compose.yaml")));
        Assert.True(File.Exists(Path.Combine(environment, "stub.py")));
        Assert.True(File.Exists(Path.Combine(environment, "Dockerfile")));
        Assert.Contains("allowed_hosts = [\"localhost\"]", File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "codex", "ok"), "task.toml")));
    }

    /// <summary>An agent variant's bundle reaches a fixture's services as environment/bundle/, and never the agent's image; a variant without one gets the directory empty, so a compose mount of ./bundle resolves.</summary>
    [Fact]
    public void Compile_PutsTheVariantsBundle_BesideTheFixturesServices()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var bundleDir = Directory.CreateDirectory(Path.Combine(repo.Root, "bundles", "docs", "guide")).FullName;
        File.WriteAllText(Path.Combine(bundleDir, "index.html"), "<h1>docs page</h1>\n");
        var exam = repo.LoadExam("conformance");
        var withBundle = exam.Variants.First(a => a.IsAgent).Id!;
        var bundles = new Dictionary<string, ResolvedBundle> { [withBundle] = new("bundles/docs", Path.Combine(repo.Root, "bundles", "docs"), "sha256:x") };
        var expDir = Path.Combine(repo.Root, "attempts", "exp");
        HarborCompile.Tasks(expDir, exam, "exp", _ => new AgentRoute("http://localhost:8765/v1", "stub"), bundles: bundles);

        var environment = Path.Combine(Layout.HarborTask(expDir, withBundle, "ok"), "environment");
        Assert.Equal("<h1>docs page</h1>\n", File.ReadAllText(Path.Combine(environment, "bundle", "guide", "index.html")));
        Assert.DoesNotContain("bundle", File.ReadAllText(Path.Combine(environment, "Dockerfile")));
        var without = exam.Variants.First(a => a.IsAgent && a.Id != withBundle).Id!;
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(Layout.HarborTask(expDir, without, "ok"), "environment", "bundle")));
    }

    [Theory]
    [InlineData("nowhere", "no docker-compose.yaml in nowhere")]
    [InlineData("clash", "Dockerfile, repo and bundle are the task's own names in environment/")]
    public void FixtureServices_AreValidated(string services, string message)
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        Directory.CreateDirectory(Path.Combine(repo.Root, "fixtures", "stub", "clash"));
        File.WriteAllText(Path.Combine(repo.Root, "fixtures", "stub", "clash", "docker-compose.yaml"), "services: {}\n");
        File.WriteAllText(Path.Combine(repo.Root, "fixtures", "stub", "clash", "Dockerfile"), "FROM scratch\n");
        repo.EditJson("../fixtures/stub/fixture.json", f => f["image"]!["services"] = services);

        Assert.Contains(repo.Problems("conformance"), p => p.Field == "image.services" && p.Message == message);
    }

    [Fact]
    public void Job_QwenCode_ImportsProctorsAgent()
    {
        var agent = HarborCompile.Job(QwenVariant, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("qwen-code")))["agents"]![0]!;
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
        repo.RemoveVariant("harbor-unit", "cc-haiku");
        repo.AddVariant("harbor-unit", """{"id": "codex-mini", "agent": "codex", "agent_version": "0.163.0", "model": "gpt-5-mini"}""");
        repo.AddVariant("harbor-unit", """{"id": "qwen", "agent": "qwen-code", "agent_version": "0.25.2", "model": "qwen3-coder-next"}""");
        var exam = repo.LoadExam("harbor-unit");
        var expDir = Path.Combine(repo.Root, "attempts", "exp");
        HarborCompile.Tasks(expDir, exam, "exp", _ => new AgentRoute(OpenAiUrl, "sk-secret"));

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
        var expDir = Path.Combine(repo.Root, "attempts", "exp");
        var hash = HarborCompile.Tasks(expDir, repo.LoadExam("harbor-unit"), "exp", _ => new AgentRoute(BaseUrl, "sk-secret"));
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

    [Fact]
    public void Count_AMadeUpToolTheAgentRefused_IsKindNone_ButAnUnknownToolThatRanIsUnmapped()
    {
        // Sonnet called `bash`; Claude Code 2.1.301 answered "No such tool available: bash" as an error result.
        static JsonObject With(bool isError)
        {
            var t = JsonNode.Parse(Trajectory("Bash", "bash"))!.AsObject();
            t["steps"]![1]!["observation"] = new JsonObject
            {
                ["results"] = new JsonArray(new JsonObject
                {
                    ["source_call_id"] = "c1",
                    ["content"] = "<tool_use_error>Error: No such tool available: bash.</tool_use_error>",
                    ["extra"] = new JsonObject { ["tool_result_metadata"] = new JsonObject { ["is_error"] = isError } },
                }),
            };
            return t;
        }
        var (core, unmapped) = Ingest.Count(With(isError: true), Variant.ClaudeCode, "ok");
        Assert.Null(unmapped);
        Assert.Equal(2, core!.ToolCalls);
        Assert.Equal(1, core.Kinds["shell"]);
        Assert.Equal(1, core.Kinds[Ingest.NoSuchTool]);
        Assert.Equal("tool 'bash' has no kind for claude-code", Ingest.Count(With(isError: false), Variant.ClaudeCode, "ok").Unmapped);
    }

    // nb as a Harbor agent: the gate exam's harbor variant.

    static readonly Variant NbVariant = new("nb-harbor", null, "nb", "Mock", "mock", null, Agent: "nb");

    [Fact]
    public void NbAgentVariant_IsAHarborVariant_ButNotAVendors()
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        var exam = repo.LoadExam("gate");
        var (bare, harbor) = (exam.Variants[0], exam.Variants[1]);
        Assert.False(bare.IsAgent);
        Assert.True(harbor.IsAgent);
        Assert.False(harbor.IsVendor);
        Assert.True(exam.HasAgentVariants);
    }

    [Theory]
    [InlineData("runner", "\"nb\"", "runner", "bare path")]
    [InlineData("agent_version", "\"0.9.0\"", "agent_version", "the binary's")]
    [InlineData("provider", "null", "provider", "required for agent nb")]
    [InlineData("id", "\"harbor\"", "id", "compiled for Harbor")]
    public void NbAgentVariant_IsValidated(string field, string value, string problemField, string message)
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        repo.EditVariant("gate", "nb-harbor", v => v[field] = JsonNode.Parse(value));
        if (field == "id") repo.RenameVariant("gate", "nb-harbor", "harbor");
        Assert.Contains(repo.Problems("gate"), p => p.Field == problemField && p.Message.Contains(message));
    }

    [Fact]
    public void NbAgentVariant_TakesABundle_ButItsTemplateCannotNameIt()
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        Directory.CreateDirectory(Path.Combine(repo.Root, "bundles", "corpus"));
        repo.EditVariant("gate", "nb-harbor", v => v["bundle"] = JsonNode.Parse("""{"path": "bundles/corpus"}"""));
        Assert.Empty(repo.Problems("gate"));

        File.AppendAllText(Path.Combine(repo.Root, "exams", "gate", "program.nb"), "# corpus: {{bundle}}\n");
        var problem = Assert.Single(repo.Problems("gate"));
        Assert.Equal("{{bundle}}", problem.Field);
        Assert.Contains("variant 'nb-harbor' runs nb through Harbor, where its bundle is not in the container", problem.Message);
    }

    [Fact]
    public void Compile_NbVariant_CopiesNbsLayer_ReachesNothingOnMock_AndCompilesTheAgent()
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        var exam = repo.LoadExam("gate");
        var expDir = Path.Combine(repo.Root, "attempts", "exp");
        var hash = HarborCompile.Tasks(expDir, exam, "exp", _ => new AgentRoute(null, null), "proctor-nb:0123456789abcdef");
        var task = Layout.HarborTask(expDir, "nb-harbor", "writes");
        Assert.False(Directory.Exists(Layout.HarborTask(expDir, "nb-bare", "writes")));

        var toml = File.ReadAllText(Path.Combine(task, "task.toml"));
        Assert.Contains("timeout_sec = 120.0\nnetwork_mode = \"no-network\"\n\n[environment]", toml);
        var dockerfile = File.ReadAllText(Path.Combine(task, "environment", "Dockerfile"));
        Assert.Contains("COPY --from=proctor-nb:0123456789abcdef /opt/nb /opt/nb\nENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1\nRUN /opt/nb/nb --version\n", dockerfile);
        Assert.DoesNotContain("claude", dockerfile);
        Assert.Contains("check 'new-file' '/tests/exam/checks/new-file.sh'\n", File.ReadAllText(Path.Combine(task, "tests", "test.sh")));

        // The agent is compiled beside the tasks, and a changed agent is a changed tree.
        var agent = Path.Combine(HarborCompile.AgentsDir(expDir), "nb_agent.py");
        Assert.Contains("class NbAgent(BaseAgent, ACPAgentMixin)", File.ReadAllText(agent));
        Assert.Contains("+", hash);
        File.AppendAllText(agent, "# edited\n");
        Assert.NotEqual(hash, HarborCompile.TreeHash(expDir));
    }

    [Fact]
    public void Job_NbVariant_ImportsProctorsAgent_WithTheAttemptsProgram_AndTheKeyByNameOnly()
    {
        var route = new AgentRoute("http://library:8086/x/qcoder/v1", "sk-secret", "LLM_GATEWAY_KEY", new() { ["LLM_GATEWAY"] = "http://library:8086/x" });
        var job = HarborCompile.Job(NbVariant, "/t", "/j", route, new NbAttempt("/attempt/program.jsonl", "/exams/nb.json"));
        var agent = job["agents"]![0]!.AsObject();
        Assert.Null(agent["name"]);
        Assert.Equal("nb_agent:NbAgent", agent["import_path"]!.GetValue<string>());
        Assert.Equal("""{"program":"/attempt/program.jsonl","workdir":"/app","config":"/exams/nb.json","secret":"LLM_GATEWAY_KEY"}""", agent["kwargs"]!.ToJsonString());
        Assert.Equal("""{"LLM_GATEWAY":"http://library:8086/x"}""", agent["env"]!.ToJsonString());
        Assert.DoesNotContain("sk-secret", job.ToJsonString());
    }

    /// <summary>A fake nb trial. <paramref name="run"/> names a captured pair in fixtures/nb-run (its JSONL and trajectory),
    /// "" writes an empty JSONL and no trajectory (nb that failed before running), null writes neither (nb never started).</summary>
    static string WriteNbTrial(string dir, string? run, string version = "0.9.0", string? exception = null, string exit = "0")
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
        if (run is not null) File.WriteAllText(Path.Combine(dir, "agent", "nb.jsonl"), run.Length > 0 ? NbRun(run, ".jsonl") : "");
        if (run is { Length: > 0 }) File.WriteAllText(Path.Combine(dir, "agent", "trajectory.json"), NbRun(run, ".trajectory.json"));
        File.WriteAllText(Path.Combine(dir, "agent", "nb.exit"), exit + "\n");
        File.WriteAllText(Path.Combine(dir, "agent", "nb.stderr.log"), "\nUnhandled exception: provider 'Mock' not configured\n");
        File.WriteAllText(Path.Combine(dir, "artifacts", "logs", "artifacts", "diff.patch"), "");
        File.WriteAllText(Path.Combine(dir, "verifier", "reward.json"), "{\"new-file\": 0}\n");
        return dir;
    }

    static string NbRun(string name, string extension) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "nb-run", name + extension));

    [Fact]
    public void Ingest_NbTrial_TakesNbsTrajectory_AndCountsIt()
    {
        using var repo = new TestRepo();
        var trial = WriteNbTrial(Path.Combine(repo.Root, "trial"), "loop-nudged");
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        var manifest = NewManifest() with { Agent = new AgentRun("nb", null, "mock", [], [], "0.24.0", null) };

        Assert.Null(Ingest.Trial(trial, attempt, NbVariant, manifest, "0.9.0"));
        Assert.True(File.Exists(Path.Combine(attempt, "agent", "nb.jsonl")));
        Assert.Equal(0, manifest.NbExitCode);
        Assert.Equal("nb", ReadJson(Path.Combine(attempt, "trajectory.json"))["agent"]!["name"]!.GetValue<string>());
        var core = ReadJson(Path.Combine(attempt, "core.json"));
        Assert.Equal("max_tool_calls", core["exit_reason"]!.GetValue<string>());   // nb's own ending
        Assert.Equal(4, core["tool_calls"]!.GetValue<int>());
        Assert.Equal(4, core["kinds"]!["shell"]!.GetValue<int>());
        Assert.Equal(50, core["input_tokens"]!.GetValue<long>());   // nb's own final_metrics
        Assert.Equal(1500, core["duration_ms"]!.GetValue<long>());

        var t = Transcript.Read(attempt);
        Assert.True(t.LoopNudged);
        Assert.Equal("Mock", t.Trailer!.Provider);
    }

    [Fact]
    public void Ingest_NbTrial_FailsAsTheBarePathDoes()
    {
        using var repo = new TestRepo();
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        AgentRun nbRun = new("nb", null, "mock", [], [], "0.24.0", null);
        string? Ingested(string trial) => Ingest.Trial(trial, attempt, NbVariant, NewManifest() with { Agent = nbRun }, "0.9.0");

        Assert.Equal("agent_version_mismatch: the image ran nb 0.8.1, the host nb that compiled the program is 0.9.0",
            Ingested(WriteNbTrial(Path.Combine(repo.Root, "t1"), "plain", version: "0.8.1")));
        Assert.Equal("nb exited 1 without a trajectory: Unhandled exception: provider 'Mock' not configured",
            Ingested(WriteNbTrial(Path.Combine(repo.Root, "t2"), "", exit: "1")));
        Assert.Equal("nb did not run in the trial (no agent/nb.jsonl)", Ingested(WriteNbTrial(Path.Combine(repo.Root, "t3"), null)));

        // Out of time is a completed run, with nothing written: nb writes both files when the run ends.
        Assert.Null(Ingested(WriteNbTrial(Path.Combine(repo.Root, "t4"), "", exception: "AgentTimeoutError", exit: "")));
        Assert.Equal("time_budget", ReadJson(Path.Combine(attempt, "core.json"))["exit_reason"]!.GetValue<string>());
    }
}
