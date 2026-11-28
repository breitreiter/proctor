using System.Text.Json;
using System.Text.Json.Nodes;
using Auspex;

namespace Auspex.Tests;

/// <summary>
/// A try through Harbor: one Harbor task whose steps are the stages, one trial per try, each step ingested
/// as its stage. A fake uv stands in for Harbor and writes what Harbor 0.24.0 writes for a multi-step trial (seen on a
/// real run, 2026-11-23): step_results in result.json, and steps/&lt;name&gt;/{agent,verifier,artifacts}.
/// </summary>
public class StagedHarborTests
{
    const string BaseUrl = "http://library:8086/x/anthropic";

    /// <summary>harbor-unit as two stages on the ledger fixture, look then fix, with a fake uv for Harbor.</summary>
    static TestRepo Repo(string trialScript, bool chained = false)
    {
        var repo = new TestRepo();
        repo.CopyExam("harbor-unit");
        repo.EditJson("harbor-unit/exam.json", e =>
        {
            if (chained) e["chained"] = true;
            var fix = e["stages"]![0]!.DeepClone();
            e["stages"] = new JsonArray(JsonNode.Parse("""{"id": "look", "about": "Find the failing test", "prompt": "Which test fails? Say its name."}"""), fix);
        });
        var uv = repo.WriteScript("fake-uv.sh", $$$"""
            #!/usr/bin/env bash
            set -euo pipefail
            cp job.json "{{{repo.Root}}}/job.seen.json"
            jobs=$(jq -r .jobs_dir job.json)
            t="$jobs/job/harbor-unit__abc123"
            step() {   # name, exception type or "", reward json
              local s="$t/steps/$1"
              mkdir -p "$s/agent" "$s/artifacts/logs/artifacts" "$s/verifier"
              echo "{\"steps\": [{\"step_id\": 1, \"source\": \"agent\", \"message\": \"did $1\", \"tool_calls\": [{\"tool_call_id\": \"a\", \"function_name\": \"Edit\", \"arguments\": {}}]}]}" > "$s/agent/trajectory.json"
              echo "diff --git a/Ledger/$1 b/Ledger/$1" > "$s/artifacts/logs/artifacts/diff.patch"
              echo "$3" > "$s/verifier/reward.json"
            }
            {{{trialScript}}}
            """);
        File.WriteAllText(Path.Combine(repo.Root, "exams", "auspex.json"), JsonSerializer.Serialize(new
        {
            nb = new { path = TestRepo.NbPath, config = TestRepo.NbMockConfig },
            agents = new Dictionary<string, object> { ["claude-code"] = new { base_url = BaseUrl, api_key = "sk-test" } },
            harbor = new { uv },
        }));
        return repo;
    }

    static string Result(string look, string fix, string? trial = null) => $$"""
        cat > "$t/result.json" <<'JSON'
        {"agent_info": {"name": "claude-code", "version": "2.1.301"}, "exception_info": {{trial ?? "null"}},
         "step_results": [{{look}}{{(fix.Length > 0 ? ", " + fix : "")}}]}
        JSON
        """;

    static string Step(string name, string? exception = null) =>
        $$"""{"step_name": "{{name}}", "agent_result": {"n_input_tokens": 10, "n_output_tokens": 5}, "agent_execution": {"started_at": "2026-11-23T10:00:00Z", "finished_at": "2026-11-23T10:00:02Z"}, "exception_info": {{(exception is null ? "null" : $$"""{"exception_type": "{{exception}}", "exception_message": "it stopped"}""")}}}""";

    const string TwoSteps = """
        step look "" '{"builds": 1, "tests-pass": 1}'
        echo 0 > "$t/steps/look/verifier/builds.exit"; echo 0 > "$t/steps/look/verifier/tests-pass.exit"
        step fix-null-deref "" '{"builds": 1, "tests-pass": 0}'
        echo 'dotnet build: 0 errors' > "$t/steps/fix-null-deref/verifier/builds.out"; echo 0 > "$t/steps/fix-null-deref/verifier/builds.exit"
        echo '1 of 6 tests failed' > "$t/steps/fix-null-deref/verifier/tests-pass.out"; echo 1 > "$t/steps/fix-null-deref/verifier/tests-pass.exit"
        """;

