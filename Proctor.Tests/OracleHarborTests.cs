using System.Text.Json.Nodes;
using Proctor;

namespace Proctor.Tests;

// The oracle on the Harbor path, short of running Harbor: the simulated user compiled beside the agents, an oracle cell's
// job and its run-less program, and OracleUser's loop itself (plans/oracle-in-proctor.md, work item 3).
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
}
