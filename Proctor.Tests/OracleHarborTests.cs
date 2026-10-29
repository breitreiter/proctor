using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

// The oracle on the Harbor path, short of running Harbor: the simulated user compiled beside the agents, an oracle cell's
// job and its run-less program, OracleUser's loop itself, and ingest of a bridged trial (plans/oracle-in-proctor.md,
// work items 3 and 4).
public class OracleHarborTests
{
    /// <summary>The gate suite with its plain task naming a sheet: one bare nb arm and one through Harbor.</summary>
    static TestRepo GateWithOracle()
    {
        var repo = new TestRepo();
        repo.CopySuite("gate");
        repo.Write("gate/oracle/plain.md", "# Answers\n\n## deploy-target\nStaging only.\n");
        repo.EditJson("gate/tasks/plain.json", t => t["oracle"] = "oracle/plain.md");
        return repo;
    }

    [Fact]
    public void Compile_AnOracleSuite_BringsTheSimulatedUser_AndItsTemplate()
    {
        using var repo = GateWithOracle();
        var expDir = Path.Combine(repo.Root, "runs", "exp");
        var hash = HarborCompile.Tasks(expDir, repo.LoadSuite("gate"), "exp", _ => new AgentRoute(null, null), "proctor-nb:0123456789abcdef");
        var agents = HarborCompile.AgentsDir(expDir);

        Assert.Equal(["nb_agent.py", "oracle_user.j2", "oracle_user.py"], Directory.GetFiles(agents).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Contains("class OracleUser(BaseAgent)", File.ReadAllText(Path.Combine(agents, "oracle_user.py")));
        Assert.Equal("{{ instruction }}\n<<<PROCTOR-BRIDGE>>>\n{{ bridge_instructions }}\n", File.ReadAllText(Path.Combine(agents, HarborCompile.OracleTemplateFile)));
        File.AppendAllText(Path.Combine(agents, "oracle_user.py"), "# edited\n");
        Assert.NotEqual(hash, HarborCompile.TreeHash(expDir));

        // The bridge's acpx config lands in the work directory; an oracle task's diff leaves it out, and no other task's does.
        Assert.Contains("git add -A -- . ':(exclude).acpxrc.json' && git diff", File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "nb-harbor", "plain"), "task.toml")));
        Assert.Contains("git add -A && git diff", File.ReadAllText(Path.Combine(Layout.HarborTask(expDir, "nb-harbor", "writes"), "task.toml")));
    }

    [Fact]
    public void Compile_ASuiteWithNoOracleTask_BringsNoSimulatedUser()
    {
        using var repo = new TestRepo();
        repo.CopySuite("gate");
        var expDir = Path.Combine(repo.Root, "runs", "exp");
        HarborCompile.Tasks(expDir, repo.LoadSuite("gate"), "exp", _ => new AgentRoute(null, null), "proctor-nb:0123456789abcdef");
        Assert.Equal(["nb_agent.py"], Directory.GetFiles(HarborCompile.AgentsDir(expDir)).Select(Path.GetFileName));
    }

    static readonly Arm NbArm = new("nb-harbor", null, "nb", "Mock", "mock", 2, null, Agent: "nb");

    [Fact]
    public void Job_AnOracleCell_IsBridged_ToOracleUserOnTheHost()
    {
        var oracle = new OracleCell("/suite/oracle/plain.md", "/host/nb", "/suites/nb.json", "Judge", 3, "/exp/harbor/agents/oracle_user.j2");
        var job = HarborCompile.Job(NbArm, "/t", "/j", new AgentRoute(null, null), new NbCell("/cell/program.jsonl", "/suites/nb.json"), oracle: oracle);

        Assert.Equal("""
            {"import_path":"oracle_user:OracleUser","user_prompt_template_path":"/exp/harbor/agents/oracle_user.j2","bridge":{"kind":"acp"},"kwargs":{"sheet":"/suite/oracle/plain.md","nb":"/host/nb","turns":3,"config":"/suites/nb.json","judge":"Judge"}}
            """, job["user_agent"]!.ToJsonString());
        Assert.Equal("nb_agent:NbAgent", job["agents"]![0]!["import_path"]!.GetValue<string>());
        Assert.Null(HarborCompile.Job(NbArm, "/t", "/j", new AgentRoute(null, null), new NbCell("/cell/program.jsonl", null))["user_agent"]);
    }

    [Fact]
    public void WithoutRun_DropsTheOneRun_AndKeepsTheRest()
    {
        var compiled = """
            {"type":"system","turn":0,"text":"be brief"}
            {"type":"run","turn":1,"prompt":"Deploy the service."}

            """;
        Assert.Equal("{\"type\":\"system\",\"turn\":0,\"text\":\"be brief\"}\n", HarborCompile.WithoutRun(compiled));
        Assert.Throws<ProctorException>(() => HarborCompile.WithoutRun("{\"type\":\"system\",\"turn\":0,\"text\":\"x\"}\n"));
    }

    /// <summary>
    /// Harbor/oracle_user.py's own tests (Proctor.Tests/python): its parsing on acpx's captured output, and its loop on a
    /// fake container, judged by the real nb --judge on Mock.
    /// </summary>
    [Fact]
    public void OracleUser_PythonTests_Pass()
    {
        var python = Path.Combine(TestRepo.SourceRoot, "Proctor.Tests", "python");
        var result = Subprocess.Run("python3", ["-B", "-m", "unittest", "-v", "test_oracle_user"], python, new Dictionary<string, string>
        {
            ["PYTHONPATH"] = $"{python}:{Path.Combine(TestRepo.SourceRoot, "Harbor")}",
            ["NB_PATH"] = TestRepo.NbPath,
            ["NB_CONFIG"] = TestRepo.NbMockConfig,
        });
        Assert.True(result.Started, $"python3 did not start: {result.Stderr}");
        Assert.True(result.ExitCode == 0, result.Stderr);
    }

    // A bridged trial as Harbor 0.24 writes one, around fixtures/nb-bridged: a recorded run of oracle_user.py driving
    // nb --acp on Mock (its capture.sh). The task asks; the sheet's answer is the second prompt; the judge calls it done.

    static JsonObject Bridged(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "nb-bridged", file)))!.AsObject();

    static string WriteBridgedTrial(string dir, Action<JsonObject>? record = null, string? exception = null, bool trajectory = true)
    {
        foreach (var d in new[] { "agent", "user-agent", "verifier", Path.Combine("artifacts", "logs", "artifacts") }) Directory.CreateDirectory(Path.Combine(dir, d));
        File.WriteAllText(Path.Combine(dir, "result.json"), new JsonObject
        {
            ["agent_info"] = new JsonObject { ["name"] = "nb", ["version"] = "0.9.0" },
            ["agent_execution"] = new JsonObject { ["started_at"] = "2026-10-29T10:00:00Z", ["finished_at"] = "2026-10-29T10:00:02Z" },
            ["exception_info"] = exception is null ? null : new JsonObject { ["exception_type"] = exception, ["exception_message"] = "timed out" },
        }.ToJsonString());
        if (trajectory) File.WriteAllText(Path.Combine(dir, "agent", "trajectory.json"), Bridged("trajectory.json").ToJsonString());
        var log = Bridged("oracle-user.json");
        record?.Invoke(log);
        File.WriteAllText(Path.Combine(dir, "user-agent", "oracle-user.json"), log.ToJsonString());
        File.WriteAllText(Path.Combine(dir, "artifacts", "logs", "artifacts", "diff.patch"), "");
        File.WriteAllText(Path.Combine(dir, "verifier", "reward.json"), "{}\n");
        return dir;
    }

    static Manifest NbManifest() => new()
    {
        RunId = "r", Experiment = "e", Arm = "nb-harbor", Task = "plain", Sample = 1, Host = "h", Versions = [], SuiteHash = "s", ProgramHash = "p",
        Agent = new AgentRun("nb", null, "mock", [], [], "0.24.0", null),
    };

    static JsonObject ReadJson(string file) => JsonNode.Parse(File.ReadAllText(file))!.AsObject();

    [Fact]
    public void Ingest_ABridgedTrial_MarksTheAnswers_AndRecordsTheJudgeApart()
    {
        using var repo = new TestRepo();
        var trial = WriteBridgedTrial(Path.Combine(repo.Root, "trial"));
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        var manifest = NbManifest();

        Assert.Null(Ingest.Trial(trial, cell, NbArm, manifest, "0.9.0", bridged: true));
        Assert.True(File.Exists(Path.Combine(cell, "user-agent", "oracle-user.json")));
        Assert.Null(manifest.NbExitCode);   // nb over the bridge has no exit of its own

        // The task stays a plain user step; the answer is marked as the bare path marks it.
        var steps = ReadJson(Path.Combine(cell, "trajectory.json"))["steps"]!.AsArray();
        Assert.Null(steps[1]!["extra"]);
        Assert.Equal("""{"source":"oracle","keys":["deploy-target"]}""", steps[3]!["extra"]!.ToJsonString());
        var t = Transcript.Read(cell);
        Assert.Equal([new UserTurn("MOCK:response=Which deploy target? MOCK:oracle=deploy-target", null),
            new UserTurn("MOCK:response=Deployed to staging. MOCK:oracle=DONE", "oracle", ["deploy-target"])], t.UserTurns);

        // The counted core is nb's two prompts; the judge's two calls are in the manifest, not in it.
        var core = ReadJson(Path.Combine(cell, "core.json"));
        Assert.Equal("ok", core["exit_reason"]!.GetValue<string>());
        Assert.Equal(2, core["turns"]!.GetValue<int>());
        Assert.Equal(20, core["input_tokens"]!.GetValue<long>());
        Assert.Equal(new OracleRun("ok", 1, 20, 10), manifest.Oracle);
    }

    [Theory]
    [InlineData("oracle_miss", "oracle_miss")]
    [InlineData("oracle_budget", "oracle_budget")]
    [InlineData("ok", "ok")]
    [InlineData("agent", "ok")]   // the turn did not end ok, so it was not judged: nb's own ending stands
    [InlineData(null, "ok")]      // killed mid-loop, between prompts
    public void Ingest_ABridgedTrial_TakesTheSimulatedUsersEnding_OnlyWhereNbsPromptNeverSawIt(string? ending, string exitReason)
    {
        using var repo = new TestRepo();
        var trial = WriteBridgedTrial(Path.Combine(repo.Root, "trial"), r => r["ending"] = ending);
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;

        Assert.Null(Ingest.Trial(trial, cell, NbArm, NbManifest(), "0.9.0", bridged: true));
        Assert.Equal(exitReason, ReadJson(Path.Combine(cell, "core.json"))["exit_reason"]!.GetValue<string>());
    }

    [Fact]
    public void Ingest_ABridgedTrial_ComparesWithTrailingWhitespaceTrimmed_AndForgivesOnlyAKilledPromptInFlight()
    {
        using var repo = new TestRepo();
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;
        var n = 0;
        string? Ingested(Action<JsonObject> record) => Ingest.Trial(WriteBridgedTrial(Path.Combine(repo.Root, $"t{++n}"), record), cell, NbArm, NbManifest(), "0.9.0", bridged: true);
        JsonObject InFlight() => new() { ["text"] = "MOCK:response=us-east.", ["keys"] = new JsonArray("region") };

        // acpx trims a prompt's trailing whitespace before nb sees it.
        Assert.Null(Ingested(r => r["prompts"]![1]!["text"] = "MOCK:response=Deployed to staging. MOCK:oracle=DONE\n\n"));

        // Killed with a third prompt sent and not answered: the trajectory may not have it.
        Assert.Null(Ingested(r => { r["ending"] = null; r["prompts"]!.AsArray().Add(InFlight()); }));
        Assert.Equal(["deploy-target"], Transcript.Read(cell).UserTurns.Single(u => u.Source == "oracle").Keys);

        // Anything else that does not line up is the strict rule's.
        const string unmapped = "adapter_unmapped: the trajectory's last user steps are not the ";
        Assert.StartsWith(unmapped + "2 prompts", Ingested(r => r["prompts"]![1]!["text"] = "Staging, please."));
        Assert.StartsWith(unmapped + "3 prompts", Ingested(r => r["prompts"]!.AsArray().Add(InFlight())));   // the ending says it finished
        Assert.StartsWith(unmapped + "3 prompts", Ingested(r =>
        {
            r["ending"] = null;
            var answered = InFlight();
            answered["stop_reason"] = "end_turn";
            r["prompts"]!.AsArray().Add(answered);
        }));
    }

    [Fact]
    public void Ingest_ABridgedTrial_Fails_OnABrokenJudge_OrAMissingRecord_OrNoPromptAnswered()
    {
        using var repo = new TestRepo();
        var cell = Directory.CreateDirectory(Path.Combine(repo.Root, "cell")).FullName;

        var manifest = NbManifest();
        var judgeError = WriteBridgedTrial(Path.Combine(repo.Root, "t1"), r =>
        {
            r["ending"] = "judge_error";
            r["error"] = "nb --judge exited 1: no such provider";
        });
        Assert.Equal("judge_error: nb --judge exited 1: no such provider", Ingest.Trial(judgeError, cell, NbArm, manifest, "0.9.0", bridged: true));
        Assert.Equal("judge_error", manifest.Oracle!.Ending);

        var noRecord = WriteBridgedTrial(Path.Combine(repo.Root, "t2"));
        File.Delete(Path.Combine(noRecord, "user-agent", "oracle-user.json"));
        Assert.Equal("the simulated user left no user-agent/oracle-user.json", Ingest.Trial(noRecord, cell, NbArm, NbManifest(), "0.9.0", bridged: true));

        Assert.Equal("nb answered no prompt over the bridge (no agent/trajectory.json)",
            Ingest.Trial(WriteBridgedTrial(Path.Combine(repo.Root, "t3"), trajectory: false), cell, NbArm, NbManifest(), "0.9.0", bridged: true));
        // Out of time with the task still in flight: a completed run with nothing in it, as on the direct path.
        var timedOut = WriteBridgedTrial(Path.Combine(repo.Root, "t4"), r =>
        {
            r["ending"] = null;
            r["prompts"] = new JsonArray(new JsonObject { ["text"] = "MOCK:response=Which deploy target? MOCK:oracle=deploy-target" });
            r["judgements"] = new JsonArray();
        }, exception: "AgentTimeoutError", trajectory: false);
        Assert.Null(Ingest.Trial(timedOut, cell, NbArm, NbManifest(), "0.9.0", bridged: true));
        Assert.Equal("time_budget", ReadJson(Path.Combine(cell, "core.json"))["exit_reason"]!.GetValue<string>());
    }
}