    const string K = "assistant=cc-haiku";

    static (string? Id, string Log) Run(TestRepo repo) => repo.Run("harbor-unit");

    static string Stage(TestRepo repo, string stage) => Path.Combine(repo.TryDir("harbor-unit", K, 1), "stages", stage);
    static string Status(TestRepo repo, string stage) => File.ReadAllText(Path.Combine(Stage(repo, stage), "status")).Trim();

    [Fact]
    public void AStagedExam_CompilesToOneTaskOfSteps_EachWithItsOwnVerifier()
    {
        using var repo = Repo(TwoSteps + "\n" + Result(Step("look"), Step("fix-null-deref")));
        var (id, _) = Run(repo);
        var task = Path.Combine(repo.Root, "log", id!, "harbor", "tasks", K, "harbor-unit");

        var toml = File.ReadAllText(Path.Combine(task, "task.toml"));
        Assert.Contains("name = \"auspex/harbor-unit\"", toml);
        Assert.EndsWith("\n[[steps]]\nname = \"look\"\n\n[[steps]]\nname = \"fix-null-deref\"\n", toml);
        Assert.Equal("Which test fails? Say its name.\n", File.ReadAllText(Path.Combine(task, "steps", "look", "instruction.md")));
        Assert.False(File.Exists(Path.Combine(task, "instruction.md")));
        // Every step has its own verifier and contract: a stage's script checks run on the workspace as it left it, and a step that timed out does not end the trial.
        foreach (var stage in new[] { "look", "fix-null-deref" })
        {
            Assert.Contains("check 'builds' ", File.ReadAllText(Path.Combine(task, "steps", stage, "tests", "test.sh")));
            Assert.Contains($"export AUSPEX_STAGE='{stage}'", File.ReadAllText(Path.Combine(task, "steps", stage, "tests", "env.sh")));
        }
        Assert.False(File.Exists(Path.Combine(task, "tests", "test.sh")));
        Assert.True(Directory.Exists(Path.Combine(task, "tests", "exam")));
        // A stage after the first starts fresh: its step puts the workspace back before the agent runs.
        // It first gives the directory back to the repository's owner, which an upload can take from it (podman), or git refuses the repository.
        Assert.Contains("chown --reference=.git . && chmod --reference=.git . && git reset -q --hard HEAD && git clean -fdq", File.ReadAllText(Path.Combine(task, "steps", "fix-null-deref", "workdir", "setup.sh")));
        Assert.False(Directory.Exists(Path.Combine(task, "steps", "look", "workdir")));
        Assert.True(Directory.Exists(Path.Combine(task, "environment", "repo")));
        Assert.EndsWith(Path.Combine("harbor", "tasks", K, "harbor-unit"), JsonNode.Parse(File.ReadAllText(Path.Combine(repo.Root, "job.seen.json")))!["tasks"]![0]!["path"]!.GetValue<string>());
    }

