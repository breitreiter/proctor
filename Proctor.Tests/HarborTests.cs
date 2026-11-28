using System.Text.Json;
using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

/// <summary>A configuration through Harbor: validation, compiling its task, ingesting a trial, and the runner with a stand-in for Harbor.</summary>
/// <summary>Runs only on a machine whose podman passes proctor's own preflight, which `run` makes for real before the fake uv is reached.</summary>
sealed class PodmanFactAttribute : FactAttribute
{
    public PodmanFactAttribute()
    {
        try { HarborRun.CheckPodman(); }
        catch (Exception e) when (e is ProctorException or System.ComponentModel.Win32Exception) { Skip = "needs podman set up as docs/podman.md says: " + e.Message.Split('\n')[0]; }
    }
}

public class HarborTests
{
    const string BaseUrl = "http://library:8086/x/anthropic";

    static JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    /// <summary>harbor-unit's one configuration, by key, and its one stage.</summary>
    const string K = "assistant=cc-haiku", StageId = "fix-null-deref";

    /// <summary>Edit harbor-unit's one assistant value.</summary>
    static void EditAssistant(TestRepo repo, Action<JsonObject> edit) =>
        repo.EditJson("harbor-unit/exam.json", e => edit(e["axes"]!["assistant"]!["values"]!["cc-haiku"]!.AsObject()));

    /// <summary>Where a configuration's task is compiled, and where its one stage's instruction and verifier are.</summary>
    static string TaskDir(string entryDir, string key = K, string exam = "harbor-unit") => Layout.HarborJobTask(entryDir, key, exam);
    static string StepDir(string entryDir, string key = K, string exam = "harbor-unit", string stage = StageId) => Path.Combine(TaskDir(entryDir, key, exam), "steps", stage);