    [Fact]
    public void ATry_IsOneTrial_AndEachStepBecomesItsStage()
    {
        using var repo = Repo(TwoSteps + "\n" + Result(Step("look"), Step("fix-null-deref")));
        var (_, log) = Run(repo);
        Assert.Contains($"{K}/1  whole", log);

        foreach (var stage in new[] { "1-look", "2-fix-null-deref" })
        {
            Assert.Equal("completed", Status(repo, stage));
            foreach (var file in new[] { "manifest.json", "trajectory.json", "core.json", "reward.json" })
                Assert.True(File.Exists(Path.Combine(Stage(repo, stage), file)), $"{stage}/{file}");
        }
        Assert.Contains("did look", File.ReadAllText(Path.Combine(Stage(repo, "1-look"), "trajectory.json")));
        // One Harbor job for the try; each stage keeps the diff Harbor collected after its step.
        var tryDir = Path.GetDirectoryName(Path.GetDirectoryName(Stage(repo, "1-look")))!;
        Assert.True(File.Exists(Path.Combine(tryDir, "harbor", "job.json")));
        Assert.Contains("Ledger/look", File.ReadAllText(Path.Combine(Stage(repo, "1-look"), "diff.patch")));
        Assert.Contains("Ledger/fix-null-deref", File.ReadAllText(Path.Combine(Stage(repo, "2-fix-null-deref"), "diff.patch")));
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(Stage(repo, "2-fix-null-deref"), "manifest.json")))!;
        Assert.Equal((2, "claude-code", "2.1.301"), (manifest["place"]!.GetValue<int>(), manifest["agent"]!["name"]!.GetValue<string>(), manifest["agent"]!["version"]!.GetValue<string>()));

        // Grading: each stage's verdicts are its own verifier's, on its own diff.
        var exam = repo.LoadExam("harbor-unit");
        var grades = Grade.Attempts(repo.Root, exam, Bank.Select(repo.Root, exam).Current, TextWriter.Null);
        Assert.Equal(("pass", "pass"), (grades[0].Checks!["builds"].Result, grades[0].Checks!["tests-pass"].Result));
        Assert.Equal(("pass", "fail", "pass"), (grades[1].Checks!["builds"].Result, grades[1].Checks!["tests-pass"].Result, grades[1].Checks!["diff-in-scope"].Result));
        Assert.Equal([true, false], grades.Select(g => g.Pass == true));
        Assert.Contains("nothing to make", Run(repo).Log);
    }

    [Theory]
    [InlineData(false, "completed")]
    [InlineData(true, "skipped")]
    public void AStepThatTimedOut_IsAStoppedStage_AndOnlyAChainedExamLeavesTheRestUnread(bool chained, string after)
    {
        using var repo = Repo(TwoSteps + "\n" + Result(Step("look", "AgentTimeoutError"), Step("fix-null-deref")), chained);
        var (_, log) = Run(repo);
        Assert.Equal(("completed", after), (Status(repo, "1-look"), Status(repo, "2-fix-null-deref")));
        Assert.Equal("time_budget", JsonNode.Parse(File.ReadAllText(Path.Combine(Stage(repo, "1-look"), "core.json")))!["exit_reason"]!.GetValue<string>());
        Assert.Contains(chained ? $"{K}/1  broke at look (time_budget)" : $"{K}/1  whole", log);
        // Harbor ran the step all the same; a chained exam's later stage is not read, so both runners grade the try alike.
        if (chained) Assert.False(File.Exists(Path.Combine(Stage(repo, "2-fix-null-deref"), "trajectory.json")));
        Assert.Contains("nothing to make", Run(repo).Log);
    }

    [Fact]
    public void AStepHarborFailed_LosesTheTry()
    {
        using var repo = Repo("step look \"\" '{\"ran\": 1}'\n" + Result(Step("look"), Step("fix-null-deref", "RuntimeError")));
        var (_, log) = Run(repo);
        Assert.Equal(("completed", "failed"), (Status(repo, "1-look"), Status(repo, "2-fix-null-deref")));
        Assert.Contains($"{K}/1  lost at fix-null-deref: harbor: RuntimeError: it stopped", log);
        var exam = repo.LoadExam("harbor-unit");
        Assert.Empty(Bank.Select(repo.Root, exam).Current);
    }

    [Fact]
    public void AnAnswerSheet_OnATryOfSeveralStages_IsRefusedThroughHarbor()
    {
        // A try of one stage with an answer sheet is a plain task and is allowed (AxesHarborTests); one of several is not, since the simulated user answers a task.
        using var repo = Repo("");
        repo.Write("harbor-unit/sheet.md", "## a\nyes\n");
        repo.EditJson("harbor-unit/exam.json", e => e["stages"]![0]!["oracle"] = "sheet.md");
        Assert.Contains(repo.Problems("harbor-unit"), p => p.Field == "axes" && p.Message.Contains("Harbor's simulated user answers one task, not a step of several"));
    }
}