    static string? Run(TestRepo repo, TextWriter? log = null)
    {
        var (exam, keys) = Verbs.ForRun(repo.Root, repo.LoadExam("harbor-unit"), null, null, null);
        return Runner.Run(repo.Root, exam, Exam.LoadConfig(repo.Root, []), null, null, "test", log ?? TextWriter.Null, keys);
    }

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
    public void HarborUnit_Loads_AsOneConfigurationThroughHarbor()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        var exam = repo.LoadExam("harbor-unit");
        var assistant = Assert.Single(exam.Assistants);
        Assert.True(assistant.IsAgent);
        Assert.True(exam.HasHarbor);
        Assert.Equal("claude-code", assistant.Agent);
        Assert.Equal("2.1.301", assistant.AgentVersion);
        Assert.Equal("0.24.0", exam.Def.Harbor!.Version);
        Assert.Equal(900_000, exam.Def.Limits!.WallMs);
        Assert.Equal(["builds", "diff-in-scope", "tests-pass"], exam.ChecksFor(exam.Stages[0]).Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("provider", "\"anthropic\"", "provider", "an nb field")]
    [InlineData("model", "\"gpt-5.4\"", "model", "not an Anthropic model")]
    [InlineData("model", "\"anthropic/claude-haiku-5-5\"", "model", "provider prefix")]
    [InlineData("agent_version", "null", "agent_version", "pinned")]
    [InlineData("agent_version", "\"latest\"", "agent_version", "not a version number")]
    [InlineData("agent", "\"cursor\"", "agent", "unknown agent")]
    public void AVendorsAgent_IsValidated(string field, string value, string problemField, string message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        EditAssistant(repo, v => v[field] = JsonNode.Parse(value));
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == "axes" && p.Message.Contains(problemField) && p.Message.Contains(message));
    }

    [Fact]
    public void ThroughHarbor_NeedsAPinnedHarbor_AWallLimit_AndAnImageOnEveryFixture()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.EditJson("harbor-unit/exam.json", e => { e.Remove("harbor"); e["limits"] = JsonNode.Parse("{\"tool_calls\": 60}"); });
        repo.EditJson("../fixtures/ledger/fixture.json", f => f.Remove("image"));
        var problems = repo.Problems("harbor-unit");
        Assert.Contains(problems, p => p.Field == "harbor.version" && p.Message.Contains("required where a value runs through Harbor"));
        Assert.Contains(problems, p => p.Field == "limits.wall_ms" && p.Message.Contains("required"));
        Assert.Contains(problems, p => p.Field == "limits.tool_calls" && p.Message.Contains("not yet"));
        Assert.Contains(problems, p => p.File.EndsWith("fixture.json") && p.Field == "image");
    }

    [Fact]
    public void ThroughHarbor_RefusesATryHook_AndABareExamRefusesTheHarborBlocks()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.WriteScript("harbor-unit/hooks/setup.sh", "#!/bin/sh\nexit 0\n");
        repo.EditJson("harbor-unit/exam.json", e => e["hooks"] = JsonNode.Parse("{\"try\": {\"setup\": \"hooks/setup.sh\"}}"));
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == "hooks.try" && p.Message.Contains("fixture's image"));

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
        var entryDir = Path.Combine(repo.Root, "log", "exp");
        var hash = HarborCompile.Tasks(entryDir, exam, _ => new AgentRoute(BaseUrl, "sk-secret"));
        Assert.Equal(hash, HarborCompile.TreeHash(entryDir));
        var (task, step) = (TaskDir(entryDir), StepDir(entryDir));

        Assert.Equal(exam.Stages[0].Prompt!.TrimEnd() + "\n", File.ReadAllText(Path.Combine(step, "instruction.md")));

        var toml = File.ReadAllText(Path.Combine(task, "task.toml"));
        Assert.Contains("name = \"proctor/harbor-unit\"", toml);
        Assert.EndsWith("\n[[steps]]\nname = \"fix-null-deref\"\n", toml);
        Assert.Contains("timeout_sec = 900.0\nnetwork_mode = \"allowlist\"\nallowed_hosts = [\"library\"]", toml);
        // The environment carries the agent's policy, so a process left running never outlives it into a public network.
        Assert.Contains("build_timeout_sec = 1800.0\nnetwork_mode = \"allowlist\"\nallowed_hosts = [\"library\"]", toml);
        Assert.DoesNotContain("public", toml);
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
        Assert.True(File.Exists(Path.Combine(tests, "exam", "exam.json")));
        var test = File.ReadAllText(Path.Combine(step, "tests", "test.sh"));
        Assert.Contains("check 'builds' '/tests/fixture/checks/builds.sh'\n", test);
        Assert.Contains("check 'tests-pass' '/tests/fixture/checks/tests-pass.sh'\n", test);
        Assert.DoesNotContain("diff-in-scope", test);
        Assert.True(File.GetUnixFileMode(Path.Combine(step, "tests", "test.sh")).HasFlag(UnixFileMode.UserExecute));
        var env = File.ReadAllText(Path.Combine(step, "tests", "env.sh"));
        Assert.Contains("export PROCTOR_WORK='/app'\n", env);
        Assert.Contains("export PROCTOR_STAGE='fix-null-deref'\n", env);
        Assert.Contains("\"files_touched\":{\"paths\":[\"Ledger/**\"]", env);

        // Nothing compiled holds the key.
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(entryDir, "harbor"), "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("sk-secret"));
    }

    [Fact]
    public void Job_StatesEverySettingHarborWouldDefault_AndCarriesNoKey()
    {
        var assistant = new Assistant("cc-haiku", null, null, null, "claude-haiku-5-5", Agent: "claude-code", AgentVersion: "2.1.301");
        var job = HarborCompile.Job(assistant, "/t", "/j", new AgentRoute(BaseUrl, "sk-secret"));
        Assert.Equal(1, job["n_attempts"]!.GetValue<int>());
        Assert.Equal(0, job["retry"]!["max_retries"]!.GetValue<int>());
        Assert.Equal("docker", job["environment"]!["type"]!.GetValue<string>());
        Assert.Equal("podman", HarborCompile.Job(assistant, "/t", "/j", new AgentRoute(BaseUrl, "sk-secret"), engine: "podman")["environment"]!["type"]!.GetValue<string>());
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

    static readonly Assistant ClaudeCode = new("cc-haiku", null, null, null, "claude-haiku-5-5", Agent: "claude-code", AgentVersion: "2.1.301");

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
        Id = "r", Log = "e", Exam = "x", Configuration = new("cc-haiku", "sha256:v"), Stage = new("fix-null-deref", "sha256:t"), Attempt = 1, Host = "h", Versions = [], ProgramHash = "p",
        Agent = new AgentTrial("claude-code", null, "claude-haiku-5-5", [], [], "0.24.0", null),
    };

    [Fact]
    public void Ingest_LiftsTheTrialIntoTheAttempt_AndCountsByProctorsDefinitions()
    {
        using var repo = new TestRepo();
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"));
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        var manifest = NewManifest();

        Assert.Null(Ingest.Trial(trial, attempt, ClaudeCode, manifest, ClaudeCode.AgentVersion!));
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
    public void Ingest_AVendorAgentWithNoTurn_FailsTheAttempt_AsAnOutage()
    {
        using var repo = new TestRepo();
        var noTurn = new JsonObject { ["schema_version"] = "ATIF-v1.7", ["steps"] = new JsonArray(new JsonObject { ["step_id"] = 1, ["source"] = "user", ["message"] = "fix it" }) };
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"), "AgentTimeoutError", trajectory: noTurn.ToJsonString());
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        Assert.StartsWith("the agent took no turn (time_budget)", Ingest.Trial(trial, attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
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
        Assert.Equal(failure, Ingest.Trial(trial, attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
        if (exitReason is not null) Assert.Equal(exitReason, ReadJson(Path.Combine(attempt, "core.json"))["exit_reason"]!.GetValue<string>());
    }

    [Fact]
    public void Ingest_IsStrict_AboutTheVersion_AndTheTools()
    {
        using var repo = new TestRepo();
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        Assert.Equal("agent_version_mismatch: the trial ran claude-code 2.1.302, the value pins 2.1.301",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t1"), version: "2.1.302"), attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
        Assert.Equal("adapter_unmapped: tool 'mcp__fs__read' has no kind for claude-code",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t2"), trajectory: Trajectory("Read", "mcp__fs__read")), attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
        Assert.Equal("adapter_unmapped: the trial has no agent/trajectory.json",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t3"), trajectory: ""), attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
        var noDiff = WriteTrial(Path.Combine(repo.Root, "t5"));
        File.Delete(Path.Combine(noDiff, "artifacts", "logs", "artifacts", "diff.patch"));
        Assert.StartsWith("the trial collected no diff", Ingest.Trial(noDiff, attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
        Assert.Equal("agent exited without a trajectory: NonZeroAgentExitCodeError: it went wrong",
            Ingest.Trial(WriteTrial(Path.Combine(repo.Root, "t4"), "NonZeroAgentExitCodeError", trajectory: ""), attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
    }

    /// <summary>
    /// A stand-in for `uv tool run harbor@… run --config job.json`: it records its argv and the two variables proctor
    /// sets, then writes one trial where the job file says: a trial of steps, as Harbor writes one for a task of steps, the one step being harbor-unit's stage.
    /// </summary>
    static string FakeUv(TestRepo repo, string trialScript) => repo.WriteScript("fake-uv.sh", $$"""
        #!/usr/bin/env bash
        set -euo pipefail
        echo "$*" > "{{repo.Root}}/uv.argv"
        echo "telemetry=${HARBOR_TELEMETRY:-} key=${ANTHROPIC_API_KEY:-}" > "{{repo.Root}}/uv.env"
        jobs=$(jq -r .jobs_dir job.json)
        t="$jobs/job/harbor-unit__abc123"
        s="$t/steps/fix-null-deref"
        mkdir -p "$s/agent" "$s/artifacts/logs/artifacts" "$s/verifier"
        {{trialScript}}
        """);

    const string GoodTrial = """
        echo '{"agent_info": {"name": "claude-code", "version": "2.1.301"}, "exception_info": null, "step_results": [{"step_name": "fix-null-deref", "agent_result": {"n_input_tokens": 10, "n_output_tokens": 5, "cost_usd": 0.001}, "agent_execution": {"started_at": "2026-11-23T10:00:00Z", "finished_at": "2026-11-23T10:00:02Z"}, "exception_info": null}]}' > "$t/result.json"
        echo '{"steps": [{"step_id": 1, "source": "agent", "message": "done", "tool_calls": [{"tool_call_id": "a", "function_name": "Edit", "arguments": {}}]}]}' > "$s/agent/trajectory.json"
        echo 'diff --git a/x b/x' > "$s/artifacts/logs/artifacts/diff.patch"
        echo '{"builds": 1, "tests-pass": 0}' > "$s/verifier/reward.json"
        echo 'dotnet build: 0 errors' > "$s/verifier/builds.out"; echo 0 > "$s/verifier/builds.exit"
        echo '1 of 6 tests failed: Parse_LineWithoutMemo_HasNullMemo' > "$s/verifier/tests-pass.out"; echo 1 > "$s/verifier/tests-pass.exit"
        """;

    [Fact]
    public void Run_ThroughHarbor_CompilesOnce_RunsHarborPerTry_AndIngests()
    {
        using var scratch = new TestRepo();
        var (repo, keyVar) = AgentRepo(FakeUv(scratch, GoodTrial));
        using var _r = repo;
        var id = Run(repo)!;
        var exam = repo.LoadExam("harbor-unit");
        var entryDir = Layout.LogEntry(repo.Root, id);

        var entry = ReadJson(Path.Combine(entryDir, "entry.json"));
        Assert.Equal("0.24.0", entry["versions"]!["harbor"]!.GetValue<string>());
        Assert.Equal("0.24.0", entry["harbor"]!["version"]!.GetValue<string>());
        Assert.Equal(HarborCompile.TreeHash(entryDir), entry["harbor"]!["tasks_hash"]!.GetValue<string>());

        var attempt = repo.StageDir("harbor-unit", K, StageId);
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
        var job = ReadJson(Path.Combine(repo.TryDir("harbor-unit", K, 1), "harbor", "job.json"));
        Assert.Equal(TaskDir(entryDir), job["tasks"]![0]!["path"]!.GetValue<string>());
        // The key reached Harbor's process and no file.
        Assert.DoesNotContain(Directory.GetFiles(entryDir, "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains($"sk-test-{keyVar}"));

        // grade reads the script verdicts the verifier left, and runs the built-ins over the trajectory and the diff.
        var grade = Assert.Single(Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null));
        Assert.False(grade.Pass);
        Assert.Equal(new Verdict(Verdict.Pass, "dotnet build: 0 errors"), grade.Checks!["builds"]);
        Assert.Equal(new Verdict(Verdict.Fail, "1 of 6 tests failed: Parse_LineWithoutMemo_HasNullMemo"), grade.Checks["tests-pass"]);
        Assert.Equal(new Verdict(Verdict.Fail, "touched 1 outside expected: x"), grade.Checks["diff-in-scope"]);
        var row = Assert.Single(Results.Collect(repo.Root, exam, Bank.Select(repo.Root, exam).Current));
        Assert.Equal("ok", row.ExitReason);
        Assert.Equal(1, row.ToolCalls);
        Assert.Equal(15, row.Usage!.Total);
        Assert.Equal(0.001, row.Usage.CostUsd);
        // The failed correctness check leads the reasons, ahead of the guardrail the exam declares first.
        Assert.Equal(["tests-pass", "diff-in-scope"], row.Reasons!.Keys);
    }

    [Fact]
    public void Grade_AScriptChangedBetweenRuns_LeavesTheOlderTrysVerdictsOutdated_AndANewRunMakesItAgain()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, GoodTrial));
        using var _r = repo;
        var first = Run(repo)!;
        AppendTo(repo, "fixtures/ledger/checks/tests-pass.sh", "# stricter now\n");

        // A second try is made by a new run, which compiles the script as it is now.
        repo.EditJson("harbor-unit/exam.json", e => e["attempts"] = 2);
        var second = Run(repo)!;
        Assert.NotEqual(first, second);
        var exam = repo.LoadExam("harbor-unit");
        var grades = Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null);
        Assert.Equal([(1, Verdict.Outdated), (2, Verdict.Fail)], grades.OrderBy(g => g.Attempt).Select(g => (g.Attempt, g.Checks!["tests-pass"].Result)));
        Assert.All(grades, g => Assert.Equal(Verdict.Pass, g.Checks!["builds"].Result));
        var firstTry = repo.TryDir("harbor-unit", K, 1);
        Assert.Equal("1 try holds outdated verdicts for tests-pass: through Harbor a script check runs when the try is made, and this check has changed or been added since, so those verdicts are left out of every number. "
            + $"Make the try again: move its directory out of bank/harbor-unit/ ({Path.GetFileName(firstTry)}) and run `proctor run harbor-unit`, which makes what is missing with the checks as they are now.", Grade.OutdatedSummary(exam.Id, grades));
        var rows = Results.Collect(repo.Root, exam, Bank.Select(repo.Root, exam).Current);
        Assert.Equal(["tests-pass: changed since the attempt was made: fixtures/ledger/checks/tests-pass.sh, so the verdict on file may not be this check's as it is now; make the attempt again", null],
            rows.Select(r => r.Outdated));

        // Moved out, it is made again by a new run, with the checks as they are now.
        Directory.Delete(firstTry, recursive: true);
        var third = Run(repo)!;
        Assert.DoesNotContain(third, new[] { first, second });
        Assert.All(Runner.LoadEntry(repo.Root, third).Attempts, a => Assert.Equal((K, 3), (a.Configuration, a.Attempt)));
        exam = repo.LoadExam("harbor-unit");
        grades = Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null);
        Assert.Equal([Verdict.Fail, Verdict.Fail], grades.Where(g => g.Checks is not null).Select(g => g.Checks!["tests-pass"].Result));
    }

    /// <summary>
    /// An agent attempt as ingest leaves it, graded on its own: script verdicts from the verifier, the rest from the trajectory
    /// and the diff. Its Harbor task is compiled first, as `run` compiles it; <paramref name="afterwards"/> changes the
    /// exam or fixture after the attempt was made, and grade reads them as they are then.
    /// </summary>
    static Dictionary<string, Verdict> GradeAgentAttempt(Action<string> edit, Action<JsonObject>? editExam = null, Action<TestRepo>? afterwards = null, bool compiled = true)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        if (editExam is not null) repo.EditJson("harbor-unit/exam.json", editExam);
        var exam = repo.LoadExam("harbor-unit");
        var task = Path.Combine(repo.Root, "task");
        if (compiled) HarborCompile.Task(task, exam, exam.Assistants[0], exam.Stages[0], new AgentRoute(BaseUrl, "k"));
        var attempt = Directory.CreateDirectory(Path.Combine(repo.Root, "attempt")).FullName;
        var trial = WriteTrial(Path.Combine(repo.Root, "trial"));
        File.WriteAllText(Path.Combine(trial, "verifier", "tests-pass.out"), "6 of 6 tests pass\n");
        Assert.Null(Ingest.Trial(trial, attempt, ClaudeCode, NewManifest(), ClaudeCode.AgentVersion!));
        edit(attempt);
        if (afterwards is not null) { afterwards(repo); exam = repo.LoadExam("harbor-unit"); }
        var context = new AttemptContext(exam.Dir, attempt, Path.Combine(repo.Root, "work"), "e", K, exam.Stages[0], 1)
            { Expect = exam.ExpectFor(exam.Stages[0]), Fixture = exam.FixtureOf(exam.Stages[0]), ScriptsInVerifier = true, Vendor = true, VerifierTask = task };
        return Grade.Attempt(context, exam, Transcript.Read(attempt));
    }

    static void AppendTo(TestRepo repo, string path, string text) => File.AppendAllText(Path.Combine(repo.Root, path), text);

    [Fact]
    public void Grade_AnEditedScript_IsOutdated_AndOnlyItsOwnCheck()
    {
        var checks = GradeAgentAttempt(_ => { }, afterwards: repo => AppendTo(repo, "fixtures/ledger/checks/builds.sh", "# stricter now\n"));
        Assert.Equal(new Verdict(Verdict.Outdated, "changed since the attempt was made: fixtures/ledger/checks/builds.sh, so the verdict on file may not be this check's as it is now; make the attempt again"), checks["builds"]);
        Assert.Equal(new Verdict(Verdict.Pass, "6 of 6 tests pass"), checks["tests-pass"]);
        Assert.Equal(Verdict.Pass, checks["diff-in-scope"].Result);   // a built-in is recomputed, as ever
        Assert.Null(Grade.Pass(checks, ["builds", "tests-pass"]));
        Assert.StartsWith("builds: changed since", Grade.Undecided(checks, ["builds", "tests-pass"]));
        Assert.Equal(Grade.Undecided(checks, ["builds", "tests-pass"]), Grade.Outdated(checks, ["builds", "tests-pass"]));
    }

    [Fact]
    public void Grade_AChangedFileBesideTheScripts_OutdatesEveryScriptCheck()
    {
        // An acceptance test, a service, a helper: whatever a script reads, from the exam or the fixture.
        var checks = GradeAgentAttempt(_ => { }, afterwards: repo => repo.Write("harbor-unit/acceptance/fix-null-deref.cs", "// new acceptance\n"));
        Assert.Equal(Verdict.Outdated, checks["builds"].Result);
        Assert.Equal(Verdict.Outdated, checks["tests-pass"].Result);
        Assert.StartsWith("changed since the attempt was made: exams/harbor-unit/acceptance/fix-null-deref.cs", checks["tests-pass"].Reason);
    }

    [Fact]
    public void Grade_AFixturesReadme_OutdatesNothing_AndAReadmeDeeperInStillDoes()
    {
        var top = GradeAgentAttempt(_ => { }, afterwards: repo => File.WriteAllText(Path.Combine(repo.Root, "fixtures", "ledger", "README.md"), "reworded\n"));
        Assert.Equal(Verdict.Pass, top["builds"].Result);
        var deeper = GradeAgentAttempt(_ => { }, afterwards: repo => repo.Write("harbor-unit/acceptance/README.md", "a script may read this\n"));
        Assert.Equal(Verdict.Outdated, deeper["builds"].Result);
    }

    [Theory]
    [InlineData("predictions/faster.json")]   // written after the tries, as a prediction about tries to come is
    [InlineData("combinations.json")]
    [InlineData("retired.json")]
    [InlineData("README.md")]                 // for people: a reworded sentence is not a changed check
    public void Grade_AFileOnlyProctorReads_OutdatesNothing(string file)
    {
        var checks = GradeAgentAttempt(_ => { }, afterwards: repo => repo.Write("harbor-unit/" + file, "{}\n"));
        Assert.Equal(Verdict.Pass, checks["builds"].Result);
        Assert.Equal(new Verdict(Verdict.Pass, "6 of 6 tests pass"), checks["tests-pass"]);
    }

    [Theory]
    [InlineData("exams/harbor-unit/acceptance/__pycache__/conftest.cpython-314.pyc")]   // left by a shakedown on this machine
    [InlineData("fixtures/ledger/checks/.pytest_cache/README.md")]
    [InlineData("task/tests/exam/acceptance/__pycache__/conftest.cpython-314.pyc")]     // compiled in on the machine the attempt was made on
    public void Grade_AToolsCacheBesideTheScripts_OutdatesNothing(string cache)
    {
        var checks = GradeAgentAttempt(_ => { }, afterwards: repo =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Root, cache))!);
            File.WriteAllText(Path.Combine(repo.Root, cache), "bytes of this machine\n");
        });
        Assert.Equal(Verdict.Pass, checks["builds"].Result);
        Assert.Equal(new Verdict(Verdict.Pass, "6 of 6 tests pass"), checks["tests-pass"]);
    }

    [Fact]
    public void AToolsCache_IsNoPartOfAFixtureOrATask_AndIsNeverCompiled()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        var before = repo.LoadExam("harbor-unit");
        foreach (var cache in new[] { "fixtures/ledger/repo/__pycache__/a.pyc", "fixtures/ledger/repo/Ledger/.pytest_cache/v/nodeids", "exams/harbor-unit/acceptance/__pycache__/a.pyc" })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo.Root, cache))!);
            File.WriteAllText(Path.Combine(repo.Root, cache), "bytes of this machine\n");
        }
        var exam = repo.LoadExam("harbor-unit");
        Assert.Equal(before.Fixtures["ledger"].Hash, exam.Fixtures["ledger"].Hash);
        Assert.Equal(before.StageHashes, exam.StageHashes);

        var task = Path.Combine(repo.Root, "task");
        HarborCompile.Task(task, exam, exam.Assistants[0], exam.Stages[0], new AgentRoute(BaseUrl, "k"));
        Assert.True(File.Exists(Path.Combine(task, "tests", "exam", "program.nb")));
        Assert.DoesNotContain(Directory.EnumerateDirectories(task, "*", SearchOption.AllDirectories), d => Tree.Litter.Contains(Path.GetFileName(d)));
    }

    [Fact]
    public void Grade_ANewScriptCheck_IsOutdated_NotAnError()
    {
        var checks = GradeAgentAttempt(_ => { }, afterwards: repo =>
        {
            repo.WriteScript("harbor-unit/checks/no-todo.sh", "#!/bin/bash\nexit 0\n");
            repo.EditJson("harbor-unit/exam.json", e => e["grading"]!["checks"]!["no-todo"] = JsonNode.Parse("{\"script\": \"checks/no-todo.sh\", \"description\": \"no TODO left\"}"));
        });
        Assert.Equal(new Verdict(Verdict.Outdated, "this check did not exist when the attempt was made, so the verifier never ran it; make the attempt again"), checks["no-todo"]);
        Assert.Equal(new Verdict(Verdict.Pass, "dotnet build: 0 errors"), checks["builds"]);   // a new check's script outdates no other check
        Assert.Equal(Verdict.Pass, checks["tests-pass"].Result);
    }

    [Fact]
    public void Grade_ACheckPointedAtAnotherScript_OrChangedExpectations_IsOutdated()
    {
        var checks = GradeAgentAttempt(_ => { }, afterwards: repo =>
        {
            File.Copy(Path.Combine(repo.Root, "fixtures/ledger/checks/builds.sh"), Path.Combine(repo.Root, "fixtures/ledger/checks/builds-release.sh"));
            var fixture = Path.Combine(repo.Root, "fixtures/ledger/fixture.json");
            File.WriteAllText(fixture, File.ReadAllText(fixture).Replace("checks/builds.sh", "checks/builds-release.sh"));
        });
        Assert.Equal(new Verdict(Verdict.Outdated, "this check ran another script when the attempt was made; make the attempt again"), checks["builds"]);
        Assert.Equal(Verdict.Pass, checks["tests-pass"].Result);

        checks = GradeAgentAttempt(_ => { }, afterwards: repo => repo.EditStage("harbor-unit", StageId, t => t["expect"]!["files_touched"]!["paths"] = new JsonArray("Ledger/**", "README.md")));
        Assert.Equal(new Verdict(Verdict.Outdated, "the task's expected values changed since the attempt was made, and the verifier ran with the old ones; make the attempt again"), checks["builds"]);
    }

    [Fact]
    public void Grade_AnUnchangedScript_KeepsItsVerdict_ThroughChangesOutsideIt()
    {
        // A value added, a built-in check added, a description reworded: none is an input to a script's verdict.
        var checks = GradeAgentAttempt(_ => { }, afterwards: repo =>
        {
            repo.EditJson("harbor-unit/exam.json", e => e["axes"]!["assistant"]!["values"]!["cc-sonnet"] = JsonNode.Parse("""{"note": "Claude Code on Sonnet", "agent": "claude-code", "agent_version": "2.1.301", "model": "claude-sonnet-5-5"}"""));
            repo.EditJson("harbor-unit/exam.json", e => e["grading"]!["checks"]!["few-calls"] = JsonNode.Parse("{\"max_tool_calls\": 2}"));
            var fixture = Path.Combine(repo.Root, "fixtures/ledger/fixture.json");
            File.WriteAllText(fixture, File.ReadAllText(fixture).Replace("the repository builds", "the solution builds"));
        });
        Assert.Equal(new Verdict(Verdict.Pass, "dotnet build: 0 errors"), checks["builds"]);
        Assert.Equal(new Verdict(Verdict.Pass, "6 of 6 tests pass"), checks["tests-pass"]);
        Assert.Equal(Verdict.Fail, checks["few-calls"].Result);
    }

    [Fact]
    public void Grade_WithNoCompiledCopy_EveryScriptCheckIsOutdated()
    {
        var checks = GradeAgentAttempt(_ => { }, compiled: false);
        Assert.Equal(new Verdict(Verdict.Outdated, "its run kept no compiled copy of its script checks, so nothing shows this verdict is the check's as it is now; make the attempt again"), checks["builds"]);
        Assert.Equal(Verdict.Outdated, checks["tests-pass"].Result);
        Assert.Equal(Verdict.Pass, checks["diff-in-scope"].Result);
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
    [InlineData("correctness")]
    [InlineData("integrity")]
    public void Validate_ANudgeOnlyCheckThatDecides_BesideAVendorsAgent_IsAProblem(string list)
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
    public void Run_HarborWithoutATrial_LosesTheTry_AndTheNextRunMakesItAgain()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "echo 'docker: Cannot connect to the Docker daemon' >&2; rm -rf \"$jobs\"; exit 1"));
        using var _r = repo;
        var id = Run(repo)!;
        var attempt = repo.StageDir("harbor-unit", K, StageId);
        Assert.Equal("failed", File.ReadAllText(Path.Combine(attempt, "status")).Trim());
        Assert.Equal("Harbor exited 1 without a trial: docker: Cannot connect to the Docker daemon", ReadJson(Path.Combine(attempt, "manifest.json"))["status_reason"]!.GetValue<string>());

        File.WriteAllText(Path.Combine(scratch.Root, "exams", "fake-uv.sh"), File.ReadAllText(Path.Combine(scratch.Root, "exams", "fake-uv.sh")).Replace("echo 'docker: Cannot connect to the Docker daemon' >&2; rm -rf \"$jobs\"; exit 1", GoodTrial));
        // A lost try is not taken up again: a new run makes the try again, under the next number.
        var again = Run(repo)!;
        Assert.NotEqual(id, again);
        Assert.Equal("failed", File.ReadAllText(Path.Combine(attempt, "status")).Trim());
        Assert.Equal("completed", File.ReadAllText(Path.Combine(repo.StageDir("harbor-unit", K, StageId, 2), "status")).Trim());
    }

    [PodmanFact]
    public void Run_OnPodman_RecordsTheEngine_AndHandsItToHarbor()
    {
        using var scratch = new TestRepo();
        var (repo, _) = AgentRepo(FakeUv(scratch, "exit 1"));
        using var _r = repo;
        var config = Path.Combine(repo.Root, "exams", "proctor.json");
        var c = ReadJson(config); c["harbor"]!["engine"] = "podman"; File.WriteAllText(config, c.ToJsonString());
        var id = Run(repo)!;
        Assert.Equal("podman", ReadJson(Path.Combine(Layout.LogEntry(repo.Root, id), "entry.json"))["harbor"]!["engine"]!.GetValue<string>());
        var job = Path.Combine(repo.TryDir("harbor-unit", K, 1), Layout.HarborDir, Layout.HarborJobFile);
        Assert.Equal("podman", ReadJson(job)["environment"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Run_WithoutTheAgentsEndpoint_FailsBeforeAnythingIsMade()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        repo.WriteProctorConfig();
        var e = Assert.Throws<ProctorException>(() => Run(repo));
        Assert.Contains("no agents.claude-code", e.Message);
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "log")));
    }

    // A second machine file (--config) in place of exams/proctor.json.

    const string PublicUrl = "https://api.anthropic.com";

    [Fact]
    public void Config_FromAnotherFile_IsTheMachine_AndMessagesNameIt()
    {
        var (repo, keyVar) = AgentRepo();
        using var _r = repo;
        var file = Path.Combine(repo.Root, "exams", "proctor.other.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            agents = new Dictionary<string, object> { ["claude-code"] = new { base_url = PublicUrl, api_key = "${" + keyVar + "}" } },
            harbor = new { engine = "podman" },
        }));
        var config = Exam.LoadConfig(repo.Root, [], file);
        Assert.Equal("podman", config.HarborOrDefault.Engine);
        Assert.Equal(Path.Combine("exams", "proctor.other.json"), config.Source);
        Assert.Equal("docker", Exam.LoadConfig(repo.Root, []).HarborOrDefault.Engine);
        Assert.Equal(Path.Combine("exams", "proctor.json"), Exam.LoadConfig(repo.Root, []).Source);
        Assert.Contains("--config nowhere.json: no such file", Assert.Throws<ProctorException>(() => Exam.LoadConfig(repo.Root, [], "nowhere.json")).Message);
    }

    [Fact]
    public void Job_NamesTheEngine_AndHarborAtItsPin()
    {
        Assert.Equal("""{"type":"podman"}""", HarborCompile.Job(ClaudeCode, "/t", "/j", new AgentRoute(PublicUrl, "sk"), engine: "podman")["environment"]!.ToJsonString());
        Assert.Equal("tool run --exclude-newer 7 days harbor@0.24.0 run --config job.json", string.Join(" ", HarborRun.UvArgs("0.24.0")));
    }

    // Codex and Qwen Code: Harbor's agents on an OpenAI-dialect route, Qwen Code's install replaced by proctor's pin check.

    const string OpenAiUrl = "http://library:8086/x/openai/v1";
    static readonly Assistant Codex = new("codex-mini", null, null, null, "gpt-5-mini", Agent: "codex", AgentVersion: "0.163.0");
    static readonly Assistant QwenCode = new("qwen", null, null, null, "qwen3-coder-next", Agent: "qwen-code", AgentVersion: "0.25.2");

    [Theory]
    [InlineData("codex", "gpt-5-mini", null)]
    [InlineData("codex", "o4-mini", null)]
    [InlineData("codex", "claude-haiku-5-5", "not an OpenAI model")]
    [InlineData("qwen-code", "qwen3-coder-next", null)]
    [InlineData("qwen-code", "gpt-5-mini", "not a Qwen model")]
    [InlineData("claude-code", "qwen3-coder-next", "not an Anthropic model")]
    public void AVendorsAgent_TakesItsVendorsModelsOnly(string agent, string model, string? message)
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        EditAssistant(repo, v => { v["agent"] = agent; v["model"] = model; });
        var problems = repo.Problems("harbor-unit").Where(p => p.Field == "axes").ToList();
        if (message is null) Assert.Empty(problems);
        else Assert.Contains(problems, p => p.Message.Contains("model: ") && p.Message.Contains(message));
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
    [InlineData("modal", HarborConfig.GoneWhere)]
    [InlineData("nerdctl", "unknown engine 'nerdctl'; known: docker, podman")]
    public void Config_KnowsDockerAndPodman_AndSaysWhereModalWent(string engine, string? message)
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
        var job = HarborCompile.Job(Codex, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("codex")));
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
        var job = HarborCompile.Job(Codex, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("codex"), Config: "/exams/codex.toml"));
        Assert.Equal("""{"version":"0.163.0","config":"/exams/codex.toml"}""", job["agents"]![0]!["kwargs"]!.ToJsonString());
    }

    [Fact]
    public void Compile_CopiesTheFixturesServices_BesideTheImage()
    {
        using var repo = new TestRepo();
        repo.CopyExam("conformance");
        var entryDir = Path.Combine(repo.Root, "log", "exp");
        HarborCompile.Tasks(entryDir, repo.LoadExam("conformance"), _ => new AgentRoute("http://localhost:8765/v1", "stub"));
        var task = TaskDir(entryDir, "script=ok,assistant=codex", "conformance");
        var environment = Path.Combine(task, "environment");

        var written = File.ReadAllText(Path.Combine(TestRepo.SourceRoot, "fixtures", "stub", "services", "docker-compose.yaml"));
        Assert.Equal(written.Replace("services:\n", "services:\n  main: { cap_drop: [NET_RAW] }\n"), File.ReadAllText(Path.Combine(environment, "docker-compose.yaml")));
        Assert.True(File.Exists(Path.Combine(environment, "stub.py")));
        Assert.True(File.Exists(Path.Combine(environment, "Dockerfile")));
        Assert.Contains("allowed_hosts = [\"localhost\"]", File.ReadAllText(Path.Combine(task, "task.toml")));
    }

    /// <summary>The agent's container loses NET_RAW beside a fixture's services, so it cannot capture what they say on the loopback it shares with them; a compose file that declares `main` is its author's.</summary>
    [Theory]
    [InlineData("services:\n  api:\n    image: x\n", "services:\n  main: { cap_drop: [NET_RAW] }\n  api:\n    image: x\n")]
    [InlineData("# why\nservices: # beside\n  api: {}\nvolumes:\n  main: {}\n", "# why\nservices: # beside\n  main: { cap_drop: [NET_RAW] }\n  api: {}\nvolumes:\n  main: {}\n")]
    [InlineData("services:\n  main:\n    cap_add: [NET_RAW]\n  api: {}\n", "services:\n  main:\n    cap_add: [NET_RAW]\n  api: {}\n")]
    public void Compile_DropsNetRaw_FromTheAgentsContainer_BesideServices(string compose, string compiled) =>
        Assert.Equal(compiled, HarborCompile.Confined(compose));

    [Theory]
    [InlineData("nowhere", "no docker-compose.yaml in nowhere")]
    [InlineData("clash", "Dockerfile, repo and params are the task's own names in environment/")]
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
        var agent = HarborCompile.Job(QwenCode, "/t", "/j", new AgentRoute(OpenAiUrl, "sk-secret", HarborCompile.KeyEnv("qwen-code")))["agents"]![0]!;
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
        repo.EditJson("harbor-unit/exam.json", e =>
        {
            e["axes"]!["assistant"]!["values"] = JsonNode.Parse("""
                { "codex-mini": {"note": "Codex", "agent": "codex", "agent_version": "0.163.0", "model": "gpt-5-mini"},
                  "qwen": {"note": "Qwen Code", "agent": "qwen-code", "agent_version": "0.25.2", "model": "qwen3-coder-next"} }
                """);
            e["configurations"] = JsonNode.Parse("""{ "codex-mini": {"assistant": "codex-mini"}, "qwen": {"assistant": "qwen"} }""");
        });
        var exam = repo.LoadExam("harbor-unit");
        var entryDir = Path.Combine(repo.Root, "log", "exp");
        HarborCompile.Tasks(entryDir, exam, _ => new AgentRoute(OpenAiUrl, "sk-secret"));

        var codex = File.ReadAllText(Path.Combine(TaskDir(entryDir, "assistant=codex-mini"), "environment", "Dockerfile"));
        Assert.Contains("npm install -g @openai/codex@0.163.0", codex);
        Assert.Contains("ln -sf \"$(command -v codex)\" /usr/local/bin/codex", codex);
        var qwen = File.ReadAllText(Path.Combine(TaskDir(entryDir, "assistant=qwen"), "environment", "Dockerfile"));
        // nvm under RUN's /bin/sh finds its own directory only when told, and Harbor's agents look for Node in ~/.nvm.
        Assert.Contains("export NVM_DIR=\"$HOME/.nvm\" && ", qwen);
        Assert.Contains("nvm install 22", qwen);
        Assert.Contains("npm install -g @qwen-code/qwen-code@0.25.2", qwen);
        Assert.Contains("allowed_hosts = [\"library\"]", File.ReadAllText(Path.Combine(TaskDir(entryDir, "assistant=qwen"), "task.toml")));

        Assert.Equal(["qwen_code_agent.py"], Directory.GetFiles(HarborCompile.AgentsDir(entryDir)).Select(Path.GetFileName));
        Assert.Contains("class QwenCodeAgent(QwenCode)", File.ReadAllText(Path.Combine(HarborCompile.AgentsDir(entryDir), "qwen_code_agent.py")));
    }

    [Fact]
    public void Compile_ClaudeCodeOnly_BringsNoAgent()
    {
        var (repo, _) = AgentRepo();
        using var _r = repo;
        var entryDir = Path.Combine(repo.Root, "log", "exp");
        var hash = HarborCompile.Tasks(entryDir, repo.LoadExam("harbor-unit"), _ => new AgentRoute(BaseUrl, "sk-secret"));
        Assert.False(Directory.Exists(HarborCompile.AgentsDir(entryDir)));
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
        var (core, unmapped) = Ingest.Count(With(isError: true), Assistant.ClaudeCode, "ok");
        Assert.Null(unmapped);
        Assert.Equal(2, core!.ToolCalls);
        Assert.Equal(1, core.Kinds["shell"]);
        Assert.Equal(1, core.Kinds[Ingest.NoSuchTool]);
        Assert.Equal("tool 'bash' has no kind for claude-code", Ingest.Count(With(isError: false), Assistant.ClaudeCode, "ok").Unmapped);
    }

    // nb as a Harbor agent: the gate exam's harbor route.

    static readonly Assistant NbThroughHarbor = new("nb-harbor", null, "nb", "Mock", "mock", Agent: "nb");

    [Fact]
    public void NbThroughHarbor_IsAnAgent_ButNotAVendors()
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        var exam = repo.LoadExam("gate");
        var (bare, harbor) = (exam.Assistants[0], exam.Assistants[1]);
        Assert.False(bare.IsAgent);
        Assert.True(harbor.IsAgent);
        Assert.False(harbor.IsVendor);
        Assert.True(exam.HasHarbor);
    }

    [Theory]
    [InlineData("agent_version", "\"0.9.0\"", "agent_version", "the binary's")]
    [InlineData("provider", "null", "provider", "required")]
    public void NbThroughHarbor_IsValidated(string field, string value, string problemField, string message)
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        repo.EditJson("gate/exam.json", e => e["axes"]!["route"]!["values"]!["harbor"]![field] = JsonNode.Parse(value));
        Assert.Contains(repo.Problems("gate"), p => p.Field == "axes" && p.Message.Contains(problemField) && p.Message.Contains(message));
    }

    [Fact]
    public void Compile_NbThroughHarbor_CopiesNbsLayer_ReachesNothingOnMock_AndCompilesTheAgent()
    {
        using var repo = new TestRepo();
        repo.CopyExam("gate");
        var exam = repo.LoadExam("gate");
        var entryDir = Path.Combine(repo.Root, "log", "exp");
        var hash = HarborCompile.Tasks(entryDir, exam, _ => new AgentRoute(null, null), "proctor-nb:0123456789abcdef");
        var task = TaskDir(entryDir, "route=harbor", "gate");
        Assert.False(Directory.Exists(TaskDir(entryDir, "route=bare", "gate")));

        var toml = File.ReadAllText(Path.Combine(task, "task.toml"));
        Assert.Contains("timeout_sec = 120.0\nnetwork_mode = \"no-network\"\n\n[environment]", toml);
        Assert.Contains("build_timeout_sec = 1800.0\nnetwork_mode = \"no-network\"", toml);
        var dockerfile = File.ReadAllText(Path.Combine(task, "environment", "Dockerfile"));
        Assert.Contains("COPY --from=proctor-nb:0123456789abcdef /opt/nb /opt/nb\nENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1\nRUN /opt/nb/nb --version\n", dockerfile);
        Assert.DoesNotContain("claude", dockerfile);
        Assert.Contains("check 'new-file' '/tests/exam/checks/new-file.sh'\n", File.ReadAllText(Path.Combine(task, "steps", "writes", "tests", "test.sh")));
        // A stage after the first starts from the workspace as the try began: its step puts it back before the agent runs.
        Assert.Contains("git reset -q --hard HEAD && git clean -fdq", File.ReadAllText(Path.Combine(task, "steps", "writes", "workdir", "setup.sh")));
        Assert.False(Directory.Exists(Path.Combine(task, "steps", "plain", "workdir")));

        // The agent is compiled beside the tasks, and a changed agent is a changed tree.
        var agent = Path.Combine(HarborCompile.AgentsDir(entryDir), "nb_agent.py");
        Assert.Contains("class NbAgent(BaseAgent, ACPAgentMixin)", File.ReadAllText(agent));
        Assert.Contains("+", hash);
        File.AppendAllText(agent, "# edited\n");
        Assert.NotEqual(hash, HarborCompile.TreeHash(entryDir));
    }

    [Fact]
    public void Job_NbThroughHarbor_ImportsProctorsAgent_WithTheAttemptsProgram_AndTheKeyByNameOnly()
    {
        var route = new AgentRoute("http://library:8086/x/qcoder/v1", "sk-secret", "LLM_GATEWAY_KEY", new() { ["LLM_GATEWAY"] = "http://library:8086/x" });
        var job = HarborCompile.Job(NbThroughHarbor, "/t", "/j", route, new NbAttempt("/attempt/program.jsonl", "/exams/nb.json"));
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
        var manifest = NewManifest() with { Agent = new AgentTrial("nb", null, "mock", [], [], "0.24.0", null) };

        Assert.Null(Ingest.Trial(trial, attempt, NbThroughHarbor, manifest, "0.9.0"));
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
        AgentTrial nbTrial = new("nb", null, "mock", [], [], "0.24.0", null);
        string? Ingested(string trial) => Ingest.Trial(trial, attempt, NbThroughHarbor, NewManifest() with { Agent = nbTrial }, "0.9.0");

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
